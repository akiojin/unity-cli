using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class EvalHandlerTests
    {
        private static JObject Call(string method, JObject args)
        {
            var type = typeof(CompilationHandler).Assembly.GetType("UnityCliBridge.Handlers.EvalHandler");
            Assert.That(type, Is.Not.Null, "EvalHandler must be provided by the Bridge package");
            return (JObject)type.GetMethod(method).Invoke(null, new object[] { args });
        }

        [TestCase("1 + 2", "expression", "completed")]
        [TestCase("Debug.Log(\"eval-test\"); return 4;", "statements", "completed")]
        [TestCase("this is invalid", "expression", "compile_error")]
        [TestCase("throw new InvalidOperationException(\"boom\");", "statements", "runtime_error")]
        [TestCase("await System.Threading.Tasks.Task.Delay(1)", "expression", "unsupported")]
        [TestCase("System.Threading.Tasks.Task.FromResult(1)", "expression", "unsupported")]
        [TestCase("async System.Threading.Tasks.Task F() {} F(); return 1;", "statements", "unsupported")]
        [TestCase("var a = new object[1]; a[0] = a; return a;", "statements", "serialization_error")]
        [TestCase("new int[1025]", "expression", "serialization_error")]
        public void EvaluatesAndClassifiesFailures(string code, string mode, string state)
        {
            var result = Call("Evaluate", new JObject { ["code"] = code, ["mode"] = mode });
            Assert.That((string)result["state"], Is.EqualTo(state), result.ToString());
            Assert.That(result["requestId"], Is.Not.Null);
            Assert.That(result["logs"], Is.TypeOf<JArray>());
            Assert.That(result["diagnostics"], Is.TypeOf<JArray>());
        }

        [Test]
        public void PreservesJsonTypesAndUnityObjectDescriptors()
        {
            var number = Call("Evaluate", new JObject { ["code"] = "42" });
            Assert.That(number["value"].Type, Is.EqualTo(JTokenType.Integer));
            var result = Call("Evaluate", new JObject { ["code"] = "new { n = 1, ok = true, values = new[] { 2, 3 }, obj = UnityEditor.Selection.activeObject }" });
            Assert.That((int)result["value"]["n"], Is.EqualTo(1));
            Assert.That(result["value"]["ok"].Type, Is.EqualTo(JTokenType.Boolean));
        }

        [TestCase("(System.DayOfWeek)99", 99)]
        [TestCase("System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance", 20)]
        public void PreservesEnumUnderlyingValues(string code, int expected)
        {
            var result = Call("Evaluate", new JObject { ["code"] = code });
            Assert.That((string)result["state"], Is.EqualTo("completed"));
            Assert.That(result["value"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((int)result["value"], Is.EqualTo(expected));
        }

        [Test]
        public void ReusesRequestAndRejectsChangedInput()
        {
            var id = Guid.NewGuid().ToString("N");
            var args = new JObject { ["requestId"] = id, ["code"] = "Guid.NewGuid().ToString()" };
            var first = Call("Evaluate", args);
            Assert.That(JToken.DeepEquals(first, Call("Evaluate", args)), Is.True);
            Assert.That(JToken.DeepEquals(first, Call("GetStatus", new JObject { ["requestId"] = id })), Is.True);
            args["code"] = "2";
            Assert.That((string)Call("Evaluate", args)["state"], Is.EqualTo("request_conflict"));
        }

        [Test]
        public void UnknownRequestDoesNotRunCode()
        {
            Assert.That((string)Call("GetStatus", new JObject { ["requestId"] = "missing" })["state"], Is.EqualTo("unknown"));
        }

        [Test]
        public void CapturesLogsAndDescribesUnityObjects()
        {
            var result = Call("Evaluate", new JObject { ["code"] = "Debug.Log(\"eval-capture\"); return UnityEditor.EditorGUIUtility.whiteTexture;", ["mode"] = "statements" });
            Assert.That((string)result["state"], Is.EqualTo("completed"), result.ToString());
            Assert.That((string)result["logs"][0]["message"], Is.EqualTo("eval-capture"));
            Assert.That((string)result["value"]["kind"], Is.EqualTo("unity_object"));
            Assert.That(result["value"]["instanceId"].Type, Is.EqualTo(JTokenType.Integer));
        }

        [TestCase("double.NaN")]
        [TestCase("new { bad = double.PositiveInfinity }")]
        [TestCase("typeof(string)")]
        public void RejectsValuesWithoutSafeJsonRepresentation(string code)
        {
            Assert.That((string)Call("Evaluate", new JObject { ["code"] = code })["state"], Is.EqualTo("serialization_error"));
        }

        [Test]
        public void CompileDiagnosticsReferToSnippet()
        {
            var result = Call("Evaluate", new JObject { ["code"] = "missingSymbol" });
            Assert.That((string)result["state"], Is.EqualTo("compile_error"));
            Assert.That((int)result["diagnostics"][0]["line"], Is.EqualTo(1));
        }

        [Test]
        public void BoundsTotalResultSize()
        {
            var result = Call("Evaluate", new JObject { ["code"] = "new[] { new string('a', 16000), new string('a', 16000), new string('a', 16000), new string('a', 16000), new string('a', 16000) }" });
            Assert.That((string)result["state"], Is.EqualTo("serialization_error"));
            Assert.That(result["value"].Type, Is.EqualTo(JTokenType.Null));
        }

        [TestCase("emittedAssemblies", 128, "reload_required")]
        [TestCase("evaluating", true, "busy")]
        public void GuardsDomainLifetimeAndReentrancy(string fieldName, object fieldValue, string state)
        {
            var type = typeof(CompilationHandler).Assembly.GetType("UnityCliBridge.Handlers.EvalHandler");
            var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            var original = field.GetValue(null);
            try
            {
                field.SetValue(null, fieldValue);
                Assert.That((string)Call("Evaluate", new JObject { ["code"] = "1" })["state"], Is.EqualTo(state));
            }
            finally { field.SetValue(null, original); }
        }

        [Test]
        public void FullCachePreservesExecutedRequestsAndRejectsNewOnes()
        {
            var args = new JObject { ["requestId"] = Guid.NewGuid().ToString("N"), ["code"] = "Guid.NewGuid().ToString()" };
            var first = Call("Evaluate", args);
            var type = typeof(CompilationHandler).Assembly.GetType("UnityCliBridge.Handlers.EvalHandler");
            var cache = (System.Collections.IDictionary)type.GetField("Results", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var inserted = new System.Collections.Generic.List<string>();
            try
            {
                while (cache.Count < 256)
                {
                    var id = Guid.NewGuid().ToString("N");
                    cache.Add(id, cache[(string)args["requestId"]]);
                    inserted.Add(id);
                }
                Assert.That((string)Call("Evaluate", new JObject { ["code"] = "1" })["state"], Is.EqualTo("reload_required"));
                Assert.That(JToken.DeepEquals(first, Call("Evaluate", args)), Is.True);
                Assert.That(JToken.DeepEquals(first, Call("GetStatus", new JObject { ["requestId"] = args["requestId"] })), Is.True);
            }
            finally { foreach (var id in inserted) cache.Remove(id); }
        }
    }
}
