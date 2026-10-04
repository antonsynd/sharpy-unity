namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;

    public class SharpyReferenceProviderTests
    {
        private const string Managed = "/Unity/Editor/Data/Managed/UnityEngine/";

        private static List<string> Filter(params string[] paths)
        {
            return SharpyReferenceProvider.Filter(paths, null);
        }

        [Test]
        public void Filter_NothingToFilter_ReturnsInputUnchanged()
        {
            var paths = new[]
            {
                Managed + "UnityEngine.CoreModule.dll",
                Managed + "UnityEngine.PhysicsModule.dll",
                "/Project/Library/ScriptAssemblies/Unity.InputSystem.ForUI.dll",
                "/Project/Assets/Plugins/Newtonsoft.Json.dll",
            };

            CollectionAssert.AreEqual(paths, SharpyReferenceProvider.Filter(paths, new string[0]));
        }

        [Test]
        public void Filter_BuiltInDenylist_Removed()
        {
            var result = Filter(
                Managed + "UnityEngine.CoreModule.dll",
                Managed + "UnityEngine.TextCoreTextEngineModule.dll",
                Managed + "UnityEngine.UIElementsModule.dll",
                "/Project/Library/ScriptAssemblies/UnityEngine.UI.dll",
                "/Project/Library/PackageCache/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll");

            CollectionAssert.AreEqual(new[] { Managed + "UnityEngine.CoreModule.dll" }, result);
        }

        [Test]
        public void Filter_EditorAssemblies_Removed()
        {
            var result = Filter(
                Managed + "UnityEditor.GIModule.dll",
                Managed + "UnityEditor.CoreModule.dll",
                Managed + "UnityEngine.CoreModule.dll");

            CollectionAssert.AreEqual(new[] { Managed + "UnityEngine.CoreModule.dll" }, result);
        }

        [Test]
        public void Filter_SharpyAndAssemblyCSharp_Removed_FirstpassKept()
        {
            var result = Filter(
                "/Project/Packages/com.antonsynd.sharpy/Plugins/Sharpy.Core/Sharpy.Core.dll",
                "/Project/Library/ScriptAssemblies/Sharpy.Unity.Runtime.dll",
                "/Project/Library/ScriptAssemblies/Assembly-CSharp.dll",
                "/Project/Library/ScriptAssemblies/Assembly-CSharp-Editor.dll",
                "/Project/Library/ScriptAssemblies/Assembly-CSharp-Editor-firstpass.dll",
                "/Project/Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll",
                Managed + "UnityEngine.CoreModule.dll");

            CollectionAssert.AreEqual(
                new[] { "/Project/Library/ScriptAssemblies/Assembly-CSharp-firstpass.dll", Managed + "UnityEngine.CoreModule.dll" },
                result);
        }

        private const string ScriptAssemblies = "/Project/Library/ScriptAssemblies/";

        // What CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName
        // answers in a project with these asmdefs.
        private static string AsmdefPathFor(string assemblyName)
        {
            switch (assemblyName)
            {
                case "UnityEngine.SpatialTracking":
                    return "Packages/com.unity.xr.legacyinputhelpers/Runtime/UnityEngine.SpatialTracking.asmdef";
                case "Unity.Multiplayer.Center.Common":
                    return "Packages\\com.unity.multiplayer.center\\Common\\Unity.Multiplayer.Center.Common.asmdef";
                case "Game.Core":
                    return "Assets/Scripts/Core/Game.Core.asmdef";
                case "MyPackagesHelper":
                    return "Assets/Packages/MyPackagesHelper.asmdef";
                default:
                    return null;
            }
        }

        [Test]
        public void ExcludePackageScriptAssemblies_PackageAsmdefs_Removed()
        {
            var result = SharpyReferenceProvider.ExcludePackageScriptAssemblies(
                new[]
                {
                    ScriptAssemblies + "UnityEngine.SpatialTracking.dll",
                    ScriptAssemblies + "Game.Core.dll",
                    ScriptAssemblies + "Unity.Multiplayer.Center.Common.dll",
                },
                AsmdefPathFor);

            CollectionAssert.AreEqual(new[] { ScriptAssemblies + "Game.Core.dll" }, result);
        }

        [Test]
        public void ExcludePackageScriptAssemblies_ProjectAndNonAsmdef_Unchanged()
        {
            var paths = new[]
            {
                Managed + "UnityEngine.CoreModule.dll",
                ScriptAssemblies + "Game.Core.dll",
                ScriptAssemblies + "MyPackagesHelper.dll",
                ScriptAssemblies + "Assembly-CSharp-firstpass.dll",
                "/Project/Library/PackageCache/com.unity.nuget.newtonsoft-json/Runtime/AOT/Newtonsoft.Json.dll",
            };

            CollectionAssert.AreEqual(paths, SharpyReferenceProvider.ExcludePackageScriptAssemblies(paths, AsmdefPathFor));
        }

        [Test]
        public void HintFor_TypeLoadFailure_PointsAtReferenceSettings()
        {
            string hint = SharpyReferenceProvider.HintFor(
                "Unexpected error: Unable to load one or more of the requested types.\n"
                + "Could not load file or assembly 'UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.");

            Assert.IsNotNull(hint);
            StringAssert.Contains("Additional References", hint);
            StringAssert.Contains("Reference Denylist", hint);
            StringAssert.Contains("sharpy#2182", hint);
        }

        [Test]
        public void HintFor_AssemblyLoadIce_ReturnsHint()
        {
            Assert.IsNotNull(SharpyReferenceProvider.HintFor(
                "error[SPY0909]: internal compiler error (FileNotFoundException): Could not load file or assembly "
                + "'UnityEngine.UIElementsModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'."));
        }

        [Test]
        public void HintFor_UnrelatedFailures_ReturnNull()
        {
            Assert.IsNull(SharpyReferenceProvider.HintFor(null));
            Assert.IsNull(SharpyReferenceProvider.HintFor(""));
            Assert.IsNull(SharpyReferenceProvider.HintFor(
                "error[SPY0909]: internal compiler error (NullReferenceException): Object reference not set to an instance of an object."));
            Assert.IsNull(SharpyReferenceProvider.HintFor(
                "error[SPY0300]: cannot resolve import 'Core.greeting'\n  --> /p/Assets/x.spy:1:6"));
        }

        [Test]
        public void Filter_UnityBcl_Removed()
        {
            var result = Filter(
                "/Unity/Resources/Scripting/NetStandard/ref/2.1.0/netstandard.dll",
                "/Unity/Resources/Scripting/NetStandard/compat/2.1.0/shims/netfx/System.Core.dll",
                "C:\\Unity\\Editor\\Data\\UnityReferenceAssemblies\\unity-4.8-api\\System.dll",
                "/Unity/Resources/Scripting/MonoBleedingEdge/lib/mono/4.7.1-api/System.Xml.dll",
                "/Elsewhere/mscorlib.dll",
                Managed + "UnityEngine.CoreModule.dll");

            CollectionAssert.AreEqual(new[] { Managed + "UnityEngine.CoreModule.dll" }, result);
        }

        [Test]
        public void Filter_UserDenylist_RemovesWithOrWithoutExtension()
        {
            var result = SharpyReferenceProvider.Filter(
                new[]
                {
                    Managed + "UnityEngine.CoreModule.dll",
                    Managed + "UnityEngine.VideoModule.dll",
                    "/Project/Assets/Plugins/Broken.dll",
                },
                new[] { "UnityEngine.VideoModule", " Broken.dll ", "", null });

            CollectionAssert.AreEqual(new[] { Managed + "UnityEngine.CoreModule.dll" }, result);
        }

        [Test]
        public void Filter_DenylistMatch_IgnoresCase()
        {
            var result = SharpyReferenceProvider.Filter(
                new[]
                {
                    Managed + "unityengine.textcoretextenginemodule.DLL",
                    "/Project/Library/ScriptAssemblies/sharpy.unity.runtime.dll",
                    Managed + "UnityEngine.VideoModule.dll",
                    Managed + "UnityEngine.CoreModule.dll",
                },
                new[] { "UNITYENGINE.VIDEOMODULE" });

            CollectionAssert.AreEqual(new[] { Managed + "UnityEngine.CoreModule.dll" }, result);
        }

        [Test]
        public void Filter_Duplicates_KeepFirstInOrder()
        {
            var result = Filter(
                Managed + "UnityEngine.PhysicsModule.dll",
                Managed + "UnityEngine.CoreModule.dll",
                Managed + "UnityEngine.PhysicsModule.dll",
                "",
                Managed + "UnityEngine.AudioModule.dll");

            CollectionAssert.AreEqual(
                new[]
                {
                    Managed + "UnityEngine.PhysicsModule.dll",
                    Managed + "UnityEngine.CoreModule.dll",
                    Managed + "UnityEngine.AudioModule.dll",
                },
                result);
        }

        private static SharpyReferenceProvider.PluginEntry Plugin(string path, bool forPlayer, bool isNative = false)
        {
            return new SharpyReferenceProvider.PluginEntry(path, isNative, forPlayer);
        }

        [Test]
        public void FallbackReferences_PlayerManagedPlugins_AllKeptAfterEngine()
        {
            var result = SharpyReferenceProvider.FallbackReferences(
                new[] { Managed + "UnityEngine.CoreModule.dll", Managed + "UnityEngine.AudioModule.dll" },
                new[]
                {
                    Plugin("/Project/Assets/Plugins/Game.Data.dll", true),
                    Plugin("/Project/Library/PackageCache/com.unity.nuget.newtonsoft-json/Runtime/AOT/Newtonsoft.Json.dll", true),
                });

            CollectionAssert.AreEqual(
                new[]
                {
                    Managed + "UnityEngine.CoreModule.dll",
                    Managed + "UnityEngine.AudioModule.dll",
                    "/Project/Assets/Plugins/Game.Data.dll",
                    "/Project/Library/PackageCache/com.unity.nuget.newtonsoft-json/Runtime/AOT/Newtonsoft.Json.dll",
                },
                result);
        }

        [Test]
        public void FallbackReferences_EditorOnlyPlugins_Dropped()
        {
            var result = SharpyReferenceProvider.FallbackReferences(
                new[] { Managed + "UnityEngine.CoreModule.dll" },
                new[]
                {
                    Plugin("/Project/Library/PackageCache/com.unity.collab-proxy/Lib/Editor/unityplastic.dll", false),
                    Plugin("/Project/Library/PackageCache/com.unity.collab-proxy/Lib/Editor/log4netPlastic.dll", false),
                    Plugin("/Project/Library/PackageCache/com.unity.analytics/Unity.Analytics.Tracker.dll", false),
                    Plugin("/Project/Assets/Plugins/Game.Data.dll", true),
                });

            CollectionAssert.AreEqual(
                new[] { Managed + "UnityEngine.CoreModule.dll", "/Project/Assets/Plugins/Game.Data.dll" },
                result);
        }

        [Test]
        public void FallbackReferences_NativeAndNonDllPlugins_Dropped()
        {
            var result = SharpyReferenceProvider.FallbackReferences(
                new string[0],
                new[]
                {
                    Plugin("/Project/Assets/Plugins/x86_64/sqlite3.dll", true, isNative: true),
                    Plugin("/Project/Assets/Plugins/macOS/libfoo.bundle", true),
                    Plugin("/Project/Assets/Plugins/Game.Data.DLL", true),
                });

            CollectionAssert.AreEqual(new[] { "/Project/Assets/Plugins/Game.Data.DLL" }, result);
        }

        [Test]
        public void EditorDependentAssemblyPaths_FlagsEditorAssemblyOnly()
        {
            var paths = SharpyReferenceProvider.EditorDependentAssemblyPaths();

            // This test assembly references UnityEditor; the runtime one does not.
            Assert.IsTrue(paths.Contains(Path.GetFullPath(typeof(SharpyReferenceProviderTests).Assembly.Location)));
            Assert.IsFalse(paths.Contains(Path.GetFullPath(typeof(Runtime.SharpyUnityRuntime).Assembly.Location)));
        }

        [Test]
        public void ToAbsolute_ProjectRelativePath_ResolvedAgainstProjectRoot()
        {
            var result = SharpyReferenceProvider.ToAbsolute(new[] { "Library/ScriptAssemblies/Unity.InputSystem.dll" });

            Assert.AreEqual(
                Path.Combine(Directory.GetCurrentDirectory(), "Library", "ScriptAssemblies", "Unity.InputSystem.dll"),
                result[0]);
        }

        [Test]
        public void Collect_IncludesUnityEngineCoreModule()
        {
            var paths = SharpyReferenceProvider.Collect();

            Assert.IsTrue(paths.TrueForAll(Path.IsPathRooted), "every path is absolute");
            Assert.IsTrue(
                paths.Exists(p => Path.GetFileName(p) == "UnityEngine.CoreModule.dll"),
                "UnityEngine.CoreModule is among the references");
        }

        [Test]
        public void GetReferences_AutoOff_ReturnsOnlyAdditionalReferences()
        {
            var references = SharpyReferenceProvider.GetReferences(false, null, new[] { "/abs/Extra.dll", " " });

            CollectionAssert.AreEqual(new[] { "/abs/Extra.dll" }, references);
        }

        [Test]
        public void GetReferences_AutoOn_DerivedThenAdditional()
        {
            var references = SharpyReferenceProvider.GetReferences(
                true, new[] { "UnityEngine.AudioModule" }, new[] { "/abs/Extra.dll" });

            Assert.IsTrue(references.Exists(p => Path.GetFileName(p) == "UnityEngine.CoreModule.dll"));
            Assert.IsFalse(references.Exists(p => Path.GetFileName(p) == "UnityEngine.AudioModule.dll"), "user denylist applied");
            Assert.IsFalse(references.Exists(p => Path.GetFileName(p) == "netstandard.dll"), "built-in filter applied");
            Assert.AreEqual("/abs/Extra.dll", references[references.Count - 1]);
        }
    }
}
