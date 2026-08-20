namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyCompilerBridgeTests
    {
        [Test]
        public void ParseJsonDiagnostics_ValidJson_ParsesCorrectly()
        {
            string json = @"[
                {
                    ""severity"": ""error"",
                    ""code"": ""SPY0200"",
                    ""line"": 5,
                    ""column"": 3,
                    ""message"": ""Unexpected token"",
                    ""phase"": ""Parser""
                }
            ]";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics(json, "test.spy");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.AreEqual("SPY0200", result[0].Code);
            Assert.AreEqual(5, result[0].Line);
            Assert.AreEqual(3, result[0].Column);
            Assert.AreEqual("Unexpected token", result[0].Message);
            Assert.AreEqual("Parser", result[0].Phase);
            Assert.AreEqual("test.spy", result[0].FilePath);
        }

        [Test]
        public void ParseJsonDiagnostics_MultipleDiagnostics_ParsesAll()
        {
            string json = @"[
                {""severity"": ""error"", ""code"": ""SPY0200"", ""line"": 1, ""column"": 1, ""message"": ""first"", ""phase"": ""Lexer""},
                {""severity"": ""warning"", ""code"": ""SPY0300"", ""line"": 2, ""column"": 5, ""message"": ""second"", ""phase"": ""TypeChecking""}
            ]";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics(json, "test.spy");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Warning, result[1].Severity);
        }

        [Test]
        public void ParseJsonDiagnostics_EmptyArray_ReturnsEmpty()
        {
            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics("[]", "test.spy");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseJsonDiagnostics_NullOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual(0, SharpyCompilerBridge.ParseJsonDiagnostics(null, "test.spy").Count);
            Assert.AreEqual(0, SharpyCompilerBridge.ParseJsonDiagnostics("", "test.spy").Count);
            Assert.AreEqual(0, SharpyCompilerBridge.ParseJsonDiagnostics("   ", "test.spy").Count);
        }

        [Test]
        public void ParseJsonDiagnostics_InvalidJson_ReturnsEmpty()
        {
            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics("not json", "test.spy");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void BuildFailureDiagnostics_JsonDiagnosticsPresent_PassesThemThrough()
        {
            var json = new List<SharpyDiagnostic>
            {
                new SharpyDiagnostic { Severity = SharpyDiagnostic.DiagnosticSeverity.Error, Code = "SPY0200" }
            };

            List<SharpyDiagnostic> result = SharpyCompilerBridge.BuildFailureDiagnostics(
                json, "stderr text", "test.spy");

            Assert.AreSame(json, result);
        }

        [Test]
        public void BuildFailureDiagnostics_EmptyJson_FallsBackToStderr()
        {
            List<SharpyDiagnostic> result = SharpyCompilerBridge.BuildFailureDiagnostics(
                new List<SharpyDiagnostic>(), "error[SPY0322]: something broke\n", "test.spy");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.AreEqual("error[SPY0322]: something broke", result[0].Message);
            Assert.AreEqual("test.spy", result[0].FilePath);
        }

        [Test]
        public void BuildFailureDiagnostics_MalformedJsonOutput_FallsBackToStderr()
        {
            List<SharpyDiagnostic> parsed = SharpyCompilerBridge.ParseJsonDiagnostics("not json", "test.spy");

            List<SharpyDiagnostic> result = SharpyCompilerBridge.BuildFailureDiagnostics(
                parsed, "raw stderr", "test.spy");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("raw stderr", result[0].Message);
        }

        [Test]
        public void BuildFailureDiagnostics_NoJsonNoStderr_ProducesGenericError()
        {
            List<SharpyDiagnostic> result = SharpyCompilerBridge.BuildFailureDiagnostics(
                null, "   ", "test.spy");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.IsNotEmpty(result[0].Message);
        }

        [Test]
        public void BuildCompileArgs_NoOptionalSettings_ProducesBaseArgs()
        {
            string args = SharpyCompilerBridge.BuildCompileArgs(
                "Assets/foo.spy", "Assets/SharpyGenerated/foo.cs", "",
                new List<string>(), new List<string>());

            Assert.AreEqual(
                "emit csharp \"Assets/foo.spy\" -o \"Assets/SharpyGenerated/foo.cs\" -t library",
                args);
        }

        [Test]
        public void BuildCompileArgs_ModulePathsAndReferences_AppendRepeatableFlags()
        {
            string args = SharpyCompilerBridge.BuildCompileArgs(
                "a.spy", "a.cs", "Game",
                new List<string> { "/mods/one", "/mods/two" },
                new List<string> { "UnityEngine.CoreModule" });

            Assert.AreEqual(
                "emit csharp \"a.spy\" -o \"a.cs\" -t library --namespace \"Game\""
                + " -m \"/mods/one\" -m \"/mods/two\" -r \"UnityEngine.CoreModule\"",
                args);
        }

        [Test]
        public void BuildCompileArgs_PathsWithSpaces_StayQuoted()
        {
            string args = SharpyCompilerBridge.BuildCompileArgs(
                "My Assets/foo.spy", "out dir/foo.cs", null,
                new List<string> { "/path with space" }, null);

            StringAssert.Contains("\"My Assets/foo.spy\"", args);
            StringAssert.Contains("-m \"/path with space\"", args);
        }

        [Test]
        public void BuildCompileArgs_EmptyEntriesInLists_AreSkipped()
        {
            string args = SharpyCompilerBridge.BuildCompileArgs(
                "a.spy", "a.cs", "",
                new List<string> { "", "   ", "/real" },
                new List<string> { null });

            Assert.AreEqual(
                "emit csharp \"a.spy\" -o \"a.cs\" -t library -m \"/real\"",
                args);
        }

        [Test]
        public void ExtractSemver_VersionWithBuildMetadata_ReturnsBareSemver()
        {
            Assert.AreEqual("0.15.0", SharpyCompilerBridge.ExtractSemver("sharpyc 0.15.0+84a2cef70"));
        }

        [Test]
        public void ExtractSemver_BareVersion_ReturnsSemver()
        {
            Assert.AreEqual("0.16.1", SharpyCompilerBridge.ExtractSemver("sharpyc 0.16.1"));
        }

        [Test]
        public void ExtractSemver_Garbage_ReturnsNull()
        {
            Assert.IsNull(SharpyCompilerBridge.ExtractSemver("unknown"));
            Assert.IsNull(SharpyCompilerBridge.ExtractSemver(null));
            Assert.IsNull(SharpyCompilerBridge.ExtractSemver(""));
        }

        [Test]
        public void MatchesPin_PinnedVersion_ReturnsTrue()
        {
            Assert.IsTrue(SharpyCompilerBridge.MatchesPin(SharpyToolchain.Version));
        }

        [Test]
        public void MatchesPin_OtherOrNull_ReturnsFalse()
        {
            Assert.IsFalse(SharpyCompilerBridge.MatchesPin("0.0.1"));
            Assert.IsFalse(SharpyCompilerBridge.MatchesPin(null));
        }

        [Test]
        public void ResolveCompilerPath_CustomPathSet_WinsOverManaged()
        {
            Assert.AreEqual(
                "/opt/tools/sharpyc",
                SharpyCompilerBridge.ResolveCompilerPath("  /opt/tools/sharpyc  "));
        }

        [Test]
        public void ResolveCompilerPath_NullOrWhitespace_FallsBackToManagedInstall()
        {
            foreach (string custom in new[] { null, "", "   " })
            {
                string resolved = SharpyCompilerBridge.ResolveCompilerPath(custom);

                StringAssert.Contains("SharpyCompiler", resolved);
                StringAssert.Contains(SharpyToolchain.Version, resolved);
            }
        }

        [Test]
        public void FirstLine_MultiLineVersionOutput_ReturnsFirstLine()
        {
            string output = "sharpyc 0.15.0+84a2cef70\nRuntime: .NET 10.0.11\nOS: macOS 26.6.2\n";

            Assert.AreEqual("sharpyc 0.15.0+84a2cef70", SharpyCompilerBridge.FirstLine(output));
        }

        [Test]
        public void FirstLine_WindowsLineEndings_ReturnsFirstLine()
        {
            string output = "sharpyc 0.16.1\r\nRuntime: .NET 10.0.11\r\n";

            Assert.AreEqual("sharpyc 0.16.1", SharpyCompilerBridge.FirstLine(output));
        }

        [Test]
        public void FirstLine_SingleLine_ReturnsTrimmed()
        {
            Assert.AreEqual("sharpyc 0.16.1", SharpyCompilerBridge.FirstLine("  sharpyc 0.16.1  \n"));
        }

        [Test]
        public void FirstLine_NullOrWhitespace_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SharpyCompilerBridge.FirstLine(null));
            Assert.AreEqual(string.Empty, SharpyCompilerBridge.FirstLine(""));
            Assert.AreEqual(string.Empty, SharpyCompilerBridge.FirstLine("   \n  "));
        }

        [Test]
        public void ParseJsonDiagnostics_WarningSeverity_MapsCorrectly()
        {
            string json = @"[{""severity"": ""warning"", ""code"": ""SPY0100"", ""line"": 1, ""column"": 1, ""message"": ""test"", ""phase"": ""Validation""}]";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics(json, "test.spy");

            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Warning, result[0].Severity);
        }

        [Test]
        public void ParseJsonDiagnostics_InfoSeverity_MapsCorrectly()
        {
            string json = @"[{""severity"": ""info"", ""code"": ""SPY0050"", ""line"": 1, ""column"": 1, ""message"": ""test"", ""phase"": ""Lexer""}]";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics(json, "test.spy");

            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Info, result[0].Severity);
        }

        [Test]
        public void ParseJsonDiagnostics_HintSeverity_MapsToInfo()
        {
            string json = @"[{""severity"": ""hint"", ""code"": ""SPY0050"", ""line"": 1, ""column"": 1, ""message"": ""test"", ""phase"": ""Lexer""}]";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseJsonDiagnostics(json, "test.spy");

            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Info, result[0].Severity);
        }
    }
}
