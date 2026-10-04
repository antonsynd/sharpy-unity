# Basic Setup Sample

This sample demonstrates how to write Sharpy (`.spy`) files in a Unity project: plain classes, a module shared between folders, and a `MonoBehaviour` written in Sharpy. The included scripts are compiled to C# automatically by the Sharpy package.

## Importing the Sample

1. Open **Window > Package Manager**.
2. Select the **Sharpy** package.
3. Expand the **Samples** section and click **Import** next to **Basic Setup**.

Unity copies the sample files into `Assets/Samples/Sharpy/<version>/Basic Setup/`.

## What Happens Next

When Unity imports the `.spy` files, the Sharpy package compiles every `.spy` under `Assets/` as one project with `sharpyc`. The generated C# files are written to `Assets/SharpyGenerated/` (mirroring the source structure), and Unity compiles them as normal C# scripts.

## Included Scripts

| File | Description |
|------|-------------|
| `Scripts/HelloSharpy.spy` | A simple utility class with a static greeting method. |
| `Scripts/GameScore.spy` | A score tracker class with fields, a constructor, and methods. |
| `Scripts/Core/greeting.spy` | A plain module of functions with no Unity dependency. |
| `Scripts/Behaviours/spinner.spy` | A `MonoBehaviour` that imports `Core/greeting.spy`, logs a greeting in `start` and spins its GameObject in `update`. |

## A MonoBehaviour in Sharpy

`spinner.spy` imports Unity types with `from unity_engine import ...` and subclasses `MonoBehaviour`. Unity message methods are written in snake_case (`start`, `update`) and compile to `Start` and `Update`. The generated file is named after the class (`Spinner.cs`), so you can add **Spinner** to a GameObject like any other component. Its `degrees_per_second` field shows up in the Inspector.

Unity's `float` is Sharpy's `float32`. Sharpy's `float` is a C# `double`, so fields and values passed to Unity APIs such as `Vector3` are declared `float32`.

### Use relative imports between your own folders

```python
from ..Core.greeting import greet, wrap_degrees
```

A relative import names the other module by its position from the importing file, so it keeps working wherever the folder ends up under `Assets/`. An imported sample, for example, lives under a version-numbered folder. An absolute import is spelled from `Assets/`, so inside this sample it would have to name the version folder and would break as soon as the sample is imported under another version.

## Using the Generated Classes

After import, you can reference the generated classes from any C# script or MonoBehaviour. Sharpy's snake_case members compile to PascalCase C#. Each `.spy` file becomes a namespace made of the **Root Namespace** setting (`SharpyScripts` by default), the file's folder path from `Assets/` and its module name; the first `namespace` line of the generated file shows it. Folder names become valid identifiers, so `Samples/Sharpy/0.1.0/Basic Setup/Scripts/HelloSharpy.spy` gives `SharpyScripts.Samples.Sharpy._010.BasicSetup.Scripts.HelloSharpy`. The `_010` segment is the sample's version folder; if you imported another version, copy the namespace from the generated file.

```csharp
using UnityEngine;
using SharpyScripts.Samples.Sharpy._010.BasicSetup.Scripts.GameScore;
using SharpyScripts.Samples.Sharpy._010.BasicSetup.Scripts.HelloSharpy;

public class ExampleUsage : MonoBehaviour
{
    private GameScore score;

    void Start()
    {
        // Static utility method (methods without `self` are static)
        string greeting = HelloSharpy.Greet("Unity");
        Debug.Log(greeting);

        // Score tracker
        score = new GameScore("Player 1");
        score.AddPoints(100);
        score.AddPoints(50);
        Debug.Log($"{score.GetPlayerName()}: {score.GetScore()} points");
    }
}
```
