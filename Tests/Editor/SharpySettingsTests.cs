namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpySettingsTests
    {
        [Test]
        public void Instance_IsNotNull()
        {
            Assert.IsNotNull(SharpySettings.instance);
        }

        [Test]
        public void DefaultGeneratedOutputPath_IsSharpyGenerated()
        {
            var settings = SharpySettings.instance;
            Assert.AreEqual("Assets/SharpyGenerated", settings.GeneratedOutputPath);
        }

        [Test]
        public void DefaultCompilerTimeout_Is30Seconds()
        {
            var settings = SharpySettings.instance;
            Assert.AreEqual(30, settings.CompilerTimeoutSeconds);
        }

        [Test]
        public void DefaultAutoCompileOnSave_IsTrue()
        {
            var settings = SharpySettings.instance;
            Assert.IsTrue(settings.AutoCompileOnSave);
        }

        [Test]
        public void DefaultRootNamespace_IsEmpty()
        {
            var settings = SharpySettings.instance;
            Assert.AreEqual("", settings.RootNamespace);
        }

        [Test]
        public void DefaultCustomCompilerPath_IsEmpty()
        {
            var settings = SharpySettings.instance;
            Assert.AreEqual("", settings.CustomCompilerPath);
        }

        [Test]
        public void DefaultAdditionalModulePaths_IsEmpty()
        {
            var settings = SharpySettings.instance;
            Assert.IsNotNull(settings.AdditionalModulePaths);
            Assert.AreEqual(0, settings.AdditionalModulePaths.Count);
        }

        [Test]
        public void DefaultAdditionalReferences_IsEmpty()
        {
            var settings = SharpySettings.instance;
            Assert.IsNotNull(settings.AdditionalReferences);
            Assert.AreEqual(0, settings.AdditionalReferences.Count);
        }
    }
}
