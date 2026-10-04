namespace Sharpy.Unity.Editor
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;

    /// <summary>
    /// Builds the text of the .spyproj that describes the Unity project's
    /// Sharpy sources for <c>sharpyc project</c>. Pure: no Unity API, and the
    /// same inputs always give byte-identical text, so the caller can skip the
    /// write when nothing changed.
    /// </summary>
    internal static class SharpyProjectFile
    {
        // sharpyc rejects a project without a RootNamespace. Must not contain a
        // "Sharpy" segment (see SharpySettings.NamespaceCollidesWithSharpy).
        public const string DefaultRootNamespace = "SharpyScripts";

        public const string AssemblyName = "SharpyGenerated";

        // sharpyc defaults to net10.0 when TargetFramework is omitted.
        public const string TargetFramework = "netstandard2.1";

        /// <param name="rootNamespace">Blank → <see cref="DefaultRootNamespace"/>.</param>
        /// <param name="sourceGlobs">One <c>SourceFile</c> per non-blank entry.</param>
        /// <param name="modulePaths">One <c>ModulePath</c> per non-blank entry.</param>
        /// <param name="references">One <c>Reference</c> per non-blank entry.</param>
        /// <param name="sourceRoot">
        /// Module root relative to the spyproj's directory (e.g. <c>../../Assets</c>);
        /// written as <c>SourceRoot</c> when non-blank.
        /// </param>
        public static string Build(
            string rootNamespace,
            IEnumerable<string> sourceGlobs,
            IEnumerable<string> modulePaths,
            IEnumerable<string> references,
            string sourceRoot)
        {
            string ns = string.IsNullOrWhiteSpace(rootNamespace)
                ? DefaultRootNamespace
                : rootNamespace.Trim();

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("Project");

                    writer.WriteStartElement("PropertyGroup");
                    writer.WriteElementString("RootNamespace", ns);
                    writer.WriteElementString("OutputType", "library");
                    writer.WriteElementString("TargetFramework", TargetFramework);
                    writer.WriteElementString("AssemblyName", AssemblyName);
                    if (!string.IsNullOrWhiteSpace(sourceRoot))
                    {
                        writer.WriteElementString("SourceRoot", sourceRoot.Trim());
                    }
                    writer.WriteEndElement();

                    WriteItemGroup(writer, "SourceFile", sourceGlobs);
                    WriteItemGroup(writer, "ModulePath", modulePaths);
                    WriteItemGroup(writer, "Reference", references);

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }
        }

        private static void WriteItemGroup(XmlWriter writer, string itemName, IEnumerable<string> includes)
        {
            if (includes == null)
            {
                return;
            }

            bool opened = false;
            foreach (string include in includes)
            {
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                if (!opened)
                {
                    writer.WriteStartElement("ItemGroup");
                    opened = true;
                }

                writer.WriteStartElement(itemName);
                writer.WriteAttributeString("Include", include.Trim());
                writer.WriteEndElement();
            }

            if (opened)
            {
                writer.WriteEndElement();
            }
        }
    }
}
