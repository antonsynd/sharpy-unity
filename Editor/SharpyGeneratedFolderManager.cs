namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class SharpyGeneratedFolderManager
    {
        static SharpyGeneratedFolderManager()
        {
            EnsureGeneratedFolder();
        }

        public static void EnsureGeneratedFolder()
        {
            string outputPath = SharpySettings.instance.GeneratedOutputPath;

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            string gitignorePath = Path.Combine(outputPath, ".gitignore");

            if (!File.Exists(gitignorePath))
            {
                File.WriteAllText(gitignorePath, "*\n");
            }
        }

        private const string AssetsPrefix = "Assets/";

        public static string GetGeneratedPath(string spyAssetPath)
        {
            string outputRoot = SharpySettings.instance.GeneratedOutputPath;
            return Path.Combine(outputRoot, SpyAssetToGeneratedRelative(spyAssetPath));
        }

        /// <summary>
        /// Assets/Scripts/Core/greeting.spy → Scripts/Core/greeting.cs, the
        /// script's path under the generated folder. A path outside Assets/
        /// is kept whole.
        /// </summary>
        internal static string SpyAssetToGeneratedRelative(string spyAssetPath)
        {
            string relativePath = spyAssetPath.Replace('\\', '/');

            if (relativePath.StartsWith(AssetsPrefix))
            {
                relativePath = relativePath.Substring(AssetsPrefix.Length);
            }

            return Path.ChangeExtension(relativePath, ".cs");
        }

        /// <summary>
        /// Inverse of <see cref="SpyAssetToGeneratedRelative"/> for scripts
        /// under Assets/: Scripts/Core/greeting.cs → Assets/Scripts/Core/greeting.spy.
        /// </summary>
        internal static string GeneratedRelativeToSpyAsset(string generatedRelativePath)
        {
            return AssetsPrefix + Path.ChangeExtension(generatedRelativePath.Replace('\\', '/'), ".spy");
        }

        /// <summary>
        /// The .spy asset a file in sharpyc's --emit-cs-to folder came from.
        /// sharpyc mirrors each source's path relative to the .spyproj's folder
        /// (Library/Sharpy), dropping the leading "../" segments, so
        /// Assets/Scripts/Core/greeting.spy is staged as Assets/Scripts/Core/greeting.cs.
        /// </summary>
        internal static string StagedToSpyAsset(string stagedRelativePath)
        {
            return Path.ChangeExtension(stagedRelativePath.Replace('\\', '/'), ".spy");
        }

        public static void CleanEmptyDirectories()
        {
            string outputPath = SharpySettings.instance.GeneratedOutputPath;

            if (!Directory.Exists(outputPath))
            {
                return;
            }

            foreach (string dir in Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories))
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);

                    string metaFile = dir + ".meta";

                    if (File.Exists(metaFile))
                    {
                        File.Delete(metaFile);
                    }
                }
            }
        }
    }
}
