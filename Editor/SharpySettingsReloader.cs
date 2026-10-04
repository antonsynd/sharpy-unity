namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Picks up edits made outside the editor (#12): when the editor loads or
    /// regains focus, re-reads SharpySettings.asset and recompiles if the
    /// fingerprint of sources, settings and compiler changed.
    /// </summary>
    [InitializeOnLoad]
    internal static class SharpySettingsReloader
    {
        static SharpySettingsReloader()
        {
            EditorApplication.focusChanged += OnFocusChanged;

            // Batch runs compile through imports or SharpyBatch.GenerateAll.
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += SharpyProjectCompiler.CompileIfStale;
            }
        }

        private static void OnFocusChanged(bool focused)
        {
            if (focused)
            {
                SharpyProjectCompiler.CompileIfStale();
            }
        }
    }
}
