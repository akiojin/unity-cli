using System.Collections;
using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Tests.Helpers;
using UnityEngine.TestTools;
using UnityCliBridge.Core;
using UnityCliBridge.Models;
using UnityCliBridge.Handlers;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Tests
{
    public class PlayerBuildHandlerTests
    {
        [TestCase("{}")]
        [TestCase("{\"target\":\"StandaloneOSX\",\"scenes\":[],\"outputPath\":\"/build/Game.app\"}")]
        [TestCase("{\"target\":\"Android\",\"scenes\":[\"Assets/X.unity\"],\"outputPath\":\"/build/Game.app\"}")]
        [TestCase("{\"target\":\"StandaloneOSX\",\"scenes\":[1],\"outputPath\":\"/build/Game.app\"}")]
        [TestCase("{\"target\":\"StandaloneOSX\",\"scenes\":[\"Assets/X.unity\"],\"outputPath\":\"/build/Game.app\",\"development\":\"true\"}")]
        public void Start_RejectsMalformedParameters(string parameters)
        {
            var response = JObject.Parse(PlayerBuildHandler.Start(new Command
            {
                Id = "invalid", Type = "build_player", Parameters = JObject.Parse(parameters)
            }));
            Assert.AreEqual("INVALID_BUILD_PARAMETERS", (string)response["code"]);
        }

        [UnityTest]
        public IEnumerator Status_CanRespondOnBackgroundThreadWithoutUnityApi() => TaskTestUtility.Await(async () =>
        {
            PlayerBuildHandler.Initialize();
            var response = await Task.Run(() => PlayerBuildHandler.Status(new Command
            {
                Id = "background", Type = "get_build_status", Parameters = new JObject { ["buildId"] = "missing" }
            }));
            var json = JObject.Parse(response);
            Assert.AreEqual("background", (string)json["id"]);
            Assert.AreEqual("BUILD_NOT_FOUND", (string)json["code"]);
        });

        [Test]
        public void Output_RequiresDedicatedDirectoryAndNeverOverwrites()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.unity/player-build-tests", Guid.NewGuid().ToString("N")));
            var path = Path.Combine(directory, "Player.app");
            Assert.AreEqual(path, PlayerBuildHandler.ValidateOutput(path, BuildTarget.StandaloneOSX));
            Assert.False(Directory.Exists(directory), "Validation must not create output folders");
            Directory.CreateDirectory(directory);
            try
            {
                Assert.AreEqual(path, PlayerBuildHandler.ValidateOutput(path, BuildTarget.StandaloneOSX));
                File.WriteAllText(Path.Combine(directory, "keep.txt"), "existing data");
                Assert.Throws<ArgumentException>(() => PlayerBuildHandler.ValidateOutput(path, BuildTarget.StandaloneOSX));
                Assert.AreEqual("existing data", File.ReadAllText(Path.Combine(directory, "keep.txt")));
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestCase("relative/Player.app")]
        [TestCase("/Player.exe")]
        public void Output_RejectsRelativeOrWrongExtension(string path)
        {
            Assert.Throws<ArgumentException>(() => PlayerBuildHandler.ValidateOutput(path, BuildTarget.StandaloneOSX));
        }

        [Test]
        public void Output_RejectsProjectAssets()
        {
            Assert.Throws<ArgumentException>(() => PlayerBuildHandler.ValidateOutput(
                Path.Combine(Application.dataPath, "GeneratedBuild", "Player.app"), BuildTarget.StandaloneOSX));
        }

        [UnityTest]
        public IEnumerator Start_RequiresExplicitParameters() => TaskTestUtility.Await(async () =>
        {
            var response = JObject.Parse(await BridgeCommandRouter.Handle(new Command
            {
                Id = "start", Type = "build_player", Parameters = new JObject()
            }));
            Assert.AreEqual("error", (string)response["status"]);
            Assert.AreEqual("INVALID_BUILD_PARAMETERS", (string)response["code"]);
        });

        [UnityTest]
        public IEnumerator Status_UnknownBuildIsNotSuccess() => TaskTestUtility.Await(async () =>
        {
            var response = JObject.Parse(await BridgeCommandRouter.Handle(new Command
            {
                Id = "status", Type = "get_build_status",
                Parameters = new JObject { ["buildId"] = "missing-build" }
            }));
            Assert.AreEqual("status", (string)response["id"]);
            Assert.AreEqual("error", (string)response["status"]);
            Assert.AreEqual("BUILD_NOT_FOUND", (string)response["code"]);
        });
    }
}
