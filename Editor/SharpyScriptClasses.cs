namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Finds the class each generated script must be named after. Unity binds
    /// a MonoBehaviour or ScriptableObject to a script asset only when the file
    /// is named after the class; sharpyc names it after the module
    /// (smoke_behaviour.cs holding SmokeBehaviour), so the component cannot be
    /// added from the Inspector, and a scene that holds one embeds a MonoScript
    /// by class name instead of referencing the script's GUID.
    /// </summary>
    internal static class SharpyScriptClasses
    {
        // Unity object types whose subclasses must live in a file named after them.
        internal static readonly HashSet<string> UnityRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.MonoBehaviour",
            "UnityEngine.ScriptableObject",
            "UnityEngine.StateMachineBehaviour",
        };

        private static readonly Regex NamespacePattern = new Regex(
            @"^\s*namespace\s+([\w.]+)",
            RegexOptions.CultureInvariant);

        // `public class Name : Base, IFoo` — generic classes cannot be attached,
        // so `class Name<T>` does not match.
        private static readonly Regex ClassPattern = new Regex(
            @"^\s*(?:(?:public|internal|private|protected|sealed|abstract|partial|static|unsafe|new)\s+)*class\s+@?(\w+)\s*(?::\s*([^{\r\n]+))?\s*$",
            RegexOptions.CultureInvariant);

        private sealed class ClassInfo
        {
            public string Owner;
            public string Name;
            public string FullName;
            public string Base;
        }

        /// <summary>
        /// For each .spy (key of <paramref name="generatedBySpy"/>) whose C#
        /// declares exactly one MonoBehaviour or ScriptableObject class, directly
        /// or through other classes in the project, that class's name. A .spy
        /// with several gets a warning in <paramref name="warnings"/> instead.
        /// </summary>
        /// <param name="isUnityObjectType">
        /// Whether a base class from outside the project (full name, no
        /// global::) is a MonoBehaviour or ScriptableObject type.
        /// </param>
        public static Dictionary<string, string> Find(
            IDictionary<string, string> generatedBySpy, Func<string, bool> isUnityObjectType, List<string> warnings)
        {
            var classes = new List<ClassInfo>();
            var byFullName = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in generatedBySpy)
            {
                foreach (ClassInfo info in Parse(entry.Key, entry.Value))
                {
                    classes.Add(info);
                    byFullName[info.FullName] = info;
                }
            }

            var unityClassesBySpy = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (ClassInfo info in classes)
            {
                if (IsUnityObject(info, byFullName, isUnityObjectType))
                {
                    if (!unityClassesBySpy.TryGetValue(info.Owner, out List<string> names))
                    {
                        names = new List<string>();
                        unityClassesBySpy[info.Owner] = names;
                    }

                    names.Add(info.Name);
                }
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<string>> entry in unityClassesBySpy)
            {
                if (entry.Value.Count == 1)
                {
                    result[entry.Key] = entry.Value[0];
                }
                else
                {
                    warnings.Add(
                        $"[Sharpy] {entry.Key} defines {entry.Value.Count} MonoBehaviour/ScriptableObject classes "
                        + $"({string.Join(", ", entry.Value)}). Unity can only attach one whose script file is named "
                        + "after it, so none of them can be added as a component; give each its own .spy file.");
                }
            }

            return result;
        }

        private static IEnumerable<ClassInfo> Parse(string owner, string cs)
        {
            string ns = string.Empty;

            foreach (string rawLine in (cs ?? string.Empty).Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');

                Match nsMatch = NamespacePattern.Match(line);
                if (nsMatch.Success)
                {
                    ns = nsMatch.Groups[1].Value;
                    continue;
                }

                Match classMatch = ClassPattern.Match(line);
                if (!classMatch.Success)
                {
                    continue;
                }

                string name = classMatch.Groups[1].Value;
                yield return new ClassInfo
                {
                    Owner = owner,
                    Name = name,
                    FullName = ns.Length > 0 ? ns + "." + name : name,
                    Base = FirstBase(classMatch.Groups[2].Value),
                };
            }
        }

        // The first entry of a base list, without global:: and generic arguments.
        private static string FirstBase(string baseList)
        {
            if (string.IsNullOrWhiteSpace(baseList))
            {
                return null;
            }

            int depth = 0;
            int end = baseList.Length;

            for (int i = 0; i < baseList.Length; i++)
            {
                char c = baseList[i];

                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    end = i;
                    break;
                }
            }

            string first = baseList.Substring(0, end).Trim();

            if (first.StartsWith("global::", StringComparison.Ordinal))
            {
                first = first.Substring("global::".Length);
            }

            int generic = first.IndexOf('<');
            return generic >= 0 ? first.Substring(0, generic) : first;
        }

        // Follows the base chain through the project's own classes; the first
        // base outside the project decides.
        private static bool IsUnityObject(
            ClassInfo info, Dictionary<string, ClassInfo> byFullName, Func<string, bool> isUnityObjectType)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string current = info.Base;

            while (current != null && seen.Add(current))
            {
                if (!byFullName.TryGetValue(current, out ClassInfo parent))
                {
                    return UnityRoots.Contains(current) || isUnityObjectType(current);
                }

                current = parent.Base;
            }

            return false;
        }
    }
}
