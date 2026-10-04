namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;

    [FilePath(AssetPath, FilePathAttribute.Location.ProjectFolder)]
    public sealed class SharpySettings : ScriptableSingleton<SharpySettings>
    {
        internal const string AssetPath = "ProjectSettings/SharpySettings.asset";

        // SessionState survives domain reloads, which is exactly the lifetime
        // of the stale singleton (#12).
        private const string WriteTimeKeyPrefix = "Sharpy.SettingsWriteTime:";

        [SerializeField] private string generatedOutputPath = "Assets/SharpyGenerated";
        internal const int DefaultCompilerTimeoutSeconds = 30;

        [SerializeField] private int compilerTimeoutSeconds = DefaultCompilerTimeoutSeconds;
        [SerializeField] private bool autoCompileOnSave = true;
        [SerializeField] private string rootNamespace = "";
        [SerializeField] private string customCompilerPath = "";
        [SerializeField] private List<string> additionalModulePaths = new List<string>();
        [SerializeField] private List<string> additionalReferences = new List<string>();
        [SerializeField] private bool autoUnityReferences = true;
        [SerializeField] private List<string> referenceDenylist = new List<string>();
        [SerializeField] private bool sourceMappedErrors = true;

        public string GeneratedOutputPath => generatedOutputPath;
        public int CompilerTimeoutSeconds => EffectiveTimeout(compilerTimeoutSeconds);
        public bool AutoCompileOnSave => autoCompileOnSave;
        public string RootNamespace => rootNamespace;
        public string CustomCompilerPath => customCompilerPath;
        public List<string> AdditionalModulePaths => additionalModulePaths;
        public List<string> AdditionalReferences => additionalReferences;
        public bool AutoUnityReferences => autoUnityReferences;
        public List<string> ReferenceDenylist => referenceDenylist;
        public bool SourceMappedErrors => sourceMappedErrors;

        public void Save()
        {
            Save(AssetPath);
        }

        // Same as ScriptableSingleton.Save(true), to any path, and remembers
        // the write time so the plugin's own save is not reloaded as an
        // outside edit.
        internal void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { this }, path, true);
            SessionState.SetString(WriteTimeKey(path), WriteTimeOf(path));
        }

        /// <summary>
        /// Re-reads ProjectSettings/SharpySettings.asset into the live instance
        /// when the file changed since this editor session last read or wrote
        /// it. ScriptableSingleton loads the file once and keeps the instance
        /// across domain reloads, so edits made outside the editor (a text
        /// editor, git pull) are otherwise ignored until a restart (#12).
        /// Returns whether settings were reloaded.
        /// </summary>
        public static bool RefreshFromDiskIfChanged()
        {
            return RefreshFromDiskIfChanged(AssetPath);
        }

        internal static bool RefreshFromDiskIfChanged(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string key = WriteTimeKey(path);
            string writeTime = WriteTimeOf(path);

            // "Differs", not "newer": a checkout can move the time backwards.
            if (SessionState.GetString(key, "") == writeTime)
            {
                return false;
            }

            // Remembered before loading, so a file that fails to load is not
            // retried on every focus change.
            SessionState.SetString(key, writeTime);

            return LoadInto(instance, path);
        }

        private static bool LoadInto(SharpySettings target, string path)
        {
            // Deserializing constructs a second SharpySettings, and the
            // ScriptableSingleton constructor logs "ScriptableSingleton already
            // exists" unless its slot is empty. Unity's own CreateAndLoad loads
            // with the slot empty, so empty it for the load and put the live
            // instance back. The field is s_Instance in 2022.3 and 6000.3.
            FieldInfo slot = typeof(ScriptableSingleton<SharpySettings>)
                .GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static);

            if (slot == null)
            {
                Debug.LogWarning("[Sharpy] Cannot reload SharpySettings.asset in this Unity version; restart the editor to pick up outside edits.");
                return false;
            }

            UnityEngine.Object[] loaded;
            slot.SetValue(null, null);

            try
            {
                loaded = InternalEditorUtility.LoadSerializedFileAndForget(path);
            }
            finally
            {
                slot.SetValue(null, target);
            }

            bool copied = false;

            foreach (UnityEngine.Object obj in loaded)
            {
                if (!copied && obj is SharpySettings fromDisk)
                {
                    EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(fromDisk), target);
                    copied = true;
                }

                DestroyImmediate(obj);
            }

            return copied;
        }

        private static string WriteTimeKey(string path)
        {
            return WriteTimeKeyPrefix + Path.GetFullPath(path);
        }

        private static string WriteTimeOf(string path)
        {
            return File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
        }

        // 0 or less (a cleared field, a hand-edited asset) means the default,
        // not an instant timeout; the cap keeps seconds * 1000 inside an int.
        internal static int EffectiveTimeout(int configured)
        {
            return configured <= 0 ? DefaultCompilerTimeoutSeconds : Math.Min(configured, int.MaxValue / 1000);
        }

        // A `Sharpy` segment in the root namespace shadows the Sharpy.* root
        // namespace inside generated code, breaking its Sharpy.Core
        // references (this bit the plugin's own source once).
        internal static bool NamespaceCollidesWithSharpy(string ns)
        {
            if (string.IsNullOrEmpty(ns))
            {
                return false;
            }

            foreach (string segment in ns.Split('.'))
            {
                if (segment == "Sharpy")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
