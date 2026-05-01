---
name: close-issues
description: Close GitHub issues that have been implemented, with verification
argument-hint: "<plan.md or issue numbers: 123,456>"
---

Close GitHub issues after verifying their implementation is complete. Accepts either a plan file path (to extract referenced issues) or a comma-separated list of issue numbers.

**Usage:**
- `/close-issues 5,6` — verify and close specific issues
- `/close-issues plans/my-plan.md` — find and close issues referenced in a plan

## Argument Handling

Parse `$ARGUMENTS` to determine input mode:

- **Comma-separated numbers** (e.g., `5,6`): treat as explicit issue list
- **File path** (e.g., `plans/foo.md`): read the file and extract issue numbers from `#NNN` references
- **Empty**: ask the user which issues to close

## Steps

### 1. Gather issue list

If a plan file was provided:
- Read the plan file
- Extract all `#NNN` issue references (regex: `#(\d+)`)
- Deduplicate and present the list to the user for confirmation before proceeding

If explicit issue numbers were provided, use them directly.

### 2. Fetch issue details

For each issue:
```bash
gh issue view <number> --json title,body,state,labels
```

Skip any issues that are already closed — report them as "already closed" in the final summary.

### 3. Verify implementation

For each open issue, verify the fix exists in the codebase:

1. Read the issue title and body to understand what was requested
2. Search for relevant commits: `git log --oneline --all --grep="#<number>"`
3. If no commit references the issue, search by keywords: `git log --oneline -20 --grep="<keyword>"`
4. Use Grep/Glob to spot-check that the described change actually exists in the codebase

Categorize each issue as:
- **VERIFIED** — implementation found
- **PARTIAL** — some work done but gaps remain
- **NOT FOUND** — no evidence of implementation

### 4. Close verified issues

For each VERIFIED issue:
```bash
gh issue close <number> --reason completed --comment "Closed — implemented in $(git log --oneline --all --grep='#<number>' | head -1 | cut -d' ' -f1). Verified in codebase."
```

### 5. Handle unverified issues

For PARTIAL issues:
- Do **not** close
- Add a comment noting what was implemented and what remains

For NOT FOUND issues:
- Do **not** close
- Report to the user

### 6. Report

Present a summary table:

```
| Issue | Title | Status | Action |
|-------|-------|--------|--------|
| #5    | ...   | VERIFIED | Closed |
| #6    | ...   | PARTIAL  | Commented |
```
