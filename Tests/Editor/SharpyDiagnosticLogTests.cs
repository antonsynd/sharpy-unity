namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyDiagnosticLogTests
    {
        [Test]
        public void Format_Located_CompilerStyle()
        {
            var diagnostic = new SharpyDiagnostic
            {
                FilePath = "Assets/Scripts/Core/greeting.spy",
                Line = 2,
                Column = 12,
                Code = "SPY0222",
                Message = "Type 'int32' does not support operator '+'"
            };

            Assert.AreEqual(
                "Assets/Scripts/Core/greeting.spy(2,12): SPY0222: Type 'int32' does not support operator '+'",
                SharpyDiagnosticLog.Format(diagnostic));
        }

        [Test]
        public void Format_FileWithoutLine_NoParentheses()
        {
            var diagnostic = new SharpyDiagnostic { FilePath = "assets/a.spy", Code = "SPY0302", Message = "Circular import" };

            Assert.AreEqual("assets/a.spy: SPY0302: Circular import", SharpyDiagnosticLog.Format(diagnostic));
        }

        [Test]
        public void Format_NoFileNoCode_PrefixedMessage()
        {
            var diagnostic = new SharpyDiagnostic { Message = "Error: Invalid .spyproj file" };

            Assert.AreEqual("[Sharpy] Error: Invalid .spyproj file", SharpyDiagnosticLog.Format(diagnostic));
        }
    }
}
