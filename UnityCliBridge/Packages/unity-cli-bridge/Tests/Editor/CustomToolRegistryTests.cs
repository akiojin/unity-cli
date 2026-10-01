using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Core;
using UnityCliBridge.Models;
using UnityCliBridge.Tools;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.Editor
{
    public class CustomToolRegistryTests
    {
        [SetUp]
        public void ResetCounter() => Fixtures.Calls = 0;

        [Test]
        public void ListToolsReturnsRegistryWithoutInvokingUserCode()
        {
            var response = JObject.Parse(BridgeCommandRouter.Handle(new Command
            {
                Id = "list-custom-tools", Type = "list_tools", Parameters = new JObject()
            }).GetAwaiter().GetResult());
            Assert.AreEqual("success", (string)response["status"], response.ToString());
            Assert.IsInstanceOf<JArray>(response["result"]?["tools"]);
        }

        [Test]
        public void SchemaUsesParameterTypesDescriptionsAndCSharpDefaults()
        {
            var schema = Registry("Spawn").Describe().Single();
            Assert.AreEqual("custom", (string)schema["source"]);
            Assert.AreEqual("remote", (string)schema["executor"]);
            Assert.AreEqual(true, (bool)schema["mutating"]);
            Assert.AreEqual("Create a light", (string)schema["description"]);
            var args = schema["params_schema"];
            Assert.AreEqual("object", (string)args["type"]);
            Assert.AreEqual(false, (bool)args["additionalProperties"]);
            CollectionAssert.AreEqual(new[] { "name" }, args["required"].Values<string>());
            Assert.AreEqual("string", (string)args["properties"]["name"]["type"]);
            Assert.AreEqual("Object name", (string)args["properties"]["name"]["description"]);
            Assert.AreEqual(1f, (float)args["properties"]["intensity"]["default"]);
            Assert.AreEqual(0, Fixtures.Calls, "Discovery must never invoke user code");
        }

        [TestCase("{}")]
        [TestCase("{\"name\":7}")]
        [TestCase("{\"name\":null}")]
        [TestCase("{\"name\":\"Sun\",\"intensity\":\"bright\"}")]
        [TestCase("{\"name\":\"Sun\",\"intensity\":1e40}")]
        [TestCase("{\"name\":\"Sun\",\"extra\":true}")]
        public void InvalidArgumentsAreRejectedBeforeInvocation(string parameters)
        {
            var result = Invoke(Registry("Spawn"), "test_spawn", JObject.Parse(parameters));
            Assert.AreEqual("INVALID_ARGUMENT", (string)result["code"], result.ToString());
            Assert.AreEqual(0, Fixtures.Calls);
        }

        [Test]
        public void InvocationUsesDefaultAndRunsOnCallingMainThread()
        {
            var result = Invoke(Registry("Spawn"), "test_spawn", new JObject { ["name"] = "Sun" });
            Assert.AreEqual("success", (string)result["status"], result.ToString());
            Assert.AreEqual("Sun", (string)result["result"]["name"]);
            Assert.AreEqual(1f, (float)result["result"]["intensity"]);
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, (int)result["result"]["thread"]);
            Assert.AreEqual(1, Fixtures.Calls);
        }

        [Test]
        public void BackgroundInvocationIsRefused()
        {
            var registry = Registry("Spawn");
            var result = Task.Run(() => Invoke(registry, "test_spawn", new JObject { ["name"] = "Sun" })).Result;
            Assert.AreEqual("PRECONDITION_FAILED", (string)result["code"]);
            Assert.AreEqual(0, Fixtures.Calls);
        }

        [Test]
        public void OptionalNullEnumBooleanAndIntegerUseStrictTypes()
        {
            var registry = Registry("Types");
            var result = Invoke(registry, "test_types", new JObject());
            Assert.AreEqual("success", (string)result["status"]);
            Assert.AreEqual(JTokenType.Null, result["result"]["name"].Type);
            foreach (var parameters in new[] {
                "{\"count\":2147483648}", "{\"count\":1.5}", "{\"longCount\":9223372036854775808}",
                "{\"enabled\":1}", "{\"kind\":0}", "{\"kind\":\"point\"}", "{\"name\":12}"
            })
                Assert.AreEqual("INVALID_ARGUMENT", (string)Invoke(registry, "test_types", JObject.Parse(parameters))["code"], parameters);
            Assert.AreEqual(1, Fixtures.Calls);
            result = Invoke(registry, "test_types", new JObject { ["kind"] = "Point", ["name"] = JValue.CreateNull(), ["enabled"] = true });
            Assert.AreEqual("success", (string)result["status"]);
        }

        [Test]
        public void ExceptionsAndSerializationFailuresAreStructured()
        {
            foreach (var name in new[] { "Throws", "BadResult" })
            {
                var result = Invoke(Registry(name), "test_" + name.ToLowerInvariant(), new JObject());
                Assert.AreEqual("error", (string)result["status"]);
                Assert.AreEqual("CUSTOM_TOOL_FAILED", (string)result["code"], result.ToString());
            }
            Assert.AreEqual("success", (string)Invoke(Registry("Spawn"), "test_spawn", new JObject { ["name"] = "After" })["status"]);
        }

        [TestCase("Collision", "ping")]
        [TestCase("LocalCollision", "read")]
        public void BuiltinNamesAreRefusedWithConsoleWarning(string method, string name)
        {
            LogAssert.Expect(LogType.Warning, new Regex("Cannot register.*" + name + ".*reserved"));
            Assert.IsEmpty(Registry(method).Describe());
        }

        [Test]
        public void DuplicateNamesRefuseBothMethods()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Cannot register.*test_duplicate.*multiple"));
            Assert.IsEmpty(Registry("DuplicateOne", "DuplicateTwo").Describe());
        }

        [TestCase("Instance")]
        [TestCase("Private")]
        [TestCase("Generic")]
        [TestCase("Async")]
        [TestCase("Unsupported")]
        public void InvalidSignaturesAreRefusedWithWarning(string method)
        {
            LogAssert.Expect(LogType.Warning, new Regex("Cannot register"));
            Assert.IsEmpty(Registry(method).Describe());
        }

        [Test]
        public void UnknownNameIsNotHandled()
        {
            Assert.IsFalse(Registry("Spawn").TryHandle(new Command { Id = "unknown", Type = "not_registered" }, out _));
        }

        private static CustomToolRegistry Registry(params string[] methods) => new CustomToolRegistry(
            methods.Select(name => typeof(Fixtures).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)),
            BuiltinToolNames.Names);

        private static JObject Invoke(CustomToolRegistry registry, string name, JObject parameters)
        {
            Assert.IsTrue(registry.TryHandle(new Command { Id = "custom-test", Type = name, Parameters = parameters }, out var response));
            return JObject.Parse(response);
        }

        public class Fixtures
        {
            public static int Calls;
            public enum LightKind { Directional, Point }

            [UnityCliTool("test_spawn", Description = "Create a light")]
            public static object Spawn([UnityCliArg("Object name")] string name, float intensity = 1f)
            {
                Calls++;
                return new { name, intensity, thread = Thread.CurrentThread.ManagedThreadId };
            }

            [UnityCliTool("test_types", Mutating = false)]
            public static object Types(string name = null, int count = 0, long longCount = 0, bool enabled = false, LightKind kind = LightKind.Directional)
            {
                Calls++;
                return new { name, count, longCount, enabled, kind = kind.ToString() };
            }

            [UnityCliTool("test_throws")]
            public static object Throws() => throw new InvalidOperationException("custom failure");
            private class Cyclic { public Cyclic Self => this; }
            [UnityCliTool("test_badresult")]
            public static object BadResult() => new Cyclic();
            [UnityCliTool("ping")]
            public static void Collision() { }
            [UnityCliTool("read")]
            public static void LocalCollision() { }
            [UnityCliTool("test_duplicate")]
            public static void DuplicateOne() { }
            [UnityCliTool("test_duplicate")]
            public static void DuplicateTwo() { }
            [UnityCliTool("test_instance")]
            public void Instance() { }
            [UnityCliTool("test_private")]
            private static void Private() { }
            [UnityCliTool("test_generic")]
            public static void Generic<T>() { }
            [UnityCliTool("test_async")]
            public static Task Async() => Task.CompletedTask;
            [UnityCliTool("test_unsupported")]
            public static void Unsupported(GameObject gameObject) { }
        }
    }
}
