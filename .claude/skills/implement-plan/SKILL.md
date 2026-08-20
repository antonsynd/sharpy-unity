---
name: implement-plan
description: Implement a plan with coordinated agents
argument-hint: "<path/to/plan.md> [--exclude \"section1,section2\"]"
---

Implement a verified plan using coordinated agents. Reads the plan, decomposes it into ordered tasks, spawns appropriate agents, and implements with incremental commits.

## Argument Handling

Parse `$ARGUMENTS` for:
1. **Plan path** — the first argument (a file path ending in `.md`)
2. **--exclude flag** — optional, comma-separated list of section names to skip

If `$ARGUMENTS` is empty, find the most recently modified `.md` file in `~/.claude/plans/`:
```bash
ls -t ~/.claude/plans/*.md | head -1
```

## Pre-Implementation Checklist

Before spawning any agents, perform these checks yourself:

### 1. Read the plan file completely

### 2. Check for verification stamp
- Look for `<!-- Verified by /verify-plan` at the top
- If **absent**: warn the user "This plan has not been verified. Consider running `/verify-plan` first." Ask whether to proceed or stop.
- If stamp says **NEEDS REVISION**: stop and tell the user "This plan was flagged as needing revision. Please address the issues in the Verification Summary before implementing."
- If stamp says **PASS** or **PASS WITH CORRECTIONS**: proceed

### 3. Check git status
- Run `git status` — if there are uncommitted changes, warn the user and ask whether to proceed or stash first

### 4. Check for partially-completed work
- Run `git diff mainline...HEAD --stat` to see what's already been changed on this branch
- If there are existing changes, note them so agents don't duplicate work

### 5. Establish baseline
- Run the `/build` skill to validate the current scripts — if validation fails before starting, stop and report the error
- Read all files in `Editor/` to understand current implementations
- Read `unity-plugin.md` for the design spec context
- Read `CLAUDE.md` for conventions

## Task Decomposition

Break the plan into commit-sized tasks using `TaskCreate`. Follow these rules:

1. **Ordering**: Tasks should follow dependency order — data models before consumers, settings before UI, compiler bridge before postprocessor
2. **Dependencies**: Use `addBlockedBy` to enforce ordering
3. **Granularity**: Each task should be one logical commit (e.g., "Add JSON diagnostic parsing to SharpyCompilerBridge", "Add hash-based skip logic to SharpyAssetPostprocessor")
4. **Test tasks**: Create test tasks alongside or immediately after each implementation task, not all at the end
5. **Excluded sections**: Skip any sections listed in the `--exclude` flag
6. **Final tasks**: Always create a "Run final verification" task blocked by all implementation tasks

## Implementation Workflow

Assign tasks to agents via `TaskUpdate` with the `owner` field. Monitor progress via `TaskList`.

### Agent Instructions

Each agent receives these instructions along with their specific task:

```
You are implementing part of a plan for the sharpy-unity Unity package.

CRITICAL RULES:
- This is a UPM package, NOT a Unity project — code can't be tested with Unity directly
- C# style: 4-space indent, Allman braces, no #nullable enable
- Namespace: Sharpy.Unity.Editor for editor code, Sharpy.Unity.Runtime for runtime code
- Unity minimum version: 2022.3 LTS (C# 9.0, netstandard2.1 compatible)
- No Unity-incompatible APIs (check Unity docs if unsure)
- Reference unity-plugin.md for design decisions and architecture
- Reference ../sharpy for compiler interface details (CLI flags, diagnostic JSON format)

WORKFLOW:
1. Read the plan section for your task
2. Read existing code patterns in the files you're modifying
3. Implement the changes
4. Stage ONLY the specific files you changed (never `git add -A` or `git add .`)
5. Commit with a descriptive conventional commit message
6. Mark your task as completed via TaskUpdate
```

### Gap Discovery

During implementation, if agents discover:
- **Missing upstream features**: Note them for the `sharpy` repo (e.g., missing `--namespace` flag)
- **Unity API concerns**: Flag APIs that may behave differently across Unity versions
- **Design spec deviations**: If the plan diverges from `unity-plugin.md`, document why

### Incremental Commits

After each task is completed by an agent:
1. Verify the agent staged only relevant files
2. The commit message should reference the plan section, e.g.: `feat: add JSON diagnostic parsing (plan step 4)`
3. Include `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` in the commit

## Final Verification

After all implementation tasks are complete:

1. Read every changed file to verify correctness
2. Check that all C# files have consistent formatting (4-space indent, Allman braces, LF line endings)
3. Verify assembly definition references are correct
4. `git diff --stat` — summarize all changes made
5. Check for any files that should have been created but weren't

## Report

Present a summary report to the user:

```markdown
## Implementation Summary

**Plan:** [plan file path]
**Branch:** [current branch]
**Commits:** [count]

### What Was Done
- (list each completed task with commit hash)

### What Was Deferred
- (list any items deferred with reasons)

### Upstream Changes Needed
- (list any changes needed in the sharpy repo)

### Files Changed
(output of `git diff mainline...HEAD --stat`)

### Next Steps
- (suggest what to do next — e.g., "Bundle compiler binaries", "Test in a Unity project")
```
