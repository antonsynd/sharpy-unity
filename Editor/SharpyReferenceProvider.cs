namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;

    /// <summary>
    /// Derives the assembly references sharpyc compiles against from Unity's
    /// own compilation setup, so nothing machine-specific is serialized.
    /// </summary>
    public static class SharpyReferenceProvider
    {
        // Assemblies sharpyc cannot load (sharpy#2182: one unloadable type
        // aborts the whole compile with ReflectionTypeLoadException), matched
        // by file name without extension. Checked against sharpyc 0.21.0 and
        // Unity 6000.3:
        //   UnityEngine.TextCoreTextEngineModule — "Could not load type
        //     'UnityEngine.TextCore.Text.FontAsset' ... the format is invalid"
        //   UnityEngine.UIElementsModule, UnityEngine.UI — the same failure,
        //     through their dependency on the module above
        //   nunit.framework (com.unity.test-framework's net40 build) — "Could
        //     not load type 'System.Web.UI.ICallbackEventHandler'"
        // The core libraries are here too: sharpyc compiles against its own
        // .NET runtime, and a second System.Object makes every predefined type
        // undefined (SPY0910 for both netstandard.dll and the .NET Framework
        // profile's mscorlib.dll).
        internal static readonly string[] BuiltInDenylist =
        {
            "UnityEngine.TextCoreTextEngineModule",
            "UnityEngine.UIElementsModule",
            "UnityEngine.UI",
            "nunit.framework",
            "netstandard",
            "mscorlib",
        };

        // Unity's own BCL (NetStandard facades, the .NET Framework profile's
        // reference assemblies) duplicates what sharpyc's runtime already
        // provides: the .NET Framework System.dll / System.Core.dll give
        // CS0433 "type exists in both" for Uri, HashSet<T>, LinkedList<T>.
        private static readonly string[] BclDirectoryNames =
        {
            "NetStandard",
            "UnityReferenceAssemblies",
            "MonoBleedingEdge",
        };

        /// <summary>
        /// The references for a project compile: the derived Unity references
        /// when <see cref="SharpySettings.AutoUnityReferences"/> is on, then
        /// <see cref="SharpySettings.AdditionalReferences"/>. Paths are absolute.
        /// </summary>
        public static List<string> GetReferences(SharpySettings settings)
        {
            return GetReferences(settings.AutoUnityReferences, settings.ReferenceDenylist, settings.AdditionalReferences);
        }

        internal static List<string> GetReferences(
            bool autoUnityReferences,
            IEnumerable<string> userDenylist,
            IEnumerable<string> additionalReferences)
        {
            var references = autoUnityReferences
                ? DeriveUnityReferences(userDenylist)
                : new List<string>();

            foreach (string extra in additionalReferences)
            {
                if (!string.IsNullOrWhiteSpace(extra) && !references.Contains(extra))
                {
                    references.Add(extra);
                }
            }

            return references;
        }

        /// <summary>
        /// Unity's references with everything sharpyc cannot use removed,
        /// regardless of <see cref="SharpySettings.AutoUnityReferences"/>.
        /// </summary>
        public static List<string> DeriveUnityReferences(IEnumerable<string> userDenylist)
        {
            var editorDependent = EditorDependentAssemblyPaths();
            var usable = new List<string>();

            foreach (string path in Collect())
            {
                if (!editorDependent.Contains(path))
                {
                    usable.Add(path);
                }
            }

            return Filter(usable, userDenylist);
        }

        /// <summary>
        /// Unfiltered, absolute reference paths: the player
        /// <c>Assembly-CSharp</c>'s references, or, when the project has no
        /// <c>Assembly-CSharp</c> yet (only .spy scripts), Unity's engine
        /// assemblies plus the plugins imported for the active build target.
        /// </summary>
        public static List<string> Collect()
        {
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                if (assembly.name == "Assembly-CSharp")
                {
                    return ToAbsolute(assembly.allReferences);
                }
            }

            // Not GetPrecompiledAssemblyPaths(UserAssembly): it lists the
            // plugins the editor loads, including editor-only ones that abort
            // sharpyc (collab-proxy's unityplastic.dll and log4netPlastic.dll,
            // com.unity.analytics' Unity.Analytics.Tracker.dll), and misses
            // player-only ones (Newtonsoft.Json's AOT build).
            var playerPlugins = new HashSet<string>();

            foreach (PluginImporter importer in PluginImporter.GetImporters(EditorUserBuildSettings.activeBuildTarget))
            {
                playerPlugins.Add(importer.assetPath);
            }

            var plugins = new List<PluginEntry>();

            foreach (PluginImporter importer in PluginImporter.GetAllImporters())
            {
                plugins.Add(new PluginEntry(
                    FileUtil.GetPhysicalPath(importer.assetPath),
                    importer.isNativePlugin,
                    playerPlugins.Contains(importer.assetPath)));
            }

            return ToAbsolute(FallbackReferences(
                CompilationPipeline.GetPrecompiledAssemblyPaths(
                    CompilationPipeline.PrecompiledAssemblySources.UnityEngine),
                plugins));
        }

        internal readonly struct PluginEntry
        {
            public readonly string Path;
            public readonly bool IsNative;
            public readonly bool ForPlayer;

            public PluginEntry(string path, bool isNative, bool forPlayer)
            {
                Path = path;
                IsNative = isNative;
                ForPlayer = forPlayer;
            }
        }

        /// <summary>
        /// The references a player script would get without an
        /// Assembly-CSharp to ask: the engine assemblies, then every managed
        /// plugin imported for the player. Editor-only plugins are left out.
        /// </summary>
        internal static List<string> FallbackReferences(
            IEnumerable<string> engineAssemblies,
            IEnumerable<PluginEntry> plugins)
        {
            var result = new List<string>(engineAssemblies);

            foreach (PluginEntry plugin in plugins)
            {
                if (plugin.ForPlayer
                    && !plugin.IsNative
                    && plugin.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(plugin.Path);
                }
            }

            return result;
        }

        // Script assemblies come back as "Library/ScriptAssemblies/x.dll";
        // sharpyc resolves relative paths against the .spyproj's folder.
        // Unity's working directory is the project root.
        internal static List<string> ToAbsolute(IEnumerable<string> paths)
        {
            var result = new List<string>();

            foreach (string path in paths)
            {
                result.Add(Path.GetFullPath(path));
            }

            return result;
        }

        /// <summary>
        /// Removes the plugin's own assemblies, the project's script
        /// assemblies, editor assemblies, Unity's BCL, and denylisted names; drops duplicates and
        /// keeps order. Denylist entries match the file name with or without
        /// ".dll", ignoring case.
        /// </summary>
        public static List<string> Filter(IEnumerable<string> paths, IEnumerable<string> userDenylist)
        {
            var denied = new HashSet<string>(BuiltInDenylist, StringComparer.OrdinalIgnoreCase);

            if (userDenylist != null)
            {
                foreach (string entry in userDenylist)
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                    {
                        denied.Add(StripDllExtension(entry.Trim()));
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string name = StripDllExtension(Path.GetFileName(path));

                // sharpyc brings its own Sharpy.Core; a second copy (and the
                // plugin's Sharpy.Unity.* assemblies) would be ambiguous.
                if (name.StartsWith("Sharpy.", StringComparison.OrdinalIgnoreCase)
                    // The generated code compiles into Assembly-CSharp itself.
                    || name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                    // Player code cannot use editor assemblies; they only come
                    // from the precompiled fallback, and UnityEditor.CoreModule
                    // crashes sharpyc ("Access is denied", sharpy#2182).
                    || name.StartsWith("UnityEditor.", StringComparison.OrdinalIgnoreCase)
                    || denied.Contains(name)
                    || IsInBclDirectory(path)
                    || !seen.Add(path))
                {
                    continue;
                }

                result.Add(path);
            }

            return result;
        }

        /// <summary>
        /// Full paths of the assemblies loaded in this editor that reference a
        /// UnityEditor assembly. Package assemblies under
        /// <c>Library/ScriptAssemblies</c> are their editor builds even in the
        /// player reference list; one that needs UnityEditor.CoreModule
        /// (Unity.InputSystem, Unity.TextMeshPro, ...) aborts sharpyc, which
        /// cannot load UnityEditor.CoreModule either (sharpy#2182).
        /// </summary>
        internal static HashSet<string> EditorDependentAssemblyPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Reflection.Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.IsDynamic || string.IsNullOrEmpty(loaded.Location))
                {
                    continue;
                }

                foreach (System.Reflection.AssemblyName reference in loaded.GetReferencedAssemblies())
                {
                    if (reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal))
                    {
                        paths.Add(Path.GetFullPath(loaded.Location));
                        break;
                    }
                }
            }

            return paths;
        }

        private static bool IsInBclDirectory(string path)
        {
            string[] segments = path.Split('/', '\\');

            // The last segment is the file name.
            for (int i = 0; i < segments.Length - 1; i++)
            {
                foreach (string bcl in BclDirectoryNames)
                {
                    if (string.Equals(segments[i], bcl, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string StripDllExtension(string fileName)
        {
            return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 4)
                : fileName;
        }
    }
}
