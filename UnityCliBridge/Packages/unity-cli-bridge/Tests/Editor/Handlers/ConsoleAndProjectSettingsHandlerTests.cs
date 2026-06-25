using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class ConsoleAndProjectSettingsHandlerTests
    {
        [Test]
        public void ReadConsole_WithCount_ReturnsCollectionShape()
        {
            var result = JObject.FromObject(ConsoleHandler.ReadConsole(new JObject
            {
                ["count"] = 5,
                ["includeStackTrace"] = false
            }));

            Assert.IsNull(result["error"]?.ToString());
            Assert.IsTrue(result.ContainsKey("logs"));
            Assert.IsTrue(result.ContainsKey("totalCaptured"));
            Assert.IsInstanceOf<JArray>(result["logs"]);
        }

        [Test]
        public void ClearConsole_WithDefaults_ReturnsSuccess()
        {
            var result = JObject.FromObject(ConsoleHandler.ClearConsole(new JObject()));

            Assert.IsNull(result["error"]?.ToString());
            Assert.IsTrue(result.Value<bool>("success"));
        }

        [Test]
        public void GetProjectSettings_DefaultParametersIncludePlayer()
        {
            var result = JObject.FromObject(ProjectSettingsHandler.GetProjectSettings(new JObject()));

            Assert.IsNull(result["error"]?.ToString());
            Assert.IsNotNull(result["player"]);
            Assert.IsNull(result["quality"]);
        }

        [Test]
        public void GetProjectSetting_WithPlayerPathReturnsValue()
        {
            var result = JObject.FromObject(ProjectSettingsHandler.GetProjectSetting(new JObject
            {
                ["path"] = "player/companyName"
            }));

            Assert.IsNull(result["error"]?.ToString());
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(PlayerSettings.companyName, result["value"]?.ToString());
        }

        [Test]
        public void SetProjectSetting_WithoutConfirmationRejectsAndDoesNotMutate()
        {
            var originalCompanyName = PlayerSettings.companyName;

            var result = JObject.FromObject(ProjectSettingsHandler.SetProjectSetting(new JObject
            {
                ["path"] = "player/companyName",
                ["value"] = originalCompanyName + "-blocked",
                ["confirmChanges"] = false
            }));

            Assert.AreEqual("CONFIRMATION_REQUIRED", result.Value<string>("code"));
            Assert.AreEqual(originalCompanyName, PlayerSettings.companyName);
        }

        [Test]
        public void SetProjectSetting_WithoutConfirmationParameterRejectsAndDoesNotMutate()
        {
            var originalCompanyName = PlayerSettings.companyName;

            var result = JObject.FromObject(ProjectSettingsHandler.SetProjectSetting(new JObject
            {
                ["path"] = "player/companyName",
                ["value"] = originalCompanyName + "-blocked"
            }));

            Assert.AreEqual("CONFIRMATION_REQUIRED", result.Value<string>("code"));
            Assert.AreEqual(originalCompanyName, PlayerSettings.companyName);
        }

        [Test]
        public void UpdateProjectSettings_WithoutConfirmationRejects()
        {
            var result = JObject.FromObject(ProjectSettingsHandler.UpdateProjectSettings(new JObject
            {
                ["player"] = new JObject
                {
                    ["productName"] = PlayerSettings.productName
                }
            }));

            Assert.AreEqual("CONFIRMATION_REQUIRED", result.Value<string>("code"));
        }
    }
}
