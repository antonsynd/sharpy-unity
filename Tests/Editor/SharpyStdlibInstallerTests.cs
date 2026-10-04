namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;

    public class SharpyStdlibInstallerTests
    {
        // Entries of sharpy-stdlib-netstandard2.1.zip on the v0.21.0 release.
        private static readonly string[] ReleaseZipEntries =
        {
            "MathNet.Numerics.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "Microsoft.Data.Sqlite.dll",
            "Sharpy.Core.dll",
            "Sharpy.Core.pdb",
            "Sharpy.Core.xml",
            "Sharpy.Stdlib.deps.json",
            "Sharpy.Stdlib.dll",
            "Sharpy.Stdlib.pdb",
            "Sharpy.Stdlib.xml",
            "SQLitePCLRaw.batteries_v2.dll",
            "SQLitePCLRaw.core.dll",
            "SQLitePCLRaw.provider.e_sqlite3.dll",
            "System.Collections.Immutable.dll",
            "System.IO.Pipelines.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Text.Encodings.Web.dll",
            "System.Text.Json.dll",
            "Tomlyn.dll",
            "YamlDotNet.dll",
        };

        private static readonly string[] NoneExisting = new string[0];

        [Test]
        public void ShouldInstall_ReleaseZip_SelectsStdlibAndDependenciesOnly()
        {
            var selected = ReleaseZipEntries
                .Where(e => SharpyStdlibInstaller.ShouldInstall(e, NoneExisting))
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "MathNet.Numerics.dll",
                    "Microsoft.Data.Sqlite.dll",
                    "Sharpy.Stdlib.dll",
                    "SQLitePCLRaw.batteries_v2.dll",
                    "SQLitePCLRaw.core.dll",
                    "SQLitePCLRaw.provider.e_sqlite3.dll",
                    "System.IO.Pipelines.dll",
                    "System.Text.Encodings.Web.dll",
                    "System.Text.Json.dll",
                    "Tomlyn.dll",
                    "YamlDotNet.dll",
                },
                selected);
        }

        [TestCase("Sharpy.Stdlib.pdb")]
        [TestCase("Sharpy.Stdlib.xml")]
        [TestCase("Sharpy.Stdlib.deps.json")]
        [TestCase("lib/")]
        [TestCase("")]
        [TestCase(null)]
        public void ShouldInstall_NonDllEntries_Skipped(string entry)
        {
            Assert.IsFalse(SharpyStdlibInstaller.ShouldInstall(entry, NoneExisting));
        }

        [TestCase("Sharpy.Core.dll")]
        [TestCase("sharpy.core.DLL")]
        [TestCase("System.Collections.Immutable.dll")]
        [TestCase("Microsoft.Bcl.AsyncInterfaces.dll")]
        [TestCase("lib/System.Runtime.CompilerServices.Unsafe.dll")]
        public void ShouldInstall_PackageShippedDlls_Skipped(string entry)
        {
            Assert.IsFalse(SharpyStdlibInstaller.ShouldInstall(entry, NoneExisting));
        }

        [Test]
        public void ShouldInstall_DllAlreadyInProject_Skipped()
        {
            var existing = new[] { "system.text.json.dll" };

            Assert.IsFalse(SharpyStdlibInstaller.ShouldInstall("System.Text.Json.dll", existing));
            // Positive controls: other DLLs, and the same DLL with nothing existing.
            Assert.IsTrue(SharpyStdlibInstaller.ShouldInstall("Tomlyn.dll", existing));
            Assert.IsTrue(SharpyStdlibInstaller.ShouldInstall("System.Text.Json.dll", NoneExisting));
        }

        [Test]
        public void ShouldInstall_EntryInSubfolder_UsesFileName()
        {
            Assert.IsTrue(SharpyStdlibInstaller.ShouldInstall("lib/netstandard2.1/Tomlyn.dll", NoneExisting));
        }

        [Test]
        public void ExistingDllNames_ExcludesInstallFolder_KeepsOthers()
        {
            var names = SharpyStdlibInstaller.ExistingDllNames(
                new[]
                {
                    "Assets/Plugins/Sharpy.Stdlib/Tomlyn.dll",
                    "/Users/dev/Game/Assets/Plugins/Sharpy.Stdlib/Sharpy.Stdlib.dll",
                    "C:\\Game\\Assets\\Plugins\\Sharpy.Stdlib\\YamlDotNet.dll",
                    "Assets/Plugins/Vendor/System.Text.Json.dll",
                    "/Users/dev/Game/Packages/com.antonsynd.sharpy/Plugins/Sharpy.Core/Sharpy.Core.dll",
                    "Assets/Plugins/NotSharpy.Stdlib/MathNet.Numerics.dll",
                    "",
                },
                SharpyStdlibInstaller.InstallFolder);

            CollectionAssert.AreEquivalent(
                new[] { "System.Text.Json.dll", "Sharpy.Core.dll", "MathNet.Numerics.dll" },
                names);
        }

        [TestCase(0, 21, 0, 0, "0.21.0", true)]
        [TestCase(0, 21, 0, 7, "0.21.0", true)]
        [TestCase(0, 21, 0, 0, "0.21.0-rc.1", true)]
        [TestCase(0, 21, 0, 0, "0.21.0+db8e05b", true)]
        [TestCase(0, 21, 1, 0, "0.21.0", false)]
        [TestCase(0, 22, 0, 0, "0.21.0", false)]
        [TestCase(1, 21, 0, 0, "0.21.0", false)]
        [TestCase(0, 21, 0, 0, "garbage", false)]
        [TestCase(0, 21, 0, 0, "", false)]
        public void VersionMatchesPin(int major, int minor, int build, int revision, string pin, bool expected)
        {
            Assert.AreEqual(
                expected,
                SharpyStdlibInstaller.VersionMatchesPin(new Version(major, minor, build, revision), pin));
        }

        [Test]
        public void VersionMatchesPin_NullVersion_False()
        {
            Assert.IsFalse(SharpyStdlibInstaller.VersionMatchesPin(null, "0.21.0"));
        }

        [Test]
        public void VersionMatchesPin_BundledSharpyCore_MatchesToolchainPin()
        {
            // Sharpy.Core and Sharpy.Stdlib come from the same release build and
            // carry the same assembly version (0.21.0.0 for 0.21.0), so the
            // bundled Core shows the real format the stdlib check compares.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(SharpyStdlibInstaller).Assembly);
            Assert.IsNotNull(package, "Sharpy.Unity.Editor should belong to the package");
            string dll = Path.Combine(package.resolvedPath, "Plugins", "Sharpy.Core", "Sharpy.Core.dll");

            Version version = AssemblyName.GetAssemblyName(dll).Version;

            Assert.IsTrue(
                SharpyStdlibInstaller.VersionMatchesPin(version, SharpyToolchain.Version),
                $"Sharpy.Core.dll {version} vs pin {SharpyToolchain.Version}");
        }
    }
}
