"""Refresh the bundled Sharpy toolchain from a pinned GitHub release.

Downloads sharpy-core-netstandard2.1.zip for the requested release, mirrors
its DLLs into Plugins/Sharpy.Core/, rewrites the version pin in
Editor/SharpyToolchain.cs, and records the change in CHANGELOG.md.

Idempotent: a second run against the same version makes no changes.
"""

import datetime
import io
import re
import sys
import urllib.request
import zipfile
from pathlib import Path

CORE_ZIP_URL = (
    "https://github.com/antonsynd/sharpy/releases/download/"
    "v{version}/sharpy-core-netstandard2.1.zip"
)

TOOLCHAIN_CS_RELPATH = Path("Editor") / "SharpyToolchain.cs"
PLUGINS_RELPATH = Path("Plugins") / "Sharpy.Core"
CHANGELOG_RELPATH = Path("CHANGELOG.md")

VERSION_PATTERN = re.compile(r'(public const string Version = ")([^"]+)(";)')
UNRELEASED_PATTERN = re.compile(r"^## \[Unreleased\]\s*$", re.MULTILINE)


def run_update_toolchain(repo_root: Path, version: str, dry_run: bool, log) -> None:
    """Execute the refresh. Exits non-zero on any failure."""
    version = version.lstrip("v")

    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        log.error("Version must be a bare semver (e.g. 0.16.1), got: %s", version)
        sys.exit(1)

    plugins_dir = repo_root / PLUGINS_RELPATH
    toolchain_cs = repo_root / TOOLCHAIN_CS_RELPATH
    changelog = repo_root / CHANGELOG_RELPATH

    old_version = _read_pinned_version(toolchain_cs, log)

    url = CORE_ZIP_URL.format(version=version)
    log.info("Downloading %s", url)

    try:
        with urllib.request.urlopen(url) as response:
            zip_data = response.read()
    except Exception as ex:
        log.error("Download failed: %s", ex)
        sys.exit(1)

    with zipfile.ZipFile(io.BytesIO(zip_data)) as zf:
        # The zip also carries Sharpy.Core.pdb/.xml/.deps.json; only the DLLs
        # belong in the Unity plugin folder.
        zip_dlls = {
            Path(name).name: zf.read(name)
            for name in zf.namelist()
            if name.endswith(".dll")
        }

    if not zip_dlls:
        log.error("No DLLs found in %s — refusing to wipe %s", url, plugins_dir)
        sys.exit(1)

    existing_dlls = {p.name for p in plugins_dir.glob("*.dll")} if plugins_dir.exists() else set()

    actions = []  # (dll name, "added" | "updated" | "unchanged" | "deleted")

    for name in sorted(zip_dlls):
        dest = plugins_dir / name
        if not dest.exists():
            actions.append((name, "added"))
        elif dest.read_bytes() != zip_dlls[name]:
            actions.append((name, "updated"))
        else:
            actions.append((name, "unchanged"))

    for name in sorted(existing_dlls - set(zip_dlls)):
        actions.append((name, "deleted"))

    dll_changes = [(n, s) for n, s in actions if s != "unchanged"]
    pin_change = old_version != version

    for name, status in actions:
        log.info("  %s: %s", name, status)

    if pin_change:
        log.info("  %s: %s -> %s", TOOLCHAIN_CS_RELPATH, old_version, version)

    if not dll_changes and not pin_change:
        log.info("Already up to date at %s — nothing to do.", version)
        return

    if dry_run:
        log.info("Dry run — no files written.")
        return

    plugins_dir.mkdir(parents=True, exist_ok=True)

    for name, status in dll_changes:
        dest = plugins_dir / name
        if status == "deleted":
            dest.unlink()
            # A .meta whose asset is gone would trigger a Unity warning, so it
            # goes too; .meta files of surviving DLLs are never touched.
            meta = plugins_dir / (name + ".meta")
            if meta.exists():
                meta.unlink()
        else:
            dest.write_bytes(zip_dlls[name])

    if pin_change:
        content = toolchain_cs.read_text()
        content = VERSION_PATTERN.sub(rf"\g<1>{version}\g<3>", content, count=1)
        toolchain_cs.write_text(content)

    _prepend_changelog_entry(changelog, old_version, version, dll_changes, log)

    log.info("Toolchain refreshed to %s (%d DLL change(s)).", version, len(dll_changes))


def _read_pinned_version(toolchain_cs: Path, log) -> str:
    if not toolchain_cs.exists():
        log.error("%s not found.", toolchain_cs)
        sys.exit(1)

    matches = VERSION_PATTERN.findall(toolchain_cs.read_text())

    if len(matches) != 1:
        log.error(
            "Expected exactly one Version constant in %s, found %d.",
            toolchain_cs,
            len(matches),
        )
        sys.exit(1)

    return matches[0][1]


def _prepend_changelog_entry(changelog: Path, old_version: str, version: str,
                             dll_changes, log) -> None:
    today = datetime.date.today().isoformat()
    lines = [f"- Sharpy toolchain: {old_version} -> {version} ({today})"]
    lines += [f"  - {name}: {status}" for name, status in dll_changes]
    entry = "\n".join(lines)

    if not changelog.exists():
        changelog.write_text(
            f"# Changelog\n\n## [Unreleased]\n\n### Changed\n{entry}\n"
        )
        return

    content = changelog.read_text()
    match = UNRELEASED_PATTERN.search(content)

    if match is None:
        log.error("No '## [Unreleased]' section in %s — add one first.", changelog)
        sys.exit(1)

    insert_at = match.end()
    content = content[:insert_at] + f"\n\n### Changed\n{entry}" + content[insert_at:]
    changelog.write_text(content)
