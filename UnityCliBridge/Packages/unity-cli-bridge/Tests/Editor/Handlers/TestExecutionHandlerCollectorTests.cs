using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class TestExecutionHandlerCollectorTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly Dictionary<FieldInfo, object> savedFields = new Dictionary<FieldInfo, object>();
        private Type collectorType;
        private Assembly runnerAssembly;
        private object collector;
        private string exportPath;
        private string runStatePath;
        private byte[] savedRunState;

        [SetUp]
        public void SetUp()
        {
            var handler = typeof(TestExecutionHandler);
            foreach (var name in new[] { "currentCollector", "isTestRunning", "runLastUpdateUtc", "lastResultPath", "lastResultSummary", "lastResultTimestampUtc" })
            {
                var field = handler.GetField(name, StaticFlags);
                Assert.NotNull(field, name);
                savedFields.Add(field, field.GetValue(null));
            }

            runStatePath = (string)handler.GetProperty("RunStatePath", StaticFlags).GetValue(null);
            savedRunState = File.Exists(runStatePath) ? File.ReadAllBytes(runStatePath) : null;
            exportPath = Path.Combine(Path.GetTempPath(), "unity-cli-collector-" + Guid.NewGuid().ToString("N") + ".json");
            collectorType = handler.GetNestedType("TestResultCollector", BindingFlags.NonPublic);
            Assert.NotNull(collectorType);
            runnerAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "UnityEditor.TestRunner");
            collector = Activator.CreateInstance(collectorType, InstanceFlags, null, new object[] { exportPath, true, "PlayMode" }, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in savedFields)
            {
                pair.Key.SetValue(null, pair.Value);
            }
            savedFields.Clear();

            if (runStatePath != null)
            {
                if (savedRunState != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(runStatePath));
                    File.WriteAllBytes(runStatePath, savedRunState);
                }
                else if (File.Exists(runStatePath))
                {
                    File.Delete(runStatePath);
                }
            }

            if (exportPath != null && File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }

        [Test]
        public void TwoLeavesAndThreeSuites_ReportOnlyTwoTestsInStatusAndExport()
        {
            var root = new TestSuite("Root");
            var assembly = new TestSuite("Assembly");
            var fixture = new TestSuite("Fixture");
            var first = CreateLeaf(nameof(FirstLeaf));
            var second = CreateLeaf(nameof(SecondLeaf));
            root.Add(assembly);
            assembly.Add(fixture);
            fixture.Add(first);
            fixture.Add(second);

            Invoke("RunStarted", AdaptTest(root));
            foreach (var test in new Test[] { first, second, fixture, assembly, root })
            {
                Invoke("TestFinished", AdaptResult(test, ResultState.Success));
            }

            typeof(TestExecutionHandler).GetField("currentCollector", StaticFlags).SetValue(null, collector);
            typeof(TestExecutionHandler).GetField("isTestRunning", StaticFlags).SetValue(null, false);
            var status = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject()));
            Assert.AreEqual("completed", status.Value<string>("status"));
            Assert.AreEqual(2, status.Value<int>("totalTests"));
            Assert.AreEqual(2, status.Value<int>("passedTests"));
            Assert.AreEqual(0, status.Value<int>("failedTests"));
            Assert.AreEqual(0, status.Value<int>("skippedTests"));
            Assert.AreEqual(0, status.Value<int>("inconclusiveTests"));
            CollectionAssert.AreEquivalent(new[] { first.FullName, second.FullName }, status["tests"].Select(test => test.Value<string>("fullName")));

            // Export directly so the regression does not alter the enclosing runner's PlayMode options.
            Invoke("ExportResults", AdaptResult(root, ResultState.Success));
            var exported = JObject.Parse(File.ReadAllText(exportPath));
            Assert.AreEqual(2, exported.Value<int>("totalTests"));
            Assert.AreEqual(2, exported.Value<int>("passed"));
            Assert.AreEqual(0, exported.Value<int>("failed"));
            Assert.AreEqual(0, exported.Value<int>("skipped"));
            Assert.AreEqual(0, exported.Value<int>("inconclusive"));
            CollectionAssert.AreEquivalent(new[] { first.FullName, second.FullName }, exported["tests"].Select(test => test.Value<string>("fullName")));
        }

        [Test]
        public void FailedSuiteAfterTwoPassedLeaves_PreservesRunFailureWithoutIncreasingTestCounts()
        {
            const string teardownMessage = "OneTimeTearDown: fixture cleanup failed";
            var root = new TestSuite("Root");
            var fixture = new TestSuite("Fixture");
            var first = CreateLeaf(nameof(FirstLeaf));
            var second = CreateLeaf(nameof(SecondLeaf));
            root.Add(fixture);
            fixture.Add(first);
            fixture.Add(second);

            Invoke("RunStarted", AdaptTest(root));
            Invoke("TestFinished", AdaptResult(first, ResultState.Success));
            Invoke("TestFinished", AdaptResult(second, ResultState.Success));
            Invoke("TestFinished", AdaptResult(fixture, ResultState.Failure, teardownMessage));
            var failedRoot = AdaptResult(root, ResultState.Failure, "One or more child suites failed");
            Invoke("TestFinished", failedRoot);

            typeof(TestExecutionHandler).GetField("currentCollector", StaticFlags).SetValue(null, collector);
            typeof(TestExecutionHandler).GetField("isTestRunning", StaticFlags).SetValue(null, false);
            var status = JObject.FromObject(TestExecutionHandler.GetTestStatus(new JObject()));
            Assert.AreEqual("completed", status.Value<string>("status"));
            Assert.AreEqual(2, status.Value<int>("totalTests"));
            Assert.AreEqual(2, status.Value<int>("passedTests"));
            Assert.AreEqual(0, status.Value<int>("failedTests"));
            Assert.IsFalse(status.Value<bool>("success"), "A failed fixture cleanup must fail the run even when both test cases passed.");
            Assert.IsTrue(status["failures"].Any(failure => failure.Value<string>("testName") == fixture.FullName && failure.Value<string>("message") == teardownMessage));
            CollectionAssert.AreEquivalent(new[] { first.FullName, second.FullName }, status["tests"].Select(test => test.Value<string>("fullName")));

            Invoke("ExportResults", failedRoot);
            var exported = JObject.Parse(File.ReadAllText(exportPath));
            Assert.AreEqual(2, exported.Value<int>("totalTests"));
            Assert.AreEqual(2, exported.Value<int>("passed"));
            Assert.AreEqual(0, exported.Value<int>("failed"));
            Assert.AreEqual("failed", exported.Value<string>("status"));
            Assert.IsTrue(exported["failures"].Any(failure => failure.Value<string>("fullName") == fixture.FullName && failure.Value<string>("message") == teardownMessage));
            CollectionAssert.AreEquivalent(new[] { first.FullName, second.FullName }, exported["tests"].Select(test => test.Value<string>("fullName")));
        }

        [TestCase(TestStatus.Passed, "PassedTests")]
        [TestCase(TestStatus.Failed, "FailedTests")]
        [TestCase(TestStatus.Skipped, "SkippedTests")]
        [TestCase(TestStatus.Inconclusive, "InconclusiveTests")]
        public void SuiteStatus_IsIgnoredWhileLeafStatusIsRetained(TestStatus status, string bucket)
        {
            var suite = new TestSuite("Fixture");
            var leaf = CreateLeaf(nameof(FirstLeaf));
            suite.Add(leaf);
            var state = new ResultState(status);
            Invoke("RunStarted", AdaptTest(suite));

            Invoke("TestFinished", AdaptResult(suite, state));
            Assert.AreEqual(0, Results("AllResults").Count, "Suite callbacks must not become test results.");
            Assert.AreEqual(0, Results(bucket).Count);

            Invoke("TestFinished", AdaptResult(leaf, state));
            Assert.AreEqual(1, Results("AllResults").Count);
            Assert.AreEqual(1, Results(bucket).Count);
            var retained = (TestExecutionHandler.TestResultData)Results(bucket)[0];
            Assert.AreEqual(leaf.FullName, retained.fullName);
            Assert.AreEqual(status.ToString(), retained.status);
        }

        [Test]
        public void EmptySuite_HasZeroTestsAndNoResults()
        {
            var suite = new TestSuite("EmptyFixture");
            Invoke("RunStarted", AdaptTest(suite));
            Invoke("TestFinished", AdaptResult(suite, ResultState.Success));
            Assert.AreEqual(0, collectorType.GetProperty("TotalTests").GetValue(collector));
            Assert.AreEqual(0, Results("AllResults").Count);
            Assert.AreEqual(0, Results("PassedTests").Count);
        }

        private static TestMethod CreateLeaf(string name)
        {
            return new TestMethod(new MethodWrapper(typeof(TestExecutionHandlerCollectorTests), name));
        }

        // These methods are NUnit model inputs, not independently discovered tests.
        public void FirstLeaf() { }
        public void SecondLeaf() { }

        private object AdaptTest(ITest test)
        {
            var childCount = test.HasChildren ? test.Tests.Count : 0;
            var children = Array.CreateInstance(runnerAssembly.GetType("UnityEditor.TestTools.TestRunner.Api.ITestAdaptor", true), childCount);
            for (var index = 0; index < childCount; index++)
            {
                children.SetValue(AdaptTest(test.Tests[index]), index);
            }

            return Activator.CreateInstance(runnerAssembly.GetType("UnityEditor.TestTools.TestRunner.Api.TestAdaptor", true), InstanceFlags, null, new object[] { test, children }, null);
        }

        private object AdaptResult(Test test, ResultState state, string message = null)
        {
            var result = test.MakeTestResult();
            result.SetResult(state, message);
            return Activator.CreateInstance(runnerAssembly.GetType("UnityEditor.TestTools.TestRunner.Api.TestResultAdaptor", true), InstanceFlags, null, new object[] { result, AdaptTest(test), null }, null);
        }

        private void Invoke(string method, object argument)
        {
            collectorType.GetMethod(method, InstanceFlags).Invoke(collector, new[] { argument });
        }

        private IList Results(string property)
        {
            return (IList)collectorType.GetProperty(property).GetValue(collector);
        }
    }
}
