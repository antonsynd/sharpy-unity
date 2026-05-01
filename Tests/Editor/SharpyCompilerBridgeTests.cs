using System.Collections.Generic;
using NUnit.Framework;

namespace Sharpy.Unity.Editor.Tests
{
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
        public void ParseTextDiagnostics_ValidText_ParsesCorrectly()
        {
            string text = "error SPY0200 (5:3): Unexpected token";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseTextDiagnostics(text, "test.spy");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.AreEqual("SPY0200", result[0].Code);
            Assert.AreEqual(5, result[0].Line);
            Assert.AreEqual(3, result[0].Column);
            Assert.AreEqual("Unexpected token", result[0].Message);
            Assert.AreEqual("test.spy", result[0].FilePath);
        }

        [Test]
        public void ParseTextDiagnostics_MultipleLines_ParsesAll()
        {
            string text = "error SPY0200 (1:1): first\nwarning SPY0300 (2:5): second";

            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseTextDiagnostics(text, "test.spy");

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void ParseTextDiagnostics_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, SharpyCompilerBridge.ParseTextDiagnostics(null, "test.spy").Count);
            Assert.AreEqual(0, SharpyCompilerBridge.ParseTextDiagnostics("", "test.spy").Count);
        }

        [Test]
        public void ParseTextDiagnostics_NonDiagnosticText_ReturnsEmpty()
        {
            List<SharpyDiagnostic> result = SharpyCompilerBridge.ParseTextDiagnostics(
                "some random output text", "test.spy");

            Assert.AreEqual(0, result.Count);
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
