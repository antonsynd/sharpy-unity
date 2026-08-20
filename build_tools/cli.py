"""Sharpy Unity build tools CLI.

Unified CLI for building, formatting, bundling DLLs, and managing the
sharpy-unity UPM package.

Usage:
    python -m build_tools <command> [options]
    build_sharpy_unity <command> [options]
"""

import click
import logging
import os
import shutil
import subprocess
import sys
from pathlib import Path

VERSION = "0.1.0"

REPO_ROOT = Path(__file__).resolve().parent.parent
SHARPY_REPO = REPO_ROOT.parent / "sharpy"

EDITOR_DIR = REPO_ROOT / "Editor"
RUNTIME_DIR = REPO_ROOT / "Runtime"
PLUGINS_DIR = REPO_ROOT / "Plugins" / "Sharpy.Core"
BINARIES_DIR = EDITOR_DIR / "Binaries"

SHARPY_CORE_CSPROJ = SHARPY_REPO / "src" / "Sharpy.Core" / "Sharpy.Core.csproj"
SHARPY_CLI_CSPROJ = SHARPY_REPO / "src" / "Sharpy.Cli" / "Sharpy.Cli.csproj"

TARGET_RIDS = ["osx-arm64", "osx-x64", "win-x64", "linux-x64"]

logging.basicConfig(
    level=logging.INFO,
    format="%(levelname)s: %(message)s",
)
log = logging.getLogger("build_sharpy_unity")


def _run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    """Run a subprocess, logging the command."""
    log.info("$ %s", " ".join(str(c) for c in cmd))
    return subprocess.run(cmd, **kwargs)


@click.group()
@click.version_option(VERSION)
def main():
    """Sharpy Unity build tools."""
    pass


# ---------------------------------------------------------------------------
# format
# ---------------------------------------------------------------------------


@main.command()
@click.option("--check", is_flag=True, help="Check only, don't modify files.")
def format(check: bool):
    """Format C# files per .editorconfig conventions."""
    cs_files = list(EDITOR_DIR.rglob("*.cs")) + list(RUNTIME_DIR.rglob("*.cs"))

    if not cs_files:
        log.info("No .cs files found.")
        return

    log.info("Found %d C# file(s).", len(cs_files))

    for f in cs_files:
        content = f.read_text()
        fixed = content.replace("\r\n", "\n")
        if not fixed.endswith("\n"):
            fixed += "\n"

        if fixed != content:
            if check:
                log.warning("Would fix: %s", f.relative_to(REPO_ROOT))
            else:
                f.write_text(fixed)
                log.info("Fixed: %s", f.relative_to(REPO_ROOT))

    log.info("Format %s.", "check complete" if check else "complete")


# ---------------------------------------------------------------------------
# bundle-core
# ---------------------------------------------------------------------------


@main.command("bundle-core")
@click.option(
    "--configuration",
    "-c",
    default="Release",
    help="Build configuration (default: Release).",
)
def bundle_core(configuration: str):
    """Build Sharpy.Core for netstandard2.1 and copy DLLs to Plugins/."""
    if not SHARPY_CORE_CSPROJ.exists():
        log.error("Sharpy.Core.csproj not found at %s", SHARPY_CORE_CSPROJ)
        log.error("Ensure the sharpy repo is at %s", SHARPY_REPO)
        sys.exit(1)

    log.info("Publishing Sharpy.Core (netstandard2.1, %s)...", configuration)

    result = _run(
        [
            "dotnet",
            "publish",
            str(SHARPY_CORE_CSPROJ),
            "-f",
            "netstandard2.1",
            "-c",
            configuration,
            "-o",
            str(REPO_ROOT / ".tmp" / "core-publish"),
        ],
    )

    if result.returncode != 0:
        log.error("dotnet publish failed.")
        sys.exit(1)

    publish_dir = REPO_ROOT / ".tmp" / "core-publish"
    PLUGINS_DIR.mkdir(parents=True, exist_ok=True)

    copied = 0
    for dll in publish_dir.glob("*.dll"):
        if dll.name.startswith("System.Private"):
            continue
        dest = PLUGINS_DIR / dll.name
        shutil.copy2(dll, dest)
        log.info("Copied %s", dll.name)
        copied += 1

    shutil.rmtree(REPO_ROOT / ".tmp", ignore_errors=True)
    log.info("Bundled %d DLL(s) to %s.", copied, PLUGINS_DIR.relative_to(REPO_ROOT))


# ---------------------------------------------------------------------------
# bundle-compiler
# ---------------------------------------------------------------------------


@main.command("bundle-compiler")
@click.option(
    "--configuration",
    "-c",
    default="Release",
    help="Build configuration (default: Release).",
)
@click.option(
    "--rid",
    multiple=True,
    default=TARGET_RIDS,
    help="Runtime identifiers to publish for.",
)
def bundle_compiler(configuration: str, rid: tuple[str, ...]):
    """Publish self-contained sharpyc binaries for each platform."""
    if not SHARPY_CLI_CSPROJ.exists():
        log.error("Sharpy.Cli.csproj not found at %s", SHARPY_CLI_CSPROJ)
        log.error("Ensure the sharpy repo is at %s", SHARPY_REPO)
        sys.exit(1)

    for target_rid in rid:
        log.info("Publishing sharpyc for %s (%s)...", target_rid, configuration)

        publish_dir = REPO_ROOT / ".tmp" / f"cli-publish-{target_rid}"

        result = _run(
            [
                "dotnet",
                "publish",
                str(SHARPY_CLI_CSPROJ),
                "-c",
                configuration,
                "--self-contained",
                "-r",
                target_rid,
                "-o",
                str(publish_dir),
            ],
        )

        if result.returncode != 0:
            log.error("dotnet publish failed for %s.", target_rid)
            continue

        dest_dir = BINARIES_DIR / target_rid
        dest_dir.mkdir(parents=True, exist_ok=True)

        binary_name = "sharpyc.exe" if "win" in target_rid else "sharpyc"
        src_binary = publish_dir / binary_name

        if src_binary.exists():
            shutil.copy2(src_binary, dest_dir / binary_name)
            log.info("Copied %s to %s", binary_name, dest_dir.relative_to(REPO_ROOT))
        else:
            log.warning("Binary %s not found in publish output.", binary_name)

    shutil.rmtree(REPO_ROOT / ".tmp", ignore_errors=True)
    log.info("Compiler bundling complete.")


# ---------------------------------------------------------------------------
# bundle-all
# ---------------------------------------------------------------------------


@main.command("bundle-all")
@click.option(
    "--configuration",
    "-c",
    default="Release",
    help="Build configuration (default: Release).",
)
@click.pass_context
def bundle_all(ctx: click.Context, configuration: str):
    """Bundle both Sharpy.Core DLLs and sharpyc compiler binaries."""
    ctx.invoke(bundle_core, configuration=configuration)
    ctx.invoke(bundle_compiler, configuration=configuration)


# ---------------------------------------------------------------------------
# smoke-compile
# ---------------------------------------------------------------------------


@main.command("smoke-compile")
@click.option(
    "--unity-path",
    default=None,
    help="Path to a Unity editor's Managed directory.",
)
def smoke_compile(unity_path):
    """Compile the package's assemblies against Unity DLLs (no license)."""
    from build_tools.smoke_compile import run_smoke_compile

    sys.exit(run_smoke_compile(unity_path))


# ---------------------------------------------------------------------------
# update-toolchain
# ---------------------------------------------------------------------------


@main.command("update-toolchain")
@click.argument("version")
@click.option("--dry-run", is_flag=True, help="Print actions without writing.")
def update_toolchain(version: str, dry_run: bool):
    """Refresh Plugins DLLs and the version pin from a pinned sharpy release."""
    from build_tools.update_toolchain import run_update_toolchain

    run_update_toolchain(REPO_ROOT, version, dry_run, log)


# ---------------------------------------------------------------------------
# clean
# ---------------------------------------------------------------------------


@main.command()
def clean():
    """Remove temporary build artifacts."""
    removed = []

    tmp_dir = REPO_ROOT / ".tmp"
    if tmp_dir.exists():
        shutil.rmtree(tmp_dir)
        removed.append(".tmp/")

    claude_tmp = REPO_ROOT / ".claude" / "tmp"
    if claude_tmp.exists():
        shutil.rmtree(claude_tmp)
        removed.append(".claude/tmp/")

    if removed:
        log.info("Removed: %s", ", ".join(removed))
    else:
        log.info("Nothing to clean.")


# ---------------------------------------------------------------------------
# info
# ---------------------------------------------------------------------------


@main.command()
def info():
    """Show package and environment info."""
    import json

    pkg_json = REPO_ROOT / "package.json"
    if pkg_json.exists():
        pkg = json.loads(pkg_json.read_text())
        click.echo(f"Package: {pkg.get('name', 'unknown')}")
        click.echo(f"Version: {pkg.get('version', 'unknown')}")
        click.echo(f"Unity:   {pkg.get('unity', 'unknown')}")
    else:
        click.echo("No package.json found.")

    click.echo(f"Repo:    {REPO_ROOT}")
    click.echo(f"Sharpy:  {SHARPY_REPO} ({'found' if SHARPY_REPO.exists() else 'NOT FOUND'})")

    for rid in TARGET_RIDS:
        binary = "sharpyc.exe" if "win" in rid else "sharpyc"
        path = BINARIES_DIR / rid / binary
        status = "bundled" if path.exists() else "missing"
        click.echo(f"  {rid}: {status}")

    core_dll = PLUGINS_DIR / "Sharpy.Core.dll"
    click.echo(f"Sharpy.Core.dll: {'bundled' if core_dll.exists() else 'missing'}")


if __name__ == "__main__":
    main()
