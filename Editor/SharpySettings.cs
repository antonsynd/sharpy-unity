using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sharpy.Unity.Editor
{
    [FilePath("ProjectSettings/SharpySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SharpySettings : ScriptableSingleton<SharpySettings>
    {
        [SerializeField] private string generatedOutputPath = "Assets/SharpyGenerated";
        [SerializeField] private int compilerTimeoutSeconds = 30;
        [SerializeField] private bool autoCompileOnSave = true;
        [SerializeField] private string rootNamespace = "";
        [SerializeField] private bool showLineDirectives;
        [SerializeField] private List<string> additionalModulePaths = new List<string>();
        [SerializeField] private List<string> additionalReferences = new List<string>();

        public string GeneratedOutputPath => generatedOutputPath;
        public int CompilerTimeoutSeconds => compilerTimeoutSeconds;
        public bool AutoCompileOnSave => autoCompileOnSave;
        public string RootNamespace => rootNamespace;
        public bool ShowLineDirectives => showLineDirectives;
        public List<string> AdditionalModulePaths => additionalModulePaths;
        public List<string> AdditionalReferences => additionalReferences;

        public void Save()
        {
            Save(true);
        }
    }
}
