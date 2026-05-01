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
  Binaries/                  # Platform-specific sharpyc binaries (editor-only)
  *.cs                       # Compiler bridge, asset postprocessor, settings, menus
Runtime/                     # Runtime C# (Sharpy.Unity.Runtime assembly)
Plugins/Sharpy.Core/         # Sharpy.Core.dll (netstandard2.1, runtime dependency)
Tests/Editor/                # Unity Test Runner tests (editor mode)
Samples~/BasicSetup/         # UPM importable sample
Documentation~/              # Package documentation (hidden from Unity)
package.json                 # UPM manifest
```

## Key Design Decisions

1. **Transpile-then-compile** — generate C# and let Unity's pipeline handle it, rather than injecting IL
2. **AssetPostprocessor** — fires on `.spy` file import to trigger compilation
3. **Generated files in `Assets/SharpyGenerated/`** — mirrors source structure, gitignored
4. **Sharpy.Core.dll as a plugin** — netstandard2.1 build, runtime dependency
5. **sharpyc as editor-only binary** — bundled per-platform, excluded from player builds

## Architecture

```
.spy file changed
  → SharpyAssetPostprocessor.OnPostprocessAllAssets()
    → SharpyCompilerBridge.CompileFile()
      → sharpyc emit csharp <file> -o <output> -t library
    → Write .cs to SharpyGenerated/
    → AssetDatabase.Refresh()
      → Unity compiles the generated C#
```

### Key Classes

| Class | File | Purpose |
|-------|------|---------|
| `SharpyCompilerBridge` | `Editor/SharpyCompilerBridge.cs` | Invokes sharpyc, captures output |
| `SharpyDiagnostic` | `Editor/SharpyDiagnostic.cs` | Parsed diagnostic from JSON output |
| `SharpyAssetPostprocessor` | `Editor/SharpyAssetPostprocessor.cs` | Detects .spy changes, triggers compilation |
| `SharpyGeneratedFolderManager` | `Editor/SharpyGeneratedFolderManager.cs` | Manages output directory lifecycle |
| `SharpySettings` | `Editor/SharpySettings.cs` | Project-level settings (ScriptableSingleton) |
| `SharpySettingsProvider` | `Editor/SharpySettingsProvider.cs` | Settings UI in Project Settings window |
| `SharpyMenuItems` | `Editor/SharpyMenuItems.cs` | Menu items for manual compilation control |
| `SharpyFileHandler` | `Editor/SharpyFileHandler.cs` | Opens .spy files in external editor |

## Assembly Definitions

- `Sharpy.Unity.Editor` — editor-only, references Runtime + Sharpy.Core.dll
- `Sharpy.Unity.Runtime` — all platforms, references Sharpy.Core.dll
- `Sharpy.Unity.Editor.Tests` — editor-only, test assembly

## Conventions

- C# style follows the sharpy project: 4-space indent, Allman braces, `LangVersion 9.0` (netstandard2.1 compatible)
- No `#nullable enable` in Unity scripts (Unity's serialization doesn't support it well)
- Namespace: `Sharpy.Unity.Editor` for editor code, `Sharpy.Unity.Runtime` for runtime code
- Unity minimum version: 2022.3 LTS

## Compiler Interface

The plugin invokes `sharpyc` via `Process.Start`:

```bash
sharpyc emit csharp <file.spy> -o <output.cs> -t library    # Single file
sharpyc project <.spyproj> --emit-cs-to <dir>               # Multi-file project
sharpyc emit diagnostics <file.spy> --format json            # Structured diagnostics
```

Exit code 0 = success, 1 = errors. Diagnostics JSON includes `severity`, `code`, `line`, `column`, `message`, `phase`.

## Testing

Tests run via Unity Test Runner in editor mode. Test assembly: `Sharpy.Unity.Editor.Tests`.

No `dotnet test` — this is a Unity package, not a .NET solution.
