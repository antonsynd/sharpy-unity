# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository

- **GitHub owner:** `antonsynd`
- **GitHub repo:** `antonsynd/sharpy-unity`
- **Related repo:** `antonsynd/sharpy` (compiler + stdlib, at `../sharpy`)

## What This Is

A Unity Package Manager (UPM) package (`com.antonsynd.sharpy`) that integrates the Sharpy programming language into the Unity Editor. It auto-compiles `.spy` files to C# via the `sharpyc` CLI, then lets Unity compile the generated C# normally.

## Package Structure

This is a UPM package, **not** a Unity project. It follows [Unity package layout conventions](https://docs.unity3d.com/Manual/cus-layout.html):

```
Editor/                      # Editor-only C# (Sharpy.Unity.Editor assembly)
  *.cs                       # Project compiler, sync, settings, menus, installers
Runtime/                     # Runtime C# (Sharpy.Unity.Runtime assembly)
Plugins/Sharpy.Core/         # Sharpy.Core.dll (netstandard2.1, runtime dependency)
Tests/Editor/                # Unity Test Runner tests (editor mode)
Samples~/BasicSetup/         # UPM importable sample
Documentation~/              # Package documentation (hidden from Unity)
package.json                 # UPM manifest
Tools~/build_tools/          # Python CLI (in a `~` folder so Unity skips it): DLL/compiler bundling, smoke-compile, toolchain updates
.github/workflows/           # CI (unity-ci.yml), release (release.yml), weekly pin check (toolchain-pin.yml)
```

## Key Design Decisions

1. **Transpile-then-compile** — generate C# and let Unity's pipeline handle it, rather than injecting IL
2. **Whole-project compilation** — every `.spy` under `Assets/` is one `sharpyc project` build (per-file emit duplicated imported modules and disagreed on namespaces). All triggers (postprocessor, menu, settings button, focus, `SharpyBatch`) go through `SharpyProjectCompiler.Compile()`
3. **Stage, then sync** — sharpyc writes to `Library/Sharpy/emit`; only exit code 0 syncs into the generated folder, so a failed build never touches `Assets/`
4. **Generated files in `Assets/SharpyGenerated/`** — mirrors source structure, gitignored; MonoBehaviour/ScriptableObject scripts are named after their class, and every `.meta` GUID is derived from the `.spy` GUID (`SharpyGeneratedMeta`) so scene references survive clean regenerates and clones
5. **Imports rooted at `Assets/`** — the spyproj sets `<SourceRoot>../../Assets</SourceRoot>` (honoured from sharpy 0.22.0); relative imports are recommended
6. **References derived, not configured** — `SharpyReferenceProvider` takes Assembly-CSharp's references minus what sharpyc cannot load (sharpy#2182); nothing machine-specific is serialized
7. **Sharpy.Core.dll as a plugin** — netstandard2.1 build, runtime dependency
8. **sharpyc downloaded, not bundled** — `SharpyBinaryDownloader` fetches the self-contained per-RID sharpyc from the sharpy GitHub release pinned in `SharpyToolchain.Version`, installing it under the project's `Library/` — never inside the package (the archive holds ~350 DLLs Unity would try to import)

## Architecture

```
.spy imported/moved/deleted (SharpyAssetPostprocessor) · Recompile All · editor load/focus with a stale fingerprint (SharpySettingsReloader) · SharpyBatch.GenerateAll
  → SharpyProjectCompiler.Compile()
    → SharpySettings.RefreshFromDiskIfChanged()
    → SharpyProjectFile.Build() → Library/Sharpy/unity.spyproj   (SharpyReferenceProvider supplies references)
    → SharpyFingerprint: up to date? → done
    → SharpyCompilerBridge.CompileProject()
      → sharpyc project Library/Sharpy/unity.spyproj --emit-cs-to Library/Sharpy/emit
      → SharpyDiagnosticParser → SharpyDiagnosticLog (Console, double-click opens the .spy)
    → exit 0 only:
      → SharpyScriptClasses + SharpyGeneratedFolderManager: class-named paths
      → SharpyLineDirectives.Rewrite (span → classic #line, absolute paths)
      → SharpyGeneratedMeta.GuidFor (GUID from the .spy GUID)
      → SharpyGeneratedSync.Sync → Assets/SharpyGenerated/ (meta before .cs, stale files removed)
      → Library/Sharpy/fingerprint; stdlib warnings (SharpyStdlibDetector)
      → AssetDatabase.Refresh() → Unity compiles the generated C#
```

### Key Classes

| Class | File | Purpose |
|-------|------|---------|
| `SharpyProjectCompiler` | `Editor/SharpyProjectCompiler.cs` | The one compile path: spyproj → sharpyc → staging → sync; fingerprint check |
| `SharpyProjectFile` | `Editor/SharpyProjectFile.cs` | Builds the `.spyproj` text (pure, deterministic) |
| `SharpyCompilerBridge` | `Editor/SharpyCompilerBridge.cs` | Runs `sharpyc project`, captures output, parses diagnostics |
| `SharpyDiagnosticParser` | `Editor/SharpyDiagnosticParser.cs` | Parses sharpyc's rendered diagnostics (errors on stderr, warnings on stdout) |
| `SharpyDiagnosticLog` | `Editor/SharpyDiagnosticLog.cs` | Logs diagnostics so double-click opens the `.spy` line |
| `SharpyDiagnostic` | `Editor/SharpyDiagnostic.cs` | One parsed diagnostic |
| `SharpyReferenceProvider` | `Editor/SharpyReferenceProvider.cs` | Derives Unity references; denylist and sharpy#2182 exclusions |
| `SharpyGeneratedOwnership` | `Editor/SharpyGeneratedOwnership.cs` | Validates the Generated Output Path and claims the folder via a marker `.gitignore`; retires the old folder when the path changes |
| `SharpyGeneratedSync` | `Editor/SharpyGeneratedSync.cs` | Makes the generated folder hold exactly the staged scripts (file system only) |
| `SharpyScriptClasses` | `Editor/SharpyScriptClasses.cs` | Finds the MonoBehaviour/ScriptableObject class a script must be named after |
| `SharpyGeneratedMeta` | `Editor/SharpyGeneratedMeta.cs` | Deterministic `.meta` GUID and text from the `.spy` GUID |
| `SharpyLineDirectives` | `Editor/SharpyLineDirectives.cs` | Rewrites span `#line` directives to the C# 9 form (or strips them) |
| `SharpyFingerprint` | `Editor/SharpyFingerprint.cs` | Hash of all compile inputs, persisted in `Library/Sharpy/fingerprint` |
| `SharpyAssetPostprocessor` | `Editor/SharpyAssetPostprocessor.cs` | Detects .spy changes, triggers one project compile |
| `SharpySettingsReloader` | `Editor/SharpySettingsReloader.cs` | On editor load/focus: reload settings from disk, compile if stale |
| `SharpyGeneratedFolderManager` | `Editor/SharpyGeneratedFolderManager.cs` | Generated folder lifecycle and `.spy` ↔ generated path mapping |
| `SharpySettings` | `Editor/SharpySettings.cs` | Project-level settings (ScriptableSingleton), reloaded when edited on disk |
| `SharpySettingsProvider` | `Editor/SharpySettingsProvider.cs` | Settings UI in Project Settings window |
| `SharpyMenuItems` | `Editor/SharpyMenuItems.cs` | Recompile All, Clean Generated (generated files only, plus `Library/Sharpy/`), View Generated C# |
| `SharpyBatch` | `Editor/SharpyBatch.cs` | `-executeMethod` entry point for headless regeneration (CI) |
| `SharpyStdlibInstaller` | `Editor/SharpyStdlibInstaller.cs` | Opt-in Sharpy.Stdlib install into `Assets/Plugins/Sharpy.Stdlib/`, version check |
| `SharpyStdlibDetector` / `SharpyStdlibModules` | `Editor/SharpyStdlibDetector.cs`, `Editor/SharpyStdlibModules.cs` | Warn when generated code needs stdlib modules that are not installed |
| `SharpyFileHandler` | `Editor/SharpyFileHandler.cs` | Opens .spy files in external editor |
| `SharpyToolchain` | `Editor/SharpyToolchain.cs` | Pinned toolchain version, release URLs, platform RID |
| `SharpyBinaryDownloader` | `Editor/SharpyBinaryDownloader.cs` | Auto-installs pinned sharpyc into `Library/` on editor load |
| `SharpyFileInspector` | `Editor/SharpyFileInspector.cs` | Custom inspector for `.spy` assets |

## Assembly Definitions

- `Sharpy.Unity.Editor` — editor-only, references Runtime + Sharpy.Core.dll
- `Sharpy.Unity.Runtime` — all platforms, references Sharpy.Core.dll
- `Sharpy.Unity.Editor.Tests` — editor-only, test assembly

## Conventions

- **Using directives go INSIDE the namespace block** in every package `.cs` file. Sharpy.Core declares 150+ root-namespace types (`Sharpy.List`, `Sharpy.Path`, `Sharpy.Math`, ...) that shadow BCL names from inside `Sharpy.Unity.*` namespaces; inner-scope usings win. `Unity.CodeEditor` must be written `global::Unity.CodeEditor` (leftmost `Unity` otherwise resolves to `Sharpy.Unity`).
- C# style follows the sharpy project: 4-space indent, Allman braces, `LangVersion 9.0` (netstandard2.1 compatible)
- No `#nullable enable` in Unity scripts (Unity's serialization doesn't support it well)
- Namespace: `Sharpy.Unity.Editor` for editor code, `Sharpy.Unity.Runtime` for runtime code
- Unity minimum version: 2022.3 LTS

## Operational Contracts

- **Parallel agents share one working tree.** Never run `git checkout` / `restore` / `clean` / `stash` / `reset` / `rm` on repo paths to tidy up — REPORT `git status` instead. Stage by explicit per-file pathspec (never `git add -A`/`.`) and check `git diff --cached --stat` before committing. Spell these prohibitions out in agent prompts; "read-only" alone is not enough.
- **Plans live in `.claude/plans/`** (repo-local, gitignored): `/create-plan` writes there and `/verify-plan`, `/implement-plan`, `/verify-implementation` read there. Plans created before 2026-10-03 remain in `~/.claude/plans/`; pass the path explicitly for those.
- **Tests are falsifiable** — every new test is mutation-checked: break the code it guards → red, restore (from a `cp` copy, never git) → green, both recorded in the commit body. A test that passes either way is a finding, not coverage; absence assertions need a positive control.
- **Commit trailers** come from the harness for the session — never hard-code a model name in skills or messages.
- Use `/commit` and `/push`; `/push` runs the pre-push gates (smoke-compile, format check, manifest/workflow validation) that CI enforces.
- **Releasing = a version bump reaching `mainline`.** Bump `package.json` `version` (and `SharpyUnityRuntime.Version`), rename CHANGELOG's `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD`, push to `mainline`. Once Unity CI passes there, `release.yml` tags `vX.Y.Z` on the tested commit and creates the GitHub release from that CHANGELOG section. Never tag by hand; an already-tagged version is skipped.

## Build Tools

```bash
PYTHONPATH=Tools~ python3 -m build_tools info                          # Package/toolchain status
PYTHONPATH=Tools~ python3 -m build_tools bundle-all                    # Build Sharpy.Core DLLs + sharpyc from ../sharpy
PYTHONPATH=Tools~ python3 -m build_tools update-toolchain <version>    # Re-pin to a sharpy release (bumps SharpyToolchain.Version + Plugins DLLs)
PYTHONPATH=Tools~ python3 -m build_tools smoke-compile                 # csc-compile all asmdefs against Unity DLLs — no Unity license needed
PYTHONPATH=Tools~ python3 -m build_tools format [--check]              # Normalize .cs line endings/EOF newlines
PYTHONPATH=Tools~ python3 -m build_tools smoke-sample                  # Pinned sharpyc compiles Samples~/BasicSetup, Unity's csc builds the output
PYTHONPATH=Tools~ python3 -m build_tools check-metas                   # Every imported asset has a tracked .meta; no orphans
PYTHONPATH=Tools~ python3 -m build_tools check-pin                     # Pin trails the latest sharpy release by <= 2 minors (weekly CI)
```

sharpyc and `Plugins/Sharpy.Core/*.dll` versions must move together — always update via `update-toolchain`, never by hand.

## Compiler Interface

The plugin invokes `sharpyc` via `Process.Start` (with `NO_COLOR=1`, UTF-8 output):

```bash
sharpyc project Library/Sharpy/unity.spyproj --emit-cs-to Library/Sharpy/emit   # Every compile
sharpyc --version                                                              # Fingerprint, settings page
```

Exit codes: 0 = success (the only one that syncs), 1 = Sharpy errors, 2 = the generated C# does not compile (may leave files in staging; it is cleared before each run), 3 = internal compiler error. `project` has no JSON diagnostics: they are rendered rustc-style (`error[SPY0200]: ...` then `--> /abs/path.spy:line:col`), errors on stderr after `Build FAILED.`, warnings on stdout on success and failure. `--emit-cs-to` must mirror the source tree and `<SourceRoot>` must be honoured — both need sharpy 0.22.0 or newer.

## Testing

Tests run via Unity Test Runner in editor mode. Test assembly: `Sharpy.Unity.Editor.Tests`.

No `dotnet test` — this is a Unity package, not a .NET solution.

CI (`.github/workflows/unity-ci.yml`): package.json validation → license-free smoke compile (csc against the unityci editor image's DLLs) and sample compile (pinned sharpyc + the editor's csc) → licensed Unity Test Runner (requires `UNITY_EMAIL`/`UNITY_PASSWORD`/`UNITY_SERIAL` secrets). Run the smoke compile locally with `PYTHONPATH=Tools~ python3 -m build_tools smoke-compile`.
