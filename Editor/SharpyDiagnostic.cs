namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using UnityEngine;

    [Serializable]
    public sealed class SharpyDiagnostic
    {
        public enum DiagnosticSeverity
        {
            Info,
            Warning,
            Error
        }

        public DiagnosticSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;

        public LogType ToUnityLogType()
        {
            return Severity switch
            {
                DiagnosticSeverity.Error => LogType.Error,
                DiagnosticSeverity.Warning => LogType.Warning,
                _ => LogType.Log
            };
        }
    }
}
