#!/usr/bin/env bash
# Auto-format C# files after Edit/Write.
# Called by Claude Code PostToolUse hook with $TOOL_INPUT_FILE_PATH set.
set -euo pipefail

FILE="${TOOL_INPUT_FILE_PATH:-}"
[ -z "$FILE" ] && exit 0

case "$FILE" in
    *.cs)
        # Ensure LF line endings and trailing newline
        if command -v sed &>/dev/null; then
            sed -i '' 's/\r$//' "$FILE" 2>/dev/null || true
        fi
        # If dotnet format is available, use it for whitespace formatting
        if command -v dotnet &>/dev/null; then
            dotnet format whitespace --include "$FILE" 2>/dev/null || true
        fi
        ;;
    build_tools/*.py)
        if command -v ruff &>/dev/null; then
            ruff format --quiet "$FILE" 2>/dev/null || true
        elif command -v black &>/dev/null; then
            black --quiet "$FILE" 2>/dev/null || true
        fi
        ;;
esac
