# Basic Setup Sample

This sample demonstrates how to write Sharpy (`.spy`) files in a Unity project. The included scripts are compiled to C# automatically by the Sharpy package.

## Importing the Sample

1. Open **Window > Package Manager**.
2. Select the **Sharpy** package.
3. Expand the **Samples** section and click **Import** next to **Basic Setup**.

Unity copies the sample files into `Assets/Samples/Sharpy/<version>/BasicSetup/`.

## What Happens Next

When Unity imports the `.spy` files, the Sharpy asset postprocessor detects them and invokes `sharpyc` to transpile each file to C#. The generated C# files are written to `Assets/SharpyGenerated/` (mirroring the source structure), and Unity compiles them as normal C# scripts.

## Included Scripts

| File | Description |
|------|-------------|
| `Scripts/HelloSharpy.spy` | A simple utility class with a static greeting method. |
| `Scripts/GameScore.spy` | A score tracker class with fields, a constructor, and methods. |

## Using the Generated Classes

After import, you can reference the generated classes from any C# script or MonoBehaviour:

```csharp
using UnityEngine;

public class ExampleUsage : MonoBehaviour
{
    private GameScore score;

    void Start()
    {
        // Static utility method
        string greeting = HelloSharpy.greet("Unity");
        Debug.Log(greeting);

        // Score tracker
        score = new GameScore("Player 1");
        score.add_points(100);
        score.add_points(50);
        Debug.Log($"{score.get_player_name()}: {score.get_score()} points");
    }
}
```
