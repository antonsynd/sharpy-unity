namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
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

        /// <summary>
        /// The module-named path of a .spy's generated script. A script named
        /// after its MonoBehaviour class is elsewhere; see <see cref="FindGeneratedPath"/>.
        /// </summary>
        public static string GetGeneratedPath(string spyAssetPath)
        {
            string outputRoot = SharpySettings.instance.GeneratedOutputPath;
            return Path.Combine(outputRoot, SpyAssetToGeneratedRelative(spyAssetPath));
        }

        /// <summary>
        /// The generated script of a .spy as an asset path, found through the
        /// GUID its .meta derives from the .spy's, so a script named after its
        /// class is found too. Falls back to <see cref="GetGeneratedPath"/>.
        /// </summary>
        public static string FindGeneratedPath(string spyAssetPath)
        {
            string spyGuid = AssetDatabase.AssetPathToGUID(spyAssetPath);

            if (!string.IsNullOrEmpty(spyGuid))
            {
                string generated = AssetDatabase.GUIDToAssetPath(SharpyGeneratedMeta.GuidFor(spyGuid));

                if (!string.IsNullOrEmpty(generated))
                {
                    return generated;
                }
            }

            return GetGeneratedPath(spyAssetPath);
        }

        /// <summary>
        /// Assets/Scripts/Core/greeting.spy → Scripts/Core/greeting.cs, the
        /// script's path under the generated folder. With
        /// <paramref name="scriptClass"/>, the file is named after that class
        /// instead (Scripts/Smoke/SmokeBehaviour.cs), which Unity needs to
        /// bind a MonoBehaviour or ScriptableObject to the script. A path
        /// outside Assets/ is kept whole.
        /// </summary>
        internal static string SpyAssetToGeneratedRelative(string spyAssetPath, string scriptClass = null)
        {
            string relativePath = spyAssetPath.Replace('\\', '/');

            if (relativePath.StartsWith(AssetsPrefix))
            {
                relativePath = relativePath.Substring(AssetsPrefix.Length);
            }

            if (string.IsNullOrEmpty(scriptClass))
            {
                return Path.ChangeExtension(relativePath, ".cs");
            }

            int slash = relativePath.LastIndexOf('/');
            return relativePath.Substring(0, slash + 1) + scriptClass + ".cs";
        }

        /// <summary>
        /// The generated path of every .spy: named after its class when
        /// <paramref name="scriptClasses"/> has one, otherwise after the module.
        /// A class name that would take another script's path, or that another
        /// .spy in the same folder also uses (file names compare
        /// case-insensitively), keeps the module name, with a warning.
        /// </summary>
        internal static Dictionary<string, string> GeneratedRelativePaths(
            IEnumerable<string> spyAssets, IDictionary<string, string> scriptClasses, List<string> warnings)
        {
            var sorted = new List<string>(spyAssets);
            sorted.Sort(StringComparer.Ordinal);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Module-named scripts first, so a class name never displaces one.
            foreach (string spy in sorted)
            {
                if (!scriptClasses.ContainsKey(spy))
                {
                    string path = SpyAssetToGeneratedRelative(spy);
                    result[spy] = path;
                    taken[path] = spy;
                }
            }

            // Class-named paths claimed by more than one .spy go to none of them.
            var claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string spy in sorted)
            {
                if (scriptClasses.TryGetValue(spy, out string scriptClass))
                {
                    string path = SpyAssetToGeneratedRelative(spy, scriptClass);

                    if (!claims.TryGetValue(path, out List<string> claimants))
                    {
                        claimants = new List<string>();
                        claims[path] = claimants;
                    }

                    claimants.Add(spy);
                }
            }

            foreach (string spy in sorted)
            {
                if (!scriptClasses.TryGetValue(spy, out string scriptClass))
                {
                    continue;
                }

                string path = SpyAssetToGeneratedRelative(spy, scriptClass);
                List<string> claimants = claims[path];
                string other = taken.TryGetValue(path, out string module) ? module
                    : claimants.Count > 1 ? claimants.Find(c => c != spy)
                    : null;

                if (other != null)
                {
                    warnings.Add(
                        $"[Sharpy] {spy}: its script cannot be named {scriptClass}.cs, which {other} also needs, "
                        + $"so the {scriptClass} component cannot be added. Rename the class or one of the files.");
                    path = SpyAssetToGeneratedRelative(spy);
                }

                result[spy] = path;
            }

            return result;
        }

        /// <summary>
        /// Inverse of <see cref="SpyAssetToGeneratedRelative"/> for module-named
        /// scripts under Assets/: Scripts/Core/greeting.cs → Assets/Scripts/Core/greeting.spy.
        /// A script named after its class maps back only through its GUID.
        /// </summary>
        internal static string GeneratedRelativeToSpyAsset(string generatedRelativePath)
        {
            return AssetsPrefix + Path.ChangeExtension(generatedRelativePath.Replace('\\', '/'), ".spy");
        }

        /// <summary>
        /// The .spy asset a file in sharpyc's --emit-cs-to folder came from.
        /// sharpyc mirrors each source's path relative to the spyproj's
        /// &lt;SourceRoot&gt; (../../Assets), staging Assets/Scripts/Core/greeting.spy
        /// as Scripts/Core/greeting.cs. A sharpyc that ignores SourceRoot mirrors
        /// it relative to the .spyproj's folder with the leading "../" dropped,
        /// as Assets/Scripts/Core/greeting.cs; both forms are accepted.
        /// </summary>
        internal static string StagedToSpyAsset(string stagedRelativePath)
        {
            string path = stagedRelativePath.Replace('\\', '/');

            if (!path.StartsWith(AssetsPrefix))
            {
                path = AssetsPrefix + path;
            }

            return Path.ChangeExtension(path, ".spy");
        }
    }
}
