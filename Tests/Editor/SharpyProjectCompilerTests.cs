namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyProjectCompilerTests
    {
        [Test]
        public void NeedsCompiler_NoSources_False()
        {
            Assert.IsFalse(SharpyProjectCompiler.NeedsCompiler(new List<string>()));
        }

        [Test]
        public void NeedsCompiler_OneSource_True()
        {
            Assert.IsTrue(SharpyProjectCompiler.NeedsCompiler(new List<string> { "Assets/a.spy" }));
        }

        [Test]
        public void ShouldSync_OnlyOnExitZero()
        {
            Assert.IsTrue(SharpyProjectCompiler.ShouldSync(0));

            foreach (int exitCode in new[] { 1, 2, 3, -1 })
            {
                Assert.IsFalse(SharpyProjectCompiler.ShouldSync(exitCode), $"exit {exitCode}");
            }
        }

        [TestCase("Assets/SharpyGenerated")]
        [TestCase("Assets/SharpyGenerated/")]
        [TestCase("Assets/Gen/Sharpy")]
        public void CheckGeneratedFolder_OwnFolderInAssets_Ok(string folder)
        {
            Assert.IsNull(SharpyProjectCompiler.CheckGeneratedFolder(
                folder, new[] { "Assets/Scripts/a.spy", "Assets/SharpyGeneratedOther/b.spy" }));
        }

        [TestCase("Assets")]
        [TestCase("Assets/")]
        [TestCase("")]
        [TestCase("Generated")]
        [TestCase("Assets/../Generated")]
        public void CheckGeneratedFolder_NotAFolderInsideAssets_Rejected(string folder)
        {
            Assert.IsNotNull(SharpyProjectCompiler.CheckGeneratedFolder(folder, new string[0]));
        }

        [Test]
        public void CheckGeneratedFolder_ContainsASource_Rejected()
        {
            StringAssert.Contains(
                "Assets/Scripts/a.spy",
                SharpyProjectCompiler.CheckGeneratedFolder("Assets/Scripts", new[] { "Assets/Scripts/a.spy" }));
        }
    }
}
