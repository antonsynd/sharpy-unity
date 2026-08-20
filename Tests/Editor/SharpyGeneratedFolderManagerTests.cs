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
    }
}
