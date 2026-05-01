using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
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
            string platformDir;

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                platformDir = SystemInfo.processorType.Contains("Apple")
                    ? "osx-arm64"
                    : "osx-x64";
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                platformDir = "win-x64";
            }
            else
            {
                platformDir = "linux-x64";
            }

            string packagePath = Path.GetFullPath("Packages/com.antonsynd.sharpy");
            string binaryName = Application.platform == RuntimePlatform.WindowsEditor
                ? "sharpyc.exe"
                : "sharpyc";

            return Path.Combine(packagePath, "Editor", "Binaries", platformDir, binaryName);
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

            return RunCompiler(args, settings.CompilerTimeoutSeconds);
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
            return RunCompiler(args, settings.CompilerTimeoutSeconds);
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

                result.Stdout = process.StandardOutput.ReadToEnd();
                result.Stderr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutSeconds * 1000))
                {
                    process.Kill();
                    result.Success = false;
                    result.ExitCode = -1;
                    result.Stderr = $"Compiler timed out after {timeoutSeconds} seconds.";
                    return result;
                }

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
