namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using InstallAction = SharpyBinaryDownloader.InstallAction;

    public class SharpyBinaryDownloaderTests
    {
        private string _root;
        private string _versionDir;
        private string _installDir;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sharpy-install-tests-" + Guid.NewGuid().ToString("N"));
            _versionDir = Path.Combine(_root, "0.0.0");
            _installDir = Path.Combine(_versionDir, "linux-x64");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        // isBatchMode, hasCustomPath, installed, versionMatches → expected.
        // (The enum is internal, so it cannot be a public test parameter.)
        [Test]
        public void DecideInstall_ManagedCompilerMissing_InstallsInBatchModeOnly()
        {
            Assert.AreEqual(InstallAction.InstallNow, SharpyBinaryDownloader.DecideInstall(true, false, false, true));
            Assert.AreEqual(InstallAction.Prompt, SharpyBinaryDownloader.DecideInstall(false, false, false, true));
        }

        [Test]
        public void DecideInstall_ManagedCompilerMismatched_InstallsInBatchModeOnly()
        {
            Assert.AreEqual(InstallAction.InstallNow, SharpyBinaryDownloader.DecideInstall(true, false, true, false));
            Assert.AreEqual(InstallAction.Prompt, SharpyBinaryDownloader.DecideInstall(false, false, true, false));
        }

        [Test]
        public void DecideInstall_ManagedCompilerCurrent_DoesNothing()
        {
            Assert.AreEqual(InstallAction.None, SharpyBinaryDownloader.DecideInstall(true, false, true, true));
            Assert.AreEqual(InstallAction.None, SharpyBinaryDownloader.DecideInstall(false, false, true, true));
        }

        [Test]
        public void DecideInstall_CustomPath_NeverInstalls()
        {
            // installed is false: the managed install is irrelevant once a custom path is set.
            Assert.AreEqual(InstallAction.None, SharpyBinaryDownloader.DecideInstall(true, true, false, true));
            Assert.AreEqual(InstallAction.Warn, SharpyBinaryDownloader.DecideInstall(true, true, false, false));
            Assert.AreEqual(InstallAction.Warn, SharpyBinaryDownloader.DecideInstall(false, true, false, false));
        }

        [Test]
        public void InstallArchive_TarGz_InstallsEveryFileAndLeavesNoStaging()
        {
            byte[] archive = TarGz(("./sharpyc", "new compiler"), ("./lib/Some.dll", "dll"));

            SharpyBinaryDownloader.InstallArchive(archive, "linux-x64", _installDir);

            Assert.AreEqual("new compiler", File.ReadAllText(Path.Combine(_installDir, "sharpyc")));
            Assert.AreEqual("dll", File.ReadAllText(Path.Combine(_installDir, "lib", "Some.dll")));
            CollectionAssert.AreEqual(new[] { _installDir }, Directory.GetFileSystemEntries(_versionDir));
        }

        [Test]
        public void InstallArchive_Zip_InstallsWindowsBinary()
        {
            string installDir = Path.Combine(_versionDir, "win-x64");

            SharpyBinaryDownloader.InstallArchive(Zip(("sharpyc.exe", "exe")), "win-x64", installDir);

            Assert.AreEqual("exe", File.ReadAllText(Path.Combine(installDir, "sharpyc.exe")));
            CollectionAssert.AreEqual(new[] { installDir }, Directory.GetFileSystemEntries(_versionDir));
        }

        [Test]
        public void InstallArchive_ExistingInstall_IsReplacedWhole()
        {
            WriteFile(Path.Combine(_installDir, "sharpyc"), "old compiler");
            WriteFile(Path.Combine(_installDir, "Stale.dll"), "stale");

            SharpyBinaryDownloader.InstallArchive(TarGz(("./sharpyc", "new compiler")), "linux-x64", _installDir);

            Assert.AreEqual("new compiler", File.ReadAllText(Path.Combine(_installDir, "sharpyc")));
            Assert.IsFalse(File.Exists(Path.Combine(_installDir, "Stale.dll")));
            CollectionAssert.AreEqual(new[] { _installDir }, Directory.GetFileSystemEntries(_versionDir));
        }

        [Test]
        public void InstallArchive_TruncatedArchive_ThrowsAndLeavesNothingInstalled()
        {
            Assert.Catch<Exception>(() =>
                SharpyBinaryDownloader.InstallArchive(TruncatedTarGz("./sharpyc"), "linux-x64", _installDir));

            Assert.IsFalse(Directory.Exists(_installDir));
            CollectionAssert.IsEmpty(Directory.GetFileSystemEntries(_versionDir));
        }

        [Test]
        public void InstallArchive_TruncatedArchive_KeepsPreviousInstall()
        {
            WriteFile(Path.Combine(_installDir, "sharpyc"), "old compiler");

            Assert.Catch<Exception>(() =>
                SharpyBinaryDownloader.InstallArchive(TruncatedTarGz("./sharpyc"), "linux-x64", _installDir));

            Assert.AreEqual("old compiler", File.ReadAllText(Path.Combine(_installDir, "sharpyc")));
            CollectionAssert.AreEqual(new[] { _installDir }, Directory.GetFileSystemEntries(_versionDir));
        }

        [Test]
        public void InstallArchive_ArchiveWithoutBinary_ThrowsAndLeavesNothingInstalled()
        {
            byte[] archive = TarGz(("./lib/Some.dll", "dll"));

            Assert.Throws<InvalidDataException>(() =>
                SharpyBinaryDownloader.InstallArchive(archive, "linux-x64", _installDir));

            Assert.IsFalse(Directory.Exists(_installDir));
        }

        // Zip needs no such test: ZipArchive.ExtractToDirectory already
        // rejects "../" and absolute entries on Unity's runtime.
        [Test]
        public void InstallArchive_TarEntryEscapingWithDotDot_ThrowsAndWritesNothingOutside()
        {
            // The staging dir sits in _versionDir, so "../evil" would land there.
            byte[] archive = TarGz(("./sharpyc", "new compiler"), ("../evil", "evil"));

            Assert.Throws<InvalidDataException>(() =>
                SharpyBinaryDownloader.InstallArchive(archive, "linux-x64", _installDir));

            Assert.IsFalse(File.Exists(Path.Combine(_versionDir, "evil")));
            Assert.IsFalse(Directory.Exists(_installDir));
        }

        [Test]
        public void InstallArchive_TarEntryWithAbsolutePath_ThrowsAndWritesNothingOutside()
        {
            // Kept short: a tar name field holds 100 bytes.
            string target = Path.Combine(Path.GetTempPath(), "sharpy-abs-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                byte[] archive = TarGz(("./sharpyc", "new compiler"), (target, "evil"));

                Assert.Throws<InvalidDataException>(() =>
                    SharpyBinaryDownloader.InstallArchive(archive, "linux-x64", _installDir));

                Assert.IsFalse(File.Exists(target));
                Assert.IsFalse(Directory.Exists(_installDir));
            }
            finally
            {
                File.Delete(target);
            }
        }

        [Test]
        public void InstallArchive_RemovesLeftoversOfInterruptedInstalls()
        {
            WriteFile(Path.Combine(_versionDir, "linux-x64.staging-0123", "sharpyc"), "partial");
            WriteFile(Path.Combine(_versionDir, "linux-x64.previous-4567", "sharpyc"), "old compiler");
            // Another platform's leftovers are not this install's to remove.
            string otherRid = Path.Combine(_versionDir, "win-x64.staging-89ab");
            WriteFile(Path.Combine(otherRid, "sharpyc.exe"), "partial");

            SharpyBinaryDownloader.InstallArchive(TarGz(("./sharpyc", "new compiler")), "linux-x64", _installDir);

            string[] entries = Directory.GetFileSystemEntries(_versionDir);
            Array.Sort(entries, StringComparer.Ordinal);
            CollectionAssert.AreEqual(new[] { _installDir, otherRid }, entries);
        }

        [Test]
        public void InstallArchive_MarksBinaryExecutable()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("No executable bit on Windows.");
            }

            SharpyBinaryDownloader.InstallArchive(TarGz(("./sharpyc", "new compiler")), "linux-x64", _installDir);

            // The tar extractor ignores modes, so the bit comes only from the chmod step.
            using var test = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/test",
                Arguments = $"-x \"{Path.Combine(_installDir, "sharpyc")}\"",
                UseShellExecute = false
            });
            test.WaitForExit();

            Assert.AreEqual(0, test.ExitCode);
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        // Same layout as the release archives: a "./" directory entry, then
        // "./"-prefixed regular files.
        private static byte[] TarGz(params (string Name, string Content)[] files)
        {
            return GZip(tar =>
            {
                WriteTarHeader(tar, "./", 0, '5');

                foreach (var (name, content) in files)
                {
                    byte[] data = Encoding.ASCII.GetBytes(content);
                    WriteTarHeader(tar, name, data.Length, '0');
                    tar.Write(data, 0, data.Length);
                    tar.Write(new byte[(512 - data.Length % 512) % 512], 0, (512 - data.Length % 512) % 512);
                }

                tar.Write(new byte[1024], 0, 1024);
            });
        }

        // A header promising more bytes than the stream holds, as left by an
        // interrupted download.
        private static byte[] TruncatedTarGz(string name)
        {
            return GZip(tar =>
            {
                WriteTarHeader(tar, name, 4096, '0');
                tar.Write(new byte[100], 0, 100);
            });
        }

        private static void WriteTarHeader(Stream tar, string name, long size, char typeFlag)
        {
            byte[] header = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
            Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0')).CopyTo(header, 124);
            header[156] = (byte)typeFlag;
            tar.Write(header, 0, header.Length);
        }

        private static byte[] GZip(Action<Stream> write)
        {
            using var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            {
                write(gzip);
            }

            return output.ToArray();
        }

        private static byte[] Zip(params (string Name, string Content)[] files)
        {
            using var output = new MemoryStream();

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var (name, content) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(content);
                }
            }

            return output.ToArray();
        }
    }
}
