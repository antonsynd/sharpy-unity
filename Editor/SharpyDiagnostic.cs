using System;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
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
