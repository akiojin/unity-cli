using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Helpers;

namespace UnityCliBridge.Tests.Helpers
{
    [TestFixture]
    public class PlayModeChangeWarningHelperTests
    {
        private Func<bool> _originalDetector;

        [SetUp]
        public void SetUp()
        {
            _originalDetector = PlayModeChangeWarningHelper.PlayModeDetector;
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = _originalDetector;
        }

        private static JObject BuildComponentParameters(string gameObjectPath)
        {
            return new JObject
            {
                ["gameObjectPath"] = gameObjectPath
            };
        }

        [Test]
        public void GetWarnings_ShouldReturnWarningForAddComponentDuringPlayMode()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = () => true;
            var parameters = BuildComponentParameters("/Player");

            var warnings = PlayModeChangeWarningHelper.GetWarnings("add_component", parameters);

            Assert.IsNotNull(warnings);
            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("PLAY_MODE_RUNTIME_CHANGES", warnings[0]["code"]);
            Assert.AreEqual("warning", warnings[0]["severity"]);
        }

        [Test]
        public void GetWarnings_ShouldReturnEmptyWhenNotPlaying()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = () => false;
            var parameters = BuildComponentParameters("/Enemy");

            var warnings = PlayModeChangeWarningHelper.GetWarnings("add_component", parameters);

            Assert.IsNull(warnings);
        }

        [Test]
        public void GetWarnings_ShouldSkipPrefabAssetFieldEdits()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = () => true;

            var parameters = new JObject
            {
                ["prefabAssetPath"] = "Assets/Prefabs/Enemy.prefab",
                ["scope"] = "prefabAsset",
                ["gameObjectPath"] = string.Empty
            };

            var warnings = PlayModeChangeWarningHelper.GetWarnings("set_component_field", parameters);

            Assert.IsNull(warnings);
        }

        [Test]
        public void GetWarnings_ShouldWarnForRuntimeSceneFieldEdit()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = () => true;

            var parameters = new JObject
            {
                ["gameObjectPath"] = "/World/Fountain",
                ["runtime"] = true
            };

            var warnings = PlayModeChangeWarningHelper.GetWarnings("set_component_field", parameters);

            Assert.IsNotNull(warnings);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Play Mode", warnings[0]["message"].ToString());
        }

        [Test]
        public void GetWarnings_ShouldWarnForInstantiatePrefab()
        {
            PlayModeChangeWarningHelper.PlayModeDetector = () => true;

            var parameters = new JObject
            {
                ["prefabPath"] = "Assets/Prefabs/Tree.prefab"
            };

            var warnings = PlayModeChangeWarningHelper.GetWarnings("instantiate_prefab", parameters);

            Assert.IsNotNull(warnings);
            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("instantiate_prefab", warnings[0]["tool"]);
        }
    }
}
