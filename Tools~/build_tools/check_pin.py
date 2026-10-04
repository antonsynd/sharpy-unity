"""Flag a toolchain pin that has fallen behind the latest sharpy release.

Compares SharpyToolchain.Version with the latest published (non-draft,
non-prerelease) sharpy GitHub release and fails when the pin trails it by
more than MAX_MINOR_LAG minor versions, or by a major version. Run weekly
by .github/workflows/toolchain-pin.yml rather than on PRs, so an upstream
release never breaks unrelated work. Re-pin with `update-toolchain`.

Uses only the standard library so CI can run it without pip:
    PYTHONPATH=Tools~ python3 -m build_tools.check_pin [--latest X.Y.Z]
"""

import argparse
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

from build_tools.update_toolchain import TOOLCHAIN_CS_RELPATH, VERSION_PATTERN

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

LATEST_RELEASE_API = "https://api.github.com/repos/antonsynd/sharpy/releases/latest"

MAX_MINOR_LAG = 2


def parse_version(text):
    """'v0.21.0' or '0.21.0' -> (0, 21, 0); ValueError otherwise."""
    match = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", text.strip())

    if match is None:
        raise ValueError(f"not a release version: {text!r}")

    return tuple(int(n) for n in match.groups())


def is_pin_stale(pinned, latest, max_minor_lag=MAX_MINOR_LAG):
    """True when `pinned` trails `latest` by a major or > max_minor_lag minors.

    A pin at or ahead of the latest release is never stale.
    """
    if latest[0] != pinned[0]:
        return latest[0] > pinned[0]

    return latest[1] - pinned[1] > max_minor_lag


def read_pinned_version(repo_root):
    matches = VERSION_PATTERN.findall((repo_root / TOOLCHAIN_CS_RELPATH).read_text())

    if len(matches) != 1:
        raise ValueError(
            f"expected one Version constant in {TOOLCHAIN_CS_RELPATH}, found {len(matches)}")

    return matches[0][1]


def fetch_latest_release_tag():
    """Tag of the latest published sharpy release, via the GitHub API."""
    request = urllib.request.Request(
        LATEST_RELEASE_API, headers={"Accept": "application/vnd.github+json"})
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")

    if token:
        request.add_header("Authorization", f"Bearer {token}")

    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)["tag_name"]


def run_check_pin(repo_root=REPO_ROOT, latest=None):
    """Compare the pin with `latest` (fetched when None). Returns an exit code."""
    try:
        pinned_text = read_pinned_version(repo_root)
        pinned = parse_version(pinned_text)
        latest_text = latest or fetch_latest_release_tag()
        latest_version = parse_version(latest_text)
    except Exception as ex:
        print(f"check-pin: error: {ex}", file=sys.stderr)
        return 1

    print(f"Pinned sharpy: {pinned_text}")
    print(f"Latest sharpy: {latest_text}")

    if is_pin_stale(pinned, latest_version):
        print(
            f"check-pin: FAIL — the pin trails the latest release by a major "
            f"version or more than {MAX_MINOR_LAG} minor versions. Re-pin with: "
            f"PYTHONPATH=Tools~ python3 -m build_tools update-toolchain "
            f"{'.'.join(str(n) for n in latest_version)}",
            file=sys.stderr,
        )
        return 1

    print("check-pin: PASS")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Fail when the toolchain pin trails the latest sharpy release.")
    parser.add_argument(
        "--latest",
        help="Compare against this version instead of fetching the latest release",
    )
    args = parser.parse_args(argv)
    return run_check_pin(latest=args.latest)


if __name__ == "__main__":
    sys.exit(main())
