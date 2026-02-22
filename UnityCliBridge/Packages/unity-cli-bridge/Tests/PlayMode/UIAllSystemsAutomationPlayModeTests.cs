using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    public class UIAllSystemsAutomationPlayModeTests
    {
        private const int DefaultTimeoutFrames = 600; // ~10s at 60fps

        private readonly System.Collections.Generic.List<GameObject> _created = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator AllUiSystems_CanFindClickAndSetValues()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            EnsureMainCamera();
            EnsureBootstrap();

            yield return WaitForAllSystemsReady(DefaultTimeoutFrames);

            var findResult = InvokeUIHandler("FindUIElements", new JObject
            {
                ["elementType"] = "Button",
                ["uiSystem"] = "all",
                ["includeInactive"] = false
            });
            AssertNoError(findResult);

            var paths = ((JArray)findResult["elements"]).Select(e => e.Value<string>("path")).ToList();
            CollectionAssert.Contains(paths, "/Canvas/UGUI_Panel/UGUI_Button");
            CollectionAssert.Contains(paths, "uitk:/UITK/UIDocument#UITK_Button");
            CollectionAssert.Contains(paths, "imgui:IMGUI/Button");

            // uGUI click updates status
            Assert.AreEqual(0, ExtractClickCount(GetUguiStatusText(), "UGUI clicks="));
            yield return Click("/Canvas/UGUI_Panel/UGUI_Button");
            yield return null;
            Assert.AreEqual(1, ExtractClickCount(GetUguiStatusText(), "UGUI clicks="));

            // uGUI set value updates status (triggerEvents=true)
            var setUgui = InvokeUIHandler("SetUIElementValue", new JObject
            {
                ["elementPath"] = "/Canvas/UGUI_Panel/UGUI_InputField",
                ["value"] = "hello",
                ["triggerEvents"] = true
            });
            AssertNoError(setUgui);
            yield return null;
            StringAssert.Contains("Input='hello'", GetUguiStatusText());

            // UI Toolkit click updates status label
            Assert.AreEqual(0, ExtractClickCount(GetUitkStatusText(), "UITK clicks="));
            yield return Click("uitk:/UITK/UIDocument#UITK_Button");
            yield return null;
            Assert.AreEqual(1, ExtractClickCount(GetUitkStatusText(), "UITK clicks="));

            // UI Toolkit set value updates state and status label (triggerEvents=true)
            var setUitkToggle = InvokeUIHandler("SetUIElementValue", new JObject
            {
                ["elementPath"] = "uitk:/UITK/UIDocument#UITK_Toggle",
                ["value"] = true,
                ["triggerEvents"] = true
            });
            AssertNoError(setUitkToggle);
            yield return null;

            var uitkToggleState = InvokeUIHandler("GetUIElementState", new JObject
            {
                ["elementPath"] = "uitk:/UITK/UIDocument#UITK_Toggle",
                ["includeInteractableInfo"] = true
            });
            AssertNoError(uitkToggleState);
            Assert.AreEqual(true, uitkToggleState.Value<bool>("value"));
            StringAssert.Contains("Toggle=True", GetUitkStatusText());

            // IMGUI click updates registry value
            Assert.AreEqual(0, GetImguiIntValue("IMGUI/Button"));
            yield return Click("imgui:IMGUI/Button");
            yield return null;
            Assert.AreEqual(1, GetImguiIntValue("IMGUI/Button"));

            // IMGUI set value updates registry value
            var setImguiText = InvokeUIHandler("SetUIElementValue", new JObject
            {
                ["elementPath"] = "imgui:IMGUI/TextField",
                ["value"] = "abc",
                ["triggerEvents"] = true
            });
            AssertNoError(setImguiText);
            yield return null;

            var imguiTextState = InvokeUIHandler("GetUIElementState", new JObject
            {
                ["elementPath"] = "imgui:IMGUI/TextField",
                ["includeInteractableInfo"] = true
            });
            AssertNoError(imguiTextState);
            Assert.AreEqual("abc", imguiTextState.Value<string>("value"));
        }

        private void EnsureMainCamera()
        {
            if (Camera.main != null) return;

            var cameraOwner = new GameObject("PlayModeTestCamera");
            cameraOwner.tag = "MainCamera";
            cameraOwner.AddComponent<Camera>();
            _created.Add(cameraOwner);
        }

        private void EnsureBootstrap()
        {
            // Avoid hard reference to Assembly-CSharp: locate by name at runtime.
            var bootstrapType = FindType("UnityCliBridge.TestScenes.AllUiSystemsTestBootstrap");
            if (bootstrapType == null)
            {
                Assert.Ignore("AllUiSystemsTestBootstrap not found (scene asset/scripts not present in this environment).");
            }

            var go = new GameObject("MCP_UI_TestBootstrap");
            go.AddComponent(bootstrapType);
            _created.Add(go);
        }

        private static IEnumerator WaitForAllSystemsReady(int timeoutFrames)
        {
            for (int i = 0; i < timeoutFrames; i++)
            {
                bool uguiReady = GameObject.Find("/Canvas/UGUI_Panel/UGUI_StatusText") != null;
                bool uitkReady = IsOk(InvokeUIHandler("GetUIElementState", new JObject { ["elementPath"] = "uitk:/UITK/UIDocument#UITK_Status" }));
                bool imguiReady = HasImguiControl("IMGUI/Button");

                if (uguiReady && uitkReady && imguiReady)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Timed out waiting for UI systems to be ready.");
        }

        private static IEnumerator Click(string elementPath)
        {
            var task = InvokeUIHandlerTask("ClickUIElement", new JObject
            {
                ["elementPath"] = elementPath,
                ["clickType"] = "left"
            });

            yield return WaitForTask(task, DefaultTimeoutFrames);

            var result = ToJObject(task.Result);
            AssertNoError(result);
            Assert.IsTrue(result.Value<bool>("success"));
        }

        private static IEnumerator WaitForTask(Task task, int timeoutFrames)
        {
            for (int i = 0; i < timeoutFrames; i++)
            {
                if (task.IsCanceled)
                {
                    Assert.Fail("Async operation was canceled.");
                }
                if (task.IsFaulted)
                {
                    Assert.Fail(task.Exception?.ToString());
                }
                if (task.IsCompleted)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Timed out waiting for async operation to complete.");
        }

        private static string GetUguiStatusText()
        {
            var state = InvokeUIHandler("GetUIElementState", new JObject
            {
                ["elementPath"] = "/Canvas/UGUI_Panel/UGUI_StatusText",
                ["includeInteractableInfo"] = true
            });
            AssertNoError(state);
            return state.Value<string>("text") ?? string.Empty;
        }

        private static string GetUitkStatusText()
        {
            var state = InvokeUIHandler("GetUIElementState", new JObject
            {
                ["elementPath"] = "uitk:/UITK/UIDocument#UITK_Status",
                ["includeInteractableInfo"] = true
            });
            AssertNoError(state);
            return state.Value<string>("text") ?? string.Empty;
        }

        private static int GetImguiIntValue(string controlId)
        {
            var state = InvokeUIHandler("GetUIElementState", new JObject
            {
                ["elementPath"] = "imgui:" + controlId,
                ["includeInteractableInfo"] = true
            });
            AssertNoError(state);
            return state["value"]?.ToObject<int>() ?? 0;
        }

        private static int ExtractClickCount(string text, string prefix)
        {
            text = (text ?? string.Empty).Replace("\r", string.Empty);
            var match = Regex.Match(text, Regex.Escape(prefix) + "(\\d+)");
            Assert.IsTrue(match.Success, $"Unable to extract click count from: {text}");
            return int.Parse(match.Groups[1].Value);
        }

        private static bool HasImguiControl(string controlId)
        {
            var found = InvokeUIHandler("FindUIElements", new JObject { ["uiSystem"] = "imgui" });
            if (found["error"] != null)
            {
                return false;
            }

            var elements = found["elements"] as JArray;
            if (elements == null)
            {
                return false;
            }

            var expectedPath = "imgui:" + controlId;
            return elements.Any(e => e?.Value<string>("path") == expectedPath);
        }

        private static JObject InvokeUIHandler(string methodName, JObject parameters)
        {
            var handlerType = FindType("UnityCliBridge.Handlers.UIInteractionHandler");
            if (handlerType == null)
            {
                Assert.Ignore("UIInteractionHandler not found (Editor assembly not loaded).");
            }

            var method = handlerType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, $"UIInteractionHandler.{methodName} not found");

            var result = method.Invoke(null, new object[] { parameters });
            return ToJObject(result);
        }

        private static Task<object> InvokeUIHandlerTask(string methodName, JObject parameters)
        {
            var handlerType = FindType("UnityCliBridge.Handlers.UIInteractionHandler");
            if (handlerType == null)
            {
                Assert.Ignore("UIInteractionHandler not found (Editor assembly not loaded).");
            }

            var method = handlerType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, $"UIInteractionHandler.{methodName} not found");

            var result = method.Invoke(null, new object[] { parameters });
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<Task<object>>(result);
            return (Task<object>)result;
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        private static bool IsOk(JObject result)
        {
            return result != null && result["error"] == null;
        }

        private static void AssertNoError(JObject result)
        {
            if (result == null)
            {
                Assert.Fail("Result is null");
            }
            if (result["error"] != null)
            {
                Assert.Fail(result["error"]?.ToString());
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
