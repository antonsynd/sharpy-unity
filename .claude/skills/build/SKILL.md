---
name: build
description: Validate C# scripts compile by running a standalone syntax check
---

Validate that the package's C# scripts have no syntax errors. Since this is a UPM package (not a Unity project), full compilation requires Unity. This skill runs a lightweight validation using `dotnet build` against a validation project if available, or falls back to Roslyn syntax checking.

**Usage:** `/build`

**Behavior:**
- On success: Shows "VALIDATION PASSED" + summary
- On failure: Shows "VALIDATION FAILED" + last 100 lines + points to full log

**Log location:** `.claude/tmp/last-build.log`

## Steps

1. Run `mkdir -p .claude/tmp` to ensure log directory exists.
2. Clear the old log with `rm -f .claude/tmp/last-build.log`.
3. Collect all `.cs` files in `Editor/`, `Runtime/`, and `Tests/Editor/` and verify they exist.
4. If a `Validation~/Validation.csproj` exists, run: `dotnet build Validation~/Validation.csproj > .claude/tmp/last-build.log 2>&1`
5. If no validation project exists, use the Read tool to read each `.cs` file and check for obvious issues (unbalanced braces, missing semicolons, duplicate class names).
6. Check result:
   - Pass: Print "=== VALIDATION PASSED ===" then `tail -10 .claude/tmp/last-build.log`
   - Fail: Print "=== VALIDATION FAILED (last 100 lines) ===" then `tail -100 .claude/tmp/last-build.log`, then echo "=== Full log: .claude/tmp/last-build.log ==="
7. Remind that full compilation requires opening the package in a Unity project.
