namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyCompilerBridgeTests
    {
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
    }
}
