namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyDiagnosticParserTests
    {
        // The fixtures are real `sharpyc project` output (0.21.0), captured in a
        // directory whose path contained a space; that directory is replaced by Root.
        private const string Root = "/Proj With Space";

        [Test]
        public void ParseTextDiagnostics_TwoErrorsInTwoFiles_ParsesBoth()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseTextDiagnostics(TwoErrorsStderr, Root);

            Assert.AreEqual(2, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0260",
                "Assets/Scripts/Game/player.spy", 5, 5);
            Assert.AreEqual("Cannot return type 'int32' from function expecting 'str'", result[0].Message);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0200",
                "Assets/Scripts/Core/greeting.spy", 2, 24);
            Assert.AreEqual("Undefined identifier 'nmae'. Did you mean 'name'?", result[1].Message);
        }

        [Test]
        public void ParseTextDiagnostics_StdoutOfSuccessfulBuild_ParsesWarningsOnly()
        {
            List<SharpyDiagnostic> result =
                SharpyDiagnosticParser.ParseTextDiagnostics(WarningsOnSuccessStdout, Root);

            Assert.AreEqual(2, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Warning, "SPY0466",
                "Assets/Scripts/Core/greeting.spy", 6, 12);
            Assert.AreEqual("'foo' is deprecated: Use bar() instead", result[0].Message);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Warning, "SPY0450",
                "Assets/Scripts/Game/player.spy", 3, 5);
        }

        [Test]
        public void ParseTextDiagnostics_BannerOnlyStdout_ReturnsEmpty()
        {
            // Positive control: the warnings test parses the same banner lines plus diagnostics.
            Assert.IsEmpty(SharpyDiagnosticParser.ParseTextDiagnostics(BannerOnlyStdout, Root));
            Assert.IsEmpty(SharpyDiagnosticParser.ParseTextDiagnostics(
                "Build succeeded.\nOutput: /Proj With Space/Library/Sharpy/bin/Debug/netstandard2.1/Game.dll\n", Root));
            Assert.IsEmpty(SharpyDiagnosticParser.ParseTextDiagnostics(InvalidProjectStderr, Root));
        }

        [Test]
        public void ParseTextDiagnostics_HeaderWithoutLocation_KeepsLineZeroAndNoPath()
        {
            // The renderer prints a bare header when a diagnostic has neither file nor line
            // (an internal compiler error, a cancelled compile), then a blank line.
            string text = "Build FAILED.\n\n"
                + "error[SPY0909]: internal compiler error: boom\n\n"
                + "Type errors:\n"
                + "error: Compilation cancelled\n\n"
                + "error[SPY0200]: Undefined identifier 'x'\n"
                + "  --> /Proj With Space/Assets/a.spy:3:7\n";

            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseTextDiagnostics(text, Root);

            Assert.AreEqual(3, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0909", string.Empty, 0, 0);
            Assert.AreEqual("internal compiler error: boom", result[0].Message);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Error, string.Empty, string.Empty, 0, 0);
            Assert.AreEqual("Compilation cancelled", result[1].Message);
            AssertDiagnostic(result[2], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0200", "Assets/a.spy", 3, 7);
        }

        [Test]
        public void ParseTextDiagnostics_PathOnlyLocationInOtherCase_IsProjectRelative()
        {
            // sharpyc lowercases the whole path of a circular-import error.
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseTextDiagnostics(CircularImportStderr, Root);

            Assert.AreEqual(1, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0302",
                "assets/scripts/core/greeting.spy", 0, 0);
        }

        [Test]
        public void ParseTextDiagnostics_MultiLineMessage_KeepsContinuationLines()
        {
            List<SharpyDiagnostic> result =
                SharpyDiagnosticParser.ParseTextDiagnostics(MultiLineMessageStderr, Root);

            Assert.AreEqual(1, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0336",
                "Assets/Scripts/Game/player.spy", 8, 10);
            StringAssert.EndsWith("Candidates:\n  pop() -> int32\n  pop(int32) -> int32", result[0].Message);
        }

        [Test]
        public void ParseTextDiagnostics_GeneratedCSharpError_IsSpy0908AtSpyLine()
        {
            List<SharpyDiagnostic> result =
                SharpyDiagnosticParser.ParseTextDiagnostics(GeneratedCSharpErrorStderr, Root);

            Assert.AreEqual(1, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0908",
                "Assets/Scripts/Game/player.spy", 2, 101);
            StringAssert.Contains("(CS0103: The name 'a' does not exist in the current context)", result[0].Message);
        }

        [Test]
        public void ParseTextDiagnostics_WindowsPaths_SplitOnLastColons()
        {
            string text = "error[SPY0220]: bad\n"
                + "  --> C:\\Proj With Space\\Assets\\Scripts\\Game\\player.spy:3:5\n\n"
                + "warning[SPY0450]: elsewhere\n"
                + "  --> D:\\Other\\lib.spy:12:1\n";

            List<SharpyDiagnostic> result =
                SharpyDiagnosticParser.ParseTextDiagnostics(text, "C:\\Proj With Space\\");

            Assert.AreEqual(2, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0220",
                "Assets/Scripts/Game/player.spy", 3, 5);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Warning, "SPY0450",
                "D:\\Other\\lib.spy", 12, 1);
        }

        [Test]
        public void ParseTextDiagnostics_PathOutsideRoot_StaysAbsolute()
        {
            string text = "error[SPY0200]: x\n  --> /Proj With Space Two/Assets/a.spy:1:2\n";

            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseTextDiagnostics(text, Root);

            Assert.AreEqual(1, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0200",
                "/Proj With Space Two/Assets/a.spy", 1, 2);
        }

        [Test]
        public void ParseTextDiagnostics_AnsiColours_AreStripped()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseTextDiagnostics(ColouredStderr, Root);

            Assert.AreEqual(1, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0200",
                "Assets/Scripts/Core/greeting.spy", 2, 24);
            Assert.AreEqual("Undefined identifier 'nmae'. Did you mean 'name'?", result[0].Message);
        }

        [Test]
        public void ParseTextDiagnostics_CrLfLineEndings_AreTolerated()
        {
            List<SharpyDiagnostic> result =
                SharpyDiagnosticParser.ParseTextDiagnostics(TwoErrorsStderr.Replace("\n", "\r\n"), Root);

            Assert.AreEqual(2, result.Count);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0200",
                "Assets/Scripts/Core/greeting.spy", 2, 24);
            Assert.AreEqual("Undefined identifier 'nmae'. Did you mean 'name'?", result[1].Message);
        }

        [Test]
        public void ParseTextDiagnostics_NullOrEmpty_ReturnsEmpty()
        {
            Assert.IsEmpty(SharpyDiagnosticParser.ParseTextDiagnostics(null, Root));
            Assert.IsEmpty(SharpyDiagnosticParser.ParseTextDiagnostics(string.Empty, Root));
        }

        [Test]
        public void ParseCompilerOutput_FailedBuild_ReadsErrorsFromStderrAndWarningsFromStdout()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseCompilerOutput(
                WarningBesideErrorStdout, WarningBesideErrorStderr, Root, failed: true);

            Assert.AreEqual(2, result.Count);
            AssertDiagnostic(result[0], SharpyDiagnostic.DiagnosticSeverity.Error, "SPY0220",
                "Assets/Scripts/Game/player.spy", 2, 5);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Warning, "SPY0466",
                "Assets/Scripts/Core/greeting.spy", 6, 12);
        }

        [Test]
        public void ParseCompilerOutput_FailedWithoutParsableError_FallsBackToRawStderr()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseCompilerOutput(
                WarningBesideErrorStdout, InvalidProjectStderr, Root, failed: true);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Warning, result[0].Severity);
            AssertDiagnostic(result[1], SharpyDiagnostic.DiagnosticSeverity.Error, string.Empty, string.Empty, 0, 0);
            Assert.AreEqual("Error: Invalid .spyproj file: <RootNamespace> is required", result[1].Message);
        }

        [Test]
        public void ParseCompilerOutput_FailedWithEmptyStderr_ReportsGenericError()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseCompilerOutput(
                BannerOnlyStdout, string.Empty, Root, failed: true);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(SharpyDiagnostic.DiagnosticSeverity.Error, result[0].Severity);
            Assert.AreEqual(SharpyDiagnosticParser.NoDiagnosticsMessage, result[0].Message);
        }

        [Test]
        public void ParseCompilerOutput_SucceededBuild_ReturnsWarningsWithoutFallback()
        {
            List<SharpyDiagnostic> result = SharpyDiagnosticParser.ParseCompilerOutput(
                WarningsOnSuccessStdout, string.Empty, Root, failed: false);

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.TrueForAll(d => d.Severity == SharpyDiagnostic.DiagnosticSeverity.Warning));
        }

        private static void AssertDiagnostic(
            SharpyDiagnostic diagnostic,
            SharpyDiagnostic.DiagnosticSeverity severity,
            string code,
            string filePath,
            int line,
            int column)
        {
            Assert.AreEqual(severity, diagnostic.Severity, "severity");
            Assert.AreEqual(code, diagnostic.Code, "code");
            Assert.AreEqual(filePath, diagnostic.FilePath, "file path");
            Assert.AreEqual(line, diagnostic.Line, "line");
            Assert.AreEqual(column, diagnostic.Column, "column");
        }

        // a2_name_and_type_pinned.stderr
        private const string TwoErrorsStderr = @"Build FAILED.

error[SPY0260]: Cannot return type 'int32' from function expecting 'str'
  --> /Proj With Space/Assets/Scripts/Game/player.spy:5:5
    |
  5 |     return x
    |     ^^^^^^^^
    |

error[SPY0200]: Undefined identifier 'nmae'. Did you mean 'name'?
  --> /Proj With Space/Assets/Scripts/Core/greeting.spy:2:24
    |
  2 |     return ""Hello, "" + nmae
    |                        ^^^^
    |

";

        // b_warning_success_head.stdout
        private const string WarningsOnSuccessStdout = @"Project: Game
Configuration: Debug
Output: library
Source files: 2

Saving generated C# code to: /Proj With Space/Library/Sharpy/emit
  Saved: Assets/Scripts/Core/greeting.cs
  Saved: Assets/Scripts/Game/player.cs

Type warnings:
warning[SPY0466]: 'foo' is deprecated: Use bar() instead
  --> /Proj With Space/Assets/Scripts/Core/greeting.spy:6:12
    |
  6 |     return foo() + name
    |            ^
    |

Validation warnings:
warning[SPY0450]: Unreachable code detected
  --> /Proj With Space/Assets/Scripts/Game/player.spy:3:5
    |
  3 |     print(""never"")
    |     ^^^^^^^^^^^^^^
    |

Build succeeded.
Output: /Proj With Space/Library/Sharpy/bin/Debug/netstandard2.1/Game.dll
";

        // a_two_errors_pinned.stdout
        private const string BannerOnlyStdout = @"Project: Game
Configuration: Debug
Output: library
Source files: 2

";

        // c_warning_and_error_pinned.stdout
        private const string WarningBesideErrorStdout = @"Project: Game
Configuration: Debug
Output: library
Source files: 2

warning[SPY0466]: 'foo' is deprecated: Use bar() instead
  --> /Proj With Space/Assets/Scripts/Core/greeting.spy:6:12
    |
  6 |     return foo() + name
    |            ^
    |

";

        // c_warning_and_error_pinned.stderr
        private const string WarningBesideErrorStderr = @"Build FAILED.

error[SPY0220]: Cannot assign type 'str' to variable of type 'int32'
  --> /Proj With Space/Assets/Scripts/Game/player.spy:2:5
    |
  2 |     x: int = ""hello""
    |     ^^^^^^^^^^^^^^^^
    |

";

        // f_circular_pinned.stderr
        private const string CircularImportStderr = @"Build FAILED.

error[SPY0302]: Circular dependency detected: greeting.spy → player.spy → greeting.spy
  --> /proj with space/assets/scripts/core/greeting.spy

";

        // h_multiline_message_pinned.stderr
        private const string MultiLineMessageStderr = @"Build FAILED.

error[SPY0336]: 'pop' has 2 overloads taking different numbers of arguments, so it cannot be used as a value without a target type to select one. Candidates:
  pop() -> int32
  pop(int32) -> int32
  --> /Proj With Space/Assets/Scripts/Game/player.spy:8:10
    |
  8 |     at = xs.pop
    |          ^^^^^^
    |

";

        // e_exit2_pinned.stderr
        private const string GeneratedCSharpErrorStderr = @"Build FAILED.

error[SPY0908]: internal error: generated C# failed to compile (CS0103: The name 'a' does not exist in the current context). This is a Sharpy compiler bug — please report it at https://github.com/antonsynd/sharpy/issues
  --> /Proj With Space/Assets/Scripts/Game/player.spy:2:101
    |
  2 |     return sum(a for a, b in pairs)
    |                                    ^
    |

";

        // f_empty_rootns_pinned.stderr
        private const string InvalidProjectStderr = @"Error: Invalid .spyproj file: <RootNamespace> is required
";

        // g_ansi_stdout_tty.stderr
        private const string ColouredStderr =
            "Build FAILED.\n"
            + "\n"
            + "\u001b[1;31merror[SPY0200]\u001b[0m: \u001b[1;37mUndefined identifier 'nmae'. Did you mean 'name'?\u001b[0m\n"
            + "  \u001b[36m-->\u001b[0m /Proj With Space/Assets/Scripts/Core/greeting.spy:2:24\n"
            + "    \u001b[36m|\u001b[0m\n"
            + " \u001b[36m 2\u001b[0m \u001b[36m|\u001b[0m     return \"Hello, \" + nmae\n"
            + "    \u001b[36m|\u001b[0m                        \u001b[1;31m^^^^\u001b[0m\n"
            + "    \u001b[36m|\u001b[0m\n"
            + "\n";
    }
}
