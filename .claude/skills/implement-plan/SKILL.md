---
name: implement-plan
description: Implement a plan with coordinated agents
argument-hint: "<path/to/plan.md> [--exclude \"section1,section2\"]"
---

Implement a verified plan using coordinated agents. Reads the plan, decomposes it into ordered commit-sized tasks, spawns appropriate agents, and lands incremental commits whose new tests have been shown to fail when the guarded code is broken.

## Argument Handling

Parse `$ARGUMENTS` for:
1. **Plan path** — the first argument (a file path ending in `.md`)
2. **--exclude flag** — optional, comma-separated list of section names to skip

Plans live in `.claude/plans/` (repo-local, gitignored). Plans created before 2026-10-03 remain in `$HOME/.claude/plans/`. If `$ARGUMENTS` is empty, do **not** pick silently — list the three newest across both directories and ask which to implement:
```bash
ls -t .claude/plans/*.md "$HOME"/.claude/plans/*.md 2>/dev/null | head -3
```

## Pre-Implementation Checklist

Before spawning any agents, perform these checks yourself:

### 1. Read the plan file completely

Note its **Issues to Close** rows and any **Adversarial Review** section. Record the plan's **base sha** — the commit it was verified against, or the commit before its first implementation commit. Every scope check below uses `<base>..HEAD`, never the branch-vs-`mainline` range.

### 2. Check for verification stamp
- Look for `<!-- Verified by /verify-plan` at the top
- If **absent**: warn the user "This plan has not been verified. Consider running `/verify-plan` first." Ask whether to proceed or stop.
- If stamp says **NEEDS REVISION**: stop and tell the user "This plan was flagged as needing revision. Please address the issues in the Verification Summary before implementing."
- If stamp says **PASS** or **PASS WITH CORRECTIONS**: proceed

### 3. Report git status
- Run `git status --short`. If there are uncommitted changes, REPORT them to the user and ask whether to proceed — do **not** stash, restore, or clean; the tree may hold a peer's work.

### 4. Check for partially-completed work
- Run `git log --oneline <base>..HEAD` and `git diff <base>...HEAD --stat` to see what the plan has already landed
- Note existing commits per phase so agents don't duplicate work; mark done items in the checklist (§Task Decomposition)

### 5. Establish baseline
- Run `PYTHONPATH=Tools~ python3 -m build_tools smoke-compile` and `PYTHONPATH=Tools~ python3 -m build_tools format --check` — if either fails before starting, stop and report the error. Record the result as `PASS @ <sha>`.
- Read all files in `Editor/` to understand current implementations
- Read `unity-plugin.md` for the design spec context
- Read `CLAUDE.md` for conventions

## Coordination

Team/task-board tools (`TeamCreate`, `TaskCreate`, `TaskUpdate`, `TaskList`) exist only in some harnesses. **Check this session's tool list first.**
- **Present** → create tasks with `TaskCreate`, assign via `TaskUpdate`, monitor via `TaskList`.
- **Absent** → coordinate with `Agent` (background) + `SendMessage`, and keep the task checklist **in the plan file itself**: add a `## Implementation Checklist` section (one line per task: `- [ ] <task> — <owner> — blocked by <tasks>`) that the lead updates as tasks land. The plan file is the board.

Never run more than one agent that edits the same files.

## Task Decomposition

Break the plan into commit-sized tasks (via `TaskCreate` or the plan-file checklist). Follow these rules:

1. **Ordering**: Tasks should follow dependency order — data models before consumers, settings before UI, compiler bridge before postprocessor
2. **Dependencies**: enforce ordering (`addBlockedBy` or the checklist's `blocked by`)
3. **Granularity**: Each task should be one logical commit (e.g., "Add JSON diagnostic parsing to SharpyCompilerBridge", "Add hash-based skip logic to SharpyAssetPostprocessor")
4. **Test tasks**: Create test tasks alongside or immediately after each implementation task, not all at the end; each includes its **mutation step** (§Guard Delivery Rule)
5. **Excluded sections**: Skip any sections listed in the `--exclude` flag
6. **Final tasks**: Always create a "Run final verification" task blocked by all implementation tasks

## Implementation Workflow

Assign tasks (via `TaskUpdate` `owner`, or by naming the owner on the checklist line) and monitor (via `TaskList`, or by re-reading the checklist and the agents' reports). After **each agent wave**: run `git diff --stat` and `git status --short` and compare against the wave's declared scope. A working-tree delta nobody claims is a finding — ask before touching it.

### Agent Instructions

Each agent receives these instructions along with their specific task:

```
You are implementing part of a plan for the sharpy-unity Unity package.

CRITICAL RULES:
- This is a UPM package, NOT a Unity project — code can't be tested with Unity directly
- C# style: 4-space indent, Allman braces, no #nullable enable
- Using directives go INSIDE the namespace block; write `global::Unity.CodeEditor` (see CLAUDE.md › Conventions)
- Namespace: Sharpy.Unity.Editor for editor code, Sharpy.Unity.Runtime for runtime code
- Unity minimum version: 2022.3 LTS (C# 9.0, netstandard2.1 compatible)
- No Unity-incompatible APIs (check Unity docs if unsure)
- Reference unity-plugin.md for design decisions and architecture
- Reference ../sharpy for compiler interface details (CLI flags, diagnostic JSON format)

SHARED TREE:
The working tree is shared with other agents. Never run `git checkout`, `git restore`,
`git clean`, `git stash`, `git reset`, or `rm` on repository paths. REPORT `git status`; do not
"make it clean". Stage with explicit per-file pathspecs and check `git diff --cached --stat`
before committing; never `git add -A` or `git add .`. Restore a mutation test from the copy you
made (`cp`), never from git.

WORKFLOW:
1. Read the plan section for your task
2. Read existing code patterns in the files you're modifying
3. Write tests first or alongside implementation (not after)
4. Implement the changes
5. Run `PYTHONPATH=Tools~ python3 -m build_tools smoke-compile` — it must pass
6. Guard delivery (if your task adds a test): make a copy of the production file (`cp`), break
   the guarded behavior (invert the predicate or revert the production hunk), run the test — it
   must go RED; restore from the copy; run again — GREEN. Record both in the commit body:
   "mutation: broken → red, restored → green". The Editor tests need the Unity Test Runner (a
   local Unity editor or CI); if neither is available, say so in the commit body
   ("mutation: not run — no Test Runner; <why the assertion fails on the broken code>") rather
   than claiming red/green. If the test cannot fail when broken, do NOT ship it as a guard —
   report it to the lead. An absence assertion needs a positive control.
7. Stage ONLY the specific files you changed by explicit pathspec; check `git diff --cached --stat`
8. Commit with a descriptive conventional commit message; use the commit trailer(s) the harness
   provides for this session
9. Report: what landed (commit hash), mutation outcomes, `git status` (mark your task completed
   via TaskUpdate if the harness has it)
```

### Gap Discovery

During implementation, if agents discover:
- **Missing upstream features**: Note them for the `sharpy` repo (e.g., missing `--namespace` flag)
- **Unity API concerns**: Flag APIs that may behave differently across Unity versions
- **Design spec deviations**: If the plan diverges from `unity-plugin.md`, document why

### Guard Delivery Rule

Any task that adds a test or guard ships with its **mutation step** and reports it in the commit body. Restore via the `cp` copy, never via git. A test that stays green when the guarded code is broken is a **finding**, not a deliverable.

### Incremental Commits

After each task is completed by an agent:
1. Verify the agent staged only relevant files (`git show --stat HEAD`)
2. The commit message should reference the plan section, e.g.: `feat: add JSON diagnostic parsing (plan step 4)`
3. Use the commit trailer(s) the harness provides for the session; never hard-code a model name
4. Update the checklist (or the task board)

## Final Verification

After all implementation tasks are complete:

1. `PYTHONPATH=Tools~ python3 -m build_tools smoke-compile` and `PYTHONPATH=Tools~ python3 -m build_tools format --check` — must pass; compare against the baseline
2. Read every changed file to verify correctness
3. Verify assembly definition references are correct
4. `git diff --stat` and `git status --short` — every delta is claimed by a commit or an agent report
5. Check for any files that should have been created but weren't

## Report

Present a summary report to the user:

```markdown
## Implementation Summary

**Plan:** [plan file path]
**Branch:** [current branch] · **Scope:** [base sha]..[HEAD sha] · **Commits:** [count]

### What Was Done
- (list each completed task with commit hash)

### Tests mutation-checked
- N fail-when-broken · M vacuous → [fixed in <hash> / reported]

### What Was Deferred
- (list any items deferred with reasons)

### Upstream Changes Needed
- (list any changes needed in the sharpy repo)

### Files Changed
(output of `git diff <base>...HEAD --stat`)

### Next Steps
- (suggest what to do next — e.g., "Run the Unity Test Runner in CI", "Test in a Unity project")
```
