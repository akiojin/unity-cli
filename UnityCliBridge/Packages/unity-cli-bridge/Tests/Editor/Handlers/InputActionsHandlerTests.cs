#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class InputActionsHandlerTests
    {
        private string assetPath;

        [SetUp]
        public void SetUp()
        {
            assetPath = "Assets/Issue247_" + Guid.NewGuid().ToString("N") + ".inputactions";
            File.WriteAllText(assetPath, "{\"name\":\"Issue247\",\"maps\":[],\"controlSchemes\":[]}");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(assetPath);
        }

        [TestCase("Button", "Button", "Button")]
        [TestCase("PassThrough", "PassThrough", "")]
        [TestCase("Value", "Value", "Vector2")]
        [TestCase(null, "Button", "Button")]
        [TestCase("invalid", "Value", "")]
        [TestCase("999", "Value", "")]
        public void CreateActionMap_PreservesActionType(string requested, string expected, string control)
        {
            var action = new JObject { ["name"] = "TestAction" };
            if (requested != null) action["type"] = requested;
            AssertSuccess(InputActionsHandler.CreateActionMap(new JObject
            {
                ["assetPath"] = assetPath, ["mapName"] = "Audit",
                ["actions"] = new JArray(action)
            }));
            AssertAction(expected, control);
        }

        [TestCase("Button", "Button", "Button")]
        [TestCase("PassThrough", "PassThrough", "")]
        [TestCase("Value", "Value", "Vector2")]
        [TestCase(null, "Button", "Button")]
        [TestCase("invalid", "Value", "")]
        [TestCase("999", "Value", "")]
        public void AddInputAction_PreservesActionType(string requested, string expected, string control)
        {
            AssertSuccess(InputActionsHandler.CreateActionMap(new JObject
            {
                ["assetPath"] = assetPath, ["mapName"] = "Audit"
            }));
            var parameters = new JObject
            {
                ["assetPath"] = assetPath, ["mapName"] = "Audit", ["actionName"] = "TestAction"
            };
            if (requested != null) parameters["actionType"] = requested;
            AssertSuccess(InputActionsHandler.AddInputAction(parameters));
            AssertAction(expected, control);
        }

        private static void AssertSuccess(object response)
        {
            var result = JObject.FromObject(response);
            Assert.IsNull(result["error"], result.ToString());
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        private void AssertAction(string expected, string control)
        {
            var state = JObject.FromObject(InputActionsHandler.GetInputActionsState(
                new JObject { ["assetPath"] = assetPath }));
            Assert.IsNull(state["error"], state.ToString());
            var action = state["actionMaps"].Single()["actions"].Single();
            Assert.AreEqual(expected, action.Value<string>("type"));
            Assert.AreEqual(control, action.Value<string>("expectedControlType") ?? "");
        }
    }
}
#endif
