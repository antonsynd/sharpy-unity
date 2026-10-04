namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Headless entry points, for CI:
    /// <c>Unity -batchmode -quit -executeMethod Sharpy.Unity.Editor.SharpyBatch.GenerateAll</c>
    /// </summary>
    public static class SharpyBatch
    {
        /// <summary>
        /// Installs the compiler if needed, then compiles every .spy and syncs
        /// the generated folder regardless of the up-to-date check. In batch
        /// mode a failure exits the editor with code 1, so CI stops before it
        /// builds stale or missing generated C#.
        /// </summary>
        public static void GenerateAll()
        {
            if (string.IsNullOrWhiteSpace(SharpySettings.instance.CustomCompilerPath)
                && !SharpyBinaryDownloader.IsCompilerInstalled())
            {
                SharpyBinaryDownloader.InstallBlocking();
            }

            bool compiled = SharpyProjectCompiler.Compile(force: true);

            if (compiled)
            {
                Debug.Log("[Sharpy] GenerateAll: generated C# is up to date.");
                return;
            }

            Debug.LogError("[Sharpy] GenerateAll failed; the generated C# was left as it was. See the errors above.");

            if (ShouldExitWithError(Application.isBatchMode, compiled))
            {
                EditorApplication.Exit(1);
            }
        }

        // Interactively (a menu or a script calling this) the editor stays up;
        // the error log is enough.
        internal static bool ShouldExitWithError(bool isBatchMode, bool compiled)
        {
            return isBatchMode && !compiled;
        }
    }
}
