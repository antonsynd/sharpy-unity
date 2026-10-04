"""License-free compile check of the package's assemblies.

Generates two csproj files mirroring the package's asmdefs — the
Sharpy.Unity.Editor assembly (Editor/ + Runtime/ sources) and the
Sharpy.Unity.Editor.Tests assembly referencing it — and builds them with
`dotnet build` against an installed Unity editor's module DLLs
(netstandard2.1, LangVersion 9). Catches compile errors in seconds,
with readable csc output and no Unity license.

The two-assembly split is deliberate: it exercises InternalsVisibleTo
exactly like Unity's own asmdef compilation does; a merged build once
hid a real bug.

Both projects get the scripting defines Unity would set for the editor's
version (UNITY_EDITOR, UNITY_2022_3, UNITY_6000_3_OR_NEWER, ...), so
version-gated `#if` branches compile against the API they target, and
CS0618 (use of an obsolete API) is an error: Unity marks APIs obsolete
one release before removing them.

Uses only the standard library so CI containers can run it without pip:
    PYTHONPATH=Tools~ python3 -m build_tools.smoke_compile [--unity-path <Managed dir>]
"""

import argparse
import glob
import os
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

MACOS_MANAGED_GLOB = (
    "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/Managed"
)
LINUX_CONTAINER_MANAGED = "/opt/unity/Editor/Data/Managed"

# Every minor release line before Unity 6, oldest first. Unity defines
# UNITY_<major>_<minor>_OR_NEWER for each one up to the running editor;
# 2023.3 shipped as 6000.0.
PRE_6000_RELEASES = (
    [(5, minor) for minor in range(3, 7)]
    + [(year, minor) for year in range(2017, 2019) for minor in range(1, 5)]
    + [(2019, minor) for minor in range(1, 5)]
    + [(year, minor) for year in range(2020, 2023) for minor in range(1, 4)]
    + [(2023, 1), (2023, 2)]
)

# Rather than tracking every obsolete API, fail on all of them.
WARNINGS_AS_ERRORS = "CS0618"

# Only the per-module DLLs under Managed/UnityEngine/ are referenced. The
# monolithic UnityEngine.dll / UnityEditor.dll facades in Managed/ itself
# duplicate every type and produce CS0433.
EDITOR_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>Sharpy.Unity.Editor</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DefineConstants>$(DefineConstants);{defines}</DefineConstants>
    <WarningsAsErrors>$(WarningsAsErrors);{warnings_as_errors}</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{repo}/Editor/**/*.cs" />
    <Compile Include="{repo}/Runtime/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <UnityModuleDll Include="{managed}/UnityEngine/*.dll" />
    <SharpyCoreDll Include="{repo}/Plugins/Sharpy.Core/*.dll" />
    <Reference Include="@(UnityModuleDll)" />
    <Reference Include="@(SharpyCoreDll)" />
  </ItemGroup>
</Project>
"""

TESTS_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>Sharpy.Unity.Editor.Tests</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DefineConstants>$(DefineConstants);{defines}</DefineConstants>
    <WarningsAsErrors>$(WarningsAsErrors);{warnings_as_errors}</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{repo}/Tests/Editor/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../editor/Sharpy.Unity.Editor.csproj" />
  </ItemGroup>
  <ItemGroup>
    <UnityModuleDll Include="{managed}/UnityEngine/*.dll" />
    <SharpyCoreDll Include="{repo}/Plugins/Sharpy.Core/*.dll" />
    <Reference Include="@(UnityModuleDll)" />
    <Reference Include="@(SharpyCoreDll)" />
    {nunit_item}
  </ItemGroup>
</Project>
"""

# Compile-time stand-in when the editor install ships no com.unity.ext.nunit
# (the unityci -base images don't): the NuGet package of the same NUnit 3.x
# line exposes the same API surface, which is all a syntax gate needs.
NUNIT_NUGET_ITEM = '<PackageReference Include="NUnit" Version="3.13.3" />'


def find_unity_managed(cli_path):
    """Resolve the Unity Managed directory: flag, env, local Hub, CI container."""
    candidates = []

    if cli_path:
        candidates.append(Path(cli_path))
    elif os.environ.get("UNITY_MANAGED_DIR"):
        candidates.append(Path(os.environ["UNITY_MANAGED_DIR"]))
    else:
        hub_installs = sorted(
            glob.glob(MACOS_MANAGED_GLOB),
            key=lambda p: _version_key(p),
            reverse=True,
        )
        candidates.extend(Path(p) for p in hub_installs)
        candidates.append(Path(LINUX_CONTAINER_MANAGED))

    for candidate in candidates:
        if (candidate / "UnityEngine").is_dir():
            return candidate

    return None


def _version_key(managed_path):
    match = re.search(r"/Editor/([^/]+)/", managed_path)
    version = match.group(1) if match else ""
    return tuple(int(n) for n in re.findall(r"\d+", version)) or (0,)


def _parse_version(text):
    """'2022.3.22f1' -> (2022, 3, 22); None unless it has three numbers."""
    numbers = tuple(int(n) for n in re.findall(r"\d+", text or ""))
    return numbers[:3] if len(numbers) >= 3 else None


def find_unity_version(managed, cli_version):
    """Resolve the editor version: flag, env, Hub install path, macOS bundle.

    The Linux editor image's path (/opt/unity/Editor/Data/Managed) carries no
    version, so CI passes --unity-version.
    """
    for text in (cli_version, os.environ.get("UNITY_VERSION")):
        if text:
            return _parse_version(text)

    from_path = _version_key(managed.as_posix())

    if len(from_path) >= 3:
        return from_path[:3]

    # macOS: .../Unity.app/Contents/Resources/Scripting/Managed
    info_plist = managed.parent.parent.parent / "Info.plist"

    if info_plist.is_file():
        with open(info_plist, "rb") as f:
            return _parse_version(plistlib.load(f).get("CFBundleVersion"))

    return None


def unity_version_defines(version):
    """The version scripting defines Unity sets for an editor of `version`."""
    major, minor, patch = version
    releases = [r for r in PRE_6000_RELEASES if r <= (major, minor)]

    if major >= 6000:
        # Unity 6 numbers minors from 0. Majors past 6000 are not modelled
        # beyond their own minors.
        releases += [(major, m) for m in range(minor + 1)]

    defines = ["UNITY_EDITOR", f"UNITY_{major}", f"UNITY_{major}_{minor}",
               f"UNITY_{major}_{minor}_{patch}"]
    defines += [f"UNITY_{a}_{b}_OR_NEWER" for a, b in releases]
    return defines


def find_nunit(managed):
    """Locate nunit.framework.dll in the editor's built-in com.unity.ext.nunit."""
    built_in_roots = [
        # macOS: .../Contents/Resources/Scripting/Managed -> Contents/Resources
        managed.parent.parent / "PackageManager" / "BuiltInPackages",
        # Linux editor image: .../Editor/Data/Managed -> Editor/Data/Resources
        managed.parent / "Resources" / "PackageManager" / "BuiltInPackages",
    ]

    for root in built_in_roots:
        package_dir = root / "com.unity.ext.nunit"

        if not package_dir.is_dir():
            continue

        # The framework folder varies by package version (net35/unity-custom
        # in 2022.3's 1.x, net40/unity-custom in newer editors); prefer the
        # Unity-custom build over any stock one that may sit alongside it.
        for pattern in ("**/unity-custom/nunit.framework.dll", "**/nunit.framework.dll"):
            for found in sorted(package_dir.glob(pattern)):
                return found

    return None


def run_smoke_compile(unity_path=None, unity_version=None):
    """Build both assemblies. Returns a process exit code."""
    managed = find_unity_managed(unity_path)

    if managed is None:
        print(
            "error: could not locate a Unity Managed directory "
            "(tried --unity-path, $UNITY_MANAGED_DIR, "
            f"{MACOS_MANAGED_GLOB}, {LINUX_CONTAINER_MANAGED})",
            file=sys.stderr,
        )
        return 1

    version = find_unity_version(managed, unity_version)

    if version is None:
        print(
            f"error: could not determine the Unity version of {managed} "
            "(pass --unity-version, e.g. 2022.3.22f1, or set $UNITY_VERSION)",
            file=sys.stderr,
        )
        return 1

    defines = unity_version_defines(version)
    nunit = find_nunit(managed)

    if nunit is not None:
        nunit_item = f'<Reference Include="{nunit.as_posix()}" />'
        nunit_label = str(nunit)
    else:
        nunit_item = NUNIT_NUGET_ITEM
        nunit_label = "no com.unity.ext.nunit in this install; restoring NUnit from NuGet"

    dotnet = shutil.which("dotnet")

    if dotnet is None:
        print("error: dotnet SDK not found on PATH", file=sys.stderr)
        return 1

    print(f"Unity Managed: {managed}")
    print(f"Unity version: {'.'.join(str(n) for n in version)} "
          f"({len(defines)} defines, e.g. {defines[-1]})")
    print(f"nunit:         {nunit_label}")

    with tempfile.TemporaryDirectory(prefix="sharpy-smoke-") as tmp:
        tmp_path = Path(tmp)
        # Each csproj gets its own directory: projects sharing a folder share
        # obj/project.assets.json, and whichever restore lands last wins — the
        # Tests project then intermittently loses its NuGet NUnit reference.
        editor_dir = tmp_path / "editor"
        tests_dir = tmp_path / "tests"
        editor_dir.mkdir()
        tests_dir.mkdir()
        substitutions = {
            "repo": REPO_ROOT.as_posix(),
            "managed": managed.as_posix(),
            "nunit_item": nunit_item,
            "defines": ";".join(defines),
            "warnings_as_errors": WARNINGS_AS_ERRORS,
        }

        (editor_dir / "Sharpy.Unity.Editor.csproj").write_text(
            EDITOR_CSPROJ.format(**substitutions))
        (tests_dir / "Sharpy.Unity.Editor.Tests.csproj").write_text(
            TESTS_CSPROJ.format(**substitutions))

        # Building the tests project builds the editor assembly through the
        # ProjectReference, preserving the two-assembly InternalsVisibleTo
        # boundary.
        result = subprocess.run(
            [dotnet, "build", "Sharpy.Unity.Editor.Tests.csproj", "-v:m", "--nologo"],
            cwd=tests_dir,
        )

    if result.returncode == 0:
        print("smoke-compile: PASS")
    else:
        print("smoke-compile: FAIL", file=sys.stderr)

    return result.returncode


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="License-free compile check of the package's assemblies.")
    parser.add_argument(
        "--unity-path",
        help="Path to a Unity editor's Managed directory "
             "(e.g. /opt/unity/Editor/Data/Managed)",
    )
    parser.add_argument(
        "--unity-version",
        help="That editor's version (e.g. 2022.3.22f1), when its path does "
             "not show it; also read from $UNITY_VERSION",
    )
    args = parser.parse_args(argv)
    return run_smoke_compile(args.unity_path, args.unity_version)


if __name__ == "__main__":
    sys.exit(main())
