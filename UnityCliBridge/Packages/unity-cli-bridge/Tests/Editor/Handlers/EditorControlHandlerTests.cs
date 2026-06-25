using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class EditorControlHandlerTests
    {
        private readonly System.Collections.Generic.List<GameObject> _created = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Selection.objects = Array.Empty<UnityEngine.Object>();
            foreach (var go in _created.Where(go => go != null))
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
            UnityCliBridge.EditorIntegration.SampleWorkflows.Cleanup();
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        private static void AssertSuccess(JObject result)
        {
            Assert.IsNull(result["error"]?.Value<string>());
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void MenuHandler_ShouldBlockBlacklistedMenuAndListUnityCliMenus()
        {
            var blocked = ToJObject(MenuHandler.ExecuteMenuItem(new JObject
            {
                ["menuPath"] = "File/Quit"
            }));

            Assert.IsFalse(blocked.Value<bool>("success"));
            StringAssert.Contains("blacklisted", blocked.Value<string>("error"));

            var menus = ToJObject(MenuHandler.ExecuteMenuItem(new JObject
            {
                ["action"] = "get_available_menus",
                ["parameters"] = new JObject
                {
                    ["filter"] = "Tools/Unity CLI"
                }
            }));

            AssertSuccess(menus);
            Assert.IsTrue(menus["availableMenus"]!.Any(menu =>
                menu.Value<string>() == UnityCliBridge.EditorIntegration.MenuItems.ServerWindowPath));
            Assert.IsTrue(menus["availableMenus"]!.Any(menu =>
                menu.Value<string>() == UnityCliBridge.EditorIntegration.MenuItems.RunSceneSamplePath));
        }

        [Test]
        public void MenuItems_ShouldDisableStateChangingCommandsDuringPlayModeTransition()
        {
            Assert.IsTrue(UnityCliBridge.EditorIntegration.MenuItems.CanRunEditorStateCommand(false));
            Assert.IsFalse(UnityCliBridge.EditorIntegration.MenuItems.CanRunEditorStateCommand(true));
        }

        [Test]
        public void SelectionHandler_ShouldSetGetDetailsAndClearSelection()
        {
            var root = new GameObject("Spec152SelectionRoot");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            _created.Add(root);
            _created.Add(child);

            var set = ToJObject(SelectionHandler.HandleCommand("set", new JObject
            {
                ["objectPaths"] = new JArray("/Spec152SelectionRoot")
            }));
            AssertSuccess(set);
            Assert.AreEqual(1, set.Value<int>("count"));

            var details = ToJObject(SelectionHandler.HandleCommand("get_details", new JObject()));
            AssertSuccess(details);
            Assert.AreEqual(1, details.Value<int>("count"));
            Assert.AreEqual(1, details.Value<int>("totalChildrenCount"));
            Assert.AreEqual("/Spec152SelectionRoot", details["selection"]?[0]?["path"]?.Value<string>());

            var clear = ToJObject(SelectionHandler.HandleCommand("clear", new JObject()));
            AssertSuccess(clear);
            Assert.AreEqual(1, clear.Value<int>("previousCount"));
        }

        [Test]
        public void TagAndLayerHandlers_ShouldProtectReservedEntries()
        {
            var tagResult = ToJObject(TagManagementHandler.HandleCommand("remove", new JObject
            {
                ["tagName"] = "Untagged"
            }));
            StringAssert.Contains("reserved tag", tagResult.Value<string>("error"));

            var layerResult = ToJObject(LayerManagementHandler.HandleCommand("remove", new JObject
            {
                ["layerName"] = "Default"
            }));
            StringAssert.Contains("reserved layer", layerResult.Value<string>("error"));

            var layerLookup = ToJObject(LayerManagementHandler.HandleCommand("get_by_index", new JObject
            {
                ["layerIndex"] = 0
            }));
            AssertSuccess(layerLookup);
            Assert.AreEqual("Default", layerLookup.Value<string>("layerName"));
        }

        [Test]
        public void WindowAndToolHandlers_ShouldReturnStableReadShapes()
        {
            var windows = ToJObject(WindowManagementHandler.HandleCommand("get", new JObject
            {
                ["includeHidden"] = true
            }));
            AssertSuccess(windows);
            Assert.IsInstanceOf<JArray>(windows["windows"]);

            var missingState = ToJObject(WindowManagementHandler.HandleCommand("get_state", new JObject
            {
                ["windowType"] = "Spec152MissingWindow"
            }));
            AssertSuccess(missingState);
            Assert.AreEqual(JTokenType.Null, missingState["state"]?.Type);

            var tools = ToJObject(ToolManagementHandler.HandleCommand("get", new JObject
            {
                ["category"] = "Animation"
            }));
            AssertSuccess(tools);
            Assert.IsInstanceOf<JArray>(tools["tools"]);

            var refresh = ToJObject(ToolManagementHandler.HandleCommand("refresh", new JObject()));
            AssertSuccess(refresh);
            Assert.GreaterOrEqual(refresh.Value<int>("toolsCount"), 3);
        }

        [Test]
        public void SampleWorkflows_ShouldCreateAndCleanupSceneSample()
        {
            UnityCliBridge.EditorIntegration.SampleWorkflows.Cleanup();

            UnityCliBridge.EditorIntegration.SampleWorkflows.RunSceneSample();

            var root = GameObject.Find(UnityCliBridge.EditorIntegration.SampleWorkflows.TempRootName);
            Assert.IsNotNull(root);
            Assert.AreEqual(1, root.transform.childCount);

            UnityCliBridge.EditorIntegration.SampleWorkflows.Cleanup();
            Assert.IsNull(GameObject.Find(UnityCliBridge.EditorIntegration.SampleWorkflows.TempRootName));
        }
    }
}
