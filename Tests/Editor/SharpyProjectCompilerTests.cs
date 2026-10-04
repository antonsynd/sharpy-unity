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

        [Test]
        public void IsStdlibInstalled_MatchesTheFileName()
        {
            Assert.IsTrue(SharpyProjectCompiler.IsStdlibInstalled(new[]
            {
                "/proj/Assets/Plugins/Sharpy.Stdlib/Tomlyn.dll",
                "/proj/Assets/Plugins/Sharpy.Stdlib/sharpy.stdlib.DLL",
            }));
        }

        [Test]
        public void IsStdlibInstalled_OnlyOtherSharpyAssemblies_False()
        {
            Assert.IsFalse(SharpyProjectCompiler.IsStdlibInstalled(new[]
            {
                "/proj/Packages/com.antonsynd.sharpy/Plugins/Sharpy.Core/Sharpy.Core.dll",
                "/proj/Assets/Plugins/Sharpy.Stdlib.Extras.dll",
            }));
        }

        private const string MathModuleCs =
            "#line 1 \"/proj/Assets/Scripts/Sharpy/Json/x.spy\"\r\n"
            + "using math = global::Sharpy.MathModule.MathModuleModule;\r\n";

        [Test]
        public void StdlibWarnings_NotInstalled_OnePerSpyUsingTheStdlib()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/b.spy"] = MathModuleCs,
                ["Assets/a.spy"] = MathModuleCs + "var d = new global::Sharpy.Deque<int>();\r\n",
                ["Assets/plain.spy"] = "using global::Sharpy;\r\nglobal::Sharpy.Builtins.Print(1);\r\n",
            };

            List<string> warnings = SharpyProjectCompiler.StdlibWarnings(generated, false);

            Assert.AreEqual(2, warnings.Count);
            StringAssert.StartsWith("[Sharpy] Assets/a.spy:", warnings[0]);
            StringAssert.StartsWith("[Sharpy] Assets/b.spy:", warnings[1]);
            StringAssert.Contains("'math'", warnings[1]);
        }

        [Test]
        public void StdlibWarnings_Installed_None()
        {
            var generated = new Dictionary<string, string> { ["Assets/a.spy"] = MathModuleCs };

            CollectionAssert.IsEmpty(SharpyProjectCompiler.StdlibWarnings(generated, true));
        }

        [Test]
        public void Decide_Force_AlwaysCompiles()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.Compile,
                SharpyProjectCompiler.Decide(true, true, true, "fp", "fp"));
        }

        [Test]
        public void Decide_UpToDate_Skips()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.UpToDate,
                SharpyProjectCompiler.Decide(false, true, false, "fp", ""));
        }

        [Test]
        public void Decide_FocusOnInputsThatFailed_Skips()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.KnownFailure,
                SharpyProjectCompiler.Decide(false, false, true, "fp", "fp"));
        }

        [Test]
        public void Decide_FocusOnChangedInputs_Compiles()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.Compile,
                SharpyProjectCompiler.Decide(false, false, true, "fp2", "fp"));
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.Compile,
                SharpyProjectCompiler.Decide(false, false, true, "fp", ""));
        }

        [Test]
        public void Decide_SaveOnInputsThatFailed_Compiles()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.Compile,
                SharpyProjectCompiler.Decide(false, false, false, "fp", "fp"));
        }
    }
}
