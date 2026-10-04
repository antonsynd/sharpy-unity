namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    /// Logs sharpyc diagnostics to the Console so that double-clicking one
    /// opens the .spy at its line, like a C# compiler error.
    /// </summary>
    internal static class SharpyDiagnosticLog
    {
        // A Console entry opens the file Unity recorded for it, which for
        // Debug.Log* is always the calling C# line (or nothing with
        // LogOption.NoStacktrace). Only these internal Debug methods take the
        // file and line explicitly (checked on 6000.3: entry.file = the .spy,
        // double-click reaches OnOpenAsset with the .spy and line). When they
        // are missing, the public fallback below logs a rich-text link that
        // opens the .spy on click instead.
        private static readonly MethodInfo LogCompilerError = FindInternalLog("LogCompilerError");
        private static readonly MethodInfo LogCompilerWarning = FindInternalLog("LogCompilerWarning");

        internal static string Format(SharpyDiagnostic diagnostic)
        {
            string code = string.IsNullOrEmpty(diagnostic.Code) ? string.Empty : diagnostic.Code + ": ";

            if (string.IsNullOrEmpty(diagnostic.FilePath))
            {
                return "[Sharpy] " + code + diagnostic.Message;
            }

            string location = diagnostic.Line > 0
                ? $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column})"
                : diagnostic.FilePath;

            return $"{location}: {code}{diagnostic.Message}";
        }

        public static void Log(SharpyDiagnostic diagnostic)
        {
            string message = Format(diagnostic);
            LogType type = diagnostic.ToUnityLogType();
            bool located = !string.IsNullOrEmpty(diagnostic.FilePath);

            MethodInfo internalLog = type == LogType.Error ? LogCompilerError
                : type == LogType.Warning ? LogCompilerWarning
                : null;

            if (located && internalLog != null && TryInvoke(internalLog, message, diagnostic))
            {
                return;
            }

            if (located && !Application.isBatchMode)
            {
                message = $"<a href=\"{diagnostic.FilePath}\" line=\"{Math.Max(diagnostic.Line, 1)}\">{message}</a>";
            }

            Debug.LogFormat(type, LogOption.NoStacktrace, null, "{0}", message);
        }

        private static bool TryInvoke(MethodInfo log, string message, SharpyDiagnostic diagnostic)
        {
            try
            {
                log.Invoke(null, new object[] { message, diagnostic.FilePath, diagnostic.Line, diagnostic.Column });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static MethodInfo FindInternalLog(string name)
        {
            MethodInfo method = typeof(Debug).GetMethod(
                name,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(string), typeof(int), typeof(int) },
                null);

            return method != null && method.ReturnType == typeof(void) ? method : null;
        }
    }
}
