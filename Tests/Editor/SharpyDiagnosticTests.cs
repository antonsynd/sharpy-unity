namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;
    using UnityEngine;

    public class SharpyDiagnosticTests
    {
        [Test]
        public void ToUnityLogType_Error_ReturnsLogTypeError()
        {
            var diagnostic = new SharpyDiagnostic
            {
                Severity = SharpyDiagnostic.DiagnosticSeverity.Error
            };

            Assert.AreEqual(LogType.Error, diagnostic.ToUnityLogType());
        }

        [Test]
        public void ToUnityLogType_Warning_ReturnsLogTypeWarning()
        {
            var diagnostic = new SharpyDiagnostic
            {
                Severity = SharpyDiagnostic.DiagnosticSeverity.Warning
            };

            Assert.AreEqual(LogType.Warning, diagnostic.ToUnityLogType());
        }

        [Test]
        public void ToUnityLogType_Info_ReturnsLogTypeLog()
        {
            var diagnostic = new SharpyDiagnostic
            {
                Severity = SharpyDiagnostic.DiagnosticSeverity.Info
            };

            Assert.AreEqual(LogType.Log, diagnostic.ToUnityLogType());
        }

        [Test]
        public void DefaultValues_AreEmptyStrings()
        {
            var diagnostic = new SharpyDiagnostic();

            Assert.AreEqual(string.Empty, diagnostic.Code);
            Assert.AreEqual(string.Empty, diagnostic.Message);
            Assert.AreEqual(string.Empty, diagnostic.FilePath);
            Assert.AreEqual(string.Empty, diagnostic.Phase);
            Assert.AreEqual(0, diagnostic.Line);
            Assert.AreEqual(0, diagnostic.Column);
        }
    }
}
