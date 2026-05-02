using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
    [InitializeOnLoad]
    public static class SharpyBinaryDownloader
    {
        private const string CompilerVersion = "v0.1.2";
        private const string ReleaseUrlBase = "https://github.com/antonsynd/sharpy/releases/download/";

        static SharpyBinaryDownloader()
        {
            if (!IsCompilerInstalled())
            {
                EditorApplication.delayCall += PromptDownload;
            }
        }

        public static bool IsCompilerInstalled()
        {
            string compilerPath = SharpyCompilerBridge.GetCompilerPath();
            return File.Exists(compilerPath);
        }

        [MenuItem("Assets/Sharpy/Download Compiler", false, 2000)]
        public static void PromptDownload()
        {
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
                    + $"Version: {CompilerVersion}\n"
                    + $"Platform: {GetPlatformRid()}",
                    "Download",
                    "Cancel");

                if (!proceed)
                {
                    return;
                }
            }

            DownloadCompilerAsync();
        }

        private static async void DownloadCompilerAsync()
        {
            string rid = GetPlatformRid();
            string extension = rid.StartsWith("win") ? "zip" : "tar.gz";
            string url = $"{ReleaseUrlBase}{CompilerVersion}/sharpyc-{rid}.{extension}";

            string packagePath = Path.GetFullPath("Packages/com.antonsynd.sharpy");
            string binariesDir = Path.Combine(packagePath, "Editor", "Binaries", rid);

            try
            {
                EditorUtility.DisplayProgressBar("Sharpy", $"Downloading compiler for {rid}...", 0.1f);

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);

                byte[] data = await client.GetByteArrayAsync(url);

                EditorUtility.DisplayProgressBar("Sharpy", "Extracting compiler...", 0.7f);

                if (Directory.Exists(binariesDir))
                {
                    Directory.Delete(binariesDir, true);
                }

                Directory.CreateDirectory(binariesDir);

                string tempFile = Path.Combine(Path.GetTempPath(), $"sharpyc-{rid}.{extension}");
                File.WriteAllBytes(tempFile, data);

                if (extension == "zip")
                {
                    ZipFile.ExtractToDirectory(tempFile, binariesDir);
                }
                else
                {
                    ExtractTarGz(tempFile, binariesDir);
                }

                File.Delete(tempFile);

                SetExecutablePermission(binariesDir, rid);

                EditorUtility.DisplayProgressBar("Sharpy", "Done!", 1.0f);
                EditorUtility.ClearProgressBar();

                Debug.Log($"[Sharpy] Compiler {CompilerVersion} installed for {rid}.");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[Sharpy] Failed to download compiler: {ex.Message}");
                EditorUtility.DisplayDialog(
                    "Sharpy Download Failed",
                    $"Failed to download the compiler:\n{ex.Message}\n\n"
                    + "You can retry via Assets > Sharpy > Download Compiler.",
                    "OK");
            }
        }

        private static string GetPlatformRid()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return SystemInfo.processorType.Contains("Apple")
                    ? "osx-arm64"
                    : "osx-x64";
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "win-x64";
            }

            return "linux-x64";
        }

        private static void ExtractTarGz(string archivePath, string outputDir)
        {
            using var fileStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            ExtractTar(gzipStream, outputDir);
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
