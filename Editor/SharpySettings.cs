namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    [FilePath("ProjectSettings/SharpySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SharpySettings : ScriptableSingleton<SharpySettings>
    {
        [SerializeField] private string generatedOutputPath = "Assets/SharpyGenerated";
        [SerializeField] private int compilerTimeoutSeconds = 30;
        [SerializeField] private bool autoCompileOnSave = true;
        [SerializeField] private string rootNamespace = "";
        [SerializeField] private string customCompilerPath = "";
        [SerializeField] private List<string> additionalModulePaths = new List<string>();
        [SerializeField] private List<string> additionalReferences = new List<string>();

        public string GeneratedOutputPath => generatedOutputPath;
        public int CompilerTimeoutSeconds => compilerTimeoutSeconds;
        public bool AutoCompileOnSave => autoCompileOnSave;
        public string RootNamespace => rootNamespace;
        public string CustomCompilerPath => customCompilerPath;
        public List<string> AdditionalModulePaths => additionalModulePaths;
        public List<string> AdditionalReferences => additionalReferences;

        public void Save()
        {
            Save(true);
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
