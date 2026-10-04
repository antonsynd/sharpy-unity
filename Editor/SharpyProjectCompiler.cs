namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// The one compile path. Every .spy under Assets/ is compiled as a single
    /// sharpyc project into a staging folder under Library/, and only a
    /// successful build is synced into the generated folder, so a failed build
    /// never touches Assets/.
    /// </summary>
    public static class SharpyProjectCompiler
    {
        // Project-relative. sharpyc also writes bin/ and obj/ here.
        internal const string LibraryFolder = "Library/Sharpy";
        internal const string ProjectFileName = "unity.spyproj";
        internal const string StagingFolderName = "emit";

        // Relative to the .spyproj's folder.
        internal const string SourceGlob = "../../Assets/**/*.spy";
        internal const string SourceRoot = "../../Assets";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static bool compiling;

        /// <summary>
        /// Compiles every .spy and syncs the generated folder. Returns false
        /// when nothing was synced (compile errors, no compiler, a compile
        /// already running). <paramref name="force"/> is for the explicit
        /// "Recompile All": it will bypass the up-to-date check once there is one.
        /// </summary>
        public static bool Compile(bool force = false)
        {
            // The Refresh at the end imports the generated scripts, which runs
            // the asset postprocessor again inside this call.
            if (compiling)
            {
                return false;
            }

            compiling = true;

            try
            {
                return CompileAndSync(force);
            }
            finally
            {
                compiling = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool CompileAndSync(bool force)
        {
            SharpySettings.RefreshFromDiskIfChanged();
            var settings = SharpySettings.instance;

            string root = ProjectRoot();
            string generatedFolder = NormalizeFolder(settings.GeneratedOutputPath);
            List<string> spyAssets = FindSpyAssets(root);

            string folderProblem = CheckGeneratedFolder(generatedFolder, spyAssets);

            if (folderProblem != null)
            {
                Debug.LogError("[Sharpy] " + folderProblem);
                return false;
            }

            // Up-to-date check (persisted fingerprint) goes here; `force` skips it.

            var staged = new Dictionary<string, string>();

            if (NeedsCompiler(spyAssets) && !BuildProject(root, settings, spyAssets, staged))
            {
                return false;
            }

            var files = new List<SharpyGeneratedFile>();

            foreach (KeyValuePair<string, string> entry in staged)
            {
                files.Add(new SharpyGeneratedFile
                {
                    RelativePath = SharpyGeneratedFolderManager.SpyAssetToGeneratedRelative(entry.Key),
                    // Paths stay absolute: Unity resolves a relative #line path
                    // against the generated .cs's own folder, in its error
                    // messages and in stack traces alike. It shows absolute
                    // paths inside the project as Assets/... anyway.
                    Content = SharpyLineDirectives.Rewrite(entry.Value, null, settings.SourceMappedErrors),
                });
            }

            SharpySyncResult sync = SharpyGeneratedSync.Sync(Path.Combine(root, generatedFolder), files);

            if (sync.Changed)
            {
                AssetDatabase.Refresh();
            }

            return true;
        }

        // Writes the .spyproj, runs sharpyc into a clean staging folder, logs
        // its diagnostics, and on success reads the staged C# keyed by .spy asset.
        private static bool BuildProject(
            string root, SharpySettings settings, ICollection<string> spyAssets, Dictionary<string, string> staged)
        {
            SharpyBinaryDownloader.EnsureVersionChecked();
            SharpyCompilerBridge.WarnOnNamespaceCollision(settings.RootNamespace);

            // Absolute and spelled from `root`: sharpyc prints diagnostic paths
            // as it was given them, and the parser strips exactly this prefix.
            string libraryDir = root + "/" + LibraryFolder;
            string projectFile = libraryDir + "/" + ProjectFileName;
            string stagingDir = libraryDir + "/" + StagingFolderName;

            string projectText = SharpyProjectFile.Build(
                settings.RootNamespace,
                new[] { SourceGlob },
                ToAbsolute(root, settings.AdditionalModulePaths),
                ToAbsolute(root, SharpyReferenceProvider.GetReferences(settings)),
                SourceRoot);

            Directory.CreateDirectory(libraryDir);
            WriteIfChanged(projectFile, projectText);

            // A failed build (exit 2) can leave files behind.
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayProgressBar("Sharpy", $"Compiling {spyAssets.Count} .spy file(s)...", 0.5f);
            }

            CompileResult result = SharpyCompilerBridge.CompileProject(projectFile, stagingDir, root);
            var sources = new HashSet<string>(spyAssets, StringComparer.Ordinal);

            foreach (SharpyDiagnostic diagnostic in result.Diagnostics)
            {
                SharpyDiagnosticLog.Log(diagnostic);
            }

            if (!ShouldSync(result.ExitCode))
            {
                return false;
            }

            if (!Directory.Exists(stagingDir))
            {
                return true;
            }

            foreach (string path in Directory.GetFiles(stagingDir, "*.cs", SearchOption.AllDirectories))
            {
                string stagedPath = path.Substring(stagingDir.Length + 1).Replace('\\', '/');
                string spyAsset = SharpyGeneratedFolderManager.StagedToSpyAsset(stagedPath);

                if (!sources.Contains(spyAsset))
                {
                    Debug.LogError(
                        $"[Sharpy] sharpyc wrote \"{stagedPath}\", which does not mirror a .spy under Assets/. "
                        + "This package needs a sharpyc whose `project --emit-cs-to` mirrors the source tree "
                        + $"(newer than 0.21.0). Generated files in {settings.GeneratedOutputPath} were left as they were.");
                    return false;
                }

                staged[spyAsset] = File.ReadAllText(path);
            }

            return true;
        }

        /// <summary>sharpyc rejects a project with no source files.</summary>
        internal static bool NeedsCompiler(ICollection<string> spyAssets)
        {
            return spyAssets.Count > 0;
        }

        /// <summary>
        /// 0 is the only success. 1 (Sharpy errors), 2 (generated C# does not
        /// compile), 3 (internal compiler error) and -1 (no compiler, timeout)
        /// all leave the generated folder as it was.
        /// </summary>
        internal static bool ShouldSync(int exitCode)
        {
            return exitCode == 0;
        }

        /// <summary>
        /// Why <paramref name="generatedFolder"/> cannot be synced, or null. The
        /// sync deletes every .cs in it that sharpyc did not produce, so it must
        /// be a folder of its own below Assets/ with no sources in it.
        /// </summary>
        internal static string CheckGeneratedFolder(string generatedFolder, IEnumerable<string> spyAssets)
        {
            string folder = NormalizeFolder(generatedFolder);

            if (!folder.StartsWith("Assets/", StringComparison.Ordinal)
                || Array.IndexOf(folder.Split('/'), "..") >= 0)
            {
                return $"Generated Output Path \"{generatedFolder}\" must be a folder inside Assets/ "
                    + "(e.g. Assets/SharpyGenerated). Nothing was generated.";
            }

            foreach (string spy in spyAssets)
            {
                if (spy.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Generated Output Path \"{generatedFolder}\" contains the source {spy}; "
                        + "it must be a folder of its own, since every .cs in it is replaced. Nothing was generated.";
                }
            }

            return null;
        }

        internal static string NormalizeFolder(string folder)
        {
            return (folder ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        // Unity's working directory is the project root.
        internal static string ProjectRoot()
        {
            return Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
        }

        // Every .spy under Assets/ as a sorted, project-relative asset path:
        // the same set the spyproj's glob gives sharpyc.
        private static List<string> FindSpyAssets(string root)
        {
            var result = new List<string>();
            string assetsDir = root + "/Assets";

            if (!Directory.Exists(assetsDir))
            {
                return result;
            }

            foreach (string path in Directory.GetFiles(assetsDir, "*.spy", SearchOption.AllDirectories))
            {
                // Windows also matches "*.spy" against longer extensions.
                if (path.EndsWith(".spy", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("Assets/" + path.Substring(assetsDir.Length + 1).Replace('\\', '/'));
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        // sharpyc resolves relative paths against the .spyproj's folder; the
        // settings mean the project root.
        private static List<string> ToAbsolute(string root, IEnumerable<string> paths)
        {
            var result = new List<string>();

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string trimmed = path.Trim();
                result.Add(Path.IsPathRooted(trimmed) ? trimmed : Path.GetFullPath(Path.Combine(root, trimmed)));
            }

            return result;
        }

        private static void WriteIfChanged(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text)
            {
                return;
            }

            File.WriteAllText(path, text, Utf8NoBom);
        }
    }
}
