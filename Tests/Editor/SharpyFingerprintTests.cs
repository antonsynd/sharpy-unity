namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyFingerprintTests
    {
        private const string Compiler = "sharpyc 0.21.0+4a6228e87";
        private const string Sync = "Assets/SharpyGenerated|sourceMappedErrors=True";

        private static string Project(params string[] references)
        {
            return SharpyProjectFile.Build(
                "", new[] { "../../Assets/**/*.spy" }, new string[0], references, "../../Assets");
        }

        private static Dictionary<string, string> Sources()
        {
            return new Dictionary<string, string>
            {
                ["Assets/Scripts/Core/greeting.spy"] = "aa11",
                ["Assets/Scripts/Smoke/smoke_behaviour.spy"] = "bb22",
            };
        }

        private static string Baseline()
        {
            return SharpyFingerprint.Compute(Project("/u/UnityEngine.CoreModule.dll"), Compiler, Sync, Sources());
        }

        [Test]
        public void Compute_SameInputs_SameFingerprint()
        {
            Assert.AreEqual(Baseline(), Baseline());
        }

        [Test]
        public void Compute_SourcesInAnotherOrder_SameFingerprint()
        {
            var reordered = new Dictionary<string, string>
            {
                ["Assets/Scripts/Smoke/smoke_behaviour.spy"] = "bb22",
                ["Assets/Scripts/Core/greeting.spy"] = "aa11",
            };

            Assert.AreEqual(
                Baseline(),
                SharpyFingerprint.Compute(Project("/u/UnityEngine.CoreModule.dll"), Compiler, Sync, reordered));
        }

        [Test]
        public void Compute_ReferenceAdded_Changes()
        {
            string withExtra = SharpyFingerprint.Compute(
                Project("/u/UnityEngine.CoreModule.dll", "/disk/Extra.dll"), Compiler, Sync, Sources());

            Assert.AreNotEqual(Baseline(), withExtra);
        }

        [Test]
        public void Compute_SpyContentChanged_Changes()
        {
            var sources = Sources();
            sources["Assets/Scripts/Core/greeting.spy"] = "aa12";

            Assert.AreNotEqual(
                Baseline(),
                SharpyFingerprint.Compute(Project("/u/UnityEngine.CoreModule.dll"), Compiler, Sync, sources));
        }

        [Test]
        public void Compute_SpyMoved_Changes()
        {
            var sources = Sources();
            sources.Remove("Assets/Scripts/Smoke/smoke_behaviour.spy");
            sources["Assets/Scripts/Game/smoke_behaviour.spy"] = "bb22";

            Assert.AreNotEqual(
                Baseline(),
                SharpyFingerprint.Compute(Project("/u/UnityEngine.CoreModule.dll"), Compiler, Sync, sources));
        }

        [Test]
        public void Compute_CompilerVersionChanged_Changes()
        {
            Assert.AreNotEqual(
                Baseline(),
                SharpyFingerprint.Compute(
                    Project("/u/UnityEngine.CoreModule.dll"), "sharpyc 0.22.0+1234567", Sync, Sources()));
        }

        [Test]
        public void Compute_SyncSettingsChanged_Changes()
        {
            Assert.AreNotEqual(
                Baseline(),
                SharpyFingerprint.Compute(
                    Project("/u/UnityEngine.CoreModule.dll"), Compiler,
                    "Assets/SharpyGenerated|sourceMappedErrors=False", Sources()));
        }

        [Test]
        public void Compute_ValuesCannotRunTogether()
        {
            var a = new Dictionary<string, string> { ["Assets/a.spy"] = "1" };
            var b = new Dictionary<string, string> { ["Assets/a.spy"] = "" };

            Assert.AreNotEqual(
                SharpyFingerprint.Compute("p", "c1", "s", b),
                SharpyFingerprint.Compute("p", "c", "1s", b));
            Assert.AreNotEqual(
                SharpyFingerprint.Compute("p", "c", "s", a),
                SharpyFingerprint.Compute("p", "c", "s", b));
        }

        [Test]
        public void IsUpToDate_SameFingerprintAndOutputsPresent_True()
        {
            string stored = SharpyFingerprint.Format("abc", new[] { "Scripts/Core/greeting.cs" });

            Assert.IsTrue(SharpyFingerprint.IsUpToDate(stored, "abc", path => path == "Scripts/Core/greeting.cs"));
        }

        [Test]
        public void IsUpToDate_OtherFingerprint_False()
        {
            string stored = SharpyFingerprint.Format("abc", new[] { "Scripts/Core/greeting.cs" });

            Assert.IsFalse(SharpyFingerprint.IsUpToDate(stored, "abd", path => true));
        }

        [Test]
        public void IsUpToDate_GeneratedScriptMissing_False()
        {
            string stored = SharpyFingerprint.Format("abc", new[] { "Scripts/Core/greeting.cs", "Scripts/Smoke/SmokeBehaviour.cs" });

            Assert.IsFalse(SharpyFingerprint.IsUpToDate(stored, "abc", path => path == "Scripts/Core/greeting.cs"));
        }

        [Test]
        public void IsUpToDate_NothingStored_False()
        {
            Assert.IsFalse(SharpyFingerprint.IsUpToDate(null, "abc", path => true));
            Assert.IsFalse(SharpyFingerprint.IsUpToDate("", "abc", path => true));
        }

        [Test]
        public void IsUpToDate_NoSources_TrueWithoutOutputs()
        {
            Assert.IsTrue(SharpyFingerprint.IsUpToDate(
                SharpyFingerprint.Format("abc", new string[0]), "abc", path => false));
        }

        [Test]
        public void SourceEntry_GuidArrivingLater_ChangesTheFingerprint()
        {
            var before = new Dictionary<string, string>
            {
                ["Assets/new.spy"] = SharpyFingerprint.SourceEntry("aa11", null),
            };
            var after = new Dictionary<string, string>
            {
                ["Assets/new.spy"] = SharpyFingerprint.SourceEntry("aa11", "9f2c4e1a7b3d4c5e8f60718293a4b5c6"),
            };

            Assert.AreNotEqual(
                SharpyFingerprint.Compute("p", Compiler, Sync, before),
                SharpyFingerprint.Compute("p", Compiler, Sync, after));
        }

        [Test]
        public void SourceEntry_SameContentAndGuid_Same()
        {
            Assert.AreEqual(
                SharpyFingerprint.SourceEntry("aa11", "9f2c4e1a7b3d4c5e8f60718293a4b5c6"),
                SharpyFingerprint.SourceEntry("aa11", "9f2c4e1a7b3d4c5e8f60718293a4b5c6"));
            Assert.AreEqual(SharpyFingerprint.SourceEntry("aa11", null), SharpyFingerprint.SourceEntry("aa11", ""));
        }
    }
}
