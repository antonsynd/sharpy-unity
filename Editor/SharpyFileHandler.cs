using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
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
                Unity.CodeEditor.CodeEditor.CurrentEditor.OpenProject(fullPath, line);
            }
            else
            {
                Unity.CodeEditor.CodeEditor.CurrentEditor.OpenProject(fullPath);
            }

            return true;
        }
    }
}
