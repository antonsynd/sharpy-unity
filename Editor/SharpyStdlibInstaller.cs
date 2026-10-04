namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;

    /// <summary>
    /// Opt-in install of Sharpy.Stdlib and its dependencies into
    /// <c>Assets/Plugins/Sharpy.Stdlib/</c>. Never automatic: the stdlib leans
    /// on native SQLite and reflection, so it is unsuitable for IL2CPP players.
    /// </summary>
    [InitializeOnLoad]
    internal static class SharpyStdlibInstaller
    {
        internal const string InstallFolder = "Assets/Plugins/Sharpy.Stdlib";
        internal const string StdlibDllName = "Sharpy.Stdlib.dll";

        // Already in Plugins/Sharpy.Core/ at the same assembly versions.
        internal static readonly string[] PackageShippedDlls =
        {
            "Sharpy.Core.dll",
            "System.Collections.Immutable.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
        };

        private const string VersionCheckedKey = "Sharpy.StdlibVersionChecked";

        static SharpyStdlibInstaller()
        {
            if (Application.isBatchMode)
            {
                // `-batchmode -quit` exits before delayCall runs.
                CheckInstalledVersionOncePerSession();
                return;
            }

            EditorApplication.delayCall += CheckInstalledVersionOncePerSession;
        }

        /// <summary>
        /// Whether an archive entry is copied into the project: DLLs only (no
        /// .pdb / .xml / .deps.json), minus what the package ships and minus
        /// any file name the project already has elsewhere (two copies of one
        /// assembly name either fail to compile or are silently deduplicated).
        /// </summary>
        internal static bool ShouldInstall(string entryName, ICollection<string> existingDllNames)
        {
            if (string.IsNullOrEmpty(entryName) || entryName.EndsWith("/") || entryName.EndsWith("\\"))
            {
                return false;
            }

            string fileName = Path.GetFileName(entryName.Replace('\\', '/'));
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (ContainsIgnoreCase(PackageShippedDlls, fileName))
            {
                return false;
            }

            return existingDllNames == null || !ContainsIgnoreCase(existingDllNames, fileName);
        }

        /// <summary>
        /// File names of precompiled assemblies outside <paramref name="installFolder"/>
        /// (paths may be project-relative or absolute, with either separator).
        /// </summary>
        internal static HashSet<string> ExistingDllNames(IEnumerable<string> precompiledPaths, string installFolder)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string folder = "/" + installFolder.Replace('\\', '/').Trim('/');

            foreach (string path in precompiledPaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string normalized = path.Replace('\\', '/');
                string dir = "/" + (Path.GetDirectoryName(normalized) ?? string.Empty).Replace('\\', '/').Trim('/');
                if (dir.EndsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                names.Add(Path.GetFileName(normalized));
            }

            return names;
        }

        /// <summary>
        /// True when an assembly version (e.g. 0.21.0.0) is the pinned toolchain
        /// version (e.g. "0.21.0"). The revision field and any pre-release or
        /// build suffix on the pin are ignored.
        /// </summary>
        internal static bool VersionMatchesPin(Version assemblyVersion, string pinnedVersion)
        {
            if (assemblyVersion == null || string.IsNullOrEmpty(pinnedVersion))
            {
                return false;
            }

            string core = pinnedVersion.Split('-', '+')[0];
            if (!Version.TryParse(core, out Version pinned))
            {
                return false;
            }

            return assemblyVersion.Major == pinned.Major
                && assemblyVersion.Minor == pinned.Minor
                && Math.Max(assemblyVersion.Build, 0) == Math.Max(pinned.Build, 0);
        }

        [MenuItem("Assets/Sharpy/Install Stdlib (experimental)")]
        private static void InstallFromMenu()
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Install Sharpy Stdlib (experimental)",
                $"Downloads Sharpy.Stdlib {SharpyToolchain.Version} and its dependencies into {InstallFolder}/.\n\n"
                + "Mono editor and player only: the stdlib relies on reflection (json, yaml, toml) and "
                + "native SQLite (sqlite3, which does not work in Unity), so IL2CPP builds are unsupported.",
                "Install",
                "Cancel");

            if (proceed)
            {
                InstallBlocking();
            }
        }

        /// <summary>
        /// Downloads the pinned stdlib archive and copies the selected DLLs into
        /// <see cref="InstallFolder"/>. Blocks the calling thread; the download
        /// runs on the thread pool, so this is safe on the main thread.
        /// Returns false (and logs) on failure.
        /// </summary>
        internal static bool InstallBlocking()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Sharpy", $"Downloading {SharpyToolchain.StdlibArchiveName}...", 0.2f);
                byte[] archive = Task.Run(DownloadArchiveAsync).GetAwaiter().GetResult();

                EditorUtility.DisplayProgressBar("Sharpy", "Installing Sharpy.Stdlib...", 0.7f);
                var existing = ExistingDllNames(
                    CompilationPipeline.GetPrecompiledAssemblyPaths(
                        CompilationPipeline.PrecompiledAssemblySources.UserAssembly),
                    InstallFolder);
                List<string> skipped;
                List<string> installed = ExtractSelected(archive, existing, out skipped);

                Debug.Log($"[Sharpy] Installed Sharpy.Stdlib {SharpyToolchain.Version} into {InstallFolder}/: "
                    + string.Join(", ", installed)
                    + (skipped.Count > 0 ? ". Skipped (already in the project): " + string.Join(", ", skipped) : "."));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sharpy] Failed to install Sharpy.Stdlib: {ex.Message}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
        }

        private static async Task<byte[]> DownloadArchiveAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            return await client.GetByteArrayAsync(SharpyToolchain.StdlibArchiveUrl).ConfigureAwait(false);
        }

        // Returns the installed file names; skipped lists DLLs left out because
        // the project already has an assembly of that name.
        private static List<string> ExtractSelected(byte[] archive, ICollection<string> existing, out List<string> skipped)
        {
            var installed = new List<string>();
            skipped = new List<string>();
            Directory.CreateDirectory(InstallFolder);

            using (var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (ShouldInstall(entry.FullName, existing))
                    {
                        entry.ExtractToFile(Path.Combine(InstallFolder, entry.Name), true);
                        installed.Add(entry.Name);
                    }
                    else if (ShouldInstall(entry.FullName, null))
                    {
                        skipped.Add(entry.Name);
                    }
                }
            }

            if (!installed.Contains(StdlibDllName) && !skipped.Contains(StdlibDllName))
            {
                throw new InvalidDataException($"{SharpyToolchain.StdlibArchiveName} did not contain {StdlibDllName}.");
            }

            return installed;
        }

        private static void CheckInstalledVersionOncePerSession()
        {
            if (SessionState.GetBool(VersionCheckedKey, false))
            {
                return;
            }

            SessionState.SetBool(VersionCheckedKey, true);
            CheckInstalledVersion();
        }

        internal static void CheckInstalledVersion()
        {
            string dll = Path.Combine(InstallFolder, StdlibDllName);
            if (!File.Exists(dll))
            {
                return;
            }

            try
            {
                // Reads the manifest without loading the assembly.
                Version installed = AssemblyName.GetAssemblyName(Path.GetFullPath(dll)).Version;
                if (!VersionMatchesPin(installed, SharpyToolchain.Version))
                {
                    Debug.LogWarning(
                        $"[Sharpy] {InstallFolder}/{StdlibDllName} is version {installed}, but the Sharpy "
                        + $"toolchain is pinned to {SharpyToolchain.Version}. Re-run "
                        + "Assets > Sharpy > Install Stdlib (experimental) to update it.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Sharpy] Could not read the version of {dll}: {ex.Message}");
            }
        }

        private static bool ContainsIgnoreCase(IEnumerable<string> names, string name)
        {
            foreach (string candidate in names)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
