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

Open **Edit > Project Settings > Sharpy** to configure the package. Settings live in `ProjectSettings/SharpySettings.asset`; edits made outside the editor (a text editor, `git pull`) are picked up when the editor regains focus.

| Setting | Default | Description |
|---------|---------|-------------|
| Custom Compiler Path | (empty) | Absolute path to a `sharpyc` binary. Overrides the managed install and suppresses the download prompt |
| Timeout (seconds) | 30 | Limit for one compile. Every compile builds the whole project, so a large project may need more. 0 or less means the default |
| Auto-compile on Save | Enabled | Compile when a `.spy` is imported, changed, moved or deleted, and on editor load or focus when the inputs changed (see [Staleness](#staleness)) |
| Generated Output Path | `Assets/SharpyGenerated` | Folder the generated C# is synced into. See [The Generated Folder](#the-generated-folder). A new path is applied on Enter or when the field loses focus |
| Source-mapped errors | Enabled | Keep `#line` directives in generated C#, so errors and stack traces name `.spy` lines. When off, they name the generated `.cs` |
| Root Namespace | (empty → `SharpyScripts`) | First segment of every generated namespace. Must not contain a `Sharpy` segment |
| Additional Module Paths | (empty) | Extra folders to resolve Sharpy imports from |
| Additional References | (empty) | Extra assemblies (absolute or project-relative paths), passed to `sharpyc` as-is and never filtered |
| Derive Unity References | Enabled | Compile against the assemblies Unity gives `Assembly-CSharp`, minus those `sharpyc` cannot load (see [References](#references)). The page shows how many were derived |
| Reference Denylist | (empty) | Assembly names (with or without `.dll`) to leave out of the derived references |

## Workflow

1. Create `.spy` files anywhere inside your `Assets/` folder.
2. On save, the package compiles every `.spy` under `Assets/` as one Sharpy project.
3. If the compile succeeds, the generated C# is synced into `Assets/SharpyGenerated/`, mirroring the source folder structure.
4. Unity compiles the generated C# through its normal script compilation pipeline.

### Manual Controls

Use the **Assets > Sharpy** menu for manual operations:

- **Recompile All** -- Compile every `.spy` file now, even if nothing changed. A single file cannot be compiled on its own, so there is no "Recompile Selected".
- **Clean Generated** -- Delete the generated `.cs` and `.cs.meta` files and the folders they leave empty (the folder itself and its marker stay), and `Library/Sharpy/` (the project file, staging folder, compiler output and fingerprint).
- **View Generated C#** -- Open the generated C# file of the selected `.spy` file.
- **Download Compiler** -- Install the pinned `sharpyc` into `Library/SharpyCompiler/`, then compile the project.
- **Install Stdlib (experimental)** -- See [Standard Library](#standard-library-experimental).

### Inspector Integration

Select a `.spy` file in the Project window to see whether it has generated C#, open it, or recompile the project.

## How Compilation Works

Every compile runs the same steps, whichever trigger started it (an import, **Recompile All**, the settings page button, editor focus, or `SharpyBatch.GenerateAll`):

1. Write `Library/Sharpy/unity.spyproj` (only when its text changed). It lists every `.spy` under `Assets/` (`../../Assets/**/*.spy`), the module root `<SourceRoot>../../Assets</SourceRoot>`, the root namespace, the target `netstandard2.1`, and the references.
2. Run `sharpyc project Library/Sharpy/unity.spyproj --emit-cs-to Library/Sharpy/emit` into an emptied staging folder. `sharpyc` also compiles the generated C# against the references before it reports success, so most code generation problems never reach Unity.
3. On exit code 0 only, sync the staged C# into the generated folder: write each script whose content changed (its `.meta` first), delete generated scripts whose `.spy` is gone, prune empty folders, then refresh the AssetDatabase once. On any other exit code the generated folder is left as it was, like a C# compile error leaves the last build in place.

One bad `.spy` therefore blocks regeneration of all of them until it is fixed. A project with no `.spy` files skips the compiler and empties the generated folder.

`sharpyc` skips sources under a folder named `bin`, `obj` or `.sharpy-crash` (any case). Such a `.spy` is not compiled; after a successful build the package logs a warning naming the folder.

### The Generated Folder

The sync replaces every `.cs` in the generated folder, so Sharpy only writes to a folder it owns:

- **Generated Output Path** must be a relative path to a folder strictly inside `Assets/` (not `Assets` itself, no `.` or `..` segments), with no `.spy` sources in it.
- A missing or empty folder is created and marked as Sharpy's with a `.gitignore` whose first line is `# Generated by Sharpy; this folder is managed by com.antonsynd.sharpy` (followed by `*`, so the generated files are not committed). A folder from an earlier version with a plain `*` `.gitignore` is adopted only if every `.cs` in it is generated C#.
- A non-empty folder without the marker is refused: the compile and **Clean Generated** log an error and change nothing in it.
- When the path changes, the next successful compile removes the generated `.cs`/`.meta` files from the old (marked) folder before syncing into the new one, and deletes the old folder if only the marker is left. Other files in it are kept. This needs a previous successful compile with this version (it is recorded in `Library/Sharpy/output-path`).

### Generated Scripts

- **One `.spy`, one `.cs`.** `Assets/Scripts/Core/greeting.spy` generates `Assets/SharpyGenerated/Scripts/Core/greeting.cs`.
- **Class-named files.** Unity attaches a MonoBehaviour, ScriptableObject or StateMachineBehaviour only from a script file named after the class. A `.spy` that defines exactly one such class (directly or through another class in the project) therefore generates `<ClassName>.cs` in the same folder, e.g. `Behaviours/spinner.spy` → `Behaviours/Spinner.cs`. A `.spy` with two such classes, or two `.spy` files in one folder that need the same file name, get a warning and keep the module-named file; give each component its own `.spy` file.
- **Deterministic GUIDs.** Each generated script's `.meta` gets a GUID derived from its `.spy` file's GUID, never from its path, name or namespace. A `.spy` that Unity has not imported yet is imported first, so it has a GUID before its script is generated. Scene and prefab references to Sharpy components survive **Clean Generated**, a fresh clone and a class rename, as long as the `.spy.meta` files are committed. Projects generated by an earlier version of this package get new GUIDs once.

### Imports and Namespaces

The module root is `Assets/`, so an absolute import is spelled from there: `Assets/Scripts/Core/greeting.spy` is `Scripts.Core.greeting`. Between your own folders, prefer relative imports:

```python
from ..Core.greeting import greet
```

A relative import keeps working wherever the folder ends up under `Assets/`; an imported package sample, for example, lives under a version-numbered folder.

Each `.spy` file becomes a namespace made of the **Root Namespace**, its folder path from `Assets/` and its module name: `Assets/Scripts/Core/greeting.spy` → `SharpyScripts.Scripts.Core.Greeting`. Module-level functions go into a static `<Module>Module` class in that namespace (`GreetingModule.Greet`). Folder names are made into valid identifiers (`Basic Setup` → `BasicSetup`, `0.2.0` → `_020`).

The `<SourceRoot>` property that pins the root at `Assets/` is honoured from sharpy 0.22.0. Older compilers root imports and namespaces at the longest common folder of all `.spy` files, which moves when you add a `.spy` in a new top-level folder; relative imports are unaffected.

### References

With **Derive Unity References** on, `sharpyc` compiles against the references Unity gives the player `Assembly-CSharp` (engine modules, precompiled plugins, asmdefs under `Assets/`), or, before `Assembly-CSharp` exists, the engine assemblies plus the plugins imported for the active build target. Nothing machine-specific is saved in the settings. Left out:

- assemblies `sharpyc` cannot load. One unloadable type aborts the whole compile ([sharpy#2182](https://github.com/antonsynd/sharpy/issues/2182)): `UnityEngine.TextCoreTextEngineModule`, `UnityEngine.UIElementsModule`, `UnityEngine.UI`, `nunit.framework`, every `UnityEditor.*` assembly, and any assembly whose editor build references `UnityEditor`;
- script assemblies built from packages (asmdefs under `Packages/`, such as Input System, TextMeshPro or Unity Services), for the same reason;
- `Sharpy.*` (sharpyc brings its own `Sharpy.Core`), `Assembly-CSharp` itself and `Assembly-CSharp-Editor*`;
- Unity's own copies of the base class library (`netstandard`, `mscorlib`, the NetStandard, UnityReferenceAssemblies and MonoBleedingEdge folders), which duplicate `sharpyc`'s runtime;
- names in **Reference Denylist**.

To use a package's API from Sharpy, add its DLL to **Additional References** (e.g. `Library/ScriptAssemblies/Unity.InputSystem.dll`). It is passed as-is, and may still crash `sharpyc` until sharpy#2182 is fixed; if the compile then fails with a type-load error, remove it again.

### Errors and Source Mapping

`sharpyc` diagnostics appear in the Console at the `.spy` file and line (`Assets/Scripts/player.spy(12,9): SPY0200: Undefined identifier 'nmae'. Did you mean 'name'?`), as errors or warnings, including errors in imported files. Double-click opens the `.spy` in your external editor. Warnings are logged on successful compiles too.

With **Source-mapped errors** on, generated C# keeps `#line` directives, so a C# error Unity reports in generated code, and a runtime exception's stack trace, also name the `.spy` line. The directives are converted to the C# 9 form Unity accepts and keep **absolute** paths: Unity resolves a relative `#line` path against the folder of the generated `.cs`, which would point into `Assets/SharpyGenerated/`. Unity still displays an absolute path inside the project as `Assets/...`.

### Staleness

After each successful sync the package stores a fingerprint in `Library/Sharpy/fingerprint`: a hash of the project file (settings and references), the compiler version, the generated-folder settings, and every `.spy` path with its content hash. When the editor loads or regains focus, a changed fingerprint (or a missing generated file) triggers a compile, so edits made by `git pull` or another tool are picked up without a restart. A compile that failed, including a missing compiler or a timeout, is not retried on focus until one of its inputs changes (installing the compiler, a new compiler path and a new timeout count); saving a `.spy` and **Recompile All** always compile.

## Headless Builds (CI)

The generated folder (`Assets/SharpyGenerated/` by default) is git-ignored. A CI checkout therefore starts with no generated C#, or with stale generated C# from a cache. When any C# script in the project uses a type generated from Sharpy, Unity's batch mode fails script compilation on load and quits before any editor code runs:

```
Aborting batchmode due to failure:
Scripts have compiler errors.
```

Run Unity twice. The first run regenerates; `-ignoreCompilerErrors` lets the editor load the plugin even though the project's scripts do not compile yet:

```sh
Unity -batchmode -nographics -quit -ignoreCompilerErrors -projectPath <project> \
  -executeMethod Sharpy.Unity.Editor.SharpyBatch.GenerateAll -logFile generate.log
```

`SharpyBatch.GenerateAll`:

- installs the pinned compiler into `Library/SharpyCompiler/` if it is missing (no custom compiler path set);
- compiles every `.spy` and replaces the generated C#, whether or not it looks up to date;
- logs `[Sharpy] GenerateAll: generated C# is up to date.` and exits 0 on success;
- logs the Sharpy errors and `[Sharpy] GenerateAll failed; ...`, leaves the generated folder untouched and exits 1 on failure.

The first run only checks the Sharpy compile. The second run builds or tests as usual and must not pass `-ignoreCompilerErrors`, so that C# errors still fail the job:

```sh
Unity -batchmode -nographics -quit -projectPath <project> -executeMethod <YourBuildMethod> -logFile build.log
```

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

- Unity 2022.3 LTS or later. Developed and tested on Unity 6000.3; 2022.3 is checked only by the CI smoke compile.
- `sharpyc` 0.22.0 or newer for project compilation (its `--emit-cs-to` mirrors the source tree). The compiler is downloaded on demand into `Library/SharpyCompiler/<version>/<platform>/`, or set **Custom Compiler Path**.

## Limitations

- `.spy` files must be inside the `Assets/` folder to be compiled, and not under a folder named `bin`, `obj` or `.sharpy-crash`.
- Every compile builds the whole project, and one `.spy` with errors blocks regeneration of all of them.
- Package APIs (Input System, TextMeshPro, UGUI, ...) are not derived and need **Additional References**; many still crash `sharpyc` ([sharpy#2182](https://github.com/antonsynd/sharpy/issues/2182)). Editor APIs (`UnityEditor`) are not available from Sharpy.
- A MonoBehaviour nested inside another class does not get a class-named file, so it cannot be added as a component.
- Sharpy's `float` is a C# `double`. Unity APIs take `float32` (C# `float`): declare fields and values passed to `Vector3`, `Time.deltaTime` arithmetic and the like as `float32`.
- The Console entry for a Sharpy diagnostic uses an internal Unity API; it is verified on Unity 6000.3, not on 2022.3.
- Custom `.spy` file icons are not yet supported.
