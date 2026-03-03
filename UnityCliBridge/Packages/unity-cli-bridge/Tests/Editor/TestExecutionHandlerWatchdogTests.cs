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
        public void Watchdog_Triggers_WhenPlayModeNotPlayingAndStale()
        {
            // Simulate PlayMode test that stopped updating 15s ago with PlayMode already false
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
            StringAssert.Contains("PlayMode", result["message"]?.ToString());
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

        [Test]
        public void Watchdog_EditMode_DoesNotTrigger_Before60Seconds()
        {
            // EditMode tests should NOT trigger watchdog at 15s (only at >60s)
            SetPrivateStaticField("isTestRunning", true);
            SetPrivateStaticField("currentTestMode", "EditMode");
            SetPrivateStaticField("currentRunId", "editmode-run");
            var now = DateTime.UtcNow;
            SetPrivateStaticField("runStartedAtUtc", now.AddSeconds(-20));
            SetPrivateStaticField("runLastUpdateUtc", now.AddSeconds(-15));
            TestExecutionHandler.PlayModeDetector = () => false;

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("running", result["status"]?.ToString(),
                "EditMode tests should not trigger watchdog at 15s elapsed");
        }

        [Test]
        public void Watchdog_EditMode_Triggers_After60Seconds()
        {
            // EditMode tests SHOULD trigger watchdog after >60s
            SetPrivateStaticField("isTestRunning", true);
            SetPrivateStaticField("currentTestMode", "EditMode");
            SetPrivateStaticField("currentRunId", "editmode-stuck");
            var now = DateTime.UtcNow;
            SetPrivateStaticField("runStartedAtUtc", now.AddSeconds(-90));
            SetPrivateStaticField("runLastUpdateUtc", now.AddSeconds(-65));
            TestExecutionHandler.PlayModeDetector = () => false;

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("error", result["status"]?.ToString());
            Assert.AreEqual("RUNNER_TIMEOUT", result["code"]?.ToString());
            StringAssert.Contains("EditMode", result["message"]?.ToString());
        }

        [Test]
        public void Watchdog_General_Triggers_After120Seconds()
        {
            // Any test mode should trigger general watchdog after >120s
            SetPrivateStaticField("isTestRunning", true);
            SetPrivateStaticField("currentTestMode", "All");
            SetPrivateStaticField("currentRunId", "general-stuck");
            var now = DateTime.UtcNow;
            SetPrivateStaticField("runStartedAtUtc", now.AddSeconds(-150));
            SetPrivateStaticField("runLastUpdateUtc", now.AddSeconds(-125));
            TestExecutionHandler.PlayModeDetector = () => true; // still playing

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("error", result["status"]?.ToString());
            Assert.AreEqual("RUNNER_TIMEOUT", result["code"]?.ToString());
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
