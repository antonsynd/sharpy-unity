---
name: push
description: Push current branch to remote with safety checks
argument-hint: "[--close-issues 123,456]"
---

Push the current branch to the remote after verifying it's safe to do so, and optionally close GitHub issues.

**Usage:**
- `/push` — push current branch
- `/push --close-issues 5,6` — push and close the specified issues

## Argument Handling

Parse `$ARGUMENTS` for:
- `--close-issues` — comma-separated list of issue numbers to close after pushing

## Steps

### 1. Pre-flight checks

Run these in parallel:
- `git status` — check for uncommitted changes
- `git branch --show-current` — get current branch name
- `git log @{u}..HEAD --oneline 2>/dev/null || echo "no upstream"` — show commits that will be pushed

### 2. Warn if needed

- If there are uncommitted changes, warn the user and suggest `/commit` first
- If the branch is `mainline`, **warn the user** that they're about to push directly to the main branch and confirm before proceeding
- If on `mainline` branch with force-push, refuse and explain the risk
- If no unpushed commits, report "Already up to date" and stop

### 3. Push

```bash
git push -u origin <branch>
```

If the push fails due to diverged history, **do not force push**. Instead, report the error and suggest `git pull --rebase` or ask the user how to proceed.

### 4. Close issues (if requested)

If `--close-issues` was provided, close each issue:

```bash
gh issue close <number> --reason completed
```

Report which issues were closed.

### 5. Report

Show:
- Branch name pushed
- Number of commits pushed
- Remote URL
- Any issues closed
