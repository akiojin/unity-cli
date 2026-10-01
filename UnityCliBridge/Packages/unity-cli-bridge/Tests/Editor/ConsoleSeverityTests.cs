using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor
{
    public class ConsoleSeverityTests
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Entries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
        private int previousFlags;
        private string previousFilter;

        [SetUp]
        public void SetUp()
        {
            previousFlags = (int)Entries.GetProperty("consoleFlags", StaticFlags).GetValue(null);
            previousFilter = (string)Entries.GetMethod("GetFilteringText", StaticFlags).Invoke(null, null);
            Entries.GetProperty("consoleFlags", StaticFlags).SetValue(null, (previousFlags | (7 << 7)) & ~1);
            Entries.GetMethod("SetFilteringText", StaticFlags).Invoke(null, new object[] { "" });
            // Initialize the handler before clearing its reflection initialization log.
            Read();
            Entries.GetMethod("Clear", StaticFlags).Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            Entries.GetMethod("Clear", StaticFlags).Invoke(null, null);
            Entries.GetProperty("consoleFlags", StaticFlags).SetValue(null, previousFlags);
            Entries.GetMethod("SetFilteringText", StaticFlags).Invoke(null, new object[] { previousFilter });
        }

        [TestCase(1 << 0, "Error")]
        [TestCase(1 << 1, "Assert")]
        [TestCase(1 << 2, "Log")]
        [TestCase(1 << 4, "Error")]
        [TestCase(1 << 6, "Error")]
        [TestCase(1 << 7, "Warning")]
        [TestCase(1 << 8, "Error")]
        [TestCase(1 << 9, "Warning")]
        [TestCase(1 << 10, "Log")]
        [TestCase(1 << 11, "Error")]
        [TestCase(1 << 12, "Warning")]
        [TestCase(1 << 17, "Exception")]
        [TestCase(1 << 21, "Assert")]
        [TestCase((1 << 9) | (1 << 18), "Warning")]
        [TestCase((1 << 10) | (1 << 22), "Log")]
        public void Mode_UsesUnitySeverityBits(int mode, string expected)
        {
            var classify = typeof(ConsoleHandler).GetMethod("GetLogTypeFromMode", StaticFlags);
            var actual = classify.Invoke(null, new object[] { mode });
            Assert.AreEqual(expected, actual.ToString());
        }

        [Test]
        public void ReadConsole_NativeWarnings_AreNotErrors()
        {
            AddNativeEntry(1 << 9, "Metal: native warning (Assertion Error Exception Warning)");
            AddNativeEntry(1 << 7, "Native importer warning");
            var read = Read();
            Assert.AreEqual(2, read["statistics"]["warnings"].Value<int>());
            Assert.AreEqual(0, read["statistics"]["errors"].Value<int>());
            foreach (var entry in read["logs"])
                Assert.AreEqual("Warning", entry["logType"].Value<string>());
            AssertCountsMatch(read);
        }

        [TestCase(100)]
        [TestCase(1)]
        public void SeverityCounts_MatchUnity_ForManagedAndNativeEntries(int limit)
        {
            Debug.Log("managed log");
            Debug.LogWarning("managed warning");
            LogAssert.Expect(LogType.Error, "managed error");
            Debug.LogError("managed error");
            LogAssert.Expect(LogType.Assert, "managed assertion");
            Debug.LogAssertion("managed assertion");
            LogAssert.Expect(LogType.Exception, new Regex("Exception: managed exception"));
            Debug.LogException(new Exception("managed exception"));
            AddNativeEntry(1 << 2, "native log");
            AddNativeEntry(1 << 7, "native warning");
            AddNativeEntry(1 << 0, "native error");
            AssertCountsMatch(Read(limit));
        }

        [Test]
        public void CompilationCounts_UseCompilerFlags_WithoutMessageHeuristics()
        {
            AddNativeEntry(1 << 11, "compiler diagnostic one");
            AddNativeEntry(1 << 12, "compiler diagnostic two");
            AddNativeEntry(1 << 12, "compiler diagnostic three");
            var state = JObject.FromObject(CompilationHandler.GetCompilationState(new JObject()));
            Assert.AreEqual(1, state["errorCount"].Value<int>());
            Assert.AreEqual(2, state["warningCount"].Value<int>());
            AssertCountsMatch(Read());
        }

        private static JObject Read(int count = 100) => JObject.FromObject(ConsoleHandler.ReadConsole(
            new JObject { ["count"] = count, ["format"] = "detailed" }));

        private static void AssertCountsMatch(JObject read)
        {
            object[] counts = { 0, 0, 0 };
            Entries.GetMethod("GetCountsByType", StaticFlags).Invoke(null, counts);
            var stats = read["statistics"];
            Assert.IsTrue(read["success"].Value<bool>(), read.ToString());
            Assert.AreEqual(counts[0], stats["errors"].Value<int>() + stats["asserts"].Value<int>() + stats["exceptions"].Value<int>(), "Console error bucket");
            Assert.AreEqual(counts[1], stats["warnings"].Value<int>(), "Console warning bucket");
            Assert.AreEqual(counts[2], stats["logs"].Value<int>(), "Console log bucket");
            var state = JObject.FromObject(CompilationHandler.GetCompilationState(new JObject()));
            Assert.AreEqual(counts[0], state["consoleErrorCount"].Value<int>());
            Assert.AreEqual(counts[1], state["consoleWarningCount"].Value<int>());
            TestContext.Out.WriteLine($"Unity {Application.unityVersion}: native counts={string.Join(",", counts)}; read_console={stats}; compilation={state}");
        }

        private static void AddNativeEntry(int mode, string message)
        {
            var type = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
            var entry = Activator.CreateInstance(type);
            type.GetField("mode").SetValue(entry, mode);
            type.GetField("message").SetValue(entry, message);
            Entries.GetMethod("AddMessageWithDoubleClickCallback", StaticFlags).Invoke(null, new[] { entry });
        }
    }
}
