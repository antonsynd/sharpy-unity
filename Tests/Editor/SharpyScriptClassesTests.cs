namespace Sharpy.Unity.Editor.Tests
{
    // Usings are inside the namespace so BCL names (Path, List, Math, ...) win
    // over same-named Sharpy.* root types from the referenced Sharpy.Core.dll.
    using System.Collections.Generic;
    using NUnit.Framework;

    public class SharpyScriptClassesTests
    {
        // The shape sharpyc 0.21.0+4a6228e87 emits for smoke_behaviour.spy (CRLF).
        private static string Module(string ns, string module, params string[] classLines)
        {
            string text = "#nullable enable\r\n\r\nusing System;\r\nusing global::Sharpy;\r\n\r\n"
                + $"namespace {ns}\r\n{{\r\n"
                + $"    [global::Sharpy.SharpyModule(\"{module}\")]\r\n"
                + $"    public static partial class {module}Module\r\n    {{\r\n    }}\r\n";

            foreach (string line in classLines)
            {
                text += $"\r\n    {line}\r\n    {{\r\n        public void Start()\r\n        {{\r\n        }}\r\n    }}\r\n";
            }

            return text + "}\r\n";
        }

        private static Dictionary<string, string> Find(Dictionary<string, string> generated, List<string> warnings = null)
        {
            return SharpyScriptClasses.Find(generated, name => false, warnings ?? new List<string>());
        }

        [Test]
        public void Find_SingleMonoBehaviour_NamesIt()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Scripts/Smoke/smoke_behaviour.spy"] = Module(
                    "SharpyScripts.Smoke.SmokeBehaviour", "SmokeBehaviour",
                    "public class SmokeBehaviour : global::UnityEngine.MonoBehaviour"),
            };

            var result = Find(generated);

            Assert.AreEqual("SmokeBehaviour", result["Assets/Scripts/Smoke/smoke_behaviour.spy"]);
        }

        [Test]
        public void Find_ScriptableObject_NamesIt()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Data/config.spy"] = Module(
                    "SharpyScripts.Data.Config", "Config",
                    "public class GameConfig : global::UnityEngine.ScriptableObject, global::System.IDisposable"),
            };

            Assert.AreEqual("GameConfig", Find(generated)["Assets/Data/config.spy"]);
        }

        [Test]
        public void Find_PlainClassesOnly_NotNamed()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Scripts/Core/greeting.spy"] = Module(
                    "SharpyScripts.Core.Greeting", "Greeting",
                    "public class Greeter",
                    "public class Loud : global::SharpyScripts.Core.Greeting.Greeter"),
            };

            CollectionAssert.IsEmpty(Find(generated));
        }

        [Test]
        public void Find_TwoMonoBehaviours_NotNamed_Warns()
        {
            var warnings = new List<string>();
            var generated = new Dictionary<string, string>
            {
                ["Assets/Scripts/two.spy"] = Module(
                    "SharpyScripts.Two", "Two",
                    "public class First : global::UnityEngine.MonoBehaviour",
                    "public class Second : global::UnityEngine.MonoBehaviour"),
            };

            CollectionAssert.IsEmpty(Find(generated, warnings));
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Assets/Scripts/two.spy", warnings[0]);
            StringAssert.Contains("First, Second", warnings[0]);
        }

        [Test]
        public void Find_BaseInAnotherModule_FollowsTheChain()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Scripts/Core/actor.spy"] = Module(
                    "SharpyScripts.Core.Actor", "Actor",
                    "public abstract class Actor : global::UnityEngine.MonoBehaviour"),
                ["Assets/Scripts/Game/player.spy"] = Module(
                    "SharpyScripts.Game.Player", "Player",
                    "public class Player : global::SharpyScripts.Core.Actor.Actor"),
            };

            var result = Find(generated);

            Assert.AreEqual("Actor", result["Assets/Scripts/Core/actor.spy"]);
            Assert.AreEqual("Player", result["Assets/Scripts/Game/player.spy"]);
        }

        [Test]
        public void Find_ExternalBase_AskedOfThePredicate()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Net/peer.spy"] = Module(
                    "SharpyScripts.Net.Peer", "Peer",
                    "public class Peer : global::Unity.Netcode.NetworkBehaviour"),
            };
            var asked = new List<string>();

            var result = SharpyScriptClasses.Find(
                generated, name => { asked.Add(name); return name == "Unity.Netcode.NetworkBehaviour"; }, new List<string>());

            Assert.AreEqual("Peer", result["Assets/Net/peer.spy"]);
            CollectionAssert.AreEqual(new[] { "Unity.Netcode.NetworkBehaviour" }, asked);
        }

        [Test]
        public void Find_GenericClass_NotNamed()
        {
            var generated = new Dictionary<string, string>
            {
                ["Assets/Scripts/pool.spy"] = Module(
                    "SharpyScripts.Pool", "Pool",
                    "public class Pool<T> : global::UnityEngine.MonoBehaviour"),
            };

            CollectionAssert.IsEmpty(Find(generated));
        }
    }
}
