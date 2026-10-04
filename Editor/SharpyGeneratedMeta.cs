namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Deterministic .meta files for generated scripts, so serialized references
    /// to Sharpy components survive a clean regenerate or a fresh clone. The
    /// GUID depends only on the owning .spy asset's GUID — never on the
    /// generated path or namespace.
    /// </summary>
    internal static class SharpyGeneratedMeta
    {
        // Changing this changes every generated script GUID and breaks every
        // scene and prefab reference to a Sharpy component.
        private const string Salt = "com.antonsynd.sharpy:generated:";

        private static readonly Regex GuidLine = new Regex(
            @"^guid:[ \t]*([0-9A-Fa-f]{32})[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// Lowercase hex MD5 of the salt plus <paramref name="spyGuid"/> (32 chars).
        /// </summary>
        public static string GuidFor(string spyGuid)
        {
            if (string.IsNullOrWhiteSpace(spyGuid))
            {
                throw new ArgumentException("A .spy asset GUID is required.", nameof(spyGuid));
            }

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Salt + spyGuid));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2"));
                }

                return hex.ToString();
            }
        }

        /// <summary>
        /// The MonoImporter .meta Unity writes for a .cs script, with the given GUID.
        /// </summary>
        public static string MetaText(string guid)
        {
            return "fileFormatVersion: 2\n"
                + "guid: " + guid + "\n"
                + "MonoImporter:\n"
                + "  externalObjects: {}\n"
                + "  serializedVersion: 2\n"
                + "  defaultReferences: []\n"
                + "  executionOrder: 0\n"
                + "  icon: {instanceID: 0}\n"
                + "  userData: \n"
                + "  assetBundleName: \n"
                + "  assetBundleVariant: \n";
        }

        /// <summary>
        /// Reads the <c>guid:</c> line of a .meta file (lowercased), for when the
        /// AssetDatabase cannot answer yet (batch mode before import).
        /// </summary>
        public static bool TryReadGuid(string metaText, out string guid)
        {
            guid = null;
            if (string.IsNullOrEmpty(metaText))
            {
                return false;
            }

            Match match = GuidLine.Match(metaText);
            if (!match.Success)
            {
                return false;
            }

            guid = match.Groups[1].Value.ToLowerInvariant();
            return true;
        }
    }
}
