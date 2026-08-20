namespace Sharpy.Unity.Editor
{
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
    }
}
