# Sharpy Unity Integration

[![Unity CI](https://github.com/antonsynd/sharpy-unity/actions/workflows/unity-ci.yml/badge.svg?branch=mainline)](https://github.com/antonsynd/sharpy-unity/actions/workflows/unity-ci.yml)

Unity Editor plugin that makes `.spy` files work seamlessly inside Unity projects. Edit a `.spy` file, and the plugin automatically compiles it to C# via `sharpyc`, which Unity then compiles normally.

## Requirements

- Unity 2022.3 LTS or later (developed and tested on Unity 6000.3; 2022.3 is covered only by the CI smoke compile)
- Sharpy compiler (`sharpyc`) — downloaded on demand (first-launch prompt, or **Assets > Sharpy > Download Compiler**). Project compilation needs a sharpy release newer than 0.21.0; with 0.21.0 the compile stops with an error that says so.

## Installation

### Via Git URL (recommended)

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter: `https://github.com/antonsynd/sharpy-unity.git`

### From Disk

1. Clone this repository
2. Open **Window > Package Manager**
3. Click **+** > **Add package from disk...**
4. Select the `package.json` in this repository

## Usage

1. Create `.spy` files anywhere under `Assets/`
2. On save, the plugin compiles **all** `.spy` files as one Sharpy project
3. Generated C# appears in `Assets/SharpyGenerated/` (git-ignored), mirroring the source folders
4. Unity compiles the generated C# normally

A `.spy` file whose only MonoBehaviour or ScriptableObject is `Player` generates `Player.cs`, so the component can be added in the Inspector. Generated scripts get GUIDs derived from their `.spy` file's GUID, so scene and prefab references survive *Clean Generated* and a fresh clone (commit the `.spy.meta` files). Sharpy errors, and C# errors in generated code, are reported at the `.spy` line; double-click opens the `.spy`.

### Imports and namespaces

Imports are spelled from `Assets/`: `Assets/Scripts/Core/greeting.spy` is `Scripts.Core.greeting`. Prefer relative imports (`from ..Core.greeting import greet`) between your own folders; they keep working when a folder moves. Each `.spy` becomes the namespace *Root Namespace* + its folder path + its module name, e.g. `SharpyScripts.Scripts.Core.Greeting`. Rooting at `Assets/` needs the sharpy release after 0.21.0; older compilers root at the common folder of all `.spy` files.

### Unity APIs

`from unity_engine import MonoBehaviour, Vector3` works with no setup: the plugin passes Unity's engine assemblies to `sharpyc` automatically. Package APIs (Input System, TextMeshPro, UGUI, ...) are not derived, because `sharpyc` cannot load many of them yet ([sharpy#2182](https://github.com/antonsynd/sharpy/issues/2182)); add a package's DLL to **Additional References** to try it. Sharpy's `float` is a C# `double`; use `float32` for Unity's `float`.

### Settings

Open **Edit > Project Settings > Sharpy** to configure:

- **Custom Compiler Path** — absolute path to a `sharpyc` binary, overriding the managed install
- **Timeout (seconds)** — limit for one whole-project compile (default 30; 0 or less means the default)
- **Auto-compile on Save** — compile on `.spy` changes, and on editor load or focus when sources, settings or the compiler changed
- **Generated Output Path** — a dedicated folder inside `Assets/` for the generated C#. Sharpy marks it with a `.gitignore` and refuses a non-empty folder it does not own. A new path is applied on Enter or when the field loses focus; the next successful compile removes the generated files from the old folder
- **Source-mapped errors** — keep `#line` directives so errors and stack traces name `.spy` lines (default on)
- **Root Namespace** — first namespace segment of generated code (default `SharpyScripts` when empty)
- **Additional Module Paths** — extra folders to resolve Sharpy imports from
- **Additional References** — extra assemblies, passed as-is
- **Derive Unity References** / **Reference Denylist** — the automatic Unity references and names to leave out of them

Settings edited outside the editor (a text editor, `git pull`) are picked up when the editor regains focus.

### Menu Items

- **Assets > Sharpy > Recompile All** — compile every `.spy` now, even if nothing changed
- **Assets > Sharpy > Clean Generated** — delete the generated `.cs`/`.meta` files (and folders left empty) and the compiler's working folder `Library/Sharpy/`
- **Assets > Sharpy > View Generated C#** — open the generated script of the selected `.spy`
- **Assets > Sharpy > Download Compiler** — install the pinned `sharpyc`, then compile
- **Assets > Sharpy > Install Stdlib (experimental)** — see [Standard Library](#standard-library-experimental)

## How It Works

```
.spy changed (or Recompile All, or focus with stale inputs)
  → write Library/Sharpy/unity.spyproj (all .spy under Assets/, Unity references)
  → sharpyc project Library/Sharpy/unity.spyproj --emit-cs-to Library/Sharpy/emit
  → on success only: sync into Assets/SharpyGenerated/ (class-named files, deterministic .meta GUIDs)
  → Unity compiles the generated C#
```

A failed compile leaves `Assets/SharpyGenerated/` untouched. See [Documentation~/index.md](Documentation~/index.md) for details and known limitations.

The plugin ships with:
- `Sharpy.Core.dll` (netstandard2.1) — runtime dependency for `Sharpy.Builtins`, `Sharpy.List<T>`, etc.

The `sharpyc` compiler itself is not part of the package. On first launch the plugin offers to download the pinned release build for your platform (macOS arm64/x64, Windows x64, Linux x64/arm64) into `Library/SharpyCompiler/<version>/<platform>/` — per-project, git-ignored by Unity convention, and editor-only.

## Headless Builds (CI)

Generated C# lives in the git-ignored `Assets/SharpyGenerated/`, so a fresh clone has none and a cached checkout can hold stale files. If any C# script uses a Sharpy type, Unity's batch mode then stops at load with `Scripts have compiler errors.`, before the plugin can regenerate anything. Regenerate in a first Unity run, then build in a second:

```sh
# 1. Regenerate. -ignoreCompilerErrors lets the editor load past the missing or stale generated C#.
Unity -batchmode -nographics -quit -ignoreCompilerErrors -projectPath <project> \
  -executeMethod Sharpy.Unity.Editor.SharpyBatch.GenerateAll -logFile generate.log

# 2. Build or test as usual, without -ignoreCompilerErrors.
Unity -batchmode -nographics -quit -projectPath <project> -executeMethod <YourBuildMethod> -logFile build.log
```

`GenerateAll` installs the pinned compiler if it is missing, compiles every `.spy`, and exits with code 1 when the Sharpy compile fails. See [Documentation~/index.md](Documentation~/index.md#headless-builds-ci) for details.

## Standard Library (experimental)

`Sharpy.Stdlib` (`import math`, `json`, `yaml`, `toml`, ...) is not bundled: it adds about 4 MB of DLLs, parts of it need reflection or native code, and it is untested under IL2CPP. Without it, a `.spy` file that imports a stdlib module still transpiles, but Unity fails with `error CS0234: The type or namespace name 'MathModule' does not exist in the namespace 'Sharpy'`.

To opt in, run **Assets > Sharpy > Install Stdlib (experimental)**. It copies the pinned release's stdlib DLLs into `Assets/Plugins/Sharpy.Stdlib/`. `math`, `json`, `yaml` and `toml` work in the editor; `sqlite3` does not, and IL2CPP builds are unsupported. See [Documentation~/index.md](Documentation~/index.md#standard-library-experimental) for details.

## Development

This is a [Unity Package Manager](https://docs.unity3d.com/Manual/CustomPackages.html) package. The structure follows UPM conventions:

```
├── Editor/                  # Editor-only scripts (compiler bridge, settings, UI)
├── Runtime/                 # Runtime scripts
├── Plugins/Sharpy.Core/     # Sharpy.Core.dll (netstandard2.1)
├── Tests/Editor/            # Unity Test Runner tests
├── Samples~/BasicSetup/     # Importable sample project
├── Documentation~/          # Package documentation
└── package.json             # UPM manifest
```

## Related

- [sharpy](https://github.com/antonsynd/sharpy) — The Sharpy compiler and standard library

## License

MIT
