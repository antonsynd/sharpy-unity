namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
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

        [TestCase("Assets/Scripts/Smoke/smoke_behaviour.spy", "SmokeBehaviour", "Scripts/Smoke/SmokeBehaviour.cs")]
        [TestCase("Assets/top.spy", "Top", "Top.cs")]
        [TestCase("Assets/My Scripts/smoke behaviour.spy", "SmokeBehaviour", "My Scripts/SmokeBehaviour.cs")]
        public void SpyAssetToGeneratedRelative_ScriptClass_NamesTheFileAfterIt(string spyAsset, string scriptClass, string generated)
        {
            Assert.AreEqual(generated, SharpyGeneratedFolderManager.SpyAssetToGeneratedRelative(spyAsset, scriptClass));
        }

        [Test]
        public void GeneratedRelativePaths_SameBasenameInTwoFolders_StayApart()
        {
            var paths = SharpyGeneratedFolderManager.GeneratedRelativePaths(
                new[] { "Assets/Scripts/Ui/greeting.spy", "Assets/Scripts/Core/greeting.spy" },
                new Dictionary<string, string>(),
                new List<string>());

            Assert.AreEqual("Scripts/Core/greeting.cs", paths["Assets/Scripts/Core/greeting.spy"]);
            Assert.AreEqual("Scripts/Ui/greeting.cs", paths["Assets/Scripts/Ui/greeting.spy"]);
        }

        [Test]
        public void GeneratedRelativePaths_ScriptClass_NamesItsFile()
        {
            var paths = SharpyGeneratedFolderManager.GeneratedRelativePaths(
                new[] { "Assets/Scripts/Smoke/smoke_behaviour.spy", "Assets/Scripts/Core/greeting.spy" },
                new Dictionary<string, string> { ["Assets/Scripts/Smoke/smoke_behaviour.spy"] = "SmokeBehaviour" },
                new List<string>());

            Assert.AreEqual("Scripts/Smoke/SmokeBehaviour.cs", paths["Assets/Scripts/Smoke/smoke_behaviour.spy"]);
            Assert.AreEqual("Scripts/Core/greeting.cs", paths["Assets/Scripts/Core/greeting.spy"]);
        }

        [Test]
        public void GeneratedRelativePaths_ClassNameTakenByAnotherModule_KeepsModuleName_Warns()
        {
            var warnings = new List<string>();

            // hero.spy's class Player would be Scripts/Player.cs, which player.spy
            // (no MonoBehaviour) already is on a case-insensitive file system.
            var paths = SharpyGeneratedFolderManager.GeneratedRelativePaths(
                new[] { "Assets/Scripts/hero.spy", "Assets/Scripts/player.spy" },
                new Dictionary<string, string> { ["Assets/Scripts/hero.spy"] = "Player" },
                warnings);

            Assert.AreEqual("Scripts/hero.cs", paths["Assets/Scripts/hero.spy"]);
            Assert.AreEqual("Scripts/player.cs", paths["Assets/Scripts/player.spy"]);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Assets/Scripts/hero.spy", warnings[0]);
        }

        [Test]
        public void GeneratedRelativePaths_SameClassInOneFolder_BothKeepModuleNames_Warn()
        {
            var warnings = new List<string>();

            var paths = SharpyGeneratedFolderManager.GeneratedRelativePaths(
                new[] { "Assets/Scripts/a.spy", "Assets/Scripts/b.spy", "Assets/Other/c.spy" },
                new Dictionary<string, string>
                {
                    ["Assets/Scripts/a.spy"] = "Player",
                    ["Assets/Scripts/b.spy"] = "Player",
                    ["Assets/Other/c.spy"] = "Player",
                },
                warnings);

            Assert.AreEqual("Scripts/a.cs", paths["Assets/Scripts/a.spy"]);
            Assert.AreEqual("Scripts/b.cs", paths["Assets/Scripts/b.spy"]);
            Assert.AreEqual("Other/Player.cs", paths["Assets/Other/c.spy"]);
            Assert.AreEqual(2, warnings.Count);
        }

        // With <SourceRoot> honoured, sharpyc stages Assets/Scripts/Core/greeting.spy
        // as <emit>/Scripts/Core/greeting.cs; without it (0.21.0+4a6228e87), as
        // <emit>/Assets/Scripts/Core/greeting.cs.
        [TestCase("Scripts/Core/greeting.cs", "Assets/Scripts/Core/greeting.spy")]
        [TestCase("Scripts/Ui/greeting.cs", "Assets/Scripts/Ui/greeting.spy")]
        [TestCase("top.cs", "Assets/top.spy")]
        [TestCase("Scripts\\Core\\greeting.cs", "Assets/Scripts/Core/greeting.spy")]
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
