namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Runtime.InteropServices;
    using UnityEngine;

    /// <summary>
    /// Single source of truth for the pinned sharpy toolchain version. The
    /// compiler download and the bundled Sharpy.Core DLLs must move together;
    /// both key off this constant.
    /// </summary>
    public static class SharpyToolchain
    {
        // Bumped by: python -m build_tools update-toolchain <version>
        public const string Version = "0.16.1";

        public static string ReleaseTag => "v" + Version;

        public static string ReleaseUrlBase =>
            "https://github.com/antonsynd/sharpy/releases/download/" + ReleaseTag + "/";

        public static string GetPlatformRid()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return SystemInfo.processorType.Contains("Apple")
                    ? "osx-arm64"
                    : "osx-x64";
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "win-x64";
            }

            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
        }
    }
}
