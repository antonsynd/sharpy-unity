"""Compile the BasicSetup sample the way the package does inside Unity.

Mirrors SharpyProjectCompiler without an editor:

1. copies Samples~/BasicSetup/Scripts to where Unity imports a sample
   (Assets/Samples/<displayName>/<version>/BasicSetup/Scripts, spaces and all);
2. writes Library/Sharpy/unity.spyproj as SharpyProjectFile.Build does,
   referencing Unity's engine modules through SharpyReferenceProvider's
   filter rules;
3. runs `sharpyc project ... --emit-cs-to` with the pinned sharpyc
   (downloaded from the release SharpyToolchain.Version names, or --sharpyc);
4. turns span #line directives into classic ones as SharpyLineDirectives
   does (paths stay absolute), then fails if any #line is left in a form
   other than `#line N "path"`, `#line hidden` or `#line default`;
5. builds the result like Unity builds Assembly-CSharp, with the editor's
   own Roslyn (<Managed>/../DotNetSdkRoslyn, run on its bundled .NET):
   netstandard2.1, LangVersion 9, the engine modules plus
   Plugins/Sharpy.Core, the Unity version defines, CS0618 and the #line
   warnings as errors. The .NET SDK's compiler is used only when the editor
   has no bundled one.

The #line form check is what guards the rewrite: Roslyn 4.x (the .NET SDK,
and Unity 6000.3's 4.3.1) accepts the C# 10 span form even at LangVersion 9,
so a compile alone only catches a missed span directive on an editor with
an older compiler.

The C# is the single source of truth for everything both sides need: the
spyproj constants, the reference denylist and BCL folder names, and the
span-directive regex are parsed out of the Editor/*.cs files with strict
patterns, and a parse that finds nothing fails the run. A data file both
read would need a .meta and loader code in the package; parsing keeps the
package unchanged and still fails the moment the C# is reshaped.

Uses only the standard library so CI containers can run it without pip:
    PYTHONPATH=Tools~ python3 -m build_tools.smoke_sample [--unity-path ...]
"""

import argparse
import json
import platform
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from pathlib import Path

from build_tools.smoke_compile import (
    WARNINGS_AS_ERRORS,
    find_unity_managed,
    find_unity_version,
    unity_version_defines,
)

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

SAMPLE_SCRIPTS = REPO_ROOT / "Samples~" / "BasicSetup" / "Scripts"
TOOLCHAIN_CS = REPO_ROOT / "Editor" / "SharpyToolchain.cs"
PROJECT_FILE_CS = REPO_ROOT / "Editor" / "SharpyProjectFile.cs"
PROJECT_COMPILER_CS = REPO_ROOT / "Editor" / "SharpyProjectCompiler.cs"
REFERENCE_PROVIDER_CS = REPO_ROOT / "Editor" / "SharpyReferenceProvider.cs"
LINE_DIRECTIVES_CS = REPO_ROOT / "Editor" / "SharpyLineDirectives.cs"

# SharpyToolchain.ReleaseUrlBase + SharpyBinaryDownloader's archive name.
SHARPYC_ARCHIVE_URL = (
    "https://github.com/antonsynd/sharpy/releases/download/v{version}/sharpyc-{rid}.tar.gz"
)

# A #line problem the C# 9 compiler only warns about: an empty file name.
LINE_WARNINGS_AS_ERRORS = "CS1709"

# What SharpyLineDirectives leaves behind: the classic form, hidden, default.
ALLOWED_LINE_DIRECTIVE = re.compile(r'^\s*#\s*line\s+(\d+\s+"[^"]*"|hidden|default)\s*$')
ANY_LINE_DIRECTIVE = re.compile(r"^\s*#\s*line\b")

SAMPLE_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>Assembly-CSharp</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DefineConstants>$(DefineConstants);{defines}</DefineConstants>
    <WarningsAsErrors>$(WarningsAsErrors);{warnings_as_errors}</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{emit}/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <UnityModuleDll Include="{managed}/UnityEngine/*.dll" />
    <SharpyCoreDll Include="{repo}/Plugins/Sharpy.Core/*.dll" />
    <Reference Include="@(UnityModuleDll)" />
    <Reference Include="@(SharpyCoreDll)" />
  </ItemGroup>
</Project>
"""


class SourceParseError(Exception):
    """A value expected in the package's C# could not be parsed."""


# ---------------------------------------------------------------------------
# Reading the C# source of truth
# ---------------------------------------------------------------------------


def read_cs_string_const(text, name, source):
    """The value of `const string <name> = "...";` (no escapes allowed)."""
    matches = re.findall(rf'\bconst\s+string\s+{re.escape(name)}\s*=\s*"([^"\\]*)"\s*;', text)

    if len(matches) != 1:
        raise SourceParseError(
            f"expected one `const string {name}` in {source}, found {len(matches)}")

    return matches[0]


def read_cs_string_array(text, name, source):
    """The items of `<name> = { "a", "b", };` — strictly string literals."""
    matches = re.findall(rf'\b{re.escape(name)}\s*=\s*\{{([^}}]*)\}}\s*;', text)

    if len(matches) != 1:
        raise SourceParseError(
            f"expected one `{name} = {{ ... }};` array in {source}, found {len(matches)}")

    body = re.sub(r"//[^\n]*", "", matches[0])

    if not re.fullmatch(r'\s*("[^"\\]*"\s*,\s*)*("[^"\\]*"\s*,?\s*)?', body):
        raise SourceParseError(
            f"`{name}` in {source} holds something other than plain string literals")

    items = re.findall(r'"([^"\\]*)"', body)

    if not items:
        raise SourceParseError(f"`{name}` in {source} is empty")

    return items


def read_cs_verbatim_regex(text, name, source):
    """The pattern of `<name> = new Regex(@"...", ...)`, as a Python regex."""
    matches = re.findall(rf'\b{re.escape(name)}\s*=\s*new\s+Regex\(\s*@"((?:[^"]|"")*)"', text)

    if len(matches) != 1:
        raise SourceParseError(
            f"expected one `{name} = new Regex(@\"...\")` in {source}, found {len(matches)}")

    # .NET and Python agree on the constructs this pattern uses (\s \d (?:) ^ $).
    return re.compile(matches[0].replace('""', '"'))


def read_package_contract():
    """Everything the run takes from the package's C#. Raises SourceParseError."""
    project_file = PROJECT_FILE_CS.read_text()
    project_compiler = PROJECT_COMPILER_CS.read_text()
    provider = REFERENCE_PROVIDER_CS.read_text()
    toolchain = TOOLCHAIN_CS.read_text()
    directives = LINE_DIRECTIVES_CS.read_text()

    return {
        "version": read_cs_string_const(toolchain, "Version", TOOLCHAIN_CS.name),
        "root_namespace": read_cs_string_const(
            project_file, "DefaultRootNamespace", PROJECT_FILE_CS.name),
        "assembly_name": read_cs_string_const(project_file, "AssemblyName", PROJECT_FILE_CS.name),
        "target_framework": read_cs_string_const(
            project_file, "TargetFramework", PROJECT_FILE_CS.name),
        "library_folder": read_cs_string_const(
            project_compiler, "LibraryFolder", PROJECT_COMPILER_CS.name),
        "project_file_name": read_cs_string_const(
            project_compiler, "ProjectFileName", PROJECT_COMPILER_CS.name),
        "source_glob": read_cs_string_const(project_compiler, "SourceGlob", PROJECT_COMPILER_CS.name),
        "source_root": read_cs_string_const(project_compiler, "SourceRoot", PROJECT_COMPILER_CS.name),
        "denylist": read_cs_string_array(provider, "BuiltInDenylist", REFERENCE_PROVIDER_CS.name),
        "bcl_folders": read_cs_string_array(provider, "BclDirectoryNames", REFERENCE_PROVIDER_CS.name),
        "span_directive": read_cs_verbatim_regex(directives, "SpanDirective", LINE_DIRECTIVES_CS.name),
    }


# ---------------------------------------------------------------------------
# The package's behaviour, ported
# ---------------------------------------------------------------------------


def _strip_dll(name):
    return name[:-4] if name.lower().endswith(".dll") else name


def filter_references(paths, denylist, bcl_folders):
    """SharpyReferenceProvider.Filter with an empty user denylist."""
    denied = {name.lower() for name in denylist}
    bcl = {folder.lower() for folder in bcl_folders}
    seen = set()
    result = []

    for path in paths:
        name = _strip_dll(Path(path).name)
        lower = name.lower()
        folders = {part.lower() for part in Path(path).parts[:-1]}

        if (lower.startswith("sharpy.")
                or lower == "assembly-csharp"
                or lower.startswith("assembly-csharp-editor")
                or lower.startswith("unityeditor.")
                or lower in denied
                or folders & bcl
                or path in seen):
            continue

        seen.add(path)
        result.append(path)

    return result


def build_project_file(contract, references):
    """SharpyProjectFile.Build's output for the default settings."""
    from xml.sax.saxutils import quoteattr

    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        "<Project>",
        "  <PropertyGroup>",
        f"    <RootNamespace>{contract['root_namespace']}</RootNamespace>",
        "    <OutputType>library</OutputType>",
        f"    <TargetFramework>{contract['target_framework']}</TargetFramework>",
        f"    <AssemblyName>{contract['assembly_name']}</AssemblyName>",
        f"    <SourceRoot>{contract['source_root']}</SourceRoot>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
        f"    <SourceFile Include={quoteattr(contract['source_glob'])} />",
        "  </ItemGroup>",
    ]

    if references:
        lines.append("  <ItemGroup>")
        lines += [f"    <Reference Include={quoteattr(ref)} />" for ref in references]
        lines.append("  </ItemGroup>")

    lines.append("</Project>")
    return "\n".join(lines) + "\n"


def rewrite_line_directives(cs_text, span_directive):
    """SharpyLineDirectives.Rewrite(text, null, keepDirectives: true)."""
    out = []

    for line in cs_text.splitlines(keepends=True):
        content = line.rstrip("\r\n")
        ending = line[len(content):]
        span = span_directive.match(content)

        if span:
            path = span.group(3).replace("\\", "/")
            out.append(f'{span.group(1)}#line {span.group(2)} "{path}"{ending}')
        else:
            out.append(line)

    return "".join(out)


def unconverted_line_directives(cs_text):
    """#line lines Unity's C# 9 may not accept, with 1-based line numbers."""
    return [
        (number, line.strip())
        for number, line in enumerate(cs_text.splitlines(), start=1)
        if ANY_LINE_DIRECTIVE.match(line) and not ALLOWED_LINE_DIRECTIVE.match(line)
    ]


# ---------------------------------------------------------------------------
# Compiling the generated C#
# ---------------------------------------------------------------------------


def unity_csc(managed):
    """(dotnet, csc.dll) bundled with the editor, or None.

    Same layout next to Managed/ on macOS (.../Scripting/) and in the Linux
    editor image (.../Editor/Data/).
    """
    root = managed.parent
    csc = root / "DotNetSdkRoslyn" / "csc.dll"
    netstandard = root / "NetStandard" / "ref" / "2.1.0" / "netstandard.dll"

    for name in ("dotnet", "dotnet.exe"):
        dotnet = root / "NetCoreRuntime" / name

        if dotnet.is_file() and csc.is_file() and netstandard.is_file():
            return dotnet, csc, netstandard

    return None


def compile_with_unity_csc(toolchain, managed, sources, defines, warnings_as_errors, out_dir):
    dotnet, csc, netstandard = toolchain
    # Unity adds the .NET Framework facades (mscorlib.dll, System.dll, ...,
    # all type forwarders to netstandard) to every .NET Standard profile
    # compile, so a module or plugin built against mscorlib still binds
    # (otherwise CS0012).
    netfx_shims = netstandard.parent.parent.parent / "compat" / "2.1.0" / "shims" / "netfx"
    references = [netstandard] + sorted(netfx_shims.glob("*.dll")) \
        + sorted((managed / "UnityEngine").glob("*.dll")) \
        + sorted((REPO_ROOT / "Plugins" / "Sharpy.Core").glob("*.dll"))

    args = [
        "-nologo", "-nostdlib", "-target:library", "-langversion:9.0",
        f"-out:{out_dir / 'Assembly-CSharp.dll'}",
        f"-define:{';'.join(defines)}",
        f"-warnaserror+:{','.join(warnings_as_errors)}",
    ]
    args += [f"-r:{ref}" for ref in references]
    args += [str(src) for src in sources]

    rsp = out_dir / "Assembly-CSharp.rsp"
    rsp.write_text("\n".join(f'"{a}"' if " " in a else a for a in args) + "\n")
    print(f"csc:           {csc} ({_csc_version(dotnet, csc)})", flush=True)
    # -noconfig is only honoured on the command line, not in a response file.
    return subprocess.run([str(dotnet), str(csc), "-noconfig", f"@{rsp}"]).returncode


def _csc_version(dotnet, csc):
    result = subprocess.run([str(dotnet), str(csc), "-version"], capture_output=True, text=True)
    return result.stdout.strip() or "unknown version"


def compile_with_dotnet_sdk(dotnet, managed, emit, defines, warnings_as_errors, out_dir):
    print("csc:           .NET SDK (this editor bundles no DotNetSdkRoslyn)", flush=True)
    (out_dir / "Assembly-CSharp.csproj").write_text(SAMPLE_CSPROJ.format(
        defines=";".join(defines),
        warnings_as_errors=";".join(warnings_as_errors),
        emit=emit.as_posix(),
        managed=managed.as_posix(),
        repo=REPO_ROOT.as_posix(),
    ))
    return subprocess.run(
        [dotnet, "build", "Assembly-CSharp.csproj", "-v:m", "--nologo"], cwd=out_dir).returncode


# ---------------------------------------------------------------------------
# sharpyc
# ---------------------------------------------------------------------------


def host_rid():
    machine = platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "x64"

    if sys.platform == "darwin":
        return f"osx-{arch}"

    if sys.platform.startswith("linux"):
        return f"linux-{arch}"

    raise RuntimeError(f"no tar.gz sharpyc build for {sys.platform}; pass --sharpyc")


def download_sharpyc(version, dest):
    url = SHARPYC_ARCHIVE_URL.format(version=version, rid=host_rid())
    print(f"Downloading {url}")

    with urllib.request.urlopen(url, timeout=300) as response:
        archive = dest / "sharpyc.tar.gz"
        archive.write_bytes(response.read())

    with tarfile.open(archive) as tar:
        tar.extractall(dest / "sharpyc")

    binary = dest / "sharpyc" / "sharpyc"

    if not binary.is_file():
        raise RuntimeError(f"{url} has no sharpyc at its root")

    binary.chmod(binary.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    return binary


# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------


def run_smoke_sample(unity_path=None, unity_version=None, sharpyc=None, sample=None):
    """Compile the sample. Returns a process exit code."""
    try:
        contract = read_package_contract()
    except SourceParseError as ex:
        print(f"smoke-sample: error: {ex}", file=sys.stderr)
        return 1

    managed = find_unity_managed(unity_path)

    if managed is None:
        print("smoke-sample: error: could not locate a Unity Managed directory "
              "(pass --unity-path)", file=sys.stderr)
        return 1

    version = find_unity_version(managed, unity_version)

    if version is None:
        print("smoke-sample: error: could not determine the Unity version "
              "(pass --unity-version)", file=sys.stderr)
        return 1

    unity_toolchain = unity_csc(managed)
    dotnet = shutil.which("dotnet")

    if unity_toolchain is None and dotnet is None:
        print("smoke-sample: error: neither the editor's DotNetSdkRoslyn nor a dotnet SDK "
              "was found", file=sys.stderr)
        return 1

    sample_dir = Path(sample) if sample else SAMPLE_SCRIPTS
    package = json.loads((REPO_ROOT / "package.json").read_text())

    with tempfile.TemporaryDirectory(prefix="sharpy-sample-") as tmp:
        tmp_path = Path(tmp)
        compiler = Path(sharpyc) if sharpyc else download_sharpyc(contract["version"], tmp_path)

        # The project root has a space, like many real ones.
        project = tmp_path / "Unity Project"
        imported = (project / "Assets" / "Samples" / package["displayName"]
                    / package["version"] / "BasicSetup" / "Scripts")
        shutil.copytree(sample_dir, imported)

        engine_dlls = sorted(str(p) for p in (managed / "UnityEngine").glob("*.dll"))
        references = filter_references(engine_dlls, contract["denylist"], contract["bcl_folders"])

        library = project / contract["library_folder"]
        library.mkdir(parents=True)
        spyproj = library / contract["project_file_name"]
        spyproj.write_text(build_project_file(contract, references))
        emit = library / "emit"

        print(f"sharpyc:       {compiler}")
        print(f"Unity Managed: {managed} ({'.'.join(map(str, version))})")
        print(f"References:    {len(references)} of {len(engine_dlls)} engine modules "
              f"(denylist: {', '.join(contract['denylist'])})", flush=True)

        result = subprocess.run(
            [str(compiler), "project", str(spyproj), "--emit-cs-to", str(emit)],
            cwd=project, capture_output=True, text=True)

        if result.returncode != 0:
            sys.stdout.write(result.stdout)
            sys.stderr.write(result.stderr)
            print(f"smoke-sample: FAIL (sharpyc exit {result.returncode})", file=sys.stderr)
            return 1

        generated = sorted(emit.rglob("*.cs"))

        if not generated:
            print("smoke-sample: FAIL (sharpyc wrote no C#)", file=sys.stderr)
            return 1

        unconverted = []

        for cs in generated:
            text = rewrite_line_directives(cs.read_text(), contract["span_directive"])
            cs.write_text(text)
            unconverted += [(cs, n, line) for n, line in unconverted_line_directives(text)]

        print(f"Generated:     {', '.join(str(p.relative_to(emit)) for p in generated)}")

        if unconverted:
            for cs, number, line in unconverted:
                print(f"{cs.relative_to(emit)}({number}): unconverted directive: {line}",
                      file=sys.stderr)
            print(f"smoke-sample: FAIL ({len(unconverted)} #line directive(s) the "
                  "SharpyLineDirectives rewrite left in a form Unity's C# 9 may reject)",
                  file=sys.stderr)
            return 1

        build_dir = tmp_path / "build"
        build_dir.mkdir()
        defines = unity_version_defines(version)
        warnings_as_errors = [WARNINGS_AS_ERRORS, LINE_WARNINGS_AS_ERRORS]

        if unity_toolchain is not None:
            code = compile_with_unity_csc(
                unity_toolchain, managed, generated, defines, warnings_as_errors, build_dir)
        else:
            code = compile_with_dotnet_sdk(
                dotnet, managed, emit, defines, warnings_as_errors, build_dir)

    if code == 0:
        print("smoke-sample: PASS")
    else:
        print("smoke-sample: FAIL", file=sys.stderr)

    return code


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Compile the BasicSetup sample with the pinned sharpyc, then csc.")
    parser.add_argument("--unity-path", help="Path to a Unity editor's Managed directory")
    parser.add_argument("--unity-version", help="That editor's version, e.g. 2022.3.22f1")
    parser.add_argument("--sharpyc", help="Use this sharpyc instead of downloading the pinned one")
    parser.add_argument("--sample", help="Sample Scripts folder (default: Samples~/BasicSetup/Scripts)")
    args = parser.parse_args(argv)
    return run_smoke_sample(args.unity_path, args.unity_version, args.sharpyc, args.sample)


if __name__ == "__main__":
    sys.exit(main())
