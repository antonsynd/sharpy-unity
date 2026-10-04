namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;

    public class SharpyGeneratedSyncTests
    {
        private const string SpyGuid = "9f2c4e1a7b3d4c5e8f60718293a4b5c6";

        private string tempDir;
        private string root;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "SharpyGeneratedSyncTests_" + Guid.NewGuid().ToString("N"));
            root = Path.Combine(tempDir, "SharpyGenerated");
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        private static SharpyGeneratedFile Generated(string relativePath, string content, string metaGuid = null)
        {
            return new SharpyGeneratedFile { RelativePath = relativePath, Content = content, MetaGuid = metaGuid };
        }

        private string Full(string relativePath)
        {
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void Put(string relativePath, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Full(relativePath)));
            File.WriteAllText(Full(relativePath), content);
        }

        [Test]
        public void Sync_NewFile_WrittenUnderMirroredPath()
        {
            var result = SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/Core/greeting.cs", "class A {}\r\n") });

            Assert.AreEqual("class A {}\r\n", File.ReadAllText(Full("Scripts/Core/greeting.cs")));
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void Sync_UnchangedContent_NotRewritten()
        {
            Put("Scripts/Core/greeting.cs", "class A {}\r\n");
            var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Full("Scripts/Core/greeting.cs"), old);

            var result = SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/Core/greeting.cs", "class A {}\r\n") });

            Assert.AreEqual(old, File.GetLastWriteTimeUtc(Full("Scripts/Core/greeting.cs")));
            Assert.IsFalse(result.Changed);
        }

        [Test]
        public void Sync_ChangedContent_Rewritten()
        {
            Put("Scripts/Core/greeting.cs", "class A {}\r\n");

            var result = SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/Core/greeting.cs", "class B {}\r\n") });

            Assert.AreEqual("class B {}\r\n", File.ReadAllText(Full("Scripts/Core/greeting.cs")));
            CollectionAssert.Contains(result.Written, Full("Scripts/Core/greeting.cs"));
        }

        [Test]
        public void Sync_StaleFile_DeletedWithItsMeta_StagedFileKept()
        {
            Put("Old/stale.cs", "class Stale {}");
            Put("Old/stale.cs.meta", "guid: 0123456789abcdef0123456789abcdef");
            Put("Scripts/kept.cs", "class Kept {}");

            SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/kept.cs", "class Kept {}") });

            Assert.IsFalse(File.Exists(Full("Old/stale.cs")));
            Assert.IsFalse(File.Exists(Full("Old/stale.cs.meta")));
            Assert.IsTrue(File.Exists(Full("Scripts/kept.cs")));
        }

        [Test]
        public void Sync_KeepsGitignoreAndFilesOutsideRoot()
        {
            Put(".gitignore", "*\n");
            string outside = Path.Combine(tempDir, "Outside.cs");
            File.WriteAllText(outside, "class Outside {}");

            SharpyGeneratedSync.Sync(root, new SharpyGeneratedFile[0]);

            Assert.IsTrue(File.Exists(Full(".gitignore")));
            Assert.IsTrue(File.Exists(outside));
        }

        [Test]
        public void Sync_NoFiles_EmptiesRootButKeepsIt()
        {
            Put("Scripts/Core/greeting.cs", "class A {}");

            SharpyGeneratedSync.Sync(root, new SharpyGeneratedFile[0]);

            Assert.IsTrue(Directory.Exists(root));
            CollectionAssert.IsEmpty(Directory.GetFileSystemEntries(root));
        }

        [Test]
        public void Sync_EmptyFolders_PrunedWithTheirMetas()
        {
            Put("Old/Deeper/stale.cs", "class Stale {}");
            Put("Old/Deeper.meta", "folderAsset: yes");
            Put("Old.meta", "folderAsset: yes");
            Put("Scripts/kept.cs", "class Kept {}");

            SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/kept.cs", "class Kept {}") });

            Assert.IsFalse(Directory.Exists(Full("Old")));
            Assert.IsFalse(File.Exists(Full("Old.meta")));
            Assert.IsTrue(Directory.Exists(Full("Scripts")));
        }

        [Test]
        public void Sync_MetaWrittenBeforeScript()
        {
            var result = SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/smoke.cs", "class S {}", SpyGuid) });

            int meta = result.Written.IndexOf(Full("Scripts/smoke.cs") + ".meta");
            int script = result.Written.IndexOf(Full("Scripts/smoke.cs"));
            Assert.That(meta, Is.GreaterThanOrEqualTo(0));
            Assert.That(script, Is.GreaterThan(meta));

            Assert.IsTrue(SharpyGeneratedMeta.TryReadGuid(
                File.ReadAllText(Full("Scripts/smoke.cs.meta")), out string guid));
            Assert.AreEqual(SpyGuid, guid);
        }

        [Test]
        public void Sync_MetaWithSameGuid_NotRewritten()
        {
            Put("Scripts/smoke.cs", "class S {}");
            Put("Scripts/smoke.cs.meta", "fileFormatVersion: 2\nguid: " + SpyGuid + "\nUnity rewrote this\n");

            var result = SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/smoke.cs", "class S {}", SpyGuid) });

            StringAssert.Contains("Unity rewrote this", File.ReadAllText(Full("Scripts/smoke.cs.meta")));
            Assert.IsFalse(result.Changed);
        }

        [Test]
        public void Sync_MetaWithOtherGuid_Replaced()
        {
            Put("Scripts/smoke.cs", "class S {}");
            Put("Scripts/smoke.cs.meta", "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");

            SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/smoke.cs", "class S {}", SpyGuid) });

            Assert.AreEqual(SharpyGeneratedMeta.MetaText(SpyGuid), File.ReadAllText(Full("Scripts/smoke.cs.meta")));
        }

        [Test]
        public void Sync_NoGuid_LeavesMetaToUnity()
        {
            SharpyGeneratedSync.Sync(root, new[] { Generated("Scripts/smoke.cs", "class S {}") });

            Assert.IsFalse(File.Exists(Full("Scripts/smoke.cs.meta")));
        }
    }
}
