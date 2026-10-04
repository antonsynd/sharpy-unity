namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Everything a compile's output depends on, as one hash: the .spyproj
    /// text (settings, references), the compiler version, the settings the
    /// sync applies, and every .spy path with its content hash. Persisted in
    /// Library/Sharpy/fingerprint after a successful sync, so staleness
    /// survives domain reloads and editor restarts (#12, #10).
    /// </summary>
    internal static class SharpyFingerprint
    {
        // Bump when the plugin starts generating different output from the
        // same inputs (e.g. a new sync rule), so existing projects regenerate.
        private const int FormatVersion = 1;

        /// <param name="sourceHashes">.spy asset path → content hash; order does not matter.</param>
        public static string Compute(
            string projectText, string compilerVersion, string syncSettings, IDictionary<string, string> sourceHashes)
        {
            var paths = new List<string>(sourceHashes.Keys);
            paths.Sort(StringComparer.Ordinal);

            var text = new StringBuilder();
            text.Append("format ").Append(FormatVersion).Append('\n');
            Append(text, "compiler", compilerVersion);
            Append(text, "sync", syncSettings);
            Append(text, "spyproj", projectText);

            foreach (string path in paths)
            {
                Append(text, "source " + path, sourceHashes[path]);
            }

            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        /// <summary>
        /// A .spy's entry in <see cref="Compute"/>'s source hashes: its content
        /// hash and its asset GUID (none yet when it has not been imported).
        /// </summary>
        public static string SourceEntry(string contentHash, string spyGuid)
        {
            return contentHash + " guid=" + (string.IsNullOrEmpty(spyGuid) ? "none" : spyGuid);
        }

        /// <summary>Lowercase hex SHA-256, used for .spy contents and the fingerprint.</summary>
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var hex = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2"));
                }

                return hex.ToString();
            }
        }

        /// <summary>The fingerprint file: the hash, then the generated scripts it produced.</summary>
        public static string Format(string fingerprint, IEnumerable<string> generatedRelativePaths)
        {
            var text = new StringBuilder(fingerprint).Append('\n');

            foreach (string path in generatedRelativePaths)
            {
                text.Append(path).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Whether <paramref name="storedText"/> (a <see cref="Format"/>ted file,
        /// or null) records <paramref name="fingerprint"/> and every script it
        /// lists still exists.
        /// </summary>
        public static bool IsUpToDate(string storedText, string fingerprint, Func<string, bool> generatedExists)
        {
            if (string.IsNullOrEmpty(storedText))
            {
                return false;
            }

            string[] lines = storedText.Split('\n');

            if (lines[0].Trim() != fingerprint)
            {
                return false;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string path = lines[i].Trim();

                if (path.Length > 0 && !generatedExists(path))
                {
                    return false;
                }
            }

            return true;
        }

        // Length-prefixed, so no value can run into the next one.
        private static void Append(StringBuilder text, string name, string value)
        {
            value = value ?? string.Empty;
            text.Append(name).Append(' ').Append(value.Length).Append('\n').Append(value).Append('\n');
        }
    }
}
