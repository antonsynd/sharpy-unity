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
        private int derivedReferenceCount = -1;
        private bool showReferenceDenylist;

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
            derivedReferenceCount = -1;
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

            var rootNamespaceProperty = serializedSettings.FindProperty("rootNamespace");

            EditorGUILayout.PropertyField(rootNamespaceProperty, new GUIContent("Root Namespace"));

            if (SharpySettings.NamespaceCollidesWithSharpy(rootNamespaceProperty.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "A \"Sharpy\" segment in the root namespace shadows the Sharpy.* runtime types that generated code references. Choose a different namespace.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("additionalModulePaths"),
                new GUIContent("Additional Module Paths"));

            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("additionalReferences"),
                new GUIContent("Additional References"));

            EditorGUI.BeginChangeCheck();

            var autoReferencesProperty = serializedSettings.FindProperty("autoUnityReferences");

            EditorGUILayout.PropertyField(
                autoReferencesProperty,
                new GUIContent(
                    "Derive Unity References",
                    "Compile against the assemblies Unity gives Assembly-CSharp (engine modules, packages, plugins), minus those sharpyc cannot load."));

            if (autoReferencesProperty.boolValue)
            {
                // Deriving walks the compilation pipeline; do it once, not per repaint.
                if (derivedReferenceCount < 0)
                {
                    derivedReferenceCount = SharpyReferenceProvider.DeriveUnityReferences(SharpySettings.instance.ReferenceDenylist).Count;
                }

                EditorGUILayout.LabelField(" ", $"{derivedReferenceCount} references derived");

                EditorGUI.indentLevel++;
                showReferenceDenylist = EditorGUILayout.Foldout(
                    showReferenceDenylist,
                    new GUIContent(
                        "Reference Denylist",
                        "Assembly names (with or without .dll) to leave out of the derived references, e.g. one that crashes sharpyc."),
                    true);

                if (showReferenceDenylist)
                {
                    EditorGUILayout.PropertyField(
                        serializedSettings.FindProperty("referenceDenylist"),
                        new GUIContent("Names"));
                }

                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                derivedReferenceCount = -1;
            }

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
