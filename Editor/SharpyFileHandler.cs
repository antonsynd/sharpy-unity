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
        // Unity 6000.3 obsoletes the int instance-ID overloads (CS0618) in
        // favour of EntityId; earlier editors have no EntityId overloads.
#if UNITY_6000_3_OR_NEWER
        [OnOpenAsset]
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            return OpenSpyAsset(AssetDatabase.GetAssetPath(entityId), line);
        }
#else
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            return OpenSpyAsset(AssetDatabase.GetAssetPath(instanceID), line);
        }
#endif

        private static bool OpenSpyAsset(string path, int line)
        {
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
