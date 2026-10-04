namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
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

        internal static string ExtractSemver(string versionFirstLine)
        {
            if (string.IsNullOrEmpty(versionFirstLine))
            {
                return null;
            }

            // Ignores any +build / -prerelease suffix.
            var match = Regex.Match(versionFirstLine, @"\d+\.\d+\.\d+");
            return match.Success ? match.Value : null;
        }

        internal static bool MatchesPin(string semver)
        {
            return semver == SharpyToolchain.Version;
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

        private const string NamespaceWarningSessionKey = "Sharpy.NamespaceCollisionWarned";

        internal static void WarnOnNamespaceCollision(string rootNamespace)
        {
            if (!SharpySettings.NamespaceCollidesWithSharpy(rootNamespace)
                || SessionState.GetBool(NamespaceWarningSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(NamespaceWarningSessionKey, true);
            UnityEngine.Debug.LogWarning(
                $"[Sharpy] Root namespace \"{rootNamespace}\" contains a \"Sharpy\" segment, "
                + "which shadows the Sharpy.* types generated code references. "
                + "Compilation of the generated C# will likely fail.");
        }

        /// <summary>
        /// Runs <c>sharpyc project</c>. Diagnostics are parsed on every exit:
        /// warnings arrive on stdout even when the build succeeds. Paths under
        /// <paramref name="projectRoot"/> become project-relative, so pass the
        /// spyproj path spelled from that same root (sharpyc prints paths the
        /// way it was given them).
        /// </summary>
        public static CompileResult CompileProject(string spyprojPath, string outputDir, string projectRoot)
        {
            var settings = SharpySettings.instance;
            var args = $"project \"{spyprojPath}\" --emit-cs-to \"{outputDir}\"";
            var result = RunCompiler(args, settings.CompilerTimeoutSeconds);
            result.Diagnostics = SharpyDiagnosticParser.ParseCompilerOutput(
                result.Stdout, result.Stderr, projectRoot, !result.Success);
            return result;
        }

        private static CompileResult RunCompiler(string arguments, int timeoutSeconds)
        {
            string compilerPath = GetCompilerPath();

            // Fallback for batch runs that reach a compile before the
            // editor-load install ran (e.g. settings could not load yet).
            if (!File.Exists(compilerPath)
                && Application.isBatchMode
                && compilerPath == GetManagedCompilerPath())
            {
                SharpyBinaryDownloader.InstallInBatchModeOnce();
            }

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
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                };

                // sharpyc colours its output when stdout looks like a terminal.
                startInfo.EnvironmentVariables["NO_COLOR"] = "1";

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
    }
}
