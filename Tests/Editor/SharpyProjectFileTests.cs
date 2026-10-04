namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Linq;
    using System.Xml.Linq;
    using NUnit.Framework;

    public class SharpyProjectFileTests
    {
        private static readonly string[] Globs = { "../../Assets/**/*.spy" };
        private static readonly string[] None = new string[0];

        private static XElement Parse(string text)
        {
            return XDocument.Parse(text).Root;
        }

        private static string[] Includes(XElement root, string itemName)
        {
            return root.Elements("ItemGroup")
                .Elements(itemName)
                .Select(e => (string)e.Attribute("Include"))
                .ToArray();
        }

        [Test]
        public void Build_WritesProjectRootAndProperties()
        {
            var root = Parse(SharpyProjectFile.Build("Game", Globs, None, None, null));
            var props = root.Element("PropertyGroup");

            Assert.AreEqual("Project", root.Name.LocalName);
            Assert.AreEqual("Game", (string)props.Element("RootNamespace"));
            Assert.AreEqual("library", (string)props.Element("OutputType"));
            Assert.AreEqual("netstandard2.1", (string)props.Element("TargetFramework"));
            Assert.AreEqual("SharpyGenerated", (string)props.Element("AssemblyName"));
            CollectionAssert.AreEqual(Globs, Includes(root, "SourceFile"));
        }

        [Test]
        public void Build_WritesOneElementPerReferenceAndModulePath()
        {
            var text = SharpyProjectFile.Build(
                "Game",
                Globs,
                new[] { "../../Assets/Lib", "  ../../Packages/x  " },
                new[] { "/u/UnityEngine.CoreModule.dll", "", "  ", "/u/UnityEngine.PhysicsModule.dll" },
                null);
            var root = Parse(text);

            CollectionAssert.AreEqual(
                new[] { "../../Assets/Lib", "../../Packages/x" },
                Includes(root, "ModulePath"));
            CollectionAssert.AreEqual(
                new[] { "/u/UnityEngine.CoreModule.dll", "/u/UnityEngine.PhysicsModule.dll" },
                Includes(root, "Reference"));
        }

        [Test]
        public void Build_EscapesXmlSpecialCharactersInPaths()
        {
            const string nasty = "/Users/a&b/<Game> \"Proj\"/Lib.dll";
            var text = SharpyProjectFile.Build("Game", Globs, None, new[] { nasty }, null);

            StringAssert.Contains("&amp;", text);
            StringAssert.Contains("&lt;", text);
            StringAssert.Contains("&quot;", text);
            CollectionAssert.AreEqual(new[] { nasty }, Includes(Parse(text), "Reference"));
        }

        [Test]
        public void Build_EmptyLists_ProduceNoItemElements()
        {
            var text = SharpyProjectFile.Build("Game", None, None, null, null);
            var root = Parse(text);

            Assert.IsEmpty(root.Elements("ItemGroup"));
            StringAssert.DoesNotContain("ItemGroup", text);
        }

        [Test]
        public void Build_OnlyNonEmptyKindsGetAnItemGroup()
        {
            // Positive control for the absence test above.
            var root = Parse(SharpyProjectFile.Build("Game", Globs, None, new[] { "a.dll" }, null));

            Assert.AreEqual(2, root.Elements("ItemGroup").Count());
            Assert.IsEmpty(Includes(root, "ModulePath"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Build_BlankNamespace_UsesDefault(string ns)
        {
            var root = Parse(SharpyProjectFile.Build(ns, Globs, None, None, null));

            Assert.AreEqual(
                SharpyProjectFile.DefaultRootNamespace,
                (string)root.Element("PropertyGroup").Element("RootNamespace"));
        }

        [Test]
        public void DefaultRootNamespace_DoesNotCollideWithSharpy()
        {
            Assert.IsFalse(SharpySettings.NamespaceCollidesWithSharpy(SharpyProjectFile.DefaultRootNamespace));
        }

        [Test]
        public void Build_SourceRoot_WrittenWhenGiven()
        {
            var root = Parse(SharpyProjectFile.Build("Game", Globs, None, None, "../../Assets"));

            Assert.AreEqual("../../Assets", (string)root.Element("PropertyGroup").Element("SourceRoot"));
        }

        [TestCase("")]
        [TestCase(null)]
        public void Build_SourceRoot_OmittedWhenBlank(string sourceRoot)
        {
            var text = SharpyProjectFile.Build("Game", Globs, None, None, sourceRoot);

            Assert.IsNull(Parse(text).Element("PropertyGroup").Element("SourceRoot"));
            StringAssert.DoesNotContain("SourceRoot", text);
        }

        [Test]
        public void Build_IsDeterministic_LfNewlines_NoBom()
        {
            var a = SharpyProjectFile.Build("Game", Globs, new[] { "m" }, new[] { "r.dll" }, "../../Assets");
            var b = SharpyProjectFile.Build("Game", Globs, new[] { "m" }, new[] { "r.dll" }, "../../Assets");

            Assert.AreEqual(a, b);
            StringAssert.DoesNotContain("\r", a);
            Assert.AreNotEqual('﻿', a[0]);
            StringAssert.StartsWith("<?xml", a);
            StringAssert.EndsWith("</Project>\n", a);
        }
    }
}
