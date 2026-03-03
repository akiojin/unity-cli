using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityCliBridge.Handlers;
using UnityCliBridge.Runtime.IMGUI;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class UIInteractionHandlerTests
    {
        private readonly System.Collections.Generic.List<GameObject> _created = new System.Collections.Generic.List<GameObject>();
        private Func<bool> _playModeDetectorBackup;

        [SetUp]
        public void SetUp()
        {
            _playModeDetectorBackup = UIInteractionHandler.PlayModeDetector;
            UIInteractionHandler.PlayModeDetector = () => true;
        }

        [TearDown]
        public void TearDown()
        {
            UIInteractionHandler.PlayModeDetector = _playModeDetectorBackup;
            foreach (var go in _created.Where(g => g != null))
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void FindUIElements_WithUiSystemImgui_ReturnsRegisteredControl()
        {
            var id = "IMGUI/TestButton_" + Guid.NewGuid().ToString("N");
            ImguiControlRegistry.RegisterControl(
                controlId: id,
                controlType: "Button",
                rect: new Rect(0, 0, 100, 30),
                isInteractable: true,
                onClick: () => { }
            );

            var parameters = new JObject { ["uiSystem"] = "imgui" };
            var result = ToJObject(UIInteractionHandler.FindUIElements(parameters));

            Assert.IsNull(result["error"]);

            var elements = result["elements"] as JArray;
            Assert.IsNotNull(elements);
            Assert.IsTrue(elements!.Any(e => e?.Value<string>("path") == "imgui:" + id));
        }

        [Test]
        public void FindUIElements_WithUiSystemUgui_ReturnsButtonOnCanvas()
        {
            var canvasName = "Canvas_" + Guid.NewGuid().ToString("N");

            var canvasGo = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _created.Add(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var buttonGo = new GameObject("TestButton", typeof(RectTransform), typeof(Image), typeof(Button));
            _created.Add(buttonGo);
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var result = ToJObject(UIInteractionHandler.FindUIElements(new JObject
            {
                ["uiSystem"] = "ugui",
                ["elementType"] = "Button",
                ["canvasFilter"] = canvasName,
                ["includeInactive"] = true
            }));

            Assert.IsNull(result["error"]);

            var elements = result["elements"] as JArray;
            Assert.IsNotNull(elements);
            Assert.IsTrue(elements!.Any(e => e?.Value<string>("path") == "/" + canvasName + "/TestButton"));
        }

        [Test]
        public void GetUIElementState_WithUiToolkitPrefix_RoutesToUiToolkit()
        {
            var state = ToJObject(UIInteractionHandler.GetUIElementState(new JObject
            {
                ["elementPath"] = "uitk:/NoSuch/UIDocument#Any",
                ["includeInteractableInfo"] = true
            }));

            Assert.IsNotNull(state["error"]);
            StringAssert.Contains("UIDocument GameObject not found", state["error"]?.ToString());
        }

        [Test]
        public void SetUIElementValue_WithUiToolkitPrefix_RoutesToUiToolkit()
        {
            var result = ToJObject(UIInteractionHandler.SetUIElementValue(new JObject
            {
                ["elementPath"] = "uitk:/NoSuch/UIDocument#Any",
                ["value"] = new JValue("x"),
                ["triggerEvents"] = true
            }));

            Assert.IsNotNull(result["error"]);
            StringAssert.Contains("UIDocument GameObject not found", result["error"]?.ToString());
        }

        [Test]
        public async Task ClickUIElement_WithUiToolkitPrefix_RoutesToUiToolkit()
        {
            var result = ToJObject(await UIInteractionHandler.ClickUIElement(new JObject
            {
                ["elementPath"] = "uitk:/NoSuch/UIDocument#Any",
                ["clickType"] = "left"
            }));

            Assert.IsNotNull(result["error"]);
            StringAssert.Contains("UIDocument GameObject not found", result["error"]?.ToString());
        }

        [Test]
        public async Task ClickUIElement_WithImguiPath_InvokesOnClick()
        {
            var id = "IMGUI/Click_" + Guid.NewGuid().ToString("N");
            int clicks = 0;

            ImguiControlRegistry.RegisterControl(
                controlId: id,
                controlType: "Button",
                rect: new Rect(0, 0, 100, 30),
                isInteractable: true,
                getValue: () => clicks,
                onClick: () => clicks++
            );

            var result = ToJObject(await UIInteractionHandler.ClickUIElement(new JObject { ["elementPath"] = "imgui:" + id }));

            Assert.IsNull(result["error"]);
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void SetUIElementValue_WithImguiPath_SetsValue()
        {
            var id = "IMGUI/Text_" + Guid.NewGuid().ToString("N");
            string value = "initial";

            ImguiControlRegistry.RegisterControl(
                controlId: id,
                controlType: "TextField",
                rect: new Rect(0, 0, 100, 30),
                isInteractable: true,
                getValue: () => value,
                setValue: token => value = token?.ToString() ?? string.Empty
            );

            var result = ToJObject(UIInteractionHandler.SetUIElementValue(new JObject
            {
                ["elementPath"] = "imgui:" + id,
                ["value"] = new JValue("updated"),
                ["triggerEvents"] = true
            }));

            Assert.IsNull(result["error"]);
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.AreEqual("updated", value);
        }

        [Test]
        public void GetUIElementState_WithImguiPath_ReturnsValue()
        {
            var id = "IMGUI/State_" + Guid.NewGuid().ToString("N");
            bool toggle = false;

            ImguiControlRegistry.RegisterControl(
                controlId: id,
                controlType: "Toggle",
                rect: new Rect(0, 0, 100, 30),
                isInteractable: true,
                getValue: () => toggle,
                setValue: token => toggle = token != null && token.ToObject<bool>()
            );

            var state = ToJObject(UIInteractionHandler.GetUIElementState(new JObject
            {
                ["elementPath"] = "imgui:" + id,
                ["includeInteractableInfo"] = true
            }));

            Assert.IsNull(state["error"]);
            Assert.AreEqual("imgui", state.Value<string>("uiSystem"));
            Assert.AreEqual("imgui:" + id, state.Value<string>("path"));
            Assert.AreEqual(false, state["value"]?.ToObject<bool>());
        }
    }
}
