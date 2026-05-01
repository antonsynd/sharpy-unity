<!-- Verified by /verify-plan on 2026-04-30 -->
<!-- Verification result: PASS WITH CORRECTIONS -->

# Sharpy Unity Integration — `Sharpy.Unity` Editor Plugin

## Context

Sharpy compiles `.spy` source files to C# via `sharpyc emit csharp`, which Unity can then compile normally. The goal is a Unity Editor plugin that makes `.spy` files "just work" inside a Unity project — edit a `.spy` file, Unity detects the change, runs the Sharpy compiler, and the generated C# is picked up by Unity's normal compilation pipeline.

**Repo name recommendation: `sharpy-unity`** — follows Unity package naming conventions (lowercase-hyphenated). The Unity package itself would be `com.antonsynd.sharpy` (UPM convention). This should be a separate repo because:
- Different release cadence from the compiler
- Unity-specific CI (Unity Test Runner, specific Unity versions)
- Users install it via Unity Package Manager, not NuGet
- Keeps the compiler repo focused

## Current State

- `sharpyc emit csharp <file.spy> -o <output.cs>` works for single files (exit 0 = success, exit 1 = errors)
- `sharpyc emit diagnostics <file.spy> --format json` outputs structured diagnostics as JSON
- `sharpyc project <.spyproj> --emit-cs-to <dir>` compiles multi-file projects
- `Sharpy.Core` multi-targets `net10.0;netstandard2.1` — the `netstandard2.1` build is Unity-compatible
- Generated C# uses `global::Sharpy.Builtins.*`, `Sharpy.List<T>`, etc. — requires `Sharpy.Core.dll` at runtime
- No Unity integration exists today

## Design Decisions

1. **Transpile-then-compile, not custom compiler** — Unity's compilation pipeline is opaque and version-sensitive. Generating C# and letting Unity compile it is far more robust than trying to inject IL or replace the compiler. This also means Sharpy diagnostics appear at edit-time, not buried in Unity's compile errors.

2. **`AssetPostprocessor` over `ScriptedImporter`** — `ScriptedImporter` is designed for non-code assets (models, data). It produces `UnityEngine.Object` assets, not source files that feed into compilation. `AssetPostprocessor` fires on any file import and can trigger C# regeneration + `AssetDatabase.Refresh()`.

3. **Generated files in `Assets/SharpyGenerated/`** — a dedicated directory keeps generated C# separate from hand-written code. A `.gitignore` entry prevents committing generated files. The directory mirrors the source structure (e.g., `Assets/Scripts/player.spy` → `Assets/SharpyGenerated/Scripts/player.cs`).

4. **Ship `Sharpy.Core.dll` as a plugin** — the `netstandard2.1` build of `Sharpy.Core` drops into `Assets/Plugins/Sharpy.Core/`. This is a runtime dependency (not editor-only).

5. **Ship `sharpyc` as an editor-only binary** — the compiler CLI is bundled in `Editor/Binaries/` (platform-specific: macOS, Windows, Linux). Editor-only so it's excluded from builds.

6. **Namespace wrapping for Unity** — generated C# from single-file `emit csharp` has no namespace (multi-file `project` compilation already wraps in `ProjectNamespace` from `.spyproj`). The plugin should pass a configurable namespace to avoid global scope pollution in Unity projects. This requires a new `--namespace` flag on `sharpyc emit csharp` for single-file mode (upstream change in `sharpy` repo). [CORRECTED: multi-file projects already support namespace wrapping via `_context.ProjectNamespace` from `.spyproj` config; only single-file `emit csharp` lacks this]

## Implementation

### Phase 1: Repository Scaffolding

**Goal:** Set up the `sharpy-unity` repo as a valid Unity Package Manager (UPM) package.

#### Tasks

1. **Create repo structure** — `sharpy-unity/`
   - `package.json` (UPM manifest: `com.antonsynd.sharpy`, version, dependencies, Unity version min)
   - `README.md`
   - `LICENSE`
   - `CHANGELOG.md`
   - `Editor/` (editor-only scripts, compiler binaries)
   - `Runtime/` (runtime helpers if needed, Sharpy.Core.dll lives here or in Plugins)
   - `Plugins/Sharpy.Core/` (netstandard2.1 DLL + meta files)
   - `Tests/Editor/` (Unity Test Runner tests)
   - `.gitignore`
   - Acceptance criteria: `sharpy-unity` can be installed via UPM from git URL
   - Commit: `chore: scaffold sharpy-unity UPM package`

2. **Add Sharpy.Core.dll** — `Plugins/Sharpy.Core/`
   - Build `Sharpy.Core` for `netstandard2.1` and copy the DLL
   - Add `.meta` files (Unity requires them)
   - Verify it loads in a test Unity project without errors
   - Add `System.Collections.Immutable.dll` (Sharpy.Core dependency)
   - Add `Microsoft.Bcl.AsyncInterfaces.dll` (Sharpy.Core netstandard2.1 dependency) [CORRECTED: Sharpy.Core.csproj also references Microsoft.Bcl.AsyncInterfaces for netstandard2.1]
   - Acceptance criteria: A Unity script can `using Sharpy;` and call `Builtins.Print()`
   - Commit: `feat: bundle Sharpy.Core.dll for Unity runtime`

3. **Add sharpyc binaries** — `Editor/Binaries/`
   - Publish self-contained `sharpyc` for macOS (arm64, x64), Windows (x64), Linux (x64)
   - Add platform-specific `.meta` files with correct platform filters
   - Mark as Editor-only (exclude from player builds)
   - Acceptance criteria: `SharpyCompilerBridge` can locate and invoke the correct binary for the current OS
   - Commit: `feat: bundle sharpyc compiler binaries (macOS/Windows/Linux)`

### Phase 2: Compiler Bridge

**Goal:** A C# editor class that invokes `sharpyc` and parses results.

#### Tasks

4. **`SharpyCompilerBridge.cs`** — `Editor/SharpyCompilerBridge.cs`
   - Locate the correct platform-specific `sharpyc` binary
   - `CompileFile(string spyPath, string outputCsPath)` — runs `sharpyc emit csharp <spy> -o <cs>`, returns structured result
   - `CompileProject(string spyprojPath, string outputDir)` — runs `sharpyc project <spyproj> --emit-cs-to <dir>`
   - `GetDiagnostics(string spyPath)` — runs `sharpyc emit diagnostics <spy> --format json`, parses JSON
   - Capture stdout, stderr, exit code
   - Parse JSON diagnostics into `SharpyDiagnostic` records (`severity`, `code`, `line`, `column`, `message`)
   - Timeout handling (kill process after configurable duration, default 30s)
   - Acceptance criteria: Unit test calls `CompileFile()` with a valid `.spy` and gets generated C# at the output path; invalid `.spy` returns diagnostics with correct line/column
   - Commit: `feat: add SharpyCompilerBridge for invoking sharpyc from editor`

5. **`SharpyDiagnostic.cs`** — `Editor/SharpyDiagnostic.cs`
   - Data class: `Severity` (Error/Warning/Info), `Code` (string, e.g. "SPY0200"), `Line`, `Column`, `Message`, `FilePath`, `Phase` [CORRECTED: the JSON output from `emit diagnostics --format json` includes a `phase` field (e.g., "Lexer", "Parser", "TypeChecking") that should be captured; `filePath` is NOT in the JSON for single-file, only inferred from context]
   - `ToUnityLogType()` helper for mapping to `LogType.Error/Warning/Log`
   - Acceptance criteria: Diagnostics parse correctly from JSON output
   - Commit: `feat: add SharpyDiagnostic model for structured compiler output`

### Phase 3: File Watcher & Auto-Compilation

**Goal:** Detect `.spy` file changes and auto-compile to C#.

#### Tasks

6. **`SharpyAssetPostprocessor.cs`** — `Editor/SharpyAssetPostprocessor.cs`
   - Subclass `AssetPostprocessor`, override `OnPostprocessAllAssets()`
   - Filter for `.spy` file imports/moves/deletes
   - On create/modify: invoke `SharpyCompilerBridge.CompileFile()`, write `.cs` to mirror path under `Assets/SharpyGenerated/`
   - On delete: delete corresponding generated `.cs` file
   - On move: delete old `.cs`, compile to new mirror path
   - Log Sharpy diagnostics to Unity Console with correct severity and double-click-to-open support (using `Debug.LogError` with context object pointing to the `.spy` file)
   - Skip regeneration if `.spy` file hasn't changed (hash check)
   - Acceptance criteria: Save a `.spy` file in Unity → generated `.cs` appears in `SharpyGenerated/` → Unity compiles it; delete the `.spy` → generated `.cs` is removed
   - Commit: `feat: auto-compile .spy files on import via AssetPostprocessor`

7. **`SharpyGeneratedFolderManager.cs`** — `Editor/SharpyGeneratedFolderManager.cs`
   - Ensure `Assets/SharpyGenerated/` exists with a `.gitignore` containing `*` (don't version generated files)
   - Create directory structure mirroring source `.spy` locations
   - Add `[InitializeOnLoad]` setup to create the folder on first load
   - Clean up empty subdirectories when `.spy` files are deleted
   - Acceptance criteria: Generated folder is created automatically, never committed to git
   - Commit: `feat: manage SharpyGenerated output directory lifecycle`

### Phase 4: Editor UX

**Goal:** Settings, manual controls, and diagnostic display.

#### Tasks

8. **`SharpySettings.cs`** — `Editor/SharpySettings.cs`
   - `ScriptableSingleton<SharpySettings>` stored in `ProjectSettings/`
   - Settings: `generatedOutputPath` (default `Assets/SharpyGenerated`), `compilerTimeoutSeconds` (default 30), `autoCompileOnSave` (default true), `rootNamespace` (default empty), `showLineDirectives` (default false), `additionalModulePaths` (list), `additionalReferences` (list)
   - Acceptance criteria: Settings persist across Unity sessions
   - Commit: `feat: add SharpySettings for project-level configuration`

9. **`SharpySettingsProvider.cs`** — `Editor/SharpySettingsProvider.cs`
   - `SettingsProvider` for `Project/Sharpy` in Unity's Preferences/Project Settings window
   - UI for all settings from `SharpySettings`
   - "Recompile All" button that forces regeneration of all `.spy` files
   - "Locate Compiler" field showing detected `sharpyc` path and version
   - Acceptance criteria: Settings appear in Project Settings → Sharpy, changes take effect immediately
   - Commit: `feat: add Sharpy settings UI in Project Settings window`

10. **`SharpyMenuItems.cs`** — `Editor/SharpyMenuItems.cs`
    - `Assets/Sharpy/Recompile All` — force-recompile every `.spy` in the project
    - `Assets/Sharpy/Recompile Selected` — recompile selected `.spy` file(s)
    - `Assets/Sharpy/Clean Generated` — delete all generated `.cs` files
    - Right-click context menu on `.spy` files: "View Generated C#" (opens the generated `.cs` in the code editor)
    - Acceptance criteria: Menu items work and show progress bar for bulk operations
    - Commit: `feat: add Sharpy editor menu items for manual compilation control`

11. **`.spy` file icon and inspector** — `Editor/SharpyFileInspector.cs`
    - Custom icon for `.spy` files in the Project window (simple snake/python-inspired icon)
    - Custom inspector when a `.spy` file is selected: shows file info, last compile status, diagnostics summary, "Recompile" button
    - Acceptance criteria: `.spy` files have a distinct icon; selecting one shows compile status
    - Commit: `feat: add custom icon and inspector for .spy files`

### Phase 5: Source Mapping & Error Navigation

**Goal:** When Unity shows a compiler error in generated C#, clicking it should navigate to the `.spy` source.

#### Tasks

12. **Enable `#line` directives by default** — `Editor/SharpyCompilerBridge.cs`
    - Pass `--show-line-directives` to `sharpyc emit csharp` so generated C# contains `#line N "file.spy"` directives
    - Unity's C# compiler respects `#line` — errors will reference the `.spy` file and line number
    - When user double-clicks an error, Unity opens the `.spy` file at the correct line (if an external editor is configured for `.spy`)
    - Acceptance criteria: A type error in generated C# shows the `.spy` filename and line in Unity Console; double-click opens the `.spy` source
    - Commit: `feat: enable #line directives for source-mapped error navigation`

13. **File association for `.spy`** — `Editor/SharpyFileHandler.cs`
    - Register `.spy` as a text file type in Unity so double-clicking opens it in the configured external editor (VS Code, Rider, etc.)
    - Use `[OnOpenAsset]` callback to handle `.spy` file opens
    - Acceptance criteria: Double-clicking a `.spy` file in the Project window opens it in the external code editor
    - Commit: `feat: register .spy files for external editor opening`

### Phase 6: Upstream Compiler Changes (in `sharpy` repo)

**Goal:** Add features to the Sharpy compiler that improve Unity integration.

#### Tasks

14. **`--namespace` flag for `emit csharp`** — `src/Sharpy.Cli/` and `src/Sharpy.Compiler/`
    - Add `--namespace <ns>` option to `emit csharp` command
    - When provided, wrap generated classes in `namespace <ns> { ... }`
    - Default: no namespace (current behavior, backwards-compatible)
    - Acceptance criteria: `sharpyc emit csharp --namespace Game.Scripts file.spy` produces C# wrapped in `namespace Game.Scripts { ... }`
    - Commit: `feat(cli): add --namespace flag for generated C# namespace wrapping`

15. **`--suppress-entry-point` flag** — `src/Sharpy.Cli/` and `src/Sharpy.Compiler/`
    - **Note:** `emit csharp` already has `--type library` (`-t library`) which sets `OutputType = "library"` and suppresses `Main()` generation. Consider whether this existing flag is sufficient for Unity's needs, or if a separate `--suppress-entry-point` is still needed for finer control (e.g., exe-mode compilation without a `Main()` method). [CORRECTED: `--type library` already exists on `emit csharp` and suppresses entry point requirements via `CompilerOptions.OutputType`]
    - Module-level code (outside functions/classes) could be emitted as an `[InitializeOnLoadMethod]` or `[RuntimeInitializeOnLoadMethod]` annotated method instead — or just warned against
    - Acceptance criteria: `sharpyc emit csharp --type library file.spy` produces C# without `static void Main()` (already works); evaluate if additional flag is needed
    - Commit: `feat(cli): add --suppress-entry-point flag for library-mode emission` (if needed beyond `--type library`)

### Phase 7: Documentation & CI

**Goal:** Docs, sample project, and CI for the Unity package.

#### Tasks

16. **Sample Unity project** — `Samples~/BasicSetup/`
    - UPM samples directory with a minimal Unity project
    - A few `.spy` files demonstrating: a MonoBehaviour written in Sharpy, using Sharpy stdlib in Unity, multi-file imports
    - `README.md` with setup instructions
    - Acceptance criteria: User can import the sample from Package Manager and see it work
    - Commit: `docs: add sample Unity project with Sharpy integration`

17. **Documentation** — `Documentation~/`
    - Installation guide (UPM from git URL, from disk, from registry)
    - Configuration guide (settings, namespace, module paths)
    - Workflow guide (editing `.spy` files, viewing generated C#, debugging)
    - Limitations and known issues
    - Acceptance criteria: A new user can follow the docs to set up Sharpy in a Unity project
    - Commit: `docs: add installation, configuration, and workflow guides`

18. **CI workflow** — `.github/workflows/unity-ci.yml`
    - Use GameCI or Unity Builder action
    - Test against Unity 2022 LTS + Unity 6
    - Run Unity Test Runner for editor tests
    - Validate that sample project compiles
    - Acceptance criteria: CI passes on PR; tests run on supported Unity versions
    - Commit: `ci: add Unity CI workflow with GameCI`

## Testing Strategy

### Unit Tests (Unity Test Runner — Editor mode)
- `SharpyCompilerBridgeTests`: invoke compiler with valid/invalid `.spy`, verify results and diagnostics
- `SharpyDiagnosticTests`: parse JSON diagnostics, verify severity/code/line mapping
- `SharpyGeneratedFolderManagerTests`: create/delete/move file mirroring
- `SharpySettingsTests`: settings persistence and defaults

### Integration Tests
- End-to-end: create `.spy` file → verify `.cs` generated → verify Unity compiles it
- Delete `.spy` → verify `.cs` cleaned up
- Modify `.spy` with error → verify diagnostics logged, no `.cs` written
- Multi-file project: imports between `.spy` files resolve correctly
- `#line` directives: verify error points to `.spy` source line

### Edge Cases
- `.spy` file with syntax errors (should show diagnostics, not crash)
- `.spy` file importing another `.spy` file (multi-file compilation)
- Renaming/moving `.spy` files (old generated files cleaned up)
- Large project (100+ `.spy` files) — performance of bulk recompilation
- Platform-specific: verify correct `sharpyc` binary selected on macOS/Windows/Linux
- Unity domain reload: verify state survives assembly reload
- `.spy` file outside `Assets/` (should be ignored)

### Negative Tests
- Missing `sharpyc` binary → clear error in Console
- `Sharpy.Core.dll` missing → clear error at compile time
- Circular imports between `.spy` files → compiler error surfaces correctly
- `.spy` file with same name as existing `.cs` file → warning about name collision

## Upstream Changes Required (in `sharpy` repo)

These changes should be implemented in the `sharpy` repo before or alongside the Unity plugin:

- **Phase 6, Task 14**: `--namespace` flag (new CLI option + codegen support)
- **Phase 6, Task 15**: `--suppress-entry-point` flag — may be unnecessary if `--type library` suffices (already exists)
- **Sharpy.Core NuGet/DLL publishing**: Automated build that produces standalone `netstandard2.1` DLL artifacts for bundling

## Repo Naming Recommendation

**`sharpy-unity`** — matches the pattern of other Unity integration packages (e.g., `firebase-unity`, `steamworks-unity`). The UPM package name would be `com.antonsynd.sharpy`.

## Issues to Close

No existing GitHub issues — this is a new initiative. Consider creating:
- `sharpy` repo: Issue for `--namespace` flag
- `sharpy` repo: Issue for `--suppress-entry-point` flag  
- `sharpy-unity` repo: Tracking issue for the full integration

## Verification Summary

**Result:** PASS WITH CORRECTIONS
**Verified on:** 2026-04-30
**Plan file:** ~/.claude/plans/plan-8ae0d3.md

### Corrections Made

1. **Task 2 — Missing dependency**: Added `Microsoft.Bcl.AsyncInterfaces.dll` to the list of DLLs to bundle. `Sharpy.Core.csproj` references both `System.Collections.Immutable` and `Microsoft.Bcl.AsyncInterfaces` for the `netstandard2.1` target.

2. **Task 5 — SharpyDiagnostic model incomplete**: The JSON output from `emit diagnostics --format json` includes a `phase` field (values like `"Lexer"`, `"Parser"`, `"TypeChecking"`, etc.) that was missing from the proposed data class. Also noted that `filePath` is not present in the JSON output for single-file compilation — it would need to be supplied by the caller.

3. **Design Decision 6 — Namespace claim corrected**: The plan stated "generated C# currently has no namespace" — this is only true for single-file `emit csharp`. Multi-file projects (`sharpyc project`) already wrap generated C# in the `ProjectNamespace` from the `.spyproj` file via `RoslynEmitter.CompilationUnit.cs`.

4. **Task 15 — `--suppress-entry-point` partially redundant**: `emit csharp` already has `--type library` (`-t library`) which sets `CompilerOptions.OutputType = "library"` and suppresses `Main()` generation. The plan should evaluate whether this existing flag suffices for Unity or if a separate flag adds value.

### Warnings

1. **NuGet transitive dependencies**: `System.Collections.Immutable` version 10.0.7 may itself pull transitive dependencies. When bundling for Unity, consider using `dotnet publish` to collect all required DLLs rather than manually listing them.

2. **`emit csharp` writes to file, not stdout**: The `EmitCSharp` function always writes the generated C# to a file (either `-o` target or default `.cs` replacement). `SharpyCompilerBridge.CompileFile()` should capture the output file path from the `Console.WriteLine` message, or better, just specify `-o` explicitly.

3. **Unity version compatibility**: The plan mentions Unity 2022 LTS + Unity 6 for CI but doesn't specify minimum Unity version for the package itself. `netstandard2.1` is supported from Unity 2021.2+. Consider documenting this.

### Missing Steps Added

1. **Sharpy.Core transitive NuGet closure**: Before Phase 1 Task 2, run `dotnet publish src/Sharpy.Core -f netstandard2.1 -c Release` and inspect the output to identify ALL required DLLs (not just the two listed). This ensures nothing is missed.

2. **`sharpyc` self-contained publish**: Phase 1 Task 3 mentions "publish self-contained sharpyc" but doesn't specify the TFM/RID matrix or the publish command. Use: `dotnet publish src/Sharpy.Cli -c Release --self-contained -r <rid>` for each target RID (osx-arm64, osx-x64, win-x64, linux-x64).

### Unchecked Claims

1. **Unity `AssetPostprocessor.OnPostprocessAllAssets()` behavior**: The plan assumes this callback fires on `.spy` file saves. Unity only imports files it recognizes as assets; `.spy` is an unknown extension and may require a `ScriptedImporter` registration just to trigger import callbacks. Could not verify without a Unity project — recommend testing early in Phase 3.

2. **`#line` directives in Unity**: The plan assumes Unity's C# compiler respects `#line` for error navigation. This is likely true (Roslyn-based compilers do), but Unity's exact compilation pipeline varies by version. Recommend verifying in target Unity versions.
