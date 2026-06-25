using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityCliBridge.Tests.PlayMode
{
    public class InputSimulationAutomationPlayModeTests
    {
        private const int DefaultTimeoutFrames = 600;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created.Where(go => go != null))
            {
                Object.Destroy(go);
            }

            _created.Clear();
        }

        [UnityTest]
        public IEnumerator KeyboardAndMouseInput_AreObserved()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            EnsureBootstrap();
            yield return WaitForHarnessReady();

            var keyboardPress = InvokeInputHandler("SimulateKeyboardInput", new JObject
            {
                ["action"] = "press",
                ["key"] = "Space"
            });
            AssertNoError(keyboardPress);
            yield return null;

            var inputState = InvokeInputHandler("GetCurrentInputState", new JObject());
            AssertNoError(inputState);
            CollectionAssert.Contains(((JArray)inputState["keyboard"]?["pressedKeys"])?.Select(token => token.ToString()).ToList(), "space");
            StringAssert.Contains("KeyboardPressed=space", GetStatusText());

            var keyboardRelease = InvokeInputHandler("SimulateKeyboardInput", new JObject
            {
                ["action"] = "release",
                ["key"] = "Space"
            });
            AssertNoError(keyboardRelease);
            yield return null;
            StringAssert.Contains("KeyboardPressed=(none)", GetStatusText());

            var keyboardType = InvokeInputHandler("SimulateKeyboardInput", new JObject
            {
                ["action"] = "type",
                ["text"] = "Hi"
            });
            AssertNoError(keyboardType);
            yield return WaitForStatusContains("TextInput=Hi");

            var mouseMove = InvokeInputHandler("SimulateMouseInput", new JObject
            {
                ["action"] = "move",
                ["x"] = 120,
                ["y"] = 140,
                ["absolute"] = true
            });
            AssertNoError(mouseMove);
            yield return WaitForStatusContains("MousePosition=120.0,140.0");

            var mouseHold = InvokeInputHandler("SimulateMouseInput", new JObject
            {
                ["action"] = "button",
                ["button"] = "left",
                ["buttonAction"] = "press",
                ["holdSeconds"] = 0.05
            });
            AssertNoError(mouseHold);
            yield return WaitForStatusContains("MouseLeftPressed=True");
            yield return WaitForCondition(() =>
                GetStatusText().Contains("MouseLeftPressed=False") &&
                GetStatusInt("MouseReleases") >= 1,
                DefaultTimeoutFrames);

            var mouseScroll = InvokeInputHandler("SimulateMouseInput", new JObject
            {
                ["action"] = "scroll",
                ["deltaX"] = 0,
                ["deltaY"] = 10
            });
            AssertNoError(mouseScroll);
            yield return WaitForStatusContains("MouseScroll=0.0,10.0");
        }

        [UnityTest]
        public IEnumerator GamepadAndTouchInput_AreObserved()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            EnsureBootstrap();
            yield return WaitForHarnessReady();

            var gamepadButton = InvokeInputHandler("SimulateGamepadInput", new JObject
            {
                ["action"] = "button",
                ["button"] = "a",
                ["buttonAction"] = "press",
                ["holdSeconds"] = 0.05
            });
            AssertNoError(gamepadButton);
            yield return WaitForStatusContains("GamepadButtonA=True");
            yield return WaitForCondition(() =>
                GetStatusText().Contains("GamepadButtonA=False") &&
                GetStatusInt("GamepadButtonReleases") >= 1,
                DefaultTimeoutFrames);

            var gamepadStick = InvokeInputHandler("SimulateGamepadInput", new JObject
            {
                ["action"] = "stick",
                ["stick"] = "left",
                ["x"] = 0.5f,
                ["y"] = 0.75f
            });
            AssertNoError(gamepadStick);
            yield return WaitForStatusContains("GamepadLeftStick=0.50,0.75");

            var gamepadTrigger = InvokeInputHandler("SimulateGamepadInput", new JObject
            {
                ["action"] = "trigger",
                ["trigger"] = "left",
                ["value"] = 0.8f
            });
            AssertNoError(gamepadTrigger);
            yield return WaitForStatusContains("GamepadLeftTrigger=0.80");

            var gamepadDpad = InvokeInputHandler("SimulateGamepadInput", new JObject
            {
                ["action"] = "dpad",
                ["direction"] = "up"
            });
            AssertNoError(gamepadDpad);
            yield return WaitForStatusContains("GamepadDpad=0.00,1.00");

            var touchTap = InvokeInputHandler("SimulateTouchInput", new JObject
            {
                ["action"] = "tap",
                ["x"] = 100,
                ["y"] = 200,
                ["touchId"] = 0
            });
            AssertNoError(touchTap);
            yield return WaitForCondition(() =>
                GetStatusInt("TouchPresses") >= 1 &&
                GetStatusInt("TouchReleases") >= 1,
                DefaultTimeoutFrames);

            var touchSwipe = InvokeInputHandler("SimulateTouchInput", new JObject
            {
                ["action"] = "swipe",
                ["startX"] = 100,
                ["startY"] = 100,
                ["endX"] = 300,
                ["endY"] = 120,
                ["duration"] = 20,
                ["touchId"] = 0
            });
            AssertNoError(touchSwipe);
            yield return WaitForCondition(() =>
                GetStatusText().Contains("TouchLast=") &&
                GetStatusText().Contains("300.0,120.0"),
                DefaultTimeoutFrames);

            var touchPinch = InvokeInputHandler("SimulateTouchInput", new JObject
            {
                ["action"] = "pinch",
                ["centerX"] = 200,
                ["centerY"] = 200,
                ["startDistance"] = 80,
                ["endDistance"] = 180
            });
            AssertNoError(touchPinch);
            yield return WaitForCondition(() => GetStatusInt("TouchMaxSimultaneous") >= 2, DefaultTimeoutFrames);

            var touchMulti = InvokeInputHandler("SimulateTouchInput", new JObject
            {
                ["action"] = "multi",
                ["touches"] = new JArray
                {
                    new JObject
                    {
                        ["x"] = 150,
                        ["y"] = 180,
                        ["phase"] = "began"
                    },
                    new JObject
                    {
                        ["x"] = 210,
                        ["y"] = 180,
                        ["phase"] = "moved"
                    }
                }
            });
            AssertNoError(touchMulti);
            yield return null;

            var inputState = InvokeInputHandler("GetCurrentInputState", new JObject());
            AssertNoError(inputState);
            Assert.GreaterOrEqual(((JArray)inputState["touchscreen"]?["activeTouches"])?.Count ?? 0, 2);
        }

        [UnityTest]
        public IEnumerator CreateInputSequence_AppliesDelayAndFinalState()
        {
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            EnsureBootstrap();
            yield return WaitForHarnessReady();

            var sequence = InvokeInputHandler("CreateInputSequence", new JObject
            {
                ["sequence"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "keyboard",
                        ["params"] = new JObject
                        {
                            ["action"] = "press",
                            ["key"] = "A",
                            ["holdSeconds"] = 0.05
                        }
                    },
                    new JObject
                    {
                        ["type"] = "mouse",
                        ["params"] = new JObject
                        {
                            ["action"] = "move",
                            ["x"] = 222,
                            ["y"] = 111,
                            ["absolute"] = true
                        }
                    },
                    new JObject
                    {
                        ["type"] = "gamepad",
                        ["params"] = new JObject
                        {
                            ["action"] = "stick",
                            ["stick"] = "left",
                            ["x"] = 0.25f,
                            ["y"] = 0.5f
                        }
                    }
                },
                ["delayBetween"] = 80
            });
            AssertNoError(sequence);
            Assert.IsTrue(sequence.Value<bool>("success"));
            Assert.GreaterOrEqual(sequence.Value<int>("totalDurationMs"), 140);

            yield return null;

            var inputState = InvokeInputHandler("GetCurrentInputState", new JObject());
            AssertNoError(inputState);
            CollectionAssert.DoesNotContain(((JArray)inputState["keyboard"]?["pressedKeys"])?.Select(token => token.ToString()).ToList(), "a");
            Assert.AreEqual(222f, inputState["mouse"]?["position"]?["x"]?.ToObject<float>() ?? -1f, 0.1f);
            Assert.AreEqual(111f, inputState["mouse"]?["position"]?["y"]?.ToObject<float>() ?? -1f, 0.1f);
            Assert.AreEqual(0.25f, inputState["gamepad"]?["sticks"]?["left"]?["x"]?.ToObject<float>() ?? -1f, 0.05f);
            Assert.AreEqual(0.5f, inputState["gamepad"]?["sticks"]?["left"]?["y"]?.ToObject<float>() ?? -1f, 0.05f);

            yield return WaitForStatusContains("MousePosition=222.0,111.0");
        }

        private void EnsureBootstrap()
        {
            var bootstrapType = FindType("UnityCliBridge.TestScenes.UnityCliInputSimulationTestBootstrap");
            if (bootstrapType == null)
            {
                Assert.Ignore("UnityCliInputSimulationTestBootstrap not found.");
            }

            var go = new GameObject("UnityCli_InputSimulation_TestBootstrap");
            go.AddComponent(bootstrapType);
            _created.Add(go);
        }

        private static IEnumerator WaitForHarnessReady()
        {
            yield return WaitForCondition(() => GetStatusText().Contains("Ready=True"), DefaultTimeoutFrames);
        }

        private static IEnumerator WaitForStatusContains(string expected)
        {
            yield return WaitForCondition(() => GetStatusText().Contains(expected), DefaultTimeoutFrames);
        }

        private static IEnumerator WaitForCondition(System.Func<bool> predicate, int timeoutFrames)
        {
            for (int i = 0; i < timeoutFrames; i++)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Timed out waiting for condition. Status:\n" + GetStatusText());
        }

        private static JObject InvokeInputHandler(string methodName, JObject parameters)
        {
            var handlerType = FindType("UnityCliBridge.Handlers.InputSystemHandler");
            if (handlerType == null)
            {
                Assert.Ignore("InputSystemHandler not found (Editor assembly not loaded).");
            }

            var method = handlerType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, $"InputSystemHandler.{methodName} not found");

            var result = method.Invoke(null, new object[] { parameters });
            return result as JObject ?? JObject.FromObject(result);
        }

        private static void AssertNoError(JObject result)
        {
            Assert.NotNull(result);
            Assert.IsNull(result["error"], result["error"]?.ToString());
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string GetStatusText()
        {
            var canvas = GameObject.Find("Canvas");
            var status = canvas != null ? canvas.transform.Find("InputE2E_Panel/InputE2E_StatusText")?.gameObject : null;
            if (status == null)
            {
                return string.Empty;
            }

            var text = status.GetComponent<Text>();
            return text != null ? text.text ?? string.Empty : string.Empty;
        }

        private static int GetStatusInt(string key)
        {
            var match = Regex.Match(GetStatusText(), "^" + Regex.Escape(key) + "=(\\d+)$", RegexOptions.Multiline);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
    }
}
