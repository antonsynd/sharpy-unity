namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.IO;
    using NUnit.Framework;

    public class SharpyGeneratedOwnershipTests
    {
        private const string GeneratedCs =
            "namespace SharpyScripts.Core.Greeting\r\n{\r\n    [global::Sharpy.SharpyModule(\"Core.greeting\")]\r\n"
            + "    public static partial class GreetingModule\r\n    {\r\n    }\r\n}\r\n";

        private static readonly string[] Sources = { "Assets/Scripts/Core/greeting.spy" };

        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SharpyGeneratedOwnershipTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        private string Put(string relativePath, string content)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        [TestCase("Assets/SharpyGenerated")]
        [TestCase("Assets/SharpyGenerated/")]
        [TestCase("Assets/Gen/Sharpy")]
        [TestCase("Assets\\Gen\\Sharpy")]
        [TestCase("Assets/ScriptsGenerated")]
        public void CheckPath_OwnFolderInsideAssets_Ok(string folder)
        {
            Assert.IsNull(SharpyGeneratedOwnership.CheckPath(root, folder, Sources));
        }

        [TestCase("Assets")]
        [TestCase("Assets/")]
        [TestCase("Assets/.")]
        [TestCase("Assets/./")]
        [TestCase("Assets/.\\")]
        [TestCase("Assets//X")]
        [TestCase("Assets/X/../Y")]
        [TestCase("Assets/../Generated")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("Generated")]
        [TestCase("assets/Generated")]
        [TestCase("/Assets/Generated")]
        public void CheckPath_NotAFolderStrictlyInsideAssets_Rejected(string folder)
        {
            Assert.IsNotNull(SharpyGeneratedOwnership.CheckPath(root, folder, new string[0]));
        }

        [Test]
        public void CheckPath_AbsolutePathIntoAssets_Rejected()
        {
            Assert.IsNotNull(SharpyGeneratedOwnership.CheckPath(
                root, Path.Combine(root, "Assets", "Generated"), new string[0]));
        }

        [Test]
        public void CheckPath_FolderHoldingSources_Rejected()
        {
            StringAssert.Contains(
                "Assets/Scripts/Core/greeting.spy",
                SharpyGeneratedOwnership.CheckPath(root, "Assets/Scripts", Sources));
        }

        [Test]
        public void Claim_NewFolder_CreatedAndMarked()
        {
            string dir = Path.Combine(root, "Assets", "SharpyGenerated");

            Assert.IsNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.IsTrue(SharpyGeneratedOwnership.IsOwned(dir));
            StringAssert.StartsWith(
                SharpyGeneratedOwnership.MarkerLine, File.ReadAllText(Path.Combine(dir, ".gitignore")));
        }

        [Test]
        public void Claim_EmptyFolder_Marked()
        {
            string dir = Path.Combine(root, "Assets", "Empty");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".DS_Store"), "");

            Assert.IsNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.IsTrue(SharpyGeneratedOwnership.IsOwned(dir));
        }

        [Test]
        public void Claim_MarkedFolderWithScripts_Owned()
        {
            string dir = Path.GetDirectoryName(Put("Assets/SharpyGenerated/.gitignore", SharpyGeneratedOwnership.MarkerText));
            Put("Assets/SharpyGenerated/Scripts/handwritten-looking.cs", "class A {}");

            Assert.IsNull(SharpyGeneratedOwnership.Claim(dir));
        }

        [Test]
        public void Claim_ForeignFolder_Refused_FilesUntouched()
        {
            string script = Put("Assets/Plugins/Hand.cs", "class Hand {}");
            string dir = Path.GetDirectoryName(script);

            Assert.IsNotNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.IsFalse(SharpyGeneratedOwnership.IsOwned(dir));
            Assert.IsFalse(File.Exists(Path.Combine(dir, ".gitignore")));
            Assert.AreEqual("class Hand {}", File.ReadAllText(script));
        }

        [Test]
        public void Claim_OtherGitignore_Refused()
        {
            string dir = Path.GetDirectoryName(Put("Assets/Cache/.gitignore", "*.tmp\n"));

            Assert.IsNotNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.AreEqual("*.tmp\n", File.ReadAllText(Path.Combine(dir, ".gitignore")));
        }

        [Test]
        public void Claim_LegacyMarkerWithOnlyGeneratedScripts_Adopted()
        {
            string dir = Path.GetDirectoryName(Put("Assets/SharpyGenerated/.gitignore", "*\n"));
            Put("Assets/SharpyGenerated/Scripts/Core/greeting.cs", GeneratedCs);

            Assert.IsNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.IsTrue(SharpyGeneratedOwnership.IsOwned(dir));
        }

        [Test]
        public void Claim_LegacyMarkerWithAHandWrittenScript_Refused()
        {
            string dir = Path.GetDirectoryName(Put("Assets/Plugins/.gitignore", "*\n"));
            Put("Assets/Plugins/Scripts/Core/greeting.cs", GeneratedCs);
            Put("Assets/Plugins/Hand.cs", "class Hand {}");

            Assert.IsNotNull(SharpyGeneratedOwnership.Claim(dir));
            Assert.AreEqual("*\n", File.ReadAllText(Path.Combine(dir, ".gitignore")));
        }

        [Test]
        public void Retire_MarkedFolderOfGeneratedScripts_RemovedWithItsMeta()
        {
            string dir = Path.GetDirectoryName(Put("Assets/Gen1/.gitignore", SharpyGeneratedOwnership.MarkerText));
            Put("Assets/Gen1.meta", "folderAsset: yes");
            Put("Assets/Gen1/Scripts/Core/greeting.cs", GeneratedCs);
            Put("Assets/Gen1/Scripts/Core/greeting.cs.meta", "guid: 0123456789abcdef0123456789abcdef");

            Assert.IsTrue(SharpyGeneratedOwnership.Retire(dir));
            Assert.IsFalse(Directory.Exists(dir));
            Assert.IsFalse(File.Exists(dir + ".meta"));
        }

        [Test]
        public void Retire_MarkedFolderWithOtherFiles_KeepsThemAndTheMarker()
        {
            string dir = Path.GetDirectoryName(Put("Assets/Gen1/.gitignore", SharpyGeneratedOwnership.MarkerText));
            string script = Put("Assets/Gen1/Scripts/greeting.cs", GeneratedCs);
            string notes = Put("Assets/Gen1/notes.txt", "mine");

            SharpyGeneratedOwnership.Retire(dir);

            Assert.IsFalse(File.Exists(script));
            Assert.IsTrue(File.Exists(notes));
            Assert.IsTrue(SharpyGeneratedOwnership.IsOwned(dir));
        }

        [Test]
        public void Retire_UnmarkedFolder_Untouched()
        {
            string script = Put("Assets/Plugins/Hand.cs", "class Hand {}");

            Assert.IsFalse(SharpyGeneratedOwnership.Retire(Path.GetDirectoryName(script)));
            Assert.IsTrue(File.Exists(script));
        }
    }
}
