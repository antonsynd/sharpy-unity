using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
    public static class SharpyMenuItems
    {
        [MenuItem("Assets/Sharpy/Recompile All", false, 1000)]
        public static void RecompileAll()
        {
            string[] spyGuids = AssetDatabase.FindAssets("", new[] { "Assets" });
            int compiled = 0;

            foreach (string guid in spyGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetExtension(path) != ".spy")
                {
                    continue;
                }

                string outputPath = SharpyGeneratedFolderManager.GetGeneratedPath(path);
                string outputDir = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                EditorUtility.DisplayProgressBar(
                    "Sharpy — Recompiling",
                    path,
                    (float)compiled / spyGuids.Length);

                SharpyCompilerBridge.CompileFile(path, outputPath);
                compiled++;
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            Debug.Log($"[Sharpy] Recompiled {compiled} .spy file(s).");
        }

        [MenuItem("Assets/Sharpy/Recompile Selected", false, 1001)]
        public static void RecompileSelected()
        {
            int compiled = 0;

            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (Path.GetExtension(path) != ".spy")
                {
                    continue;
                }

                string outputPath = SharpyGeneratedFolderManager.GetGeneratedPath(path);
                string outputDir = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                SharpyCompilerBridge.CompileFile(path, outputPath);
                compiled++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Sharpy] Recompiled {compiled} selected .spy file(s).");
        }

        [MenuItem("Assets/Sharpy/Recompile Selected", true)]
        private static bool RecompileSelectedValidation()
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

                string generatedPath = SharpyGeneratedFolderManager.GetGeneratedPath(path);

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
