using System;
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
        }

        [Test]
        public void PersistedRunningState_IsReturnedWhenCollectorMissing()
        {
            // simulate a running state persisted to file
            typeof(TestExecutionHandler).GetMethod("SaveRunState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { "running", null });

            // clear live state to mimic domain reload/connection drop
            typeof(TestExecutionHandler).GetField("isTestRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, false);
            typeof(TestExecutionHandler).GetField("currentCollector", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, null);

            var resultObj = TestExecutionHandler.GetTestStatus(new JObject());
            var result = JObject.FromObject(resultObj);

            Assert.AreEqual("running", result["status"]?.ToString());
            Assert.IsTrue(result["persisted"]?.ToObject<bool>() ?? false);
        }
    }
}
