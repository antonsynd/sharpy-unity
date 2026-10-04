namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using NUnit.Framework;

    public class SharpyLineDirectivesTests
    {
        private const string Root = "/Users/dev/My Game";

        // Shaped like `sharpyc project ... --emit-cs-to` output from sharpyc
        // 0.21.0+db8e05b (only the project-root prefix differs): CRLF line
        // endings, directives at column 0, both span and classic forms.
        private const string Generated =
            "    public class Mover\r\n" +
            "    {\r\n" +
            "        public int Step(int x)\r\n" +
            "#line 4 \"/Users/dev/My Game/Assets/Scripts/mover.spy\"\r\n" +
            "        {\r\n" +
            "#line (5, 9) - (5, 27) 12 \"/Users/dev/My Game/Assets/Scripts/mover.spy\"\r\n" +
            "            var y = x + this.Speed;\r\n" +
            "#line (6, 9) - (7, 27) 12 \"/Users/dev/My Game/Assets/Scripts/mover.spy\"\r\n" +
            "            if (y > 10)\r\n" +
            "#line hidden\r\n" +
            "            {\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "#line default\r\n";

        private const string Rewritten =
            "    public class Mover\r\n" +
            "    {\r\n" +
            "        public int Step(int x)\r\n" +
            "#line 4 \"Assets/Scripts/mover.spy\"\r\n" +
            "        {\r\n" +
            "#line 5 \"Assets/Scripts/mover.spy\"\r\n" +
            "            var y = x + this.Speed;\r\n" +
            "#line 6 \"Assets/Scripts/mover.spy\"\r\n" +
            "            if (y > 10)\r\n" +
            "#line hidden\r\n" +
            "            {\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "#line default\r\n";

        private const string Stripped =
            "    public class Mover\r\n" +
            "    {\r\n" +
            "        public int Step(int x)\r\n" +
            "        {\r\n" +
            "            var y = x + this.Speed;\r\n" +
            "            if (y > 10)\r\n" +
            "            {\r\n" +
            "            }\r\n" +
            "        }\r\n" +
            "    }\r\n";

        [Test]
        public void Rewrite_RealOutput_SpanBecomesClassic_PathsRelative_CrlfPreserved()
        {
            Assert.AreEqual(Rewritten, SharpyLineDirectives.Rewrite(Generated, Root, true));
        }

        [Test]
        public void Rewrite_KeepFalse_RemovesEveryDirectiveLine()
        {
            Assert.AreEqual(Stripped, SharpyLineDirectives.Rewrite(Generated, Root, false));
        }

        [TestCase("#line (5, 9) - (5, 35) 12 \"/p/Assets/x.spy\"", "#line 5 \"Assets/x.spy\"")]
        [TestCase("#line (5, 9) - (5, 35) \"/p/Assets/x.spy\"", "#line 5 \"Assets/x.spy\"")]
        [TestCase("#line (11,5)-(11,18) 12 \"/p/Assets/x.spy\"", "#line 11 \"Assets/x.spy\"")]
        public void Rewrite_SpanForm_BecomesClassicWithStartLine(string input, string expected)
        {
            Assert.AreEqual(expected, SharpyLineDirectives.Rewrite(input, "/p", true));
        }

        [Test]
        public void Rewrite_ClassicForm_PathMadeRelative()
        {
            Assert.AreEqual(
                "#line 4 \"Assets/x.spy\"\n",
                SharpyLineDirectives.Rewrite("#line 4 \"/proj/Assets/x.spy\"\n", "/proj/", true));
        }

        [TestCase("#line hidden\n")]
        [TestCase("#line default\n")]
        [TestCase("#line 7\n")]
        [TestCase("    #line hidden\r\n")]
        public void Rewrite_DirectivesWithoutPath_Untouched(string input)
        {
            Assert.AreEqual(input, SharpyLineDirectives.Rewrite(input, "/proj", true));
        }

        [Test]
        public void Rewrite_LeadingWhitespace_Preserved()
        {
            Assert.AreEqual(
                "\t  #line 3 \"Assets/x.spy\"\n",
                SharpyLineDirectives.Rewrite("\t  #line (3, 1) - (3, 9) 4 \"/proj/Assets/x.spy\"\n", "/proj", true));
        }

        [Test]
        public void Rewrite_PathOutsideProject_KeptButNormalized()
        {
            Assert.AreEqual(
                "#line 2 \"D:/other/lib.spy\"",
                SharpyLineDirectives.Rewrite("#line 2 \"D:\\other\\lib.spy\"", "C:\\proj", true));
            Assert.AreEqual(
                "#line 2 \"/proj2/Assets/x.spy\"",
                SharpyLineDirectives.Rewrite("#line 2 \"/proj2/Assets/x.spy\"", "/proj", true));
        }

        [Test]
        public void Rewrite_WindowsSeparators_MadeRelative_DriveCaseInsensitive()
        {
            Assert.AreEqual(
                "#line 5 \"Assets/Scripts/x.spy\"",
                SharpyLineDirectives.Rewrite(
                    "#line (5, 9) - (5, 35) 12 \"c:\\Proj\\Assets\\Scripts\\x.spy\"", "C:\\proj", true));
        }

        [Test]
        public void Rewrite_PosixRoot_IsCaseSensitive()
        {
            Assert.AreEqual(
                "#line 5 \"/Proj/Assets/x.spy\"",
                SharpyLineDirectives.Rewrite("#line 5 \"/Proj/Assets/x.spy\"", "/proj", true));
        }

        [Test]
        public void Rewrite_TextWithoutDirectives_ReturnedUnchanged()
        {
            const string text = "// #region\r\nclass A\r\n{\r\n    string s = \"#line\";\r\n}";

            Assert.AreEqual(text, SharpyLineDirectives.Rewrite(text, "/proj", true));
            Assert.AreEqual(text, SharpyLineDirectives.Rewrite(text, "/proj", false));
        }

        [Test]
        public void Rewrite_KeepFalse_LastLineWithoutNewline_Removed()
        {
            Assert.AreEqual("a\n", SharpyLineDirectives.Rewrite("a\n#line default", "/proj", false));
        }
    }
}
