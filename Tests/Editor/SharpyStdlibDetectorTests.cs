namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyStdlibDetectorTests
    {
        [Test]
        public void FindModules_ImportMath_FindsMath()
        {
            CollectionAssert.AreEqual(new[] { "math" }, SharpyStdlibDetector.FindModules(MathCs));
        }

        [Test]
        public void FindModules_JsonAndCollectionsTypes_FindsBothSorted()
        {
            // `json` is an alias of global::Sharpy.Json; deque and Counter are
            // global::Sharpy.Deque<int> and global::Sharpy.Counter<int>.
            CollectionAssert.AreEqual(
                new[] { "collections", "json" },
                SharpyStdlibDetector.FindModules(JsonCollectionsCs));
        }

        [Test]
        public void FindModules_CoreOnlyCode_FindsNothing()
        {
            // Sharpy.Operator, Sharpy.List, Sharpy.Dict, Sharpy.Set and Sharpy.Builtins all
            // live in Sharpy.Core.dll. Positive control: the two tests above.
            CollectionAssert.IsEmpty(SharpyStdlibDetector.FindModules(CoreOnlyCs));
        }

        [Test]
        public void FindModules_NamespaceUsing_FindsModule()
        {
            CollectionAssert.AreEqual(
                new[] { "statistics" },
                SharpyStdlibDetector.FindModules("using global::Sharpy.Statistics;\n"));
        }

        [Test]
        public void FindModules_CommentsAndLineDirectives_AreSkipped()
        {
            string skipped = "// see global::Sharpy.Json\n"
                + "#line (2, 5) - (2, 9) 12 \"/proj/Sharpy.Json/a.spy\"\n"
                + "            return 1;\n";

            CollectionAssert.IsEmpty(SharpyStdlibDetector.FindModules(skipped));
            CollectionAssert.AreEqual(
                new[] { "json" },
                SharpyStdlibDetector.FindModules("            return global::Sharpy.Json.Dumps(x);\n"));
        }

        [Test]
        public void FindModules_SharpyAsTailOfLongerName_IsIgnored()
        {
            CollectionAssert.IsEmpty(SharpyStdlibDetector.FindModules("Game.Sharpy.Json.Dumps(x);"));
            CollectionAssert.AreEqual(new[] { "json" }, SharpyStdlibDetector.FindModules("Sharpy.Json.Dumps(x);"));
        }

        [Test]
        public void FindModules_NullOrEmpty_FindsNothing()
        {
            CollectionAssert.IsEmpty(SharpyStdlibDetector.FindModules(null));
            CollectionAssert.IsEmpty(SharpyStdlibDetector.FindModules(string.Empty));
        }

        [Test]
        public void FormatWarning_OneModule_NamesModuleAndMenuItem()
        {
            Assert.AreEqual(
                "[Sharpy] Assets/Scripts/uses_math.spy: uses the Sharpy stdlib module 'math', "
                + "but Sharpy.Stdlib.dll is not in this project, so the generated C# will not compile. "
                + "Install it with Assets/Sharpy/Install Stdlib (experimental), or remove the import.",
                SharpyStdlibDetector.FormatWarning("Assets/Scripts/uses_math.spy", new[] { "math" }));
        }

        [Test]
        public void FormatWarning_SeveralModules_ListsAll()
        {
            StringAssert.Contains(
                "uses the Sharpy stdlib modules 'collections', 'json', but",
                SharpyStdlibDetector.FormatWarning("a.spy", new List<string> { "collections", "json" }));
        }

        [Test]
        public void StdlibTable_MatchesPinnedToolchain()
        {
            // update-toolchain regenerates the table on every re-pin.
            Assert.AreEqual(SharpyToolchain.Version, SharpyStdlibModules.SourceVersion);
        }

        [Test]
        public void StdlibTable_MapsAttributedAndHelperTypes()
        {
            Assert.AreEqual("math", SharpyStdlibModules.TypeModules["Sharpy.MathModule.MathModuleModule"]);
            // No [SharpyModule]/[SharpyModuleType]: mapped through its source folder (Functools/).
            Assert.AreEqual("functools", SharpyStdlibModules.TypeModules["Sharpy.LruCache"]);
        }

        // `sharpyc project` (0.21.0) output for Assets/Scripts/uses_math.spy
        private const string MathCs = @"#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::Sharpy;
using Game;
using math = global::Sharpy.MathModule.MathModuleModule;

namespace Game.UsesMath
{
    [global::Sharpy.SharpyModule(""uses_math"")]
    public static partial class UsesMathModule
    {
        public static double Hyp(double a, double b)
        {
#line (4, 5) - (4, 37) 12 ""/Proj With Space/Assets/Scripts/uses_math.spy""
            return math.Sqrt(a * a + b * b);
#line hidden
        }
    }
}
#line default
";

        // `sharpyc project` (0.21.0) output for Assets/Scripts/uses_json_collections.spy
        private const string JsonCollectionsCs = @"#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::Sharpy;
using Game;
using json = global::Sharpy.Json;

namespace Game.UsesJsonCollections
{
    [global::Sharpy.SharpyModule(""uses_json_collections"")]
    public static partial class UsesJsonCollectionsModule
    {
        public static string Dump(Sharpy.Dict<string, int> x)
        {
#line (5, 5) - (5, 29) 12 ""/Proj With Space/Assets/Scripts/uses_json_collections.spy""
            global::Sharpy.Deque<int> q = new global::Sharpy.Deque<int>();
#line (6, 5) - (6, 16) 12 ""/Proj With Space/Assets/Scripts/uses_json_collections.spy""
            q.Append(1);
#line (7, 5) - (7, 32) 12 ""/Proj With Space/Assets/Scripts/uses_json_collections.spy""
            var c = new global::Sharpy.Counter<int>(new Sharpy.List<int>() { 1, 2, 2 });
#line (8, 5) - (8, 52) 12 ""/Proj With Space/Assets/Scripts/uses_json_collections.spy""
            return json.Dumps((object?)x) + global::Sharpy.Builtins.Str(global::Sharpy.Builtins.Len(q)) + global::Sharpy.Builtins.Str(c[2]);
#line hidden
        }
    }
}
#line default
";

        // `sharpyc project` (0.21.0) output for Assets/Scripts/core_only.spy
        private const string CoreOnlyCs = @"#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::Sharpy;
using Game;
using @operator = global::Sharpy.Operator;

namespace Game.CoreOnly
{
    [global::Sharpy.SharpyModule(""core_only"")]
    public static partial class CoreOnlyModule
    {
        public static int Total(Sharpy.List<int> xs)
        {
#line (4, 5) - (4, 34) 12 ""/Proj With Space/Assets/Scripts/core_only.spy""
            Sharpy.Dict<string, int> d = new Sharpy.Dict<string, int>()
#line hidden
            {
                {
                    ""a"",
                    1
                }
            };
#line (5, 5) - (5, 27) 12 ""/Proj With Space/Assets/Scripts/core_only.spy""
            Sharpy.Set<int> s = new global::Sharpy.Set<int>(xs);
#line (6, 5) - (6, 69) 12 ""/Proj With Space/Assets/Scripts/core_only.spy""
            return @operator.Add(global::Sharpy.Builtins.Len(xs), global::Sharpy.Builtins.Len(d)) + global::Sharpy.Builtins.Len(s) + global::Sharpy.Builtins.Len(global::Sharpy.Builtins.Sorted<int>(xs));
#line hidden
        }
    }
}
#line default
";
    }
}
