namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    public sealed class SharpyAssetPostprocessor : AssetPostprocessor
    {
        // Every .spy belongs to one project compile, so any number of changed
        // sources means one Compile().
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!TouchesSources(
                    SharpySettings.instance.GeneratedOutputPath,
                    importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
            {
                return;
            }

            SharpySettings.RefreshFromDiskIfChanged();

            if (SharpySettings.instance.AutoCompileOnSave)
            {
                SharpyProjectCompiler.Compile();
            }
        }

        /// <summary>
        /// Whether any of the paths is a .spy source. The generated folder is
        /// skipped: the sync's own writes there must not start another compile.
        /// </summary>
        internal static bool TouchesSources(string generatedFolder, params IEnumerable<string>[] assetPaths)
        {
            string generatedPrefix = SharpyProjectCompiler.NormalizeFolder(generatedFolder) + "/";

            foreach (IEnumerable<string> paths in assetPaths)
            {
                foreach (string path in paths)
                {
                    if (path.EndsWith(".spy", StringComparison.OrdinalIgnoreCase)
                        && !path.Replace('\\', '/').StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
