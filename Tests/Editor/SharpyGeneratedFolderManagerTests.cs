namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyGeneratedFolderManagerTests
    {
        [Test]
        public void GetGeneratedPath_AssetsPrefix_StripsAssetsAndMirrors()
        {
            string result = SharpyGeneratedFolderManager.GetGeneratedPath("Assets/Scripts/player.spy");

            Assert.IsTrue(result.EndsWith("Scripts/player.cs") || result.EndsWith("Scripts\\player.cs"));
            Assert.IsTrue(result.Contains("SharpyGenerated") || result.Contains(SharpySettings.instance.GeneratedOutputPath));
        }

        [Test]
        public void GetGeneratedPath_ChangesExtensionToCs()
        {
            string result = SharpyGeneratedFolderManager.GetGeneratedPath("Assets/test.spy");

            Assert.IsTrue(result.EndsWith("test.cs") || result.EndsWith("test.cs"));
        }

        [Test]
        public void GetGeneratedPath_NestedPath_PreservesStructure()
        {
            string result = SharpyGeneratedFolderManager.GetGeneratedPath("Assets/Scripts/Game/Player/movement.spy");

            Assert.IsTrue(
                result.Contains("Scripts/Game/Player/movement.cs")
                || result.Contains("Scripts\\Game\\Player\\movement.cs"));
        }

        [Test]
        public void GetGeneratedPath_NoAssetsPrefix_UsesFullRelativePath()
        {
            string result = SharpyGeneratedFolderManager.GetGeneratedPath("other/test.spy");

            Assert.IsTrue(result.Contains("other"));
            Assert.IsTrue(result.EndsWith("test.cs") || result.EndsWith("test.cs"));
        }

        [TestCase("Assets/Scripts/Core/greeting.spy", "Scripts/Core/greeting.cs")]
        [TestCase("Assets/Scripts/Ui/greeting.spy", "Scripts/Ui/greeting.cs")]
        [TestCase("Assets/My Scripts/smoke behaviour.spy", "My Scripts/smoke behaviour.cs")]
        [TestCase("Assets/top.spy", "top.cs")]
        public void SpyAssetToGeneratedRelative_MirrorsUnderAssets(string spyAsset, string generated)
        {
            Assert.AreEqual(generated, SharpyGeneratedFolderManager.SpyAssetToGeneratedRelative(spyAsset));
        }

        [TestCase("Scripts/Core/greeting.cs", "Assets/Scripts/Core/greeting.spy")]
        [TestCase("Scripts/Ui/greeting.cs", "Assets/Scripts/Ui/greeting.spy")]
        [TestCase("My Scripts/smoke behaviour.cs", "Assets/My Scripts/smoke behaviour.spy")]
        [TestCase("Scripts\\Core\\greeting.cs", "Assets/Scripts/Core/greeting.spy")]
        public void GeneratedRelativeToSpyAsset_InvertsTheMirror(string generated, string spyAsset)
        {
            Assert.AreEqual(spyAsset, SharpyGeneratedFolderManager.GeneratedRelativeToSpyAsset(generated));
            Assert.AreEqual(
                generated.Replace('\\', '/'),
                SharpyGeneratedFolderManager.SpyAssetToGeneratedRelative(spyAsset));
        }

        // sharpyc HEAD stages Assets/Scripts/Core/greeting.spy as
        // <emit>/Assets/Scripts/Core/greeting.cs.
        [TestCase("Assets/Scripts/Core/greeting.cs", "Assets/Scripts/Core/greeting.spy")]
        [TestCase("Assets/Scripts/Ui/greeting.cs", "Assets/Scripts/Ui/greeting.spy")]
        [TestCase("Assets/My Scripts/smoke behaviour.cs", "Assets/My Scripts/smoke behaviour.spy")]
        [TestCase("Assets\\Scripts\\Core\\greeting.cs", "Assets/Scripts/Core/greeting.spy")]
        public void StagedToSpyAsset_MapsMirroredStagingPath(string staged, string spyAsset)
        {
            Assert.AreEqual(spyAsset, SharpyGeneratedFolderManager.StagedToSpyAsset(staged));
        }
    }
}
