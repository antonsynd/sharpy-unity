using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
    public sealed class SharpyAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!SharpySettings.instance.AutoCompileOnSave)
            {
                return;
            }

            foreach (string asset in importedAssets)
            {
                if (Path.GetExtension(asset) == ".spy")
                {
                    CompileSpyFile(asset);
                }
            }

            foreach (string asset in deletedAssets)
            {
                if (Path.GetExtension(asset) == ".spy")
                {
                    DeleteGeneratedFile(asset);
                }
            }

            for (int i = 0; i < movedAssets.Length; i++)
            {
                if (Path.GetExtension(movedAssets[i]) == ".spy")
                {
                    DeleteGeneratedFile(movedFromAssetPaths[i]);
                    CompileSpyFile(movedAssets[i]);
                }
            }
        }

        private static void CompileSpyFile(string spyAssetPath)
        {
            string outputPath = SharpyGeneratedFolderManager.GetGeneratedPath(spyAssetPath);
            string outputDir = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var result = SharpyCompilerBridge.CompileFile(spyAssetPath, outputPath);

            if (result.Success)
            {
                AssetDatabase.ImportAsset(outputPath);
            }
            else
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    switch (diagnostic.ToUnityLogType())
                    {
                        case LogType.Error:
                            Debug.LogError($"[Sharpy] {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Code}: {diagnostic.Message}");
                            break;
                        case LogType.Warning:
                            Debug.LogWarning($"[Sharpy] {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Code}: {diagnostic.Message}");
                            break;
                        default:
                            Debug.Log($"[Sharpy] {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Code}: {diagnostic.Message}");
                            break;
                    }
                }

                if (result.Diagnostics.Count == 0 && !string.IsNullOrEmpty(result.Stderr))
                {
                    Debug.LogError($"[Sharpy] Compilation failed: {result.Stderr}");
                }
            }
        }

        private static void DeleteGeneratedFile(string spyAssetPath)
        {
            string generatedPath = SharpyGeneratedFolderManager.GetGeneratedPath(spyAssetPath);

            if (File.Exists(generatedPath))
            {
                AssetDatabase.DeleteAsset(generatedPath);
            }
        }
    }
}
