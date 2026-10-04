"""Check that every asset Unity imports from the package has a tracked .meta.

An immutable install (git URL, registry tarball) cannot have .meta files
generated on the fly: Unity logs "has no meta file, but it's in an
immutable folder" and skips the asset. So every file and folder Unity
imports must ship its .meta, and the GUIDs must be the committed ones.

Unity's import skips any file or folder whose name starts with '.', ends
with '~', is 'cvs', or has the '.tmp' extension, along with everything
beneath a skipped folder. The rules here mirror that:

- every tracked file outside a skipped path, and every folder above one,
  needs a tracked `<path>.meta` (the package root itself needs none);
- every tracked .meta must belong to such an asset. Metas under a skipped
  folder are left alone: Samples~ content is copied into Assets/ on
  import and may carry its own metas.

Works on git's index (`git ls-files`), not the working tree, so it checks
what a git-URL install will actually see.

Uses only the standard library so CI can run it without pip:
    PYTHONPATH=Tools~ python3 -m build_tools.check_metas
"""

import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

META_SUFFIX = ".meta"


def is_skipped_name(name):
    """True when Unity's importer ignores a file or folder with this name."""
    return (
        name.startswith(".")
        or name.endswith("~")
        or name.lower() == "cvs"
        or name.lower().endswith(".tmp")
    )


def find_meta_problems(tracked_paths):
    """Return (missing, orphaned) for a list of repo-relative POSIX paths.

    missing: imported assets (files and folders) with no tracked .meta.
    orphaned: tracked .meta files whose asset Unity does not import.
    """
    tracked = set(tracked_paths)
    metas = {p for p in tracked if p.endswith(META_SUFFIX)}
    assets = tracked - metas

    imported = set()

    for path in assets:
        parts = path.split("/")

        if any(is_skipped_name(part) for part in parts):
            continue

        for depth in range(1, len(parts) + 1):
            imported.add("/".join(parts[:depth]))

    missing = sorted(a for a in imported if a + META_SUFFIX not in metas)

    orphaned = []

    for meta in sorted(metas):
        folders = meta.split("/")[:-1]

        if any(is_skipped_name(folder) for folder in folders):
            continue

        if meta[: -len(META_SUFFIX)] not in imported:
            orphaned.append(meta)

    return missing, orphaned


def git_tracked_paths(repo_root):
    """Paths in git's index, relative to repo_root, POSIX separators."""
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=repo_root,
        check=True,
        capture_output=True,
    )
    return [p for p in result.stdout.decode("utf-8").split("\0") if p]


def run_check_metas(repo_root=REPO_ROOT):
    """Check the index. Returns a process exit code."""
    missing, orphaned = find_meta_problems(git_tracked_paths(repo_root))

    for path in missing:
        print(f"missing .meta: {path} (expected {path}{META_SUFFIX})", file=sys.stderr)

    for meta in orphaned:
        print(f"orphaned .meta: {meta} (no imported asset at {meta[: -len(META_SUFFIX)]})",
              file=sys.stderr)

    if missing or orphaned:
        print(
            f"check-metas: FAIL ({len(missing)} missing, {len(orphaned)} orphaned)",
            file=sys.stderr,
        )
        return 1

    print("check-metas: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(run_check_metas())
