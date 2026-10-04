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

        private const string LoadFailure =
            "Unexpected error: Unable to load one or more of the requested types.\n"
            + "Could not load file or assembly 'log4net, Version=1.2.15.0'.";

        [Test]
        public void FailureHint_FailedRunThatCouldNotLoadAReference_Hints()
        {
            StringAssert.Contains("sharpy#2182", SharpyProjectCompiler.FailureHint(1, "", LoadFailure));
        }

        [Test]
        public void FailureHint_LoadFailureOnStdout_Hints()
        {
            Assert.IsNotNull(SharpyProjectCompiler.FailureHint(3, LoadFailure, "Build FAILED."));
        }

        [Test]
        public void FailureHint_SuccessfulRun_None()
        {
            Assert.IsNull(SharpyProjectCompiler.FailureHint(0, LoadFailure, LoadFailure));
        }

        [Test]
        public void FailureHint_OrdinarySharpyError_None()
        {
            Assert.IsNull(SharpyProjectCompiler.FailureHint(
                1, "", "Build FAILED.\n\nerror[SPY0222]: Type 'int32' does not support operator '+'\n"));
        }

        [TestCase("Assets/Gen1", "Assets/Gen2", true)]
        [TestCase("Assets/Gen1", "Assets/Gen1/", false)]
        [TestCase("Assets/Gen1", "assets/gen1", false)]
        [TestCase(null, "Assets/Gen2", false)]
        [TestCase("", "Assets/Gen2", false)]
        public void IsOutputFolderChange(string previous, string current, bool expected)
        {
            Assert.AreEqual(expected, SharpyProjectCompiler.IsOutputFolderChange(previous, current));
        }

        [Test]
        public void FailureKey_TimeoutChanged_RetriesOnFocus()
        {
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.Compile,
                SharpyProjectCompiler.Decide(
                    false, false, true,
                    SharpyProjectCompiler.FailureKey("fp", 60),
                    SharpyProjectCompiler.FailureKey("fp", 30)));
            Assert.AreEqual(
                SharpyProjectCompiler.CompileDecision.KnownFailure,
                SharpyProjectCompiler.Decide(
                    false, false, true,
                    SharpyProjectCompiler.FailureKey("fp", 30),
                    SharpyProjectCompiler.FailureKey("fp", 30)));
        }

        [Test]
        public void CompilerIdentity_ManagedInstall_ChangesWhenInstalled()
        {
            string missing = SharpyProjectCompiler.CompilerIdentity("", "/p/Library/SharpyCompiler/sharpyc", false, () => "never");
            string installed = SharpyProjectCompiler.CompilerIdentity("", "/p/Library/SharpyCompiler/sharpyc", true, () => "never");

            Assert.AreNotEqual(missing, installed);
            StringAssert.Contains(SharpyToolchain.Version, installed);
        }

        [Test]
        public void CompilerIdentity_CustomCompiler_AskedOnlyWhenPresent()
        {
            int asked = 0;

            Assert.AreEqual(
                "missing /opt/sharpyc",
                SharpyProjectCompiler.CompilerIdentity("/opt/sharpyc", "/opt/sharpyc", false, () => { asked++; return "v"; }));
            Assert.AreEqual(0, asked);
            Assert.AreEqual(
                "sharpyc 0.22.0",
                SharpyProjectCompiler.CompilerIdentity("/opt/sharpyc", "/opt/sharpyc", true, () => "sharpyc 0.22.0"));
        }
    }
}
