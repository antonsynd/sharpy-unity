namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;

    public class SharpySettingsReloadTests
    {
        private const string EditedReference = "/disk/Edited.dll";

        private string snapshot;
        private string tempDirectory;
        private string assetPath;

        [SetUp]
        public void SetUp()
        {
            snapshot = EditorJsonUtility.ToJson(SharpySettings.instance);
            tempDirectory = Path.Combine(Path.GetTempPath(), "sharpy-settings-" + Guid.NewGuid().ToString("N"));
            assetPath = Path.Combine(tempDirectory, "SharpySettings.asset");
        }

        [TearDown]
        public void TearDown()
        {
            // Every test loads into the live singleton; put it back.
            EditorJsonUtility.FromJsonOverwrite(snapshot, SharpySettings.instance);
            SessionState.EraseString("Sharpy.SettingsWriteTime:" + Path.GetFullPath(assetPath));

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        // An outside edit: a reference added to the saved YAML, written later.
        private void EditOnDisk()
        {
            string text = File.ReadAllText(assetPath);
            Assert.IsTrue(text.Contains("additionalReferences: []"), "fixture: saved asset has no references");
            File.WriteAllText(assetPath, text.Replace("additionalReferences: []", "additionalReferences:\n  - " + EditedReference));
            BumpWriteTime();
        }

        // File-system timestamps can be coarse; make the change unmistakable.
        private void BumpWriteTime()
        {
            File.SetLastWriteTimeUtc(assetPath, File.GetLastWriteTimeUtc(assetPath).AddSeconds(10));
        }

        [Test]
        public void Refresh_ModifiedOnDisk_LoadsNewListIntoLiveInstance()
        {
            var live = SharpySettings.instance;
            live.Save(assetPath);
            EditOnDisk();

            Assert.IsTrue(SharpySettings.RefreshFromDiskIfChanged(assetPath));

            Assert.AreSame(live, SharpySettings.instance, "the singleton instance is kept");
            CollectionAssert.AreEqual(new[] { EditedReference }, SharpySettings.instance.AdditionalReferences);
        }

        [Test]
        public void Refresh_AfterOwnSave_DoesNotReload()
        {
            SharpySettings.instance.Save(assetPath);

            Assert.IsFalse(SharpySettings.RefreshFromDiskIfChanged(assetPath));

            // Positive control: the same file, written by someone else, reloads.
            BumpWriteTime();
            Assert.IsTrue(SharpySettings.RefreshFromDiskIfChanged(assetPath));
        }

        [Test]
        public void Refresh_Unchanged_ReloadsOnlyOnce()
        {
            SharpySettings.instance.Save(assetPath);
            EditOnDisk();

            Assert.IsTrue(SharpySettings.RefreshFromDiskIfChanged(assetPath), "first sight of the edit reloads");
            Assert.IsFalse(SharpySettings.RefreshFromDiskIfChanged(assetPath), "unchanged since then");
        }

        [Test]
        public void Refresh_MissingFile_ReturnsFalse()
        {
            Assert.IsFalse(SharpySettings.RefreshFromDiskIfChanged(assetPath));
        }
    }
}
