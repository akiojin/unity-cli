using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor
{
    public class TestExecutionHandlerReloadTests
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private const string SessionKey = "UnityCliBridge.TestExecution.EditorSessionId";
        private readonly Dictionary<FieldInfo, object> originalFields = new Dictionary<FieldInfo, object>();
        private string statePath;
        private byte[] originalState;
        private string originalSession;
        private string exportPath;

        [SetUp]
        public void SetUp()
        {
            foreach (var field in typeof(TestExecutionHandler).GetFields(StaticFlags))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                    originalFields[field] = field.GetValue(null);
            }
            statePath = (string)typeof(TestExecutionHandler).GetProperty("RunStatePath", StaticFlags).GetValue(null);
            originalState = File.Exists(statePath) ? File.ReadAllBytes(statePath) : null;
            originalSession = SessionState.GetString(SessionKey, "");
            exportPath = Path.Combine(Path.GetTempPath(), "unity-cli-reload-" + Guid.NewGuid().ToString("N") + ".json");
            ClearLiveState();
        }

        [TearDown]
        public void TearDown()
        {
            var api = GetField("testRunnerApi") as TestRunnerApi;
            var collector = GetField("currentCollector") as ICallbacks;
            if (api != null && collector != null)
                api.UnregisterCallbacks(collector);

            var originalApi = originalFields[typeof(TestExecutionHandler).GetField("testRunnerApi", StaticFlags)];
            if (api != null && !ReferenceEquals(api, originalApi))
                UnityEngine.Object.DestroyImmediate(api);
            foreach (var item in originalFields)
                item.Key.SetValue(null, item.Value);
            originalFields.Clear();

            if (originalState != null)
                File.WriteAllBytes(statePath, originalState);
            else if (File.Exists(statePath))
                File.Delete(statePath);
            SessionState.SetString(SessionKey, originalSession);
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }

        [Test]
        public void RunningRun_RestoresCollectorAndExportOptions_AfterReload()
        {
            SaveRunningRun();
            ClearLiveState();

            Invoke("RestoreRunAfterReload");

            var collector = GetField("currentCollector");
            Assert.IsNotNull(collector, "Domain reload must recreate the test callback collector.");
            Assert.IsNotNull(GetField("testRunnerApi"));
            Assert.AreEqual(exportPath, GetCollectorField(collector, "exportPath"));
            Assert.AreEqual(true, GetCollectorField(collector, "includeDetailsInFile"));
            var status = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject()));
            Assert.AreEqual("running", status["status"]?.ToString());
            Assert.AreEqual("reload-run", status["runId"]?.ToString());
            Assert.AreEqual("PlayMode", status["testMode"]?.ToString());

            Invoke("RestoreRunAfterReload");
            Assert.AreSame(collector, GetField("currentCollector"), "Repeated initialization must not replace the registered collector.");
        }

        [Test]
        public void CompletedRun_PreservesStatusAndExportedResults_AfterReload()
        {
            SaveRunningRun();
            var collector = GetField("currentCollector");
            var result = new TestExecutionHandler.TestResultData
            {
                name = "ReloadProbe",
                fullName = "ReloadTests.ReloadProbe",
                status = "Passed",
                duration = 0.25
            };
            ((List<TestExecutionHandler.TestResultData>)collector.GetType().GetProperty("PassedTests").GetValue(collector)).Add(result);
            ((List<TestExecutionHandler.TestResultData>)collector.GetType().GetProperty("AllResults").GetValue(collector)).Add(result);
            collector.GetType().GetProperty("TotalTests").GetSetMethod(true).Invoke(collector, new object[] { 1 });
            ((ICallbacks)collector).RunFinished(null);
            ClearLiveState();

            Invoke("RestoreRunAfterReload");

            var status = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject { ["includeTestResults"] = true }));
            Assert.AreEqual("completed", status["status"]?.ToString());
            Assert.AreEqual("reload-run", status["runId"]?.ToString());
            Assert.AreEqual(1, status["totalTests"]?.ToObject<int>());
            Assert.AreEqual(1, status["passedTests"]?.ToObject<int>());
            Assert.AreEqual("ReloadTests.ReloadProbe", status["tests"]?[0]?["fullName"]?.ToString());
            Assert.AreEqual("available", status["latestResult"]?["status"]?.ToString());

            var exported = JObject.FromObject(TestExecutionHandler.GetLastTestResults(new JObject()));
            Assert.AreEqual("available", exported["status"]?.ToString());
            Assert.AreEqual(exportPath, exported["path"]?.ToString());
            Assert.AreEqual(1, exported["summary"]?["passed"]?.ToObject<int>());
            Assert.IsNotEmpty(exported["fileContent"]?.ToString());

            // Polling once must not erase durable completion before another reload.
            ClearLiveState();
            Invoke("RestoreRunAfterReload");
            var repeated = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject()));
            Assert.AreEqual("completed", repeated["status"]?.ToString());
            Assert.AreEqual("reload-run", repeated["runId"]?.ToString());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RunningRun_DoesNotAttachToUnrelatedOrStaleRun(bool unrelatedSession)
        {
            SaveRunningRun();
            var state = JObject.Parse(File.ReadAllText(statePath));
            if (unrelatedSession)
                state["editorSessionId"] = "another-editor-session";
            else
                state["lastUpdate"] = DateTime.UtcNow.AddSeconds(-130);
            File.WriteAllText(statePath, state.ToString());
            ClearLiveState();

            Invoke("RestoreRunAfterReload");

            Assert.IsNull(GetField("currentCollector"));
            Assert.AreEqual(false, GetField("isTestRunning"));
            Assert.IsNull(GetField("testRunnerApi"));
        }

        [Test]
        public void FinalSuiteFailure_RemainsFailed_WhenAllLeafTestsPassed()
        {
            SaveRunningRun();
            var leaf = new SyntheticResult
            {
                Test = new SyntheticTest { Name = "PassingLeaf", IsSuite = false },
                TestStatus = TestStatus.Passed
            };
            var suite = new SyntheticResult
            {
                Test = new SyntheticTest { Name = "TeardownFixture", IsSuite = true, ChildTests = new[] { leaf.Test } },
                TestStatus = TestStatus.Failed,
                Message = "OneTimeTearDown: teardown diagnostic",
                StackTrace = "at TeardownFixture.Cleanup()",
                ChildResults = new[] { leaf }
            };
            ((ICallbacks)GetField("currentCollector")).RunFinished(suite);
            ClearLiveState();
            Invoke("RestoreRunAfterReload");

            var status = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject()));
            Assert.AreEqual("completed", status["status"]?.ToString());
            Assert.AreEqual(false, status["success"]?.ToObject<bool>(),
                "A failing suite teardown must fail the run even when every leaf passed.");
            Assert.AreEqual(1, status["totalTests"]?.ToObject<int>());
            Assert.AreEqual(1, status["passedTests"]?.ToObject<int>());
            Assert.AreEqual(0, status["failedTests"]?.ToObject<int>(), "Counts represent leaf tests only.");
            StringAssert.Contains("teardown diagnostic", status["failures"]?.ToString());
            StringAssert.Contains("TeardownFixture.Cleanup", status["failures"]?.ToString());

            var exported = JObject.FromObject(TestExecutionHandler.GetLastTestResults(new JObject()));
            Assert.AreEqual("failed", exported["summary"]?["status"]?.ToString());
            Assert.AreEqual(0, exported["summary"]?["failed"]?.ToObject<int>());
            StringAssert.Contains("teardown diagnostic", exported["summary"]?["failures"]?.ToString());
        }

        // Minimal result tree through UTF's public callback contracts: a suite can
        // fail independently of its leaves (for example in OneTimeTearDown).
        private sealed class SyntheticResult : ITestResultAdaptor
        {
            public ITestAdaptor Test { get; set; }
            public string Name => Test.Name;
            public string FullName => Test.FullName;
            public string ResultState => TestStatus.ToString();
            public TestStatus TestStatus { get; set; }
            public double Duration => 0.25;
            public DateTime StartTime => DateTime.UtcNow.AddSeconds(-1);
            public DateTime EndTime => DateTime.UtcNow;
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public int AssertCount => 0;
            public int FailCount => 0;
            public int PassCount => 1;
            public int SkipCount => 0;
            public int InconclusiveCount => 0;
            public SyntheticResult[] ChildResults { get; set; } = new SyntheticResult[0];
            public bool HasChildren => ChildResults.Length > 0;
            public IEnumerable<ITestResultAdaptor> Children => ChildResults;
            public string Output => "";
            public NUnit.Framework.Interfaces.TNode ToXml() => new NUnit.Framework.Interfaces.TNode("test-result");
        }

        private sealed class SyntheticTest : ITestAdaptor
        {
            public string Id => Name;
            public string Name { get; set; }
            public string FullName => "ReloadTests." + Name;
            public int TestCaseCount => 1;
            public ITestAdaptor[] ChildTests { get; set; } = new ITestAdaptor[0];
            public bool HasChildren => ChildTests.Length > 0;
            public bool IsSuite { get; set; }
            public IEnumerable<ITestAdaptor> Children => ChildTests;
            public ITestAdaptor Parent => null;
            public int TestCaseTimeout => 0;
            public NUnit.Framework.Interfaces.ITypeInfo TypeInfo => null;
            public NUnit.Framework.Interfaces.IMethodInfo Method => null;
            public object[] Arguments => new object[0];
            public string[] Categories => new string[0];
            public bool IsTestAssembly => false;
            public UnityEditor.TestTools.TestRunner.Api.RunState RunState => UnityEditor.TestTools.TestRunner.Api.RunState.Runnable;
            public string Description => "";
            public string SkipReason => "";
            public string ParentId => null;
            public string ParentFullName => null;
            public string UniqueName => FullName;
            public string ParentUniqueName => null;
            public int ChildIndex => 0;
            public TestMode TestMode => TestMode.PlayMode;
        }

        private void SaveRunningRun()
        {
            var collectorType = typeof(TestExecutionHandler).GetNestedType("TestResultCollector", BindingFlags.NonPublic);
            var collector = Activator.CreateInstance(collectorType, new object[] { exportPath, true, "PlayMode" });
            SetField("currentCollector", collector);
            SetField("currentRunId", "reload-run");
            SetField("currentTestMode", "PlayMode");
            SetField("runStartedAtUtc", DateTime.UtcNow);
            SetField("runLastUpdateUtc", DateTime.UtcNow);
            SetField("isTestRunning", true);
            Invoke("SaveRunState", "running", null);
        }

        private void ClearLiveState()
        {
            foreach (var name in new[] { "testRunnerApi", "currentCollector", "currentRunId", "currentTestMode",
                         "runStartedAtUtc", "runLastUpdateUtc", "lastResultPath", "lastResultSummary", "lastResultTimestampUtc", "persistedCompletedResult" })
            {
                // persistedCompletedResult is introduced with durable completion restoration.
                typeof(TestExecutionHandler).GetField(name, StaticFlags)?.SetValue(null, null);
            }
            SetField("isTestRunning", false);
        }

        private static object GetCollectorField(object collector, string name) =>
            collector.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(collector);

        private static object GetField(string name) => typeof(TestExecutionHandler).GetField(name, StaticFlags).GetValue(null);

        private static void SetField(string name, object value) => typeof(TestExecutionHandler).GetField(name, StaticFlags).SetValue(null, value);

        private static void Invoke(string name, params object[] arguments)
        {
            var method = typeof(TestExecutionHandler).GetMethod(name, StaticFlags);
            Assert.IsNotNull(method, "Missing reload lifecycle method: " + name);
            method.Invoke(null, arguments);
        }
    }
}
