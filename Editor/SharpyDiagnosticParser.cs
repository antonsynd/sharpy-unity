namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Parses the diagnostics sharpyc renders for humans (its DiagnosticRenderer):
    /// <code>
    /// error[SPY0200]: Undefined identifier 'nmae'. Did you mean 'name'?
    ///   --> /abs/path/Assets/Scripts/Core/greeting.spy:2:24
    ///     |
    ///   2 |     return "Hello, " + nmae
    ///     |                        ^^^^
    /// </code>
    /// `sharpyc project` has no JSON diagnostics: errors go to stderr after
    /// "Build FAILED.", warnings go to stdout on success and on failure.
    /// </summary>
    internal static class SharpyDiagnosticParser
    {
        // The severities DiagnosticRenderer.RenderHeader prints; the code is optional.
        private static readonly Regex HeaderPattern = new Regex(
            @"^(error|warning|info|hint)(?:\[([^\]\s]+)\])?: (.*)$",
            RegexOptions.Compiled);

        // `--> path:line:col`, `--> path:line` or `--> path`. The lazy path with the
        // trailing groups anchored at the end splits on the LAST `:digits`, so a
        // Windows drive colon stays in the path.
        private static readonly Regex LocationPattern = new Regex(
            @"^\s*-->\s+(.+?)(?::(\d+)(?::(\d+))?)?\s*$",
            RegexOptions.Compiled);

        // sharpyc colours only when its stdout is a terminal, but then it colours
        // stderr too. Also strips terminal mode sequences such as ESC[?1h ESC=.
        private static readonly Regex AnsiPattern = new Regex(
            @"\x1b(?:\[[0-9;?]*[A-Za-z]|[=>])",
            RegexOptions.Compiled);

        // The renderer's stand-in when a diagnostic has a line but no file.
        private const string UnknownSourcePath = "<source>";

        internal const string NoDiagnosticsMessage = "Compilation failed with no diagnostics.";

        /// <summary>
        /// Parses stderr and stdout of one sharpyc run. When the run failed but no
        /// error could be parsed (e.g. `Error: Invalid .spyproj file`), the raw
        /// stderr is added as a single error so the failure is never silent.
        /// </summary>
        internal static List<SharpyDiagnostic> ParseCompilerOutput(
            string stdout, string stderr, string projectRoot, bool failed)
        {
            var diagnostics = ParseTextDiagnostics(stderr, projectRoot);
            diagnostics.AddRange(ParseTextDiagnostics(stdout, projectRoot));

            if (failed && !diagnostics.Exists(d => d.Severity == SharpyDiagnostic.DiagnosticSeverity.Error))
            {
                var raw = StripAnsi(stderr ?? string.Empty).Trim();
                diagnostics.Add(new SharpyDiagnostic
                {
                    Severity = SharpyDiagnostic.DiagnosticSeverity.Error,
                    Message = raw.Length == 0 ? NoDiagnosticsMessage : raw
                });
            }

            return diagnostics;
        }

        /// <summary>
        /// Pairs each `severity[CODE]: message` header with the `-->` line that
        /// follows it. A header with no location keeps line 0 and an empty path.
        /// Paths under <paramref name="projectRoot"/> become project-relative with
        /// '/' separators; other paths are returned as printed.
        /// </summary>
        internal static List<SharpyDiagnostic> ParseTextDiagnostics(string text, string projectRoot)
        {
            var diagnostics = new List<SharpyDiagnostic>();

            if (string.IsNullOrEmpty(text))
            {
                return diagnostics;
            }

            // The diagnostic whose header was just read and which has no location yet.
            // Lines before its `-->` (or the blank line that ends it) continue its message.
            SharpyDiagnostic pending = null;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = StripAnsi(rawLine.TrimEnd('\r'));

                var header = HeaderPattern.Match(line);
                if (header.Success)
                {
                    pending = new SharpyDiagnostic
                    {
                        Severity = ParseSeverity(header.Groups[1].Value),
                        Code = header.Groups[2].Value,
                        Message = header.Groups[3].Value
                    };
                    diagnostics.Add(pending);
                    continue;
                }

                if (pending == null)
                {
                    continue;
                }

                var location = LocationPattern.Match(line);
                if (location.Success)
                {
                    pending.FilePath = ToProjectPath(location.Groups[1].Value, projectRoot);
                    pending.Line = ParseNumber(location.Groups[2]);
                    pending.Column = ParseNumber(location.Groups[3]);
                    pending = null;
                }
                else if (line.Trim().Length == 0)
                {
                    pending = null;
                }
                else
                {
                    pending.Message += "\n" + line;
                }
            }

            return diagnostics;
        }

        private static SharpyDiagnostic.DiagnosticSeverity ParseSeverity(string severity)
        {
            return severity switch
            {
                "error" => SharpyDiagnostic.DiagnosticSeverity.Error,
                "warning" => SharpyDiagnostic.DiagnosticSeverity.Warning,
                _ => SharpyDiagnostic.DiagnosticSeverity.Info
            };
        }

        private static int ParseNumber(Group group)
        {
            return group.Success && int.TryParse(group.Value, out var value) ? value : 0;
        }

        private static string ToProjectPath(string path, string projectRoot)
        {
            if (path == UnknownSourcePath)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(projectRoot))
            {
                return path;
            }

            // Case-insensitive: sharpyc lowercases the whole path in some diagnostics
            // (SPY0302 circular imports).
            var normalized = path.Replace('\\', '/');
            var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : path;
        }

        private static string StripAnsi(string text)
        {
            return text.IndexOf('\x1b') < 0 ? text : AnsiPattern.Replace(text, string.Empty);
        }
    }
}
