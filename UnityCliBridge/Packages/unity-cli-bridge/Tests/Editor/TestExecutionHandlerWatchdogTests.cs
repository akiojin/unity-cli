using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor
{
    public class TestExecutionHandlerWatchdogTests
    {
        private readonly Type _handlerType = typeof(TestExecutionHandler);

        [TearDown]
        public void TearDown()
        {
            // Clean up static state to avoid pollution
            SetPrivateStaticField("isTestRunning", false);
            SetPrivateStaticField("currentCollector", null);
            SetPrivateStaticField("currentTestMode", null);
            SetPrivateStaticField("currentRunId", null);
            SetPrivateStaticField("runStartedAtUtc", (DateTime?)null);
            SetPrivateStaticField("runLastUpdateUtc", (DateTime?)null);
            // Restore default detectors
            TestExecutionHandler.ResetForTesting();
        }

        [Test]
        public void Watchdog_Triggers_WhenNotPlayingAndStale()
        {
            // Simulate running test that stopped updating 15s ago with PlayMode already false
            SetPrivateStaticField("isTestRunning", true);
            SetPrivateStaticField("currentTestMode", "PlayMode");
            SetPrivateStaticField("currentRunId", "watchdog-run");
            var now = DateTime.UtcNow;
            SetPrivateStaticField("runStartedAtUtc", now.AddSeconds(-20));
            SetPrivateStaticField("runLastUpdateUtc", now.AddSeconds(-15));
            TestExecutionHandler.PlayModeDetector = () => false; // force not playing

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("error", result["status"]?.ToString());
            Assert.AreEqual("RUNNER_TIMEOUT", result["code"]?.ToString());
        }

        [Test]
        public void Watchdog_DoesNotTrigger_WhenRecentUpdate()
        {
            SetPrivateStaticField("isTestRunning", true);
            SetPrivateStaticField("currentTestMode", "PlayMode");
            SetPrivateStaticField("currentRunId", "running-run");
            var now = DateTime.UtcNow;
            SetPrivateStaticField("runStartedAtUtc", now.AddSeconds(-5));
            SetPrivateStaticField("runLastUpdateUtc", now.AddSeconds(-2));
            TestExecutionHandler.PlayModeDetector = () => false; // not playing

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("running", result["status"]?.ToString());
            Assert.AreEqual("running-run", result["runId"]?.ToString());
        }

        private void SetPrivateStaticField(string name, object value)
        {
            var f = _handlerType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (f == null)
            {
                Assert.Fail($"Field {name} not found on TestExecutionHandler.");
            }
            f.SetValue(null, value);
        }
    }
}
