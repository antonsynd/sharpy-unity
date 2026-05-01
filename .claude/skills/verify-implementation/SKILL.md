---
name: verify-implementation
description: Verify completed plan implementation, fix gaps, and commit fixes
argument-hint: "<path/to/plan.md>"
---

Verify that a plan has been fully and correctly implemented. Reads the plan, checks every step against the actual codebase, audits multiple dimensions, fixes any gaps found, and commits all fixes.

## Argument Handling

If `$ARGUMENTS` is non-empty, use it as the path to the plan file.

If `$ARGUMENTS` is empty, find the most recently modified `.md` file in `~/.claude/plans/`:
```bash
ls -t ~/.claude/plans/*.md 2>/dev/null | head -1
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
2. **Stage specific files**: never use `git add -A` or `git add .`
3. **Incremental commits**: group related fixes into logical commits:
   - `fix: complete missing implementation for [plan step X]`
   - `fix: correct compiler interface assumptions`
   - `chore: fix convention violations from plan implementation`
   - `test: add test stubs for [feature]`
4. **Include co-author**: all commits must include `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>`

## Final Verification

After all fixes are committed:

1. Read every `.cs` file that was changed to verify correctness
2. Verify assembly definitions are consistent (references, platform constraints)
3. Check `package.json` is valid JSON
4. `git diff mainline...HEAD --stat` — summarize all changes
5. Verify no files were accidentally deleted or left empty

If issues persist after 3 remediation loops, report them as unresolved.

## Report

Present the verification report to the user:

```markdown
## Implementation Verification Report

**Plan:** [plan file path]
**Branch:** [current branch]
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

(output of `git diff mainline...HEAD --stat`)
```
