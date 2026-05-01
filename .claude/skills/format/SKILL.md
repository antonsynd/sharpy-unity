---
name: format
description: Format C# code whitespace per project .editorconfig conventions
---

Format C# code whitespace. This repo uses an `.editorconfig` matching the sharpy project conventions (4-space indent, Allman braces, LF line endings).

**Usage:** `/format`

**Behavior:**
- On success: Shows "FORMAT COMPLETE" + output
- On failure: Shows "FORMAT FAILED" + last 50 lines

**Log location:** `.claude/tmp/last-format.log`

## Steps

1. Run `mkdir -p .claude/tmp` to ensure log directory exists.
2. Clear the old log with `rm -f .claude/tmp/last-format.log`.
3. Find all `.cs` files in the package: `find Editor Runtime Tests -name '*.cs' 2>/dev/null`
4. For each file, ensure it has LF line endings and trailing newline. Use `sed` or equivalent.
5. Log results to `.claude/tmp/last-format.log`.
6. Check result:
   - Success: Print "=== FORMAT COMPLETE ===" then `cat .claude/tmp/last-format.log`
   - Failure: Print "=== FORMAT FAILED (last 50 lines) ===" then `tail -50 .claude/tmp/last-format.log`
