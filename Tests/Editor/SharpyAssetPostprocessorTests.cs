namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyAssetPostprocessorTests
    {
        private const string Generated = "Assets/SharpyGenerated";

        private static readonly string[] None = new string[0];

        [Test]
        public void TouchesSources_ImportedSpy_True()
        {
            Assert.IsTrue(SharpyAssetPostprocessor.TouchesSources(
                Generated, new[] { "Assets/Scripts/Core/greeting.spy" }, None, None, None));
        }

        [Test]
        public void TouchesSources_DeletedSpy_True()
        {
            Assert.IsTrue(SharpyAssetPostprocessor.TouchesSources(
                Generated, None, new[] { "Assets/Scripts/Core/greeting.spy" }, None, None));
        }

        [Test]
        public void TouchesSources_SpyRenamedToAnotherExtension_True()
        {
            Assert.IsTrue(SharpyAssetPostprocessor.TouchesSources(
                Generated, None, None, new[] { "Assets/Scripts/greeting.txt" }, new[] { "Assets/Scripts/greeting.spy" }));
        }

        [Test]
        public void TouchesSources_OnlyScriptsAndOtherAssets_False()
        {
            Assert.IsFalse(SharpyAssetPostprocessor.TouchesSources(
                Generated,
                new[] { "Assets/SharpyGenerated/Scripts/Core/greeting.cs", "Assets/Scenes/Main.unity", "Assets/spy.txt" },
                new[] { "Assets/SharpyGenerated/Scripts/Smoke/SmokeBehaviour.cs" },
                None,
                None));
        }

        [Test]
        public void TouchesSources_SpyInsideGeneratedFolder_False()
        {
            Assert.IsFalse(SharpyAssetPostprocessor.TouchesSources(
                Generated + "/", new[] { "Assets/SharpyGenerated/stray.spy" }, None, None, None));
        }

        [Test]
        public void TouchesSources_UppercaseExtension_True()
        {
            Assert.IsTrue(SharpyAssetPostprocessor.TouchesSources(
                Generated, new[] { "Assets/Scripts/Greeting.SPY" }, None, None, None));
        }
    }
}
