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

        public static string GetGeneratedPath(string spyAssetPath)
        {
            string outputRoot = SharpySettings.instance.GeneratedOutputPath;
            string relativePath = spyAssetPath;

            if (relativePath.StartsWith("Assets/"))
            {
                relativePath = relativePath.Substring("Assets/".Length);
            }

            string csFileName = Path.ChangeExtension(relativePath, ".cs");
            return Path.Combine(outputRoot, csFileName);
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
