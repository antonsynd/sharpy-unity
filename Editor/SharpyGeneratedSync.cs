namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>One generated script the sync should leave in the generated folder.</summary>
    internal sealed class SharpyGeneratedFile
    {
        /// <summary>Path under the generated folder, '/' separators.</summary>
        public string RelativePath;

        public string Content;

        /// <summary>GUID for the script's .meta; null leaves the .meta to Unity.</summary>
        public string MetaGuid;
    }

    internal sealed class SharpySyncResult
    {
        /// <summary>Full paths written, in write order (.meta files included).</summary>
        public readonly List<string> Written = new List<string>();

        /// <summary>Full paths of files and folders deleted.</summary>
        public readonly List<string> Deleted = new List<string>();

        public bool Changed => Written.Count > 0 || Deleted.Count > 0;
    }

    /// <summary>
    /// Makes the generated folder hold exactly the given scripts. File system
    /// only (no AssetDatabase), so the caller refreshes once afterwards and
    /// tests can run it against a temp directory.
    /// </summary>
    internal static class SharpyGeneratedSync
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Writes each file whose content differs (its .meta first, when the
        /// GUID is missing or different), deletes generated .cs files and their
        /// .meta files that are not in <paramref name="files"/>, and prunes
        /// folders left empty. Touches only .cs and .cs.meta files and folders
        /// under <paramref name="generatedRoot"/>; the root itself and its
        /// other files (the .gitignore) are kept.
        /// </summary>
        public static SharpySyncResult Sync(string generatedRoot, IEnumerable<SharpyGeneratedFile> files)
        {
            var result = new SharpySyncResult();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(generatedRoot);

            foreach (SharpyGeneratedFile file in files)
            {
                string csPath = Path.Combine(generatedRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                string metaPath = csPath + ".meta";
                keep.Add(Path.GetFullPath(csPath));
                keep.Add(Path.GetFullPath(metaPath));

                Directory.CreateDirectory(Path.GetDirectoryName(csPath));

                // The .meta goes first: once Unity has imported the .cs without
                // it, the script already has a random GUID.
                if (file.MetaGuid != null && !MetaHasGuid(metaPath, file.MetaGuid))
                {
                    File.WriteAllText(metaPath, SharpyGeneratedMeta.MetaText(file.MetaGuid), Utf8NoBom);
                    result.Written.Add(metaPath);
                }

                if (WriteIfChanged(csPath, file.Content))
                {
                    result.Written.Add(csPath);
                }
            }

            DeleteStale(generatedRoot, keep, result);
            PruneEmptyFolders(generatedRoot, result);

            return result;
        }

        private static bool MetaHasGuid(string metaPath, string guid)
        {
            return File.Exists(metaPath)
                && SharpyGeneratedMeta.TryReadGuid(File.ReadAllText(metaPath), out string existing)
                && string.Equals(existing, guid, StringComparison.OrdinalIgnoreCase);
        }

        // An unchanged file keeps its write time, so Unity does not reimport
        // (and recompile) it.
        private static bool WriteIfChanged(string path, string content)
        {
            byte[] bytes = Utf8NoBom.GetBytes(content ?? string.Empty);

            if (File.Exists(path) && BytesEqual(File.ReadAllBytes(path), bytes))
            {
                return false;
            }

            File.WriteAllBytes(path, bytes);
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void DeleteStale(string generatedRoot, HashSet<string> keep, SharpySyncResult result)
        {
            foreach (string path in Directory.GetFiles(generatedRoot, "*", SearchOption.AllDirectories))
            {
                bool generated = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cs.meta", StringComparison.OrdinalIgnoreCase);

                if (generated && !keep.Contains(Path.GetFullPath(path)))
                {
                    File.Delete(path);
                    result.Deleted.Add(path);
                }
            }
        }

        // Deepest first, so a folder whose only content was an empty folder
        // (and that folder's .meta) goes too. The root stays.
        private static void PruneEmptyFolders(string generatedRoot, SharpySyncResult result)
        {
            var folders = new List<string>(Directory.GetDirectories(generatedRoot, "*", SearchOption.AllDirectories));
            folders.Sort((a, b) => b.Length.CompareTo(a.Length));

            foreach (string folder in folders)
            {
                if (Directory.GetFileSystemEntries(folder).Length > 0)
                {
                    continue;
                }

                Directory.Delete(folder);
                result.Deleted.Add(folder);

                string folderMeta = folder + ".meta";

                if (File.Exists(folderMeta))
                {
                    File.Delete(folderMeta);
                    result.Deleted.Add(folderMeta);
                }
            }
        }
    }
}
