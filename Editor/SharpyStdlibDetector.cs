namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Finds the Sharpy stdlib modules a generated C# file depends on, so a missing
    /// Sharpy.Stdlib.dll can be explained instead of surfacing as CS0246/CS0234.
    /// sharpyc spells stdlib references fully qualified (`using math =
    /// global::Sharpy.MathModule.MathModuleModule;`, `new global::Sharpy.Deque&lt;int&gt;()`),
    /// and <see cref="SharpyStdlibModules"/> maps those names to modules.
    /// </summary>
    internal static class SharpyStdlibDetector
    {
        internal const string InstallMenuItem = "Assets/Sharpy/Install Stdlib (experimental)";

        // A dotted name rooted at Sharpy that is not the tail of a longer name
        // (so `Game.Sharpy.Json` does not count).
        private static readonly Regex SharpyNamePattern = new Regex(
            @"(?<![\w.])(?:global::)?(Sharpy(?:\.[A-Za-z_][A-Za-z0-9_]*)+)",
            RegexOptions.Compiled);

        /// <summary>
        /// The distinct stdlib modules <paramref name="generatedCs"/> references,
        /// sorted. Comment and preprocessor lines (`#line` paths) are skipped.
        /// </summary>
        internal static List<string> FindModules(string generatedCs)
        {
            var modules = new SortedSet<string>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(generatedCs))
            {
                return new List<string>();
            }

            foreach (var rawLine in generatedCs.Split('\n'))
            {
                var line = rawLine.TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in SharpyNamePattern.Matches(line))
                {
                    var module = ModuleOf(match.Groups[1].Value);
                    if (module != null)
                    {
                        modules.Add(module);
                    }
                }
            }

            return new List<string>(modules);
        }

        /// <summary>
        /// The warning logged once per .spy whose generated C# needs stdlib modules
        /// that the project does not have.
        /// </summary>
        internal static string FormatWarning(string spyPath, IReadOnlyList<string> modules)
        {
            var quoted = new List<string>();
            foreach (var module in modules)
            {
                quoted.Add($"'{module}'");
            }

            var noun = quoted.Count == 1 ? "module" : "modules";
            return $"[Sharpy] {spyPath}: uses the Sharpy stdlib {noun} {string.Join(", ", quoted)}, "
                + "but Sharpy.Stdlib.dll is not in this project, so the generated C# will not compile. "
                + $"Install it with {InstallMenuItem}, or remove the import.";
        }

        // The module of the longest dotted prefix that names a stdlib type or
        // namespace: `Sharpy.Json.Dumps` → `Sharpy.Json` → json.
        private static string ModuleOf(string dottedName)
        {
            var name = dottedName;

            while (true)
            {
                if (SharpyStdlibModules.TypeModules.TryGetValue(name, out var module)
                    || SharpyStdlibModules.NamespaceModules.TryGetValue(name, out module))
                {
                    return module;
                }

                var dot = name.LastIndexOf('.');
                if (dot < 0)
                {
                    return null;
                }

                name = name.Substring(0, dot);
            }
        }
    }
}
