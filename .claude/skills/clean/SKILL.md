---
name: clean
description: Clean temporary files and build artifacts
disable-model-invocation: true
---

Clean temporary files, logs, and build artifacts from the working directory.

**Usage:** `/clean`

## Steps

1. Remove Claude tmp files: `rm -rf .claude/tmp/`
2. Remove any .NET build artifacts: `find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -exec rm -rf {} + 2>/dev/null`
3. Print "=== CLEANUP COMPLETE ===" with summary of what was removed.
