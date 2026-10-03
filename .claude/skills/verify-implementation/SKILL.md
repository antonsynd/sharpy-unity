---
name: verify-implementation
description: Verify completed plan implementation, fix gaps, and commit fixes
argument-hint: "<path/to/plan.md>"
---

Verify that a plan has been fully and correctly implemented. Reads the plan, checks every step against the actual codebase, audits multiple dimensions, fixes any gaps found, and commits all fixes.

## Argument Handling

If `$ARGUMENTS` is non-empty, use it as the path to the plan file.

Plans live in `.claude/plans/` (repo-local, gitignored); plans created before 2026-10-03 remain in `$HOME/.claude/plans/`. If `$ARGUMENTS` is empty, do **not** pick silently — list the three newest across both directories and ask which to verify:
```bash
ls -t .claude/plans/*.md "$HOME"/.claude/plans/*.md 2>/dev/null | head -3
```

If no plan file is found, ask the user to provide the plan path explicitly.

Read the plan file completely before proceeding.

## Pre-Verification Checklist

### 1. Validate plan file

- Confirm the file exists and is readable
- Check for the `/verify-plan` stamp — search for `<!-- Verified by /verify-plan`
  - If **absent**: warn the user but proceed
  - If present and **NEEDS REVISION**: warn and note in final report
  - If **PASS** or **PASS WITH CORRECTIONS**: proceed normally
- Check for implementation evidence via `git log --oneline`
- Record the plan's **base sha** — the commit it was verified against, or the commit before its first implementation commit. Every scope check below uses `<base>..HEAD`, never `mainline...HEAD`.
- Run `git status --short` and REPORT any uncommitted changes — do **not** stash, restore, reset, or clean; the tree may hold a peer's work

### 2. Identify the plan's scope

Extract from the plan:
- Every **file path** mentioned (files that should have been created or modified)
- Every **step/task** described
- Every **class, method, or property** introduced
- Every **Unity API** referenced (AssetPostprocessor, ScriptableSingleton, SettingsProvider, etc.)
- Every **compiler interface detail** (CLI flags, diagnostic fields, exit codes)

Build a **completeness checklist** — a structured list of every deliverable the plan describes.

## Verification Dimensions

### 1. Completeness Audit

For EVERY item in the checklist:
1. Use Glob to verify referenced files exist
2. Use Grep to verify referenced classes, methods, properties exist
3. Use Read to verify the implementation matches what the plan describes (not just that a file exists, but that the content is correct)

Report each item as:
- **DONE** — fully implemented as described
- **PARTIAL** — partially implemented (describe what's missing)
- **MISSING** — not implemented at all
- **DIVERGED** — implemented differently than planned (describe the divergence)

### 2. Structural Review

Check the changed files against project conventions:

**UPM Package Conventions:**
- Assembly definitions have correct platform constraints
- Editor-only code is in `Editor/` with `includePlatforms: ["Editor"]`
- Runtime code in `Runtime/` has no editor-only API usage
- `package.json` version matches if it was supposed to change
- No Unity-incompatible APIs in runtime code

**C# Conventions:**
- 4-space indent, Allman braces
- No `#nullable enable`
- Namespace: `Sharpy.Unity.Editor` or `Sharpy.Unity.Runtime`
- No string interpolation with `$` in contexts where `string.Format` would be safer for Unity compatibility
- Properties with backing fields use `[SerializeField]` where appropriate

**Design Spec Alignment:**
- Check against `unity-plugin.md` design decisions
- Transpile-then-compile approach maintained
- AssetPostprocessor (not ScriptedImporter) used for .spy detection
- Generated files go to configurable output path
- Sharpy.Core.dll treated as runtime dependency
- sharpyc treated as editor-only binary

### 3. Compiler Interface Accuracy

If the plan references compiler behavior:
- **CLI flags**: Verify against `../sharpy/src/Sharpy.Cli/` source (check actual command definitions)
- **Diagnostics JSON format**: Cross-reference with actual `emit diagnostics` output structure
- **Exit codes**: Confirm 0 = success, 1 = errors
- **Sharpy.Core API**: Verify class/method names against `../sharpy/src/Sharpy.Core/`

### 4. Code Quality

For each changed file:
- No dead code, commented-out code, or debug leftovers
- No TODO/FIXME comments without context
- No magic numbers or strings that should be constants
- No copy-paste duplication
- Error handling covers edge cases (missing binary, timeout, malformed JSON)
- Platform handling considers macOS/Windows/Linux where relevant

### 5. Test Coverage

- Check if test stubs exist in `Tests/Editor/` for new functionality
- Verify test assembly references are correct
- Flag any testable logic that has no corresponding test
- **Tests must be falsifiable**: for each new test, check the commit body records its mutation step (guarded code broken → red, restored → green). Where it is missing, do it: `cp` the production file, break the guarded behavior, run the test (Unity Test Runner) — it must fail — then restore from the copy. If no Test Runner is available, read the assertion against the broken code and say so. A test that cannot fail when its subject is broken is a finding, not coverage; an absence assertion needs a positive control.

## Remediation Phase

Address every issue found:

| Category | Action |
|----------|--------|
| MISSING implementation | Implement it |
| PARTIAL implementation | Complete it |
| Convention violation | Fix the code |
| Missing tests | Write test stubs |
| Compiler interface error | Fix to match actual CLI |
| Dead code / debug leftovers | Remove them |
| Formatting issues | Fix indentation, line endings, braces |

### Remediation Rules

1. **Fix in priority order**: missing implementations > convention violations > missing tests > formatting
2. **Stage specific files** by explicit pathspec and check `git diff --cached --stat`: never use `git add -A` or `git add .`, and never `git checkout`/`restore`/`stash`/`reset`/`clean` to tidy the tree
3. **Incremental commits**: group related fixes into logical commits:
   - `fix: complete missing implementation for [plan step X]`
   - `fix: correct compiler interface assumptions`
   - `chore: fix convention violations from plan implementation`
   - `test: add test stubs for [feature]`
4. **Commit trailers**: use the trailer(s) the harness provides for this session; never hard-code a model name

## Final Verification

After all fixes are committed:

1. `python3 -m build_tools smoke-compile` and `python3 -m build_tools format --check` — must pass
2. Read every `.cs` file that was changed to verify correctness
3. Verify assembly definitions are consistent (references, platform constraints)
4. Check `package.json` is valid JSON
5. `git diff <base>...HEAD --stat` — summarize all changes
6. Verify no files were accidentally deleted or left empty

If issues persist after 3 remediation loops, report them as unresolved.

## Report

Present the verification report to the user:

```markdown
## Implementation Verification Report

**Plan:** [plan file path]
**Branch:** [current branch] · **Scope:** [base sha]..[HEAD sha]
**Verified on:** YYYY-MM-DD

### Completeness

| Status | Count |
|--------|-------|
| Fully implemented | N |
| Was partial (now fixed) | N |
| Was missing (now fixed) | N |
| Diverged from plan (acceptable) | N |
| Unresolved | N |

### Structural Review

- **Convention violations found:** N (N fixed)
- **Design spec deviations:** N
- **Compiler interface errors:** N (N fixed)

### Code Quality

- **Critical issues found:** N (N fixed)
- **Warnings found:** N (N fixed)

### Fixes Applied

| Commit | Description | Category |
|--------|-------------|----------|
| abc1234 | ... | missing-impl / convention / compiler-interface / test / cleanup |

### Unresolved Items

(List any items that could not be fixed, with explanation)

### Files Changed (total, including plan implementation + fixes)

(output of `git diff <base>...HEAD --stat`)
```
