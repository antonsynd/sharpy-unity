# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **Breaking** — read before upgrading a project that already uses Sharpy:
  - Generated script GUIDs change once: they are now derived from each `.spy` file's GUID, so re-link scene/prefab references to Sharpy components after the first compile (#7).
  - Generated files are named after their MonoBehaviour/ScriptableObject class (`spinner.spy` → `Spinner.cs`) instead of the module (#7).
  - **Recompile Selected** is removed; a single file cannot be compiled on its own in project mode.
  - `build_tools/` moved to `Tools~/build_tools/`; run it as `PYTHONPATH=Tools~ python3 -m build_tools ...` (#6).
  - **Generated Output Path** must be a dedicated, Sharpy-owned folder inside `Assets/` (marked by a `.gitignore`); a non-empty folder Sharpy does not own is refused.
  - Imports and namespaces are rooted at `Assets/` (`<SourceRoot>`; needs the sharpy release after 0.21.0), so namespaces of existing generated code change; prefer relative imports.
  - A Generated Output Path changed before upgrading is not retired automatically; delete the old folder's generated files by hand.
- Every `.spy` under `Assets/` is compiled as one `sharpyc project` build, staged in `Library/Sharpy/` and synced into the generated folder only on success (#4).
- All compile triggers (import, Recompile All, settings button, editor focus, batch) go through `SharpyProjectCompiler`.
- Diagnostics come from the real compile's output (errors on stderr, warnings on stdout), at `.spy` file and line, double-click opens the `.spy` (#3).
- `#line` directives are rewritten to the C# 9 form with absolute paths, so C# errors and stack traces name `.spy` lines (#11).
- Compiler timeout now bounds a whole-project compile; 0 or less means the 30 s default.
- **Clean Generated** deletes only generated `.cs`/`.meta` files and empty folders, plus `Library/Sharpy/`.
- A missing compiler or a timeout is not retried on every editor focus until the compiler or timeout changes.
- Sharpy toolchain: 0.16.1 -> 0.21.0 (2026-10-03)
  - Microsoft.Bcl.AsyncInterfaces.dll: updated
  - Sharpy.Core.dll: updated
  - System.Collections.Immutable.dll: updated
- Sharpy toolchain: May-era v0.1.2 build -> 0.16.1 (2026-08-20)
  - Microsoft.Bcl.AsyncInterfaces.dll: updated
  - Sharpy.Core.dll: updated
  - System.Collections.Immutable.dll: updated
  - System.Runtime.CompilerServices.Unsafe.dll: unchanged

### Added
- Unity references derived automatically from `Assembly-CSharp` (engine modules, plugins, project asmdefs), minus assemblies sharpyc cannot load (sharpy#2182); **Derive Unity References** and **Reference Denylist** settings (#2).
- A warning with a fix when a compile fails on an unloadable reference.
- **Source-mapped errors** setting (default on) replacing the removed "Show #line Directives" (#11).
- Persisted fingerprint (`Library/Sharpy/fingerprint`): a compile runs on editor load or focus when sources, settings or the compiler changed (#10, #12).
- `SharpyBatch.GenerateAll` for `-executeMethod`, and a two-run headless CI recipe with `-ignoreCompilerErrors` (#10).
- **Assets > Sharpy > Install Stdlib (experimental)**: opt-in Sharpy.Stdlib install into `Assets/Plugins/Sharpy.Stdlib/`, with a version-mismatch warning (#8).
- A warning naming the stdlib modules generated code needs when Sharpy.Stdlib is not installed (#8).
- A warning for `.spy` files sharpyc skips (under `bin/`, `obj/` or `.sharpy-crash/`).
- Basic Setup sample: a MonoBehaviour importing a module from another folder with a relative import (#2).
- CI: `check-metas`, a weekly `check-pin` staleness check (#1), and `smoke-sample`, which compiles the sample with the pinned sharpyc (#2).
- Initial UPM package scaffolding
- Editor assembly with compiler bridge stubs
- Runtime assembly for Sharpy.Core integration
- Editor test assembly

### Fixed
- Batch mode installs the compiler synchronously instead of never (#5).
- `.meta` files are tracked, so git-URL (immutable) installs import the package; `build_tools` is no longer imported as assets (#6).
- No CS0618 on Unity 6000.3: `OnOpenAsset` uses `EntityId` there (#9).
- Settings edited on disk (text editor, `git pull`) are picked up without restarting the editor (#12).
- Duplicate types from per-file emit writing imported modules next to each importer (#4).
- A `.spy` not yet imported can no longer leave its script with a random GUID (#7).
- Changing **Generated Output Path** retires the old folder's generated files instead of compiling both copies (CS0101).
- Sharpy never deletes files it did not generate: no recursive delete of the configured folder, and paths such as `Assets/.` are refused.
- Upper-case `.SPY` sources map to their generated files.
- Compiler archive extraction rejects entries outside the install folder and removes leftovers of interrupted installs.
- `update-toolchain` writes `.meta` files for DLLs it adds; `format --check` exits non-zero when files need formatting.

### Removed
- **Recompile Selected** menu item.
- Per-file `emit csharp` compilation, the `emit diagnostics` re-run and its JSON parser (#3, #4).
