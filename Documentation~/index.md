# Sharpy Unity Package

Integrate the Sharpy programming language into the Unity Editor. The package auto-compiles `.spy` files to C# via the `sharpyc` CLI, then lets Unity compile the generated C# normally.

## Installation

### From Git URL

1. Open **Window > Package Manager**.
2. Click **+** > **Add package from git URL**.
3. Enter: `https://github.com/antonsynd/sharpy-unity.git`
4. Click **Add**.

### From Disk

1. Clone the repository to your local machine.
2. Open **Window > Package Manager**.
3. Click **+** > **Add package from disk**.
4. Navigate to the cloned repository and select `package.json`.

## Configuration

Open **Edit > Project Settings > Sharpy** to configure the package.

| Setting | Default | Description |
|---------|---------|-------------|
| Generated Output Path | `Assets/SharpyGenerated` | Directory where compiled C# files are written |
| Compiler Timeout | (default) | Maximum time in seconds to wait for `sharpyc` to finish |
| Auto-compile on Save | Enabled | Automatically compile `.spy` files when they are imported or changed |
| Root Namespace | (empty) | Default namespace applied to generated C# files |
| Show #line Directives | Disabled | Include `#line` directives in generated C# for source-level debugging |
| Additional Module Paths | (empty) | Extra paths to search for Sharpy modules |
| Additional References | (empty) | Extra assembly references passed to the compiler |

## Workflow

1. Create `.spy` files anywhere inside your `Assets/` folder.
2. On save, the asset postprocessor detects the change and invokes `sharpyc` to compile each file to C#.
3. Generated C# files are written to `Assets/SharpyGenerated/`, mirroring the source folder structure.
4. Unity compiles the generated C# through its normal script compilation pipeline.

### Manual Controls

Use the **Assets > Sharpy** menu for manual operations:

- **Recompile All** -- Recompile every `.spy` file in the project.
- **Recompile Selected** -- Recompile only the currently selected `.spy` file(s).
- **Clean Generated** -- Delete all files in the generated output directory.
- **View Generated C#** -- Open the generated C# file corresponding to the selected `.spy` file.

### Inspector Integration

Select a `.spy` file in the Project window to view its compile status in the Inspector panel.

## Compiler Interface

The package invokes the `sharpyc` CLI under the hood. These are the commands used:

```bash
# Compile a single file
sharpyc emit csharp <file.spy> -o <output.cs> -t library

# Compile a multi-file project
sharpyc project <.spyproj> --emit-cs-to <dir>

# Get structured diagnostics
sharpyc emit diagnostics <file.spy> --format json
```

Exit code `0` indicates success. Exit code `1` indicates errors. Diagnostics JSON includes `severity`, `code`, `line`, `column`, `message`, and `phase` fields.

## Standard Library (experimental)

The package ships only `Sharpy.Core`. `Sharpy.Stdlib`, which provides Python-style modules such as `math`, `json`, `yaml` and `toml`, is not bundled. It adds about 4 MB of DLLs (MathNet.Numerics, YamlDotNet, Tomlyn, System.Text.Json, Microsoft.Data.Sqlite and others), parts of it rely on reflection or native code, and none of it is tested under IL2CPP.

Without it, a `.spy` file that imports a stdlib module still transpiles, but Unity cannot compile the result:

```
error CS0234: The type or namespace name 'MathModule' does not exist in the namespace 'Sharpy' (are you missing an assembly reference?)
```

### Installing

Run **Assets > Sharpy > Install Stdlib (experimental)**. It downloads `sharpy-stdlib-netstandard2.1.zip` from the sharpy release matching the pinned toolchain and copies its DLLs into `Assets/Plugins/Sharpy.Stdlib/`. Commit that folder if your team needs it. The installer skips:

- DLLs the package already ships (`Sharpy.Core`, `System.Collections.Immutable`, `Microsoft.Bcl.AsyncInterfaces`, `System.Runtime.CompilerServices.Unsafe`), and `.pdb`, `.xml` and `.deps.json` files.
- Any DLL whose file name the project already has elsewhere, such as another plugin's `System.Text.Json.dll`. Unity 6000.3 loads only one assembly per name and silently ignores the other copy, so the installer keeps the existing one and lists what it skipped in the Console. If that copy is older than the one the stdlib was built against, the modules that use it can fail at runtime.

On editor load, the package warns when the installed `Sharpy.Stdlib.dll` version differs from the pinned toolchain. Run the menu item again after updating the package. Re-installing overwrites files but does not delete DLLs that an older stdlib shipped and a newer one dropped. Delete `Assets/Plugins/Sharpy.Stdlib/` first for a clean install.

### Module Support

Checked in the Unity 6000.3 editor (Mono). Mono players use the same runtime but were not tested separately; other modules are untested.

| Module | Status |
|--------|--------|
| `math` | Works |
| `json` | Works. The netstandard2.1 build uses a hand-written parser, not System.Text.Json |
| `yaml` | Works (YamlDotNet) |
| `toml` | Works (Tomlyn, which uses System.Text.Json) |
| `sqlite3` | Does not work. The zip contains no native `e_sqlite3` library, and `Microsoft.Data.Sqlite` references SQLitePCLRaw 2.1 while the zip ships 3.0 |

### IL2CPP

IL2CPP builds are unsupported with the stdlib installed:

- `yaml` uses YamlDotNet's reflection-based `Deserializer`, and the conversion to Sharpy types invokes methods through reflection.
- `toml` calls Tomlyn's generic `TomlSerializer` entry points, which Tomlyn marks as reflection-based and unsafe for trimming and AOT.
- `sqlite3` needs a native library.
- `json` does not use reflection in this build, but the stdlib as a whole has not been tested under IL2CPP.

## Requirements

- Unity 2022.3 LTS or later.
- Sharpy compiler binaries (bundled in the package under `Editor/Binaries/`).

## Limitations

- `.spy` files must be inside the `Assets/` folder to trigger auto-compilation.
- The `--namespace` flag for single-file compilation requires a future `sharpyc` update.
- Custom `.spy` file icons are not yet supported.
