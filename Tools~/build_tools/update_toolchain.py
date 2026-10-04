"""Refresh the bundled Sharpy toolchain from a pinned GitHub release.

Downloads sharpy-core-netstandard2.1.zip for the requested release, mirrors
its DLLs into Plugins/Sharpy.Core/, rewrites the version pin in
Editor/SharpyToolchain.cs, regenerates the stdlib type table in
Editor/SharpyStdlibModules.cs from sharpy-stdlib-netstandard2.1.zip, and
records the change in CHANGELOG.md. A DLL the release adds gets a new
.meta (a git-URL install skips any asset without one); existing .meta files
are never rewritten, so GUIDs stay stable. The table needs the .NET 10 SDK
(`dotnet run` of a single-file app).

Idempotent: a second run against the same version makes no changes.
"""

import datetime
import io
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import zipfile
from pathlib import Path

CORE_ZIP_URL = (
    "https://github.com/antonsynd/sharpy/releases/download/"
    "v{version}/sharpy-core-netstandard2.1.zip"
)

STDLIB_ZIP_URL = (
    "https://github.com/antonsynd/sharpy/releases/download/"
    "v{version}/sharpy-stdlib-netstandard2.1.zip"
)

TOOLCHAIN_CS_RELPATH = Path("Editor") / "SharpyToolchain.cs"
PLUGINS_RELPATH = Path("Plugins") / "Sharpy.Core"
CHANGELOG_RELPATH = Path("CHANGELOG.md")
STDLIB_TABLE_RELPATH = Path("Editor") / "SharpyStdlibModules.cs"
STDLIB_TYPES_SCRIPT = Path(__file__).resolve().parent / "stdlib_types.cs"

VERSION_PATTERN = re.compile(r'(public const string Version = ")([^"]+)(";)')
UNRELEASED_PATTERN = re.compile(r"^## \[Unreleased\][ \t]*$", re.MULTILINE)


def run_update_toolchain(repo_root: Path, version: str, dry_run: bool, log) -> None:
    """Execute the refresh. Exits non-zero on any failure."""
    version = version.lstrip("v")

    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        log.error("Version must be a bare semver (e.g. 0.16.1), got: %s", version)
        sys.exit(1)

    plugins_dir = repo_root / PLUGINS_RELPATH
    toolchain_cs = repo_root / TOOLCHAIN_CS_RELPATH
    changelog = repo_root / CHANGELOG_RELPATH
    stdlib_table = repo_root / STDLIB_TABLE_RELPATH

    old_version = _read_pinned_version(toolchain_cs, log)

    url = CORE_ZIP_URL.format(version=version)
    zip_data = _download(url, log)

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

    table_text = _build_stdlib_table(version, zip_dlls.get("Sharpy.Core.dll"), log)
    old_table_text = stdlib_table.read_text() if stdlib_table.exists() else None
    table_change = table_text != old_table_text

    dll_changes = [(n, s) for n, s in actions if s != "unchanged"]
    pin_change = old_version != version

    for name, status in actions:
        log.info("  %s: %s", name, status)

    for name, status in actions:
        if status != "deleted" and not (plugins_dir / (name + ".meta")).exists():
            log.info("  %s.meta: new guid", name)

    if pin_change:
        log.info("  %s: %s -> %s", TOOLCHAIN_CS_RELPATH, old_version, version)

    if table_change:
        log.info("  %s: regenerated", STDLIB_TABLE_RELPATH)

    if not dll_changes and not pin_change and not table_change:
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
            write_dll_meta_if_missing(dest)

    if pin_change:
        content = toolchain_cs.read_text()
        content = VERSION_PATTERN.sub(rf"\g<1>{version}\g<3>", content, count=1)
        toolchain_cs.write_text(content)

    changes = list(dll_changes)

    if table_change:
        stdlib_table.write_text(table_text)
        changes.append((STDLIB_TABLE_RELPATH.as_posix(), "regenerated"))

    _prepend_changelog_entry(changelog, old_version, version, changes, log)

    log.info("Toolchain refreshed to %s (%d DLL change(s)).", version, len(dll_changes))


def dll_meta_text(guid: str) -> str:
    """A plugin DLL's .meta in the minimal form the committed ones use.

    No importer block: Unity applies its PluginImporter defaults (Any
    Platform, Editor included), which is what a netstandard2.1 runtime
    dependency needs.
    """
    return f"fileFormatVersion: 2\nguid: {guid}\n"


def write_dll_meta_if_missing(dll: Path) -> bool:
    """Give `dll` a .meta with a fresh guid unless it has one. True if written."""
    meta = dll.with_name(dll.name + ".meta")

    if meta.exists():
        return False

    meta.write_text(dll_meta_text(uuid.uuid4().hex))
    return True


def _download(url: str, log) -> bytes:
    log.info("Downloading %s", url)

    try:
        with urllib.request.urlopen(url) as response:
            return response.read()
    except Exception as ex:
        log.error("Download failed: %s", ex)
        sys.exit(1)


def _build_stdlib_table(version: str, core_dll, log) -> str:
    """Render Editor/SharpyStdlibModules.cs for the stdlib of `version`.

    stdlib_types.cs reads the release's Sharpy.Stdlib.dll/.pdb and the shipped
    Sharpy.Core.dll and prints "<CLR type name>\t<module>" per stdlib-only type.
    """
    if core_dll is None:
        log.error("Sharpy.Core.dll missing from the core zip — cannot diff the stdlib against it.")
        sys.exit(1)

    dotnet = shutil.which("dotnet")
    if dotnet is None:
        log.error("dotnet (.NET 10 SDK) not found on PATH — needed to regenerate %s.", STDLIB_TABLE_RELPATH)
        sys.exit(1)

    stdlib_zip = _download(STDLIB_ZIP_URL.format(version=version), log)

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        with zipfile.ZipFile(io.BytesIO(stdlib_zip)) as zf:
            for name in ("Sharpy.Stdlib.dll", "Sharpy.Stdlib.pdb"):
                (tmp_dir / name).write_bytes(zf.read(name))
        (tmp_dir / "Sharpy.Core.dll").write_bytes(core_dll)

        result = subprocess.run(
            [dotnet, "run", str(STDLIB_TYPES_SCRIPT), "--",
             str(tmp_dir / "Sharpy.Stdlib.dll"),
             str(tmp_dir / "Sharpy.Stdlib.pdb"),
             str(tmp_dir / "Sharpy.Core.dll")],
            capture_output=True, text=True,
        )

    if result.returncode != 0:
        log.error("stdlib_types.cs failed (exit %d):\n%s%s", result.returncode, result.stdout, result.stderr)
        sys.exit(1)

    rows = [line.split("\t") for line in result.stdout.splitlines() if line.strip()]
    if not rows or any(len(row) != 2 for row in rows):
        log.error("Unexpected stdlib_types.cs output:\n%s", result.stdout)
        sys.exit(1)

    return render_stdlib_table(version, rows, log)


def render_stdlib_table(version: str, rows, log) -> str:
    """C# source of SharpyStdlibModules from (CLR type name, module) rows."""
    types = {}
    for clr_name, module in rows:
        # Generated C# spells Sharpy.Deque`1 as Sharpy.Deque<T>.
        name = re.sub(r"`\d+$", "", clr_name)
        if types.get(name, module) != module:
            log.error("%s maps to both %s and %s.", name, types[name], module)
            sys.exit(1)
        types[name] = module

    # A namespace other than the shared root `Sharpy` whose types all belong to
    # one module stands for that module (e.g. `using global::Sharpy.Statistics;`).
    namespace_modules = {}
    for name, module in types.items():
        namespace = name.rpartition(".")[0]
        if namespace != "Sharpy":
            namespace_modules.setdefault(namespace, set()).add(module)
    namespaces = {ns: mods.pop() for ns, mods in namespace_modules.items() if len(mods) == 1}

    def entries(table):
        return "".join(f'            {{ "{key}", "{table[key]}" }},\n' for key in sorted(table))

    return (
        "// <auto-generated>\n"
        f"// Generated by `python -m build_tools update-toolchain {version}` from Sharpy.Stdlib.dll\n"
        "// in sharpy-stdlib-netstandard2.1.zip (see Tools~/build_tools/stdlib_types.cs).\n"
        "// Do not edit by hand; re-run update-toolchain.\n"
        "// </auto-generated>\n"
        "\n"
        "namespace Sharpy.Unity.Editor\n"
        "{\n"
        "    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win\n"
        "    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.\n"
        "    using System.Collections.Generic;\n"
        "\n"
        "    internal static class SharpyStdlibModules\n"
        "    {\n"
        "        /// <summary>The Sharpy release these tables were generated from.</summary>\n"
        f'        internal const string SourceVersion = "{version}";\n'
        "\n"
        "        /// <summary>\n"
        "        /// Public types of Sharpy.Stdlib.dll that Sharpy.Core.dll does not define, by C# name\n"
        "        /// (generic arity removed), mapped to the Sharpy module that owns each.\n"
        "        /// </summary>\n"
        "        internal static readonly Dictionary<string, string> TypeModules = new Dictionary<string, string>\n"
        "        {\n"
        f"{entries(types)}"
        "        };\n"
        "\n"
        "        /// <summary>Namespaces declared only by Sharpy.Stdlib.dll, mapped to their module.</summary>\n"
        "        internal static readonly Dictionary<string, string> NamespaceModules = new Dictionary<string, string>\n"
        "        {\n"
        f"{entries(namespaces)}"
        "        };\n"
        "    }\n"
        "}\n"
    )


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

    if old_version == version:
        headline = f"- Sharpy toolchain: refreshed at {version} ({today})"
    else:
        headline = f"- Sharpy toolchain: {old_version} -> {version} ({today})"

    lines = [headline]
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
    # Reuse an existing "### Changed" heading directly under [Unreleased]
    # rather than stacking a duplicate heading on every bump.
    changed = re.compile(r"\s*### Changed[ \t]*\n").match(content, insert_at)

    if changed is not None:
        insert_at = changed.end()
        content = content[:insert_at] + f"{entry}\n" + content[insert_at:]
    else:
        content = content[:insert_at] + f"\n\n### Changed\n{entry}" + content[insert_at:]

    changelog.write_text(content)
