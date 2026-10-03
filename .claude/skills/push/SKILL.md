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

### 2.5. Pre-push gates

Catch what CI would fail on **before** pushing. Pick the gates from the files the outgoing commits touch (`git diff --name-only @{u}..HEAD`, or `origin/mainline..HEAD` when there is no upstream):

| Commits touch | Run |
|---------------|-----|
| any `.cs` or `.asmdef` file, or `Plugins/` | `python3 -m build_tools smoke-compile` (CI's license-free `smoke` job — compiles every asmdef against Unity's DLLs; needs a local Unity editor install or `--unity-path`) |
| any `.cs` file | `python3 -m build_tools format --check` — whole repo, not just the files you touched: a file someone else created slips through per-file formatting |
| `package.json` | `python3 -c "import json; json.load(open('package.json'))"` (CI's `validate` job) |
| `.github/workflows/*.yml` | `python3 -c "import yaml,sys; [yaml.safe_load(open(f)) for f in sys.argv[1:]]" .github/workflows/*.yml` |

- If a gate fails, stop: fix it (for formatting run `python3 -m build_tools format`), commit the fix with `/commit`, and re-run the gate.
- If smoke-compile cannot find a Unity install, say so and ask whether to push anyway — never report it as passed.

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
