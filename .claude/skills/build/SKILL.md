---
name: build
description: Compile every asmdef against Unity's DLLs (license-free smoke compile) with smart output truncation
---

Compile the package's C# the way Unity would, without a Unity license: `python3 -m build_tools smoke-compile` compiles each assembly definition (`Sharpy.Unity.Runtime`, `Sharpy.Unity.Editor`, `Sharpy.Unity.Editor.Tests`) separately with csc against the installed editor's managed DLLs — the same check as CI's `smoke` job. Output is truncated to avoid token overload while the full log is kept for investigation.

## Output Format

- On success: Shows "BUILD SUCCEEDED" + last 10 lines
- On failure: Shows "BUILD FAILED" + last 100 lines + points to full log

## Steps

Run each step as a separate Bash call:

1. Run `mkdir -p .claude/tmp` to ensure log directory exists.
2. Clear the old log with `rm -f .claude/tmp/last-build.log`.
3. Run: `python3 -m build_tools smoke-compile $ARGUMENTS > .claude/tmp/last-build.log 2>&1` (pass `--unity-path <Editor/Data/Managed dir>` through `$ARGUMENTS`, or set `UNITY_MANAGED_DIR`, if no Unity Hub editor is auto-detected).
4. Check exit code:
   - Exit 0: Print "=== BUILD SUCCEEDED ===" then `tail -10 .claude/tmp/last-build.log`
   - Exit non-zero: Print "=== BUILD FAILED (last 100 lines) ===" then `tail -100 .claude/tmp/last-build.log`, then echo "=== Full log: .claude/tmp/last-build.log ==="
5. If the log says no Unity install was found, report that — do not claim the build passed.

Smoke-compile checks compilation only; running the Editor tests still needs the Unity Test Runner (CI's licensed `test` job).
