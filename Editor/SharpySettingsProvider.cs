using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
    public sealed class SharpySettingsProvider : SettingsProvider
    {
        private SerializedObject serializedSettings;

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

            string compilerPath = SharpyCompilerBridge.GetCompilerPath();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Compiler Path", compilerPath);
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
