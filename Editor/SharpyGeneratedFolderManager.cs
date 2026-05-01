using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
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
