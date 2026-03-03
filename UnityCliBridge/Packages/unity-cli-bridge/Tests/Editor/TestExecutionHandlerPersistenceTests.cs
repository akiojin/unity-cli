using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor
{
    public class TestExecutionHandlerPersistenceTests
    {
        [TearDown]
        public void TearDown()
        {
            TestExecutionHandler.ResetForTesting();
            // Clear any persisted state
            InvokeClearRunState();
        }

        [Test]
        public void PersistedRunningState_IsReturnedWhenCollectorMissing()
        {
            // simulate a running state persisted to file (with recent lastUpdate)
            InvokeSaveRunState("running", null);

            // clear live state to mimic domain reload/connection drop
            SetField("isTestRunning", false);
            SetField("currentCollector", null);

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("running", result["status"]?.ToString());
            Assert.IsTrue(result["persisted"]?.ToObject<bool>() ?? false);
        }

        [Test]
        public void PersistedRunningState_ReturnsTimeout_WhenStale()
        {
            // Write a persisted state file with lastUpdate > 120s ago
            var statePath = GetRunStatePath();
            var dir = Path.GetDirectoryName(statePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var staleState = new
            {
                runId = "stale-run",
                testMode = "EditMode",
                status = "running",
                runStartedAt = DateTime.UtcNow.AddSeconds(-300),
                lastUpdate = DateTime.UtcNow.AddSeconds(-200),
                code = (string)null
            };
            File.WriteAllText(statePath, JsonConvert.SerializeObject(staleState));

            // clear live state
            SetField("isTestRunning", false);
            SetField("currentCollector", null);

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("error", result["status"]?.ToString(),
                "Stale persisted state should return error");
            Assert.AreEqual("RUNNER_TIMEOUT", result["code"]?.ToString());
            Assert.IsTrue(result["persisted"]?.ToObject<bool>() ?? false);

            // State file should be cleared
            Assert.IsFalse(File.Exists(statePath),
                "Stale state file should be deleted after timeout");
        }

        [Test]
        public void PersistedRunningState_WithNoLastUpdate_ReturnsTimeout()
        {
            // Write a persisted state file with null lastUpdate
            var statePath = GetRunStatePath();
            var dir = Path.GetDirectoryName(statePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var noUpdateState = new
            {
                runId = "no-update-run",
                testMode = "EditMode",
                status = "running",
                runStartedAt = DateTime.UtcNow.AddSeconds(-300),
                lastUpdate = (DateTime?)null,
                code = (string)null
            };
            File.WriteAllText(statePath, JsonConvert.SerializeObject(noUpdateState));

            SetField("isTestRunning", false);
            SetField("currentCollector", null);

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("error", result["status"]?.ToString(),
                "Persisted state with null lastUpdate should return error");
            Assert.AreEqual("RUNNER_TIMEOUT", result["code"]?.ToString());
        }

        [Test]
        public void SaveRunState_SetsLastUpdateToCurrentTime()
        {
            var before = DateTime.UtcNow;
            SetField("currentRunId", "save-test");
            SetField("currentTestMode", "EditMode");
            SetField("runStartedAtUtc", (DateTime?)before);
            SetField("runLastUpdateUtc", (DateTime?)before.AddSeconds(-30)); // old value

            InvokeSaveRunState("running", null);

            var statePath = GetRunStatePath();
            Assert.IsTrue(File.Exists(statePath));
            var json = File.ReadAllText(statePath);
            var state = JObject.Parse(json);

            var lastUpdate = state["lastUpdate"]?.ToObject<DateTime>();
            Assert.IsNotNull(lastUpdate, "lastUpdate should not be null");
            // lastUpdate should be close to now, not to the old runLastUpdateUtc
            var diff = (DateTime.UtcNow - lastUpdate.Value).TotalSeconds;
            Assert.Less(diff, 5, "lastUpdate should be close to current time, not stale runLastUpdateUtc");
        }

        private void SetField(string name, object value)
        {
            var f = typeof(TestExecutionHandler).GetField(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(null, value);
        }

        private void InvokeSaveRunState(string status, string code)
        {
            typeof(TestExecutionHandler).GetMethod("SaveRunState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { status, code });
        }

        private void InvokeClearRunState()
        {
            typeof(TestExecutionHandler).GetMethod("ClearRunState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, null);
        }

        private string GetRunStatePath()
        {
            var prop = typeof(TestExecutionHandler).GetProperty("RunStatePath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (string)prop?.GetValue(null);
        }
    }
}
