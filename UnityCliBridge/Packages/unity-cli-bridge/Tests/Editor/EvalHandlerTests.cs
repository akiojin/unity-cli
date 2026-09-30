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
                Assert.That((string)Call("Evaluate", new JObject { ["code"] = Unique("1") })["state"], Is.EqualTo(state));
            }
            finally { field.SetValue(null, original); }
        }

        private static string Unique(string expression) => expression + " /* " + Guid.NewGuid().ToString("N") + " */";

        private static JObject Stats() => Call("GetStats", new JObject());

        [Test]
        public void IdenticalSourceReusesOneAssemblyAndStillExecutes()
        {
            var code = Unique("Guid.NewGuid().ToString()");
            var before = Stats();
            var first = Call("Evaluate", new JObject { ["code"] = code });
            var second = Call("Evaluate", new JObject { ["code"] = code });
            var after = Stats();
            Assert.That((string)first["state"], Is.EqualTo("completed"), first.ToString());
            Assert.That((string)second["state"], Is.EqualTo("completed"), second.ToString());
            Assert.That((string)second["value"], Is.Not.EqualTo((string)first["value"]), "a cached compilation must run the code again");
            Assert.That((int)after["emittedAssemblies"] - (int)before["emittedAssemblies"], Is.EqualTo(1));
            Assert.That((int)after["compileCacheHits"] - (int)before["compileCacheHits"], Is.EqualTo(1));
        }

        [Test]
        public void ModeIsPartOfTheCompilationKey()
        {
            var code = Unique("1");
            Assert.That((string)Call("Evaluate", new JObject { ["code"] = code })["state"], Is.EqualTo("completed"));
            Assert.That((string)Call("Evaluate", new JObject { ["code"] = code, ["mode"] = "statements" })["state"], Is.EqualTo("compile_error"));
        }

        [Test]
        public void CachedCompilationReplaysWarnings()
        {
            var code = "int unused = 1; /* " + Guid.NewGuid().ToString("N") + " */ return 2;";
            var first = Call("Evaluate", new JObject { ["code"] = code, ["mode"] = "statements" });
            var second = Call("Evaluate", new JObject { ["code"] = code, ["mode"] = "statements" });
            Assert.That(((JArray)first["diagnostics"]).Count, Is.GreaterThan(0), first.ToString());
            Assert.That(JToken.DeepEquals(first["diagnostics"], second["diagnostics"]), Is.True);
        }

        [Test]
        public void DistinctSourcesShareMetadataReferences()
        {
            Call("Evaluate", new JObject { ["code"] = Unique("1") });
            var before = Stats();
            Call("Evaluate", new JObject { ["code"] = Unique("2") });
            Call("Evaluate", new JObject { ["code"] = Unique("3") });
            var after = Stats();
            Assert.That((int)after["referenceBuilds"], Is.EqualTo((int)before["referenceBuilds"]));
            Assert.That((int)after["referenceCount"], Is.GreaterThan(0));
            Assert.That((int)after["emittedAssemblies"] - (int)before["emittedAssemblies"], Is.EqualTo(2));
        }

        [Test]
        public void NewlyLoadedAssemblyBecomesResolvable()
        {
            var name = "EvalProbe" + Guid.NewGuid().ToString("N");
            var probe = name + ".Value";
            Assert.That((string)Call("Evaluate", new JObject { ["code"] = probe })["state"], Is.EqualTo("compile_error"));
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".dll");
            try
            {
                var build = Call("Evaluate", new JObject { ["mode"] = "statements", ["code"] =
                    "var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(\"public static class " + name + " { public static int Value => 42; }\");" +
                    "var refs = new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };" +
                    "var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);" +
                    "using (var file = System.IO.File.Create(@\"" + path + "\")) {" +
                    "var emit = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(\"" + name + "\", new[] { tree }, refs, options).Emit(file);" +
                    "if (!emit.Success) throw new Exception(string.Join(\";\", emit.Diagnostics)); }" +
                    "System.Reflection.Assembly.LoadFrom(@\"" + path + "\"); return 1;" });
                Assert.That((string)build["state"], Is.EqualTo("completed"), build.ToString());
                var resolved = Call("Evaluate", new JObject { ["code"] = probe });
                Assert.That((string)resolved["state"], Is.EqualTo("completed"), resolved.ToString());
                Assert.That((int)resolved["value"], Is.EqualTo(42));
            }
            finally { System.IO.File.Delete(path); }
        }

        [Test]
        public void CachedSourceStillRunsAtTheAssemblyLimit()
        {
            var code = Unique("7");
            Call("Evaluate", new JObject { ["code"] = code });
            var type = typeof(CompilationHandler).Assembly.GetType("UnityCliBridge.Handlers.EvalHandler");
            var field = type.GetField("emittedAssemblies", BindingFlags.Static | BindingFlags.NonPublic);
            var original = field.GetValue(null);
            try
            {
                field.SetValue(null, 128);
                var cached = Call("Evaluate", new JObject { ["code"] = code });
                Assert.That((string)cached["state"], Is.EqualTo("completed"), cached.ToString());
                Assert.That((int)cached["value"], Is.EqualTo(7));
            }
            finally { field.SetValue(null, original); }
        }

        [Test]
        public void FullResultCacheEvictsTheOldestRequest()
        {
            var oldest = Guid.NewGuid().ToString("N");
            var first = Call("Evaluate", new JObject { ["requestId"] = oldest, ["code"] = "1" });
            Assert.That(JToken.DeepEquals(first, Call("GetStatus", new JObject { ["requestId"] = oldest })), Is.True);
            string newest = null;
            for (var i = 0; i < 256; i++)
            {
                newest = Guid.NewGuid().ToString("N");
                Assert.That((string)Call("Evaluate", new JObject { ["requestId"] = newest, ["code"] = "1" })["state"], Is.EqualTo("completed"));
            }
            Assert.That((string)Call("GetStatus", new JObject { ["requestId"] = oldest })["state"], Is.EqualTo("unknown"));
            Assert.That((string)Call("GetStatus", new JObject { ["requestId"] = newest })["state"], Is.EqualTo("completed"));
            Assert.That((int)Stats()["storedResults"], Is.EqualTo(256));
        }

        [Test]
        public void StatsExposeDomainCountersAndMemory()
        {
            Call("Evaluate", new JObject { ["code"] = "1" });
            var stats = Call("GetStats", new JObject { ["collect"] = true });
            foreach (var key in new[] { "evaluations", "emittedAssemblies", "maxEmittedAssemblies", "cachedCompilations", "compileCacheHits",
                         "referenceCount", "referenceBuilds", "storedResults", "maxStoredResults", "loadedAssemblies", "managedMemoryBytes", "managedMemoryGrowthBytes" })
                Assert.That(stats[key]?.Type, Is.EqualTo(JTokenType.Integer), key + ": " + stats);
            Assert.That((int)stats["maxEmittedAssemblies"], Is.EqualTo(128));
            Assert.That((int)stats["maxStoredResults"], Is.EqualTo(256));
            Assert.That((int)stats["loadedAssemblies"], Is.GreaterThan((int)stats["emittedAssemblies"]));
            Assert.That(stats["warmedUp"].Type, Is.EqualTo(JTokenType.Boolean));
        }
    }
}
