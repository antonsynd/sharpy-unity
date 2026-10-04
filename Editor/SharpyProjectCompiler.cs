namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Compilation;
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
        internal const string FingerprintFileName = "fingerprint";

        // Relative to the .spyproj's folder.
        internal const string SourceGlob = "../../Assets/**/*.spy";
        internal const string SourceRoot = "../../Assets";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static bool compiling;
        private static bool compileRequested;

        /// <summary>
        /// Compiles every .spy and syncs the generated folder. Returns true when
        /// the generated folder is up to date: synced now, or already matching
        /// the stored fingerprint (skipped unless <paramref name="force"/>).
        /// False when nothing was synced (compile errors, no compiler, a
        /// compile already running).
        /// </summary>
        public static bool Compile(bool force = false)
        {
            // The Refresh at the end imports the generated scripts, which runs
            // the asset postprocessor again inside this call. A .spy picked up
            // by that import is compiled once this pass is done.
            if (compiling)
            {
                compileRequested = true;
                return false;
            }

            compiling = true;

            try
            {
                bool result;
                int passes = 0;

                do
                {
                    compileRequested = false;
                    result = CompileAndSync(force);
                }
                while (compileRequested && ++passes < 3);

                return result;
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

            string projectText = SharpyProjectFile.Build(
                settings.RootNamespace,
                new[] { SourceGlob },
                ToAbsolute(root, settings.AdditionalModulePaths),
                ToAbsolute(root, SharpyReferenceProvider.GetReferences(settings)),
                SourceRoot);

            string fingerprintPath = root + "/" + LibraryFolder + "/" + FingerprintFileName;
            string generatedDir = root + "/" + generatedFolder;
            string fingerprint = SharpyFingerprint.Compute(
                projectText, CompilerVersion(settings), SyncSettings(settings), HashSources(root, spyAssets));

            if (!force && SharpyFingerprint.IsUpToDate(
                    File.Exists(fingerprintPath) ? File.ReadAllText(fingerprintPath) : null,
                    fingerprint,
                    path => File.Exists(generatedDir + "/" + path)))
            {
                return true;
            }

            var staged = new Dictionary<string, string>();

            if (NeedsCompiler(spyAssets) && !BuildProject(root, settings, projectText, spyAssets, staged))
            {
                return false;
            }

            var warnings = new List<string>();
            Dictionary<string, string> scriptClasses = SharpyScriptClasses.Find(staged, IsUnityObjectType, warnings);
            Dictionary<string, string> paths = SharpyGeneratedFolderManager.GeneratedRelativePaths(
                staged.Keys, scriptClasses, warnings);

            foreach (string warning in warnings)
            {
                Debug.LogWarning(warning);
            }

            var files = new List<SharpyGeneratedFile>();

            foreach (KeyValuePair<string, string> entry in staged)
            {
                string spyGuid = SpyGuid(root, entry.Key);

                files.Add(new SharpyGeneratedFile
                {
                    RelativePath = paths[entry.Key],
                    // Paths stay absolute: Unity resolves a relative #line path
                    // against the generated .cs's own folder, in its error
                    // messages and in stack traces alike. It shows absolute
                    // paths inside the project as Assets/... anyway.
                    Content = SharpyLineDirectives.Rewrite(entry.Value, null, settings.SourceMappedErrors),
                    // Tied to the .spy, not to the script's path or name, so
                    // scene references survive a regenerate, a fresh clone, a
                    // move or a class rename.
                    MetaGuid = spyGuid == null ? null : SharpyGeneratedMeta.GuidFor(spyGuid),
                });
            }

            SharpySyncResult sync = SharpyGeneratedSync.Sync(generatedDir, files);

            // Only after a successful sync: a failed compile keeps the old
            // fingerprint (or none), so the next check tries again.
            Directory.CreateDirectory(root + "/" + LibraryFolder);
            File.WriteAllText(
                fingerprintPath,
                SharpyFingerprint.Format(fingerprint, files.ConvertAll(file => file.RelativePath)),
                Utf8NoBom);

            bool stdlibInstalled = IsStdlibInstalled(CompilationPipeline.GetPrecompiledAssemblyPaths(
                CompilationPipeline.PrecompiledAssemblySources.UserAssembly));

            foreach (string warning in StdlibWarnings(staged, stdlibInstalled))
            {
                Debug.LogWarning(warning);
            }

            if (sync.Changed)
            {
                AssetDatabase.Refresh();
            }

            return true;
        }

        // Writes the .spyproj, runs sharpyc into a clean staging folder, logs
        // its diagnostics, and on success reads the staged C# keyed by .spy asset.
        private static bool BuildProject(
            string root,
            SharpySettings settings,
            string projectText,
            ICollection<string> spyAssets,
            Dictionary<string, string> staged)
        {
            SharpyBinaryDownloader.EnsureVersionChecked();
            SharpyCompilerBridge.WarnOnNamespaceCollision(settings.RootNamespace);

            // Absolute and spelled from `root`: sharpyc prints diagnostic paths
            // as it was given them, and the parser strips exactly this prefix.
            string libraryDir = root + "/" + LibraryFolder;
            string projectFile = libraryDir + "/" + ProjectFileName;
            string stagingDir = libraryDir + "/" + StagingFolderName;

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

        /// <summary>Whether Sharpy.Stdlib.dll is among the project's own precompiled assemblies.</summary>
        internal static bool IsStdlibInstalled(IEnumerable<string> precompiledAssemblyPaths)
        {
            foreach (string path in precompiledAssemblyPaths)
            {
                if (string.Equals(Path.GetFileName(path), "Sharpy.Stdlib.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One warning per .spy whose generated C# needs stdlib modules, unless
        /// Sharpy.Stdlib.dll is installed. sharpyc compiles against its own
        /// copy, so without this Unity only reports CS0246/CS0234 in the
        /// generated code.
        /// </summary>
        internal static List<string> StdlibWarnings(IDictionary<string, string> generatedBySpy, bool stdlibInstalled)
        {
            var warnings = new List<string>();

            if (stdlibInstalled)
            {
                return warnings;
            }

            var spyAssets = new List<string>(generatedBySpy.Keys);
            spyAssets.Sort(StringComparer.Ordinal);

            foreach (string spy in spyAssets)
            {
                List<string> modules = SharpyStdlibDetector.FindModules(generatedBySpy[spy]);

                if (modules.Count > 0)
                {
                    warnings.Add(SharpyStdlibDetector.FormatWarning(spy, modules));
                }
            }

            return warnings;
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

        /// <summary>
        /// Compiles when anything the output depends on changed since the last
        /// successful compile: a .spy edited outside Unity while it was not
        /// focused, SharpySettings.asset edited on disk, a new compiler at the
        /// custom path. Runs on editor load and when the editor regains focus.
        /// </summary>
        internal static void CompileIfStale()
        {
            SharpySettings.RefreshFromDiskIfChanged();

            // A compile ends in a script recompile and domain reload.
            if (!SharpySettings.instance.AutoCompileOnSave
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                return;
            }

            Compile(force: false);
        }

        private const string CompilerVersionKeyPrefix = "Sharpy.CompilerVersion:";

        // The managed install is the pinned version by construction. A custom
        // compiler is asked once per session, and again when its binary changes.
        private static string CompilerVersion(SharpySettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.CustomCompilerPath))
            {
                return "managed " + SharpyToolchain.Version;
            }

            string path = SharpyCompilerBridge.GetCompilerPath();

            if (!File.Exists(path))
            {
                return "missing " + path;
            }

            string stamp = path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|";
            string cached = SessionState.GetString(CompilerVersionKeyPrefix + path, "");

            if (cached.StartsWith(stamp, StringComparison.Ordinal))
            {
                return cached.Substring(stamp.Length);
            }

            string version = SharpyCompilerBridge.GetCompilerVersion();
            SessionState.SetString(CompilerVersionKeyPrefix + path, stamp + version);
            return version;
        }

        // Settings the sync applies after sharpyc, which the spyproj does not hold.
        private static string SyncSettings(SharpySettings settings)
        {
            return $"{NormalizeFolder(settings.GeneratedOutputPath)}|sourceMappedErrors={settings.SourceMappedErrors}";
        }

        private static Dictionary<string, string> HashSources(string root, IEnumerable<string> spyAssets)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string spy in spyAssets)
            {
                hashes[spy] = SharpyFingerprint.Hash(File.ReadAllBytes(root + "/" + spy));
            }

            return hashes;
        }

        // The AssetDatabase knows every imported .spy; the .meta on disk
        // covers one it has not imported yet (batch mode, a fresh clone).
        private static string SpyGuid(string root, string spyAsset)
        {
            string guid = AssetDatabase.AssetPathToGUID(spyAsset);

            if (!string.IsNullOrEmpty(guid))
            {
                return guid;
            }

            string metaPath = root + "/" + spyAsset + ".meta";

            return File.Exists(metaPath) && SharpyGeneratedMeta.TryReadGuid(File.ReadAllText(metaPath), out guid)
                ? guid
                : null;
        }

        // A base class from a package or plugin (e.g. a NetworkBehaviour).
        private static bool IsUnityObjectType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);

                if (type != null)
                {
                    return typeof(MonoBehaviour).IsAssignableFrom(type)
                        || typeof(ScriptableObject).IsAssignableFrom(type);
                }
            }

            return false;
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
