namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using NUnit.Framework;

    public class SharpyGeneratedMetaTests
    {
        private const string SpyGuid = "9f2c4e1a7b3d4c5e8f60718293a4b5c6";

        [Test]
        public void GuidFor_GoldenValue()
        {
            // python3 -c 'import hashlib; print(hashlib.md5(
            //     b"com.antonsynd.sharpy:generated:9f2c4e1a7b3d4c5e8f60718293a4b5c6").hexdigest())'
            // If this changes, every scene reference to a Sharpy component breaks.
            Assert.AreEqual("3fdaf6b052d2a2acbe084dc1b9545162", SharpyGeneratedMeta.GuidFor(SpyGuid));
        }

        [Test]
        public void GuidFor_SameInput_SameGuid()
        {
            Assert.AreEqual(SharpyGeneratedMeta.GuidFor(SpyGuid), SharpyGeneratedMeta.GuidFor(SpyGuid));
        }

        [Test]
        public void GuidFor_DifferentInput_DifferentGuid()
        {
            Assert.AreNotEqual(
                SharpyGeneratedMeta.GuidFor(SpyGuid),
                SharpyGeneratedMeta.GuidFor("0f2c4e1a7b3d4c5e8f60718293a4b5c6"));
        }

        [Test]
        public void GuidFor_Is32LowercaseHex()
        {
            StringAssert.IsMatch("^[0-9a-f]{32}$", SharpyGeneratedMeta.GuidFor(SpyGuid));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void GuidFor_BlankInput_Throws(string spyGuid)
        {
            Assert.Throws<ArgumentException>(() => SharpyGeneratedMeta.GuidFor(spyGuid));
        }

        [Test]
        public void MetaText_IsMonoImporterTemplate_AndRoundTrips()
        {
            string guid = SharpyGeneratedMeta.GuidFor(SpyGuid);
            string text = SharpyGeneratedMeta.MetaText(guid);

            Assert.AreEqual(
                "fileFormatVersion: 2\n"
                + "guid: 3fdaf6b052d2a2acbe084dc1b9545162\n"
                + "MonoImporter:\n"
                + "  externalObjects: {}\n"
                + "  serializedVersion: 2\n"
                + "  defaultReferences: []\n"
                + "  executionOrder: 0\n"
                + "  icon: {instanceID: 0}\n"
                + "  userData: \n"
                + "  assetBundleName: \n"
                + "  assetBundleVariant: \n",
                text);
            Assert.IsTrue(SharpyGeneratedMeta.TryReadGuid(text, out string read));
            Assert.AreEqual(guid, read);
        }

        [Test]
        public void TryReadGuid_UnityWrittenMeta_CrlfAndUppercase()
        {
            const string meta = "fileFormatVersion: 2\r\nguid: 28ED6746BAFDE48639BD2914442815C1\r\nfolderAsset: yes\r\n";

            Assert.IsTrue(SharpyGeneratedMeta.TryReadGuid(meta, out string guid));
            Assert.AreEqual("28ed6746bafde48639bd2914442815c1", guid);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("fileFormatVersion: 2\n")]
        [TestCase("fileFormatVersion: 2\nguid: 28ed6746bafde486\n")]
        [TestCase("fileFormatVersion: 2\nguid: 28ed6746bafde48639bd2914442815zz\n")]
        [TestCase("fileFormatVersion: 2\nguid: 28ed6746bafde48639bd2914442815c1ff\n")]
        [TestCase("fileFormatVersion: 2\n  guid: 28ed6746bafde48639bd2914442815c1\n")]
        public void TryReadGuid_MalformedText_ReturnsFalse(string meta)
        {
            Assert.IsFalse(SharpyGeneratedMeta.TryReadGuid(meta, out string guid));
            Assert.IsNull(guid);
        }
    }
}
