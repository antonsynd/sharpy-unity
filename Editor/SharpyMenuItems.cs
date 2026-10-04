namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    public static class SharpyMenuItems
    {
        // There is no "Recompile Selected": every .spy is compiled as part of
        // one project, so a single file cannot be compiled on its own.
        [MenuItem("Assets/Sharpy/Recompile All", false, 1000)]
        public static void RecompileAll()
        {
            if (SharpyProjectCompiler.Compile(force: true))
            {
                Debug.Log("[Sharpy] Recompiled all .spy files.");
            }
        }

        [MenuItem("Assets/Sharpy/View Generated C#", false, 1003)]
        public static void ViewGeneratedCSharp()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (Path.GetExtension(path) != ".spy")
                {
                    continue;
                }

                string generatedPath = SharpyGeneratedFolderManager.FindGeneratedPath(path);

                if (File.Exists(generatedPath))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(generatedPath);

                    if (asset != null)
                    {
                        AssetDatabase.OpenAsset(asset);
                    }
                }
                else
                {
                    Debug.LogWarning($"[Sharpy] No generated C# found for {path}. Try recompiling first.");
                }
            }
        }

        [MenuItem("Assets/Sharpy/View Generated C#", true)]
        private static bool ViewGeneratedCSharpValidation()
        {
            foreach (var obj in Selection.objects)
            {
                if (Path.GetExtension(AssetDatabase.GetAssetPath(obj)) == ".spy")
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem("Assets/Sharpy/Clean Generated", false, 1002)]
        public static void CleanGenerated()
        {
            string outputPath = SharpySettings.instance.GeneratedOutputPath;

            // The generated folder is deleted whole; never Assets/ itself.
            string folderProblem = SharpyProjectCompiler.CheckGeneratedFolder(outputPath, new string[0]);

            if (folderProblem != null)
            {
                Debug.LogError("[Sharpy] " + folderProblem);
                return;
            }

            // The .spyproj, the staging folder, and sharpyc's bin/ and obj/.
            if (Directory.Exists(SharpyProjectCompiler.LibraryFolder))
            {
                Directory.Delete(SharpyProjectCompiler.LibraryFolder, true);
            }

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);

                string metaFile = outputPath + ".meta";

                if (File.Exists(metaFile))
                {
                    File.Delete(metaFile);
                }

                AssetDatabase.Refresh();
                Debug.Log("[Sharpy] Cleaned all generated files.");
            }

            SharpyGeneratedFolderManager.EnsureGeneratedFolder();
        }
    }
}
