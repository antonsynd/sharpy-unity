namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using UnityEditor;

    /// <summary>
    /// Picks up SharpySettings.asset edits made outside the editor when the
    /// editor regains focus (#12).
    /// </summary>
    [InitializeOnLoad]
    internal static class SharpySettingsReloader
    {
        static SharpySettingsReloader()
        {
            EditorApplication.focusChanged += OnFocusChanged;
        }

        private static void OnFocusChanged(bool focused)
        {
            if (focused)
            {
                SharpySettings.RefreshFromDiskIfChanged();
            }
        }
    }
}
