using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class PlayModeHandlerTests
    {
        [Test]
        public void GetEditorState_ShouldReturnPlayModeDiagnostics()
        {
            var result = PlayModeHandler.HandleCommand("get_editor_state", new JObject());

            Assert.AreEqual("success", result.Value<string>("status"));
            Assert.IsNotNull(result["state"]?["isPlaying"]);
            Assert.IsNotNull(result["state"]?["isPaused"]);
            Assert.IsNotNull(result["state"]?["playability"]?["reason"]);
        }

        [Test]
        public void PauseGame_ShouldReturnErrorWhenNotPlaying()
        {
            var result = PlayModeHandler.HandleCommand("pause_game", new JObject());

            Assert.AreEqual("error", result.Value<string>("status"));
            StringAssert.Contains("Not in play mode", result.Value<string>("error"));
        }
    }
}
