namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;

    public sealed class CompileResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
        public List<SharpyDiagnostic> Diagnostics { get; set; } = new List<SharpyDiagnostic>();
    }

    public static class SharpyCompilerBridge
    {
        public static string GetCompilerPath()
        {
            return ResolveCompilerPath(SharpySettings.instance.CustomCompilerPath);
        }

        // A non-empty custom path wins over the managed install; it is the
        // escape hatch for dev machines running sharpyc as a dotnet tool.
        internal static string ResolveCompilerPath(string customCompilerPath)
        {
            return string.IsNullOrWhiteSpace(customCompilerPath)
                ? GetManagedCompilerPath()
                : customCompilerPath.Trim();
        }

        // Managed installs live under the project's Library/ folder: writable
        // even for immutable (git-URL) package installs, never imported as
        // assets, and version-segmented so a pin bump triggers a fresh
        // download without disturbing the old one.
        public static string GetManagedCompilerPath()
        {
            string rid = SharpyToolchain.GetPlatformRid();
            string binaryName = rid.StartsWith("win") ? "sharpyc.exe" : "sharpyc";

            return Path.GetFullPath(
                Path.Combine("Library", "SharpyCompiler", SharpyToolchain.Version, rid, binaryName));
        }

        public static string GetCompilerVersion()
        {
            var result = RunCompiler("--version", 10);
            return result.Success ? FirstLine(result.Stdout) : "unknown";
        }

        // `--version` emits three lines (version, runtime, OS); only the first
        // identifies the compiler.
        internal static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            int newline = trimmed.IndexOf('\n');
            string line = newline >= 0 ? trimmed.Substring(0, newline) : trimmed;
            return line.Trim();
        }

        public static CompileResult CompileFile(string spyPath, string outputCsPath)
        {
            var settings = SharpySettings.instance;
            var args = $"emit csharp \"{spyPath}\" -o \"{outputCsPath}\" -t library";

            if (!string.IsNullOrEmpty(settings.RootNamespace))
            {
                args += $" --namespace \"{settings.RootNamespace}\"";
            }

            if (settings.ShowLineDirectives)
            {
                args += " --show-line-directives";
            }

            var result = RunCompiler(args, settings.CompilerTimeoutSeconds);

            if (!result.Success)
            {
                result.Diagnostics = ParseTextDiagnostics(result.Stderr, spyPath);
            }

            return result;
        }

        public static CompileResult CompileProject(string spyprojPath, string outputDir)
        {
            var settings = SharpySettings.instance;
            var args = $"project \"{spyprojPath}\" --emit-cs-to \"{outputDir}\"";
            return RunCompiler(args, settings.CompilerTimeoutSeconds);
        }

        public static CompileResult GetDiagnostics(string spyPath)
        {
            var settings = SharpySettings.instance;
            var args = $"emit diagnostics \"{spyPath}\" --format json";
            var result = RunCompiler(args, settings.CompilerTimeoutSeconds);
            result.Diagnostics = ParseJsonDiagnostics(result.Stdout, spyPath);
            return result;
        }

        private static CompileResult RunCompiler(string arguments, int timeoutSeconds)
        {
            string compilerPath = GetCompilerPath();

            if (!File.Exists(compilerPath))
            {
                return new CompileResult
                {
                    Success = false,
                    ExitCode = -1,
                    Stderr = $"Sharpy compiler not found at: {compilerPath}"
                };
            }

            var result = new CompileResult();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = compilerPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);

                if (process == null)
                {
                    result.Success = false;
                    result.ExitCode = -1;
                    result.Stderr = "Failed to start sharpyc process.";
                    return result;
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutSeconds * 1000))
                {
                    process.Kill();
                    result.Success = false;
                    result.ExitCode = -1;
                    result.Stderr = $"Compiler timed out after {timeoutSeconds} seconds.";
                    return result;
                }

                result.Stdout = stdoutTask.Result;
                result.Stderr = stderrTask.Result;
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -1;
                result.Stderr = ex.Message;
            }

            return result;
        }

        internal static List<SharpyDiagnostic> ParseJsonDiagnostics(string json, string fallbackFilePath)
        {
            var diagnostics = new List<SharpyDiagnostic>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return diagnostics;
            }

            json = json.Trim();

            if (!json.StartsWith("["))
            {
                return diagnostics;
            }

            var wrapper = JsonUtility.FromJson<DiagnosticArrayWrapper>(
                "{\"items\":" + json + "}");

            if (wrapper?.items == null)
            {
                return diagnostics;
            }

            foreach (var item in wrapper.items)
            {
                diagnostics.Add(new SharpyDiagnostic
                {
                    Severity = ParseSeverity(item.severity),
                    Code = item.code ?? string.Empty,
                    Line = item.line,
                    Column = item.column,
                    Message = item.message ?? string.Empty,
                    Phase = item.phase ?? string.Empty,
                    FilePath = fallbackFilePath
                });
            }

            return diagnostics;
        }

        private static readonly Regex TextDiagnosticPattern = new Regex(
            @"^(error|warning|info|hint)\s+(\S+)\s+\((\d+):(\d+)\):\s+(.+)$",
            RegexOptions.Multiline);

        internal static List<SharpyDiagnostic> ParseTextDiagnostics(string text, string fallbackFilePath)
        {
            var diagnostics = new List<SharpyDiagnostic>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return diagnostics;
            }

            foreach (Match match in TextDiagnosticPattern.Matches(text))
            {
                diagnostics.Add(new SharpyDiagnostic
                {
                    Severity = ParseSeverity(match.Groups[1].Value),
                    Code = match.Groups[2].Value,
                    Line = int.Parse(match.Groups[3].Value),
                    Column = int.Parse(match.Groups[4].Value),
                    Message = match.Groups[5].Value,
                    FilePath = fallbackFilePath
                });
            }

            return diagnostics;
        }

        private static SharpyDiagnostic.DiagnosticSeverity ParseSeverity(string severity)
        {
            return severity?.ToLowerInvariant() switch
            {
                "error" => SharpyDiagnostic.DiagnosticSeverity.Error,
                "warning" => SharpyDiagnostic.DiagnosticSeverity.Warning,
                _ => SharpyDiagnostic.DiagnosticSeverity.Info
            };
        }

        [Serializable]
        private class DiagnosticJsonItem
        {
            public string severity;
            public string code;
            public int line;
            public int column;
            public string message;
            public string phase;
        }

        [Serializable]
        private class DiagnosticArrayWrapper
        {
            public DiagnosticJsonItem[] items;
        }
    }
}
