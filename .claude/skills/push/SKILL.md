---
name: push
description: Push current branch to remote with safety checks
---

Push the current branch to the remote after verifying it's safe to do so.

**Usage:** `/push`

## Steps

### 1. Check state

Run these in parallel:
- `git status` — check for uncommitted changes
- `git branch --show-current` — get current branch name
- `git log @{u}..HEAD --oneline 2>/dev/null` — check unpushed commits (may fail if no upstream)

### 2. Warn if needed

- If there are uncommitted changes, warn the user and suggest `/commit` first
- If on `mainline` branch with force-push, refuse and explain the risk
- If no unpushed commits, report "Already up to date" and stop

### 3. Push

```bash
git push -u origin <branch>
```

### 4. Report

Show the pushed commits and remote URL.
