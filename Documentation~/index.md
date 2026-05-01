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

## Requirements

- Unity 2022.3 LTS or later.
- Sharpy compiler binaries (bundled in the package under `Editor/Binaries/`).

## Limitations

- `.spy` files must be inside the `Assets/` folder to trigger auto-compilation.
- The `--namespace` flag for single-file compilation requires a future `sharpyc` update.
- Custom `.spy` file icons are not yet supported.
