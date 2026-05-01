---
name: verify-plan
description: Verify a plan for accuracy against the codebase and design spec
argument-hint: "<path/to/plan.md>"
---

Verify a plan file for accuracy against the actual sharpy-unity codebase and the `unity-plugin.md` design spec. Reads the plan, extracts every verifiable claim, checks each against the codebase, and edits the plan directly to fix inaccuracies. Adds a verification stamp at the top.

## Argument Handling

If `$ARGUMENTS` is non-empty, use it as the path to the plan file.

If `$ARGUMENTS` is empty, find the most recently modified `.md` file in `~/.claude/plans/` using:
```bash
ls -t ~/.claude/plans/*.md | head -1
```

Read the plan file completely before proceeding.

## Verification Dimensions

Check each dimension in order. For each claim found, verify it against the actual codebase using Glob, Grep, and Read tools.

### 1. Structural Accuracy

Verify every concrete reference in the plan:
- **File paths**: Use Glob to confirm every referenced file/directory exists
- **Class/method names**: Use Grep to confirm they exist where claimed
- **Unity API references**: Verify the Unity APIs mentioned are correct (e.g., `AssetPostprocessor`, `ScriptableSingleton`, `SettingsProvider`)
- **sharpyc CLI flags**: Cross-reference against the sharpy repo at `../sharpy/src/Sharpy.Cli/` for actual flag names

Flag as error: any path, name, or API that doesn't exist. Fix inline if the correct reference can be determined.

### 2. Consistency with Design Spec

Check the plan follows `unity-plugin.md` design decisions:
- **Transpile-then-compile**: Not custom compiler injection
- **AssetPostprocessor**: Not ScriptedImporter (unless there's a good reason documented)
- **Generated files in configurable output path**: Default `Assets/SharpyGenerated/`
- **Sharpy.Core.dll as plugin**: Runtime dependency, not editor-only
- **sharpyc as editor-only**: Platform-specific binaries in `Editor/Binaries/`
- **Assembly definitions**: Correct platform constraints and references
- **C# compatibility**: Code must work with Unity 2022.3+ (C# 9.0, netstandard2.1)

Flag as warning: any design spec deviation. Add a note explaining the correct approach.

### 3. Consistency with Sharpy Compiler

If the plan references compiler behavior:
- **CLI flags**: Verify against `../sharpy/src/Sharpy.Cli/` source
- **Diagnostics JSON format**: Verify against actual `emit diagnostics --format json` output structure
- **Exit codes**: Confirm 0 = success, 1 = errors
- **Sharpy.Core API**: Verify class/method names against `../sharpy/src/Sharpy.Core/`

Flag as error: incorrect compiler interface assumptions.

### 4. Completeness

Check that nothing is missing:
- **All affected files listed**: If changing behavior, all impacted editor scripts should be mentioned
- **Tests specified**: Every implementation change should have corresponding test additions
- **Settings integration**: New features should integrate with `SharpySettings` where appropriate
- **Error handling**: Plan should address what happens when sharpyc fails, times out, or is missing
- **Platform handling**: Changes to binary invocation should consider macOS/Windows/Linux

Flag as warning: missing steps. Add them as suggestions in the verification summary.

## Output

After verification, edit the plan file directly:

### 1. Add verification stamp at the very top of the file

```markdown
<!-- Verified by /verify-plan on YYYY-MM-DD -->
<!-- Verification result: [PASS / PASS WITH CORRECTIONS / NEEDS REVISION] -->
```

### 2. Add a Verification Summary section at the end

```markdown
## Verification Summary

**Result:** [PASS / PASS WITH CORRECTIONS / NEEDS REVISION]
**Verified on:** YYYY-MM-DD
**Plan file:** [path]

### Corrections Made
- (list each inline correction with before/after)

### Warnings
- (list each warning with explanation)

### Missing Steps Added
- (list suggestions for missing steps)

### Unchecked Claims
- (list any claims that couldn't be verified, with reason)
```

### 3. Fix errors inline

For each error found, edit the plan text directly to correct it. Mark corrections with `[CORRECTED: reason]` so the author can review changes.

Present a brief summary to the user after editing is complete.
