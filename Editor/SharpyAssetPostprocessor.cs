namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using UnityEditor;
    using UnityEngine;

    public sealed class SharpyAssetPostprocessor : AssetPostprocessor
    {
        private static readonly Dictionary<string, string> FileHashes = new Dictionary<string, string>();

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
            string fullPath = Path.GetFullPath(spyAssetPath);
            string hash = ComputeFileHash(fullPath);

            if (hash != null
                && FileHashes.TryGetValue(spyAssetPath, out string cachedHash)
                && cachedHash == hash)
            {
                return;
            }

            string outputPath = SharpyGeneratedFolderManager.GetGeneratedPath(spyAssetPath);
            string outputDir = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var result = SharpyCompilerBridge.CompileFile(spyAssetPath, outputPath);

            if (result.Success)
            {
                if (hash != null)
                {
                    FileHashes[spyAssetPath] = hash;
                }

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
            FileHashes.Remove(spyAssetPath);

            string generatedPath = SharpyGeneratedFolderManager.GetGeneratedPath(spyAssetPath);

            if (File.Exists(generatedPath))
            {
                AssetDatabase.DeleteAsset(generatedPath);
            }
        }

        private static string ComputeFileHash(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            using var md5 = MD5.Create();
            using var stream = File.OpenRead(fullPath);
            byte[] hashBytes = md5.ComputeHash(stream);
            return System.BitConverter.ToString(hashBytes);
        }
    }
}
