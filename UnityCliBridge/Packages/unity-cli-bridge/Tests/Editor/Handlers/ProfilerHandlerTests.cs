using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class ProfilerHandlerTests
    {
        [TearDown]
        public void TearDown()
        {
            ProfilerHandler.Stop(new JObject());
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void Start_ShouldRejectInvalidMode()
        {
            var result = ToJObject(ProfilerHandler.Start(new JObject
            {
                ["mode"] = "invalid"
            }));

            Assert.AreEqual("E_INVALID_MODE", result.Value<string>("code"));
            Assert.IsNotNull(result.Value<string>("error"));
        }

        [Test]
        public void Metrics_ShouldListAvailableAndRequireMetricNamesForCurrentValues()
        {
            var list = ToJObject(ProfilerHandler.GetAvailableMetrics(new JObject
            {
                ["listAvailable"] = true
            }));

            Assert.IsNull(list["error"]?.Value<string>());
            Assert.IsNotNull(list["categories"]?["Memory"]);
            Assert.IsNotNull(list["categories"]?["Rendering"]);

            var missing = ToJObject(ProfilerHandler.GetAvailableMetrics(new JObject()));

            Assert.AreEqual("E_INVALID_PARAMETER", missing.Value<string>("code"));
            StringAssert.Contains("No metrics specified", missing.Value<string>("error"));
        }

        [Test]
        public void StartStatusStop_ShouldManageSingleSession()
        {
            var start = ToJObject(ProfilerHandler.Start(new JObject
            {
                ["recordToFile"] = false,
                ["maxDurationSec"] = 30
            }));

            Assert.IsNull(start["error"]?.Value<string>());
            Assert.IsTrue(start.Value<bool>("isRecording"));
            var sessionId = start.Value<string>("sessionId");
            Assert.IsNotEmpty(sessionId);

            var duplicate = ToJObject(ProfilerHandler.Start(new JObject
            {
                ["recordToFile"] = false
            }));
            Assert.AreEqual("E_ALREADY_RUNNING", duplicate.Value<string>("code"));
            Assert.AreEqual(sessionId, duplicate.Value<string>("sessionId"));

            var status = ToJObject(ProfilerHandler.GetStatus(new JObject()));
            Assert.IsTrue(status.Value<bool>("isRecording"));
            Assert.AreEqual(sessionId, status.Value<string>("sessionId"));
            Assert.IsNotNull(status["remainingSec"]);

            var stop = ToJObject(ProfilerHandler.Stop(new JObject
            {
                ["sessionId"] = sessionId
            }));
            Assert.IsNull(stop["error"]?.Value<string>());
            Assert.AreEqual(sessionId, stop.Value<string>("sessionId"));
            Assert.IsNull(stop["outputPath"]?.Type == JTokenType.Null ? null : stop["outputPath"]);
            Assert.GreaterOrEqual(stop.Value<double>("duration"), 0);

            var idle = ToJObject(ProfilerHandler.GetStatus(new JObject()));
            Assert.IsFalse(idle.Value<bool>("isRecording"));
        }
    }
}
