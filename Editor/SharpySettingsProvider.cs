namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public sealed class SharpySettingsProvider : SettingsProvider
    {
        private SerializedObject serializedSettings;
        private string cachedVersion;
        private string cachedVersionPath;

        public SharpySettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SharpySettingsProvider("Project/Sharpy", SettingsScope.Project)
            {
                keywords = new HashSet<string>(new[]
                {
                    "sharpy", "spy", "compiler", "transpiler", "namespace"
                })
            };
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            serializedSettings = new SerializedObject(SharpySettings.instance);
        }

        public override void OnGUI(string searchContext)
        {
            if (serializedSettings == null)
            {
                return;
            }

            serializedSettings.Update();

            EditorGUILayout.LabelField("Compiler", EditorStyles.boldLabel);

            var customPathProperty = serializedSettings.FindProperty("customCompilerPath");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(
                customPathProperty,
                new GUIContent(
                    "Custom Compiler Path",
                    "Absolute path to a sharpyc binary. When set, it wins over the managed install and the download prompt is suppressed."));

            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string picked = EditorUtility.OpenFilePanel("Select sharpyc binary", "", "");

                if (!string.IsNullOrEmpty(picked))
                {
                    customPathProperty.stringValue = picked;
                }
            }

            EditorGUILayout.EndHorizontal();

            string compilerPath = SharpyCompilerBridge.ResolveCompilerPath(customPathProperty.stringValue);
            bool compilerExists = System.IO.File.Exists(compilerPath);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Resolved Path", compilerPath);

            if (compilerExists)
            {
                if (cachedVersion == null || cachedVersionPath != compilerPath)
                {
                    cachedVersion = SharpyCompilerBridge.GetCompilerVersion();
                    cachedVersionPath = compilerPath;
                }

                EditorGUILayout.TextField("Version", cachedVersion);
            }
            else
            {
                EditorGUILayout.TextField("Version", "not found");
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("compilerTimeoutSeconds"),
                new GUIContent("Timeout (seconds)"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compilation", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("autoCompileOnSave"),
                new GUIContent("Auto-compile on Save"));

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("generatedOutputPath"),
                new GUIContent("Generated Output Path"));

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("rootNamespace"),
                new GUIContent("Root Namespace"));

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("showLineDirectives"),
                new GUIContent("Show #line Directives"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("additionalModulePaths"),
                new GUIContent("Additional Module Paths"));

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("additionalReferences"),
                new GUIContent("Additional References"));

            EditorGUILayout.Space();

            if (GUILayout.Button("Recompile All .spy Files"))
            {
                SharpyMenuItems.RecompileAll();
            }

            if (serializedSettings.ApplyModifiedProperties())
            {
                SharpySettings.instance.Save();
            }
        }
    }
}
