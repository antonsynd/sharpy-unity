using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
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

            string generatedPath = SharpyGeneratedFolderManager.GetGeneratedPath(path);
            bool hasGenerated = File.Exists(generatedPath);

            EditorGUILayout.LabelField("Generated C#", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", hasGenerated ? "Compiled" : "Not compiled");

            if (hasGenerated)
            {
                EditorGUILayout.LabelField("Output", generatedPath);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Recompile"))
            {
                string outputDir = Path.GetDirectoryName(generatedPath);

                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var result = SharpyCompilerBridge.CompileFile(path, generatedPath);

                if (result.Success)
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[Sharpy] Recompiled {path}");
                }
                else
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        switch (diagnostic.ToUnityLogType())
                        {
                            case LogType.Error:
                                Debug.LogError($"[Sharpy] {diagnostic.Code}: {diagnostic.Message}");
                                break;
                            case LogType.Warning:
                                Debug.LogWarning($"[Sharpy] {diagnostic.Code}: {diagnostic.Message}");
                                break;
                            default:
                                Debug.Log($"[Sharpy] {diagnostic.Code}: {diagnostic.Message}");
                                break;
                        }
                    }

                    if (result.Diagnostics.Count == 0 && !string.IsNullOrEmpty(result.Stderr))
                    {
                        Debug.LogError($"[Sharpy] Compilation failed: {result.Stderr}");
                    }
                }
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
