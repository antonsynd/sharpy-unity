namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Net.Http;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class SharpyBinaryDownloader
    {
        static SharpyBinaryDownloader()
        {
            CleanupLegacyPackageBinaries();

            // Deferred so SharpySettings isn't loaded during InitializeOnLoad.
            EditorApplication.delayCall += AutoInstallIfNeeded;
        }

        private static void AutoInstallIfNeeded()
        {
            // A custom compiler path opts out of the managed install; a
            // version mismatch there only warns.
            if (!string.IsNullOrWhiteSpace(SharpySettings.instance.CustomCompilerPath))
            {
                EnsureVersionChecked();
                return;
            }

            if (!IsCompilerInstalled())
            {
                if (Application.isBatchMode)
                {
                    Debug.Log("[Sharpy] Compiler not installed; downloading automatically (batch mode).");
                    DownloadCompilerAsync();
                }
                else
                {
                    PromptDownload();
                }

                return;
            }

            EnsureVersionChecked();
        }

        private const string VersionCheckedSessionKey = "Sharpy.VersionGuardChecked";

        // Compares the active compiler's version against the pin, once per
        // editor session. Managed installs offer a re-download on mismatch;
        // custom paths only warn (the user opted out of management).
        internal static void EnsureVersionChecked()
        {
            if (SessionState.GetBool(VersionCheckedSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(VersionCheckedSessionKey, true);

            bool isCustom = !string.IsNullOrWhiteSpace(SharpySettings.instance.CustomCompilerPath);

            if (!File.Exists(SharpyCompilerBridge.GetCompilerPath()))
            {
                return;
            }

            string versionLine = SharpyCompilerBridge.GetCompilerVersion();
            string semver = SharpyCompilerBridge.ExtractSemver(versionLine);

            if (semver == null)
            {
                Debug.LogWarning($"[Sharpy] Could not determine compiler version (got \"{versionLine}\").");
                return;
            }

            if (SharpyCompilerBridge.MatchesPin(semver))
            {
                return;
            }

            if (isCustom)
            {
                Debug.LogWarning(
                    $"[Sharpy] Custom compiler is {semver}, but this package pins {SharpyToolchain.Version}. "
                    + "Generated code may not match the bundled Sharpy.Core.dll.");
                return;
            }

            if (Application.isBatchMode)
            {
                Debug.Log($"[Sharpy] Installed compiler is {semver}; downloading pinned {SharpyToolchain.Version}.");
                DownloadCompilerAsync();
                return;
            }

            bool redownload = EditorUtility.DisplayDialog(
                "Sharpy Compiler Version Mismatch",
                $"The installed Sharpy compiler is {semver}, but this package pins {SharpyToolchain.Version}.\n\n"
                + "Download the pinned version?",
                "Download",
                "Not Now");

            if (redownload)
            {
                DownloadCompilerAsync();
            }
        }

        public static bool IsCompilerInstalled()
        {
            string compilerPath = SharpyCompilerBridge.GetManagedCompilerPath();
            return File.Exists(compilerPath);
        }

        [MenuItem("Assets/Sharpy/Download Compiler", false, 2000)]
        public static void PromptDownload()
        {
            if (Application.isBatchMode)
            {
                DownloadCompilerAsync();
                return;
            }

            if (IsCompilerInstalled())
            {
                bool redownload = EditorUtility.DisplayDialog(
                    "Sharpy Compiler",
                    "The Sharpy compiler is already installed. Re-download?",
                    "Re-download",
                    "Cancel");

                if (!redownload)
                {
                    return;
                }
            }
            else
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Sharpy Compiler Required",
                    "The Sharpy compiler binary is not installed. Download it now?\n\n"
                    + $"Version: {SharpyToolchain.Version}\n"
                    + $"Platform: {SharpyToolchain.GetPlatformRid()}",
                    "Download",
                    "Cancel");

                if (!proceed)
                {
                    return;
                }
            }

            DownloadCompilerAsync();
        }

        // Pre-0.16 versions of this package extracted the compiler inside the
        // package itself, where Unity imported every file as an asset. Only
        // mutable (embedded/local) installs can be cleaned; anything else is
        // left alone.
        private static void CleanupLegacyPackageBinaries()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(SharpyBinaryDownloader).Assembly);

                if (package == null
                    || (package.source != UnityEditor.PackageManager.PackageSource.Embedded
                        && package.source != UnityEditor.PackageManager.PackageSource.Local))
                {
                    return;
                }

                string legacyDir = Path.Combine(package.resolvedPath, "Editor", "Binaries");

                if (!Directory.Exists(legacyDir))
                {
                    return;
                }

                Directory.Delete(legacyDir, true);

                string metaFile = legacyDir + ".meta";

                if (File.Exists(metaFile))
                {
                    File.Delete(metaFile);
                }

                Debug.Log("[Sharpy] Removed legacy in-package compiler binaries; the compiler now installs under Library/SharpyCompiler.");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Sharpy] Could not remove legacy Editor/Binaries directory: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads and installs the pinned compiler, blocking the calling
        /// thread until done. Safe on the main thread: the download runs on
        /// the thread pool, so nothing waits on a continuation queued to
        /// Unity's synchronization context. Returns false (and logs) on failure.
        /// </summary>
        public static bool InstallBlocking()
        {
            string rid = SharpyToolchain.GetPlatformRid();
            string installDir = Path.GetDirectoryName(SharpyCompilerBridge.GetManagedCompilerPath());

            try
            {
                byte[] archive = Task.Run(() => DownloadArchiveAsync(rid)).GetAwaiter().GetResult();
                InstallArchive(archive, rid, installDir);
                LogInstalled(rid);
                return true;
            }
            catch (Exception ex)
            {
                LogDownloadFailure(ex);
                return false;
            }
        }

        // Interactive wrapper around the same steps as InstallBlocking: the
        // work runs on the thread pool while the main thread keeps the
        // progress bar up and the editor responsive.
        private static async void DownloadCompilerAsync()
        {
            string rid = SharpyToolchain.GetPlatformRid();
            string installDir = Path.GetDirectoryName(SharpyCompilerBridge.GetManagedCompilerPath());

            try
            {
                EditorUtility.DisplayProgressBar("Sharpy", $"Downloading compiler for {rid}...", 0.1f);

                byte[] archive = await Task.Run(() => DownloadArchiveAsync(rid));

                EditorUtility.DisplayProgressBar("Sharpy", "Extracting compiler...", 0.7f);

                await Task.Run(() => InstallArchive(archive, rid, installDir));

                EditorUtility.DisplayProgressBar("Sharpy", "Done!", 1.0f);
                EditorUtility.ClearProgressBar();

                LogInstalled(rid);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                LogDownloadFailure(ex);

                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "Sharpy Download Failed",
                        $"Failed to download the compiler:\n{ex.Message}\n\n"
                        + "You can retry via Assets > Sharpy > Download Compiler.",
                        "OK");
                }
            }
        }

        private static async Task<byte[]> DownloadArchiveAsync(string rid)
        {
            string url = $"{SharpyToolchain.ReleaseUrlBase}sharpyc-{rid}.{ArchiveExtension(rid)}";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            return await client.GetByteArrayAsync(url).ConfigureAwait(false);
        }

        private static string ArchiveExtension(string rid)
        {
            return rid.StartsWith("win") ? "zip" : "tar.gz";
        }

        private static void LogInstalled(string rid)
        {
            Debug.Log($"[Sharpy] Compiler {SharpyToolchain.Version} installed for {rid} under Library/SharpyCompiler.");
        }

        private static void LogDownloadFailure(Exception ex)
        {
            if (Application.isBatchMode)
            {
                // A warning, not an error: an error log would fail any
                // batch test run that merely lacked network access.
                Debug.LogWarning($"[Sharpy] Failed to download compiler: {ex.Message}");
                return;
            }

            Debug.LogError($"[Sharpy] Failed to download compiler: {ex.Message}");
        }

        // Extracts into a sibling staging directory and renames it into
        // place, so an interrupted or failed install never leaves a partial
        // folder that IsCompilerInstalled would accept. The staging
        // directory shares the destination's parent, so the rename never
        // crosses volumes. Throws on failure; any previous install is kept.
        internal static void InstallArchive(byte[] archive, string rid, string installDir)
        {
            string binaryName = rid.StartsWith("win") ? "sharpyc.exe" : "sharpyc";
            string suffix = Guid.NewGuid().ToString("N");
            string stagingDir = installDir + ".staging-" + suffix;
            string previousDir = installDir + ".previous-" + suffix;

            Directory.CreateDirectory(stagingDir);

            try
            {
                using (var archiveStream = new MemoryStream(archive))
                {
                    if (ArchiveExtension(rid) == "zip")
                    {
                        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                        zip.ExtractToDirectory(stagingDir);
                    }
                    else
                    {
                        using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
                        ExtractTar(gzipStream, stagingDir);
                    }
                }

                if (!File.Exists(Path.Combine(stagingDir, binaryName)))
                {
                    throw new InvalidDataException($"The compiler archive does not contain {binaryName}.");
                }

                SetExecutablePermission(stagingDir, rid);

                // Directory.Move refuses an existing destination, so the old
                // install steps aside first and comes back if the swap fails.
                if (Directory.Exists(installDir))
                {
                    Directory.Move(installDir, previousDir);
                }

                try
                {
                    Directory.Move(stagingDir, installDir);
                }
                catch
                {
                    if (Directory.Exists(previousDir))
                    {
                        Directory.Move(previousDir, installDir);
                    }

                    throw;
                }
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
                TryDeleteDirectory(previousDir);
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Sharpy] Could not remove {dir}: {ex.Message}");
            }
        }

        private static void ExtractTar(Stream stream, string outputDir)
        {
            byte[] buffer = new byte[512];

            while (true)
            {
                int bytesRead = ReadFull(stream, buffer, 0, 512);

                if (bytesRead == 0)
                {
                    break;
                }

                if (IsAllZeros(buffer))
                {
                    break;
                }

                string name = System.Text.Encoding.ASCII.GetString(buffer, 0, 100).Trim('\0', ' ');

                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                string sizeStr = System.Text.Encoding.ASCII.GetString(buffer, 124, 12).Trim('\0', ' ');
                long size = Convert.ToInt64(sizeStr, 8);
                byte typeFlag = buffer[156];

                string fullPath = Path.Combine(outputDir, name);

                if (typeFlag == (byte)'5' || name.EndsWith("/"))
                {
                    Directory.CreateDirectory(fullPath);
                }
                else
                {
                    string dir = Path.GetDirectoryName(fullPath);

                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using var outFile = File.Create(fullPath);
                    long remaining = size;

                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(remaining, buffer.Length);
                        int read = ReadFull(stream, buffer, 0, toRead);

                        if (read == 0)
                        {
                            throw new EndOfStreamException($"The compiler archive is truncated inside {name}.");
                        }

                        outFile.Write(buffer, 0, read);
                        remaining -= read;
                    }

                    long padding = (512 - (size % 512)) % 512;

                    if (padding > 0)
                    {
                        byte[] skip = new byte[padding];
                        ReadFull(stream, skip, 0, (int)padding);
                    }
                }
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private static bool IsAllZeros(byte[] buffer)
        {
            foreach (byte b in buffer)
            {
                if (b != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SetExecutablePermission(string binariesDir, string rid)
        {
            if (rid.StartsWith("win"))
            {
                return;
            }

            string binaryPath = Path.Combine(binariesDir, "sharpyc");

            if (!File.Exists(binaryPath))
            {
                return;
            }

            try
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{binaryPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit(5000);
            }
            catch
            {
                Debug.LogWarning("[Sharpy] Could not set executable permission on sharpyc. You may need to run: chmod +x " + binaryPath);
            }
        }
    }
}
