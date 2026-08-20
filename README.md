# Sharpy Unity Integration

Unity Editor plugin that makes `.spy` files work seamlessly inside Unity projects. Edit a `.spy` file, and the plugin automatically compiles it to C# via `sharpyc`, which Unity then compiles normally.

## Requirements

- Unity 2022.3 LTS or later
- Sharpy compiler (`sharpyc`) — downloaded on demand (first-launch prompt, or **Assets > Sharpy > Download Compiler**)

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
2. The plugin detects changes and runs `sharpyc` automatically
3. Generated C# appears in `Assets/SharpyGenerated/` (excluded from git)
4. Unity compiles the generated C# normally

### Settings

Open **Edit > Project Settings > Sharpy** to configure:

- **Auto-compile on Save** — toggle automatic compilation
- **Generated Output Path** — where generated C# files are written
- **Root Namespace** — namespace wrapper for generated code
- **Compiler Timeout** — max seconds per compilation
- **Custom Compiler Path** — absolute path to a `sharpyc` binary, overriding the managed install

### Menu Items

- **Assets > Sharpy > Recompile All** — force-recompile every `.spy` file
- **Assets > Sharpy > Recompile Selected** — recompile selected `.spy` files
- **Assets > Sharpy > Clean Generated** — delete all generated C# files

## How It Works

```
.spy file saved → AssetPostprocessor detects change → sharpyc emit csharp → .cs written → Unity compiles
```

The plugin ships with:
- `Sharpy.Core.dll` (netstandard2.1) — runtime dependency for `Sharpy.Builtins`, `Sharpy.List<T>`, etc.

The `sharpyc` compiler itself is not part of the package. On first launch the plugin offers to download the pinned release build for your platform (macOS arm64/x64, Windows x64, Linux x64/arm64) into `Library/SharpyCompiler/<version>/<platform>/` — per-project, git-ignored by Unity convention, and editor-only.

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
