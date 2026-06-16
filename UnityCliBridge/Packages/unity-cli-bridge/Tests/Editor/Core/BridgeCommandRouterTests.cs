using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Core;
using UnityCliBridge.Models;

namespace UnityCliBridge.Tests.Editor.Core
{
    [TestFixture]
    public class BridgeCommandRouterTests
    {
        [Test]
        public async Task Handle_Ping_ReturnsExistingSuccessResponseShape()
        {
            var response = await BridgeCommandRouter.Handle(new Command
            {
                Id = "cmd-1",
                Type = "PING",
                Parameters = JObject.FromObject(new { message = "hello" })
            });

            var json = JObject.Parse(response);

            Assert.AreEqual("cmd-1", json["id"]?.Value<string>());
            Assert.AreEqual("success", json["status"]?.Value<string>());
            Assert.AreEqual("pong", json["result"]?["message"]?.Value<string>());
            Assert.AreEqual("hello", json["result"]?["echo"]?.Value<string>());
        }

        [Test]
        public async Task Handle_UnknownCommand_ReturnsExistingErrorResponseShape()
        {
            var response = await BridgeCommandRouter.Handle(new Command
            {
                Id = "cmd-2",
                Type = "missing_tool",
                Parameters = new JObject()
            });

            var json = JObject.Parse(response);

            Assert.AreEqual("cmd-2", json["id"]?.Value<string>());
            Assert.AreEqual("error", json["status"]?.Value<string>());
            Assert.AreEqual("UNKNOWN_COMMAND", json["code"]?.Value<string>());
            Assert.AreEqual("missing_tool", json["details"]?["commandType"]?.Value<string>());
        }

        [Test]
        public void RegisteredCommandTypes_ExposesDelegateRegistry()
        {
            var commandTypes = BridgeCommandRouter.RegisteredCommandTypes.ToArray();

            Assert.Contains("ping", commandTypes);
            Assert.Contains("get_command_stats", commandTypes);
            Assert.False(commandTypes.Contains("missing_tool"));
        }
    }
}
