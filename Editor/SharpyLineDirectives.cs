namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Rewrites the <c>#line</c> directives in sharpyc's generated C# so Unity
    /// can compile them and open the right file. sharpyc emits both the classic
    /// form and the C# 10 span form, which Unity's C# 9 rejects; both become
    /// the classic <c>#line N "path"</c>.
    /// The project compiler passes a null project root, so paths stay absolute:
    /// Unity resolves a relative <c>#line</c> path against the folder of the .cs
    /// that contains it, in its compiler messages and in the PDB (stack traces)
    /// alike, so <c>"Assets/x.spy"</c> in Assets/SharpyGenerated/Sub/y.cs means
    /// Assets/SharpyGenerated/Sub/Assets/x.spy. Unity still shows an absolute
    /// path inside the project as Assets/x.spy.
    /// </summary>
    internal static class SharpyLineDirectives
    {
        // #line (5, 9) - (5, 35) 12 "/abs/x.spy" — the char offset is optional.
        private static readonly Regex SpanDirective = new Regex(
            @"^(\s*)#\s*line\s*\(\s*(\d+)\s*,\s*\d+\s*\)\s*-\s*\(\s*\d+\s*,\s*\d+\s*\)\s*(?:\d+\s*)?""([^""]*)""\s*$",
            RegexOptions.CultureInvariant);

        // #line 4 "/abs/x.spy"
        private static readonly Regex ClassicDirective = new Regex(
            @"^(\s*)#\s*line\s+(\d+)\s+""([^""]*)""(.*)$",
            RegexOptions.CultureInvariant);

        // Any #line, including hidden / default / a bare line number.
        private static readonly Regex AnyDirective = new Regex(
            @"^\s*#\s*line\b",
            RegexOptions.CultureInvariant);

        private static readonly Regex DrivePath = new Regex(
            @"^[A-Za-z]:",
            RegexOptions.CultureInvariant);

        /// <param name="csText">Generated C# text.</param>
        /// <param name="projectRoot">Unity project root; paths under it become relative. Blank → paths only normalised.</param>
        /// <param name="keepDirectives">False removes every <c>#line</c> line, newline included.</param>
        public static string Rewrite(string csText, string projectRoot, bool keepDirectives)
        {
            if (string.IsNullOrEmpty(csText) || csText.IndexOf('#') < 0)
            {
                return csText;
            }

            var result = new StringBuilder(csText.Length);
            int start = 0;
            while (start < csText.Length)
            {
                int newline = csText.IndexOf('\n', start);
                int end = newline < 0 ? csText.Length : newline + 1;

                // Content excludes the line ending, which is copied through unchanged.
                int contentEnd = newline < 0 ? csText.Length : newline;
                if (contentEnd > start && csText[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }

                string content = csText.Substring(start, contentEnd - start);
                if (!AnyDirective.IsMatch(content))
                {
                    result.Append(csText, start, end - start);
                }
                else if (keepDirectives)
                {
                    result.Append(RewriteDirective(content, projectRoot));
                    result.Append(csText, contentEnd, end - contentEnd);
                }

                start = end;
            }

            return result.ToString();
        }

        private static string RewriteDirective(string directive, string projectRoot)
        {
            Match span = SpanDirective.Match(directive);
            if (span.Success)
            {
                return span.Groups[1].Value + "#line " + span.Groups[2].Value
                    + " \"" + MakeRelative(span.Groups[3].Value, projectRoot) + "\"";
            }

            Match classic = ClassicDirective.Match(directive);
            if (classic.Success)
            {
                return classic.Groups[1].Value + "#line " + classic.Groups[2].Value
                    + " \"" + MakeRelative(classic.Groups[3].Value, projectRoot) + "\""
                    + classic.Groups[4].Value;
            }

            // #line hidden, #line default, #line 7
            return directive;
        }

        internal static string MakeRelative(string path, string projectRoot)
        {
            string normalized = path.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return normalized;
            }

            string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var comparison = DrivePath.IsMatch(root)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return normalized.Length > root.Length && normalized.StartsWith(root, comparison)
                ? normalized.Substring(root.Length)
                : normalized;
        }
    }
}
