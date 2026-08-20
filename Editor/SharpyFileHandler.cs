namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.IO;
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEngine;

    public static class SharpyFileHandler
    {
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            string path = AssetDatabase.GetAssetPath(instanceID);

            if (Path.GetExtension(path) != ".spy")
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);

            if (line > 0)
            {
                global::Unity.CodeEditor.CodeEditor.CurrentEditor.OpenProject(fullPath, line);
            }
            else
            {
                global::Unity.CodeEditor.CodeEditor.CurrentEditor.OpenProject(fullPath);
            }

            return true;
        }
    }
}
