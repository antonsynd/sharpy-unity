namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(DefaultAsset))]
    public sealed class SharpyFileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            string path = AssetDatabase.GetAssetPath(target);

            if (Path.GetExtension(path) != ".spy")
            {
                base.OnInspectorGUI();
                return;
            }

            GUI.enabled = true;

            EditorGUILayout.LabelField("Sharpy Source File", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Path", path);

            string fullPath = Path.GetFullPath(path);

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                EditorGUILayout.LabelField("Size", EditorUtility.FormatBytes(fileInfo.Length));
                EditorGUILayout.LabelField("Modified", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            EditorGUILayout.Space();

            string generatedPath = SharpyGeneratedFolderManager.FindGeneratedPath(path);
            bool hasGenerated = File.Exists(generatedPath);

            EditorGUILayout.LabelField("Generated C#", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", hasGenerated ? "Compiled" : "Not compiled");

            if (hasGenerated)
            {
                EditorGUILayout.LabelField("Output", generatedPath);
            }

            EditorGUILayout.Space();

            // A .spy compiles only as part of the whole project.
            if (GUILayout.Button("Recompile All"))
            {
                SharpyMenuItems.RecompileAll();
            }

            if (hasGenerated && GUILayout.Button("View Generated C#"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(generatedPath);

                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset);
                }
            }
        }
    }
}
