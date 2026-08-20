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

Uses only the standard library so CI containers can run it without pip:
    python3 -m build_tools.smoke_compile [--unity-path <Managed dir>]
"""

import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

MACOS_MANAGED_GLOB = (
    "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/Managed"
)
LINUX_CONTAINER_MANAGED = "/opt/unity/Editor/Data/Managed"

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
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{repo}/Tests/Editor/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="Sharpy.Unity.Editor.csproj" />
  </ItemGroup>
  <ItemGroup>
    <UnityModuleDll Include="{managed}/UnityEngine/*.dll" />
    <SharpyCoreDll Include="{repo}/Plugins/Sharpy.Core/*.dll" />
    <Reference Include="@(UnityModuleDll)" />
    <Reference Include="@(SharpyCoreDll)" />
    <Reference Include="{nunit}" />
  </ItemGroup>
</Project>
"""


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


def run_smoke_compile(unity_path=None):
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

    nunit = find_nunit(managed)

    if nunit is None:
        print(
            f"error: could not locate nunit.framework.dll near {managed}",
            file=sys.stderr,
        )
        return 1

    dotnet = shutil.which("dotnet")

    if dotnet is None:
        print("error: dotnet SDK not found on PATH", file=sys.stderr)
        return 1

    print(f"Unity Managed: {managed}")
    print(f"nunit:         {nunit}")

    with tempfile.TemporaryDirectory(prefix="sharpy-smoke-") as tmp:
        tmp_path = Path(tmp)
        substitutions = {
            "repo": REPO_ROOT.as_posix(),
            "managed": managed.as_posix(),
            "nunit": nunit.as_posix(),
        }

        (tmp_path / "Sharpy.Unity.Editor.csproj").write_text(
            EDITOR_CSPROJ.format(**substitutions))
        (tmp_path / "Sharpy.Unity.Editor.Tests.csproj").write_text(
            TESTS_CSPROJ.format(**substitutions))

        # Building the tests project builds the editor assembly through the
        # ProjectReference, preserving the two-assembly InternalsVisibleTo
        # boundary.
        result = subprocess.run(
            [dotnet, "build", "Sharpy.Unity.Editor.Tests.csproj", "-v:m", "--nologo"],
            cwd=tmp_path,
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
    args = parser.parse_args(argv)
    return run_smoke_compile(args.unity_path)


if __name__ == "__main__":
    sys.exit(main())
