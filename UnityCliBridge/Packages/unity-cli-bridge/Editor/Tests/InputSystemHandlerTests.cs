#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && UNITY_INPUT_SYSTEM_PACKAGE
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using UnityCliBridge.Handlers;

// Conditionally include Input System namespace only if available
#pragma warning disable CS0234 // Type or namespace does not exist
using UnityEngine.InputSystem;
#pragma warning restore CS0234

namespace UnityCliBridge.Tests
{
    /// <summary>
    /// Tests for InputSystemHandler
    /// </summary>
    [TestFixture]
    public class InputSystemHandlerTests
    {
        private const string InputSystemTypeName = "UnityEngine.InputSystem.InputSystem, Unity.InputSystem";

        private static bool inputSystemAvailable;

        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
        private Touchscreen touchscreen;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            inputSystemAvailable = Type.GetType(InputSystemTypeName) != null;

            if (!inputSystemAvailable)
            {
                Assert.Ignore("Unity Input System package is not available; skipping InputSystemHandlerTests.");
            }
        }

        [SetUp]
        public void Setup()
        {
            // Clean up any existing devices. Iterate in reverse so that removing devices
            // does not invalidate our index and guard against null entries that can appear
            // in InputSystem.devices during teardown of previous tests.
            for (var i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                var device = InputSystem.devices[i];
                if (device != null)
                {
                    InputSystem.RemoveDevice(device);
                }
            }

            // Add test devices
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            touchscreen = InputSystem.AddDevice<Touchscreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (!inputSystemAvailable)
            {
                return;
            }

            // Clean up test devices
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (gamepad != null) InputSystem.RemoveDevice(gamepad);
            if (touchscreen != null) InputSystem.RemoveDevice(touchscreen);

            keyboard = null;
            mouse = null;
            gamepad = null;
            touchscreen = null;
        }

        #region Keyboard Tests

        [Test]
        public void SimulateKeyboardInput_PressKey_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "press",
                ["key"] = "A"
            };

            // Act
            var result = InputSystemHandler.SimulateKeyboardInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("press", resultJson["action"].ToString());
            Assert.AreEqual("A", resultJson["key"].ToString());
            Assert.IsTrue(keyboard.aKey.isPressed);
        }

        [Test]
        public void SimulateKeyboardInput_ReleaseKey_Success()
        {
            // Arrange - First press the key
            var pressParams = new JObject
            {
                ["action"] = "press",
                ["key"] = "A"
            };
            InputSystemHandler.SimulateKeyboardInput(pressParams);
            InputSystem.Update();
            Assert.IsTrue(keyboard.aKey.isPressed);

            // Act - Release the key
            var releaseParams = new JObject
            {
                ["action"] = "release",
                ["key"] = "A"
            };
            var result = InputSystemHandler.SimulateKeyboardInput(releaseParams);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("release", resultJson["action"].ToString());
            Assert.IsFalse(keyboard.aKey.isPressed);
        }

        [Test]
        public void SimulateKeyboardInput_TypeText_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "type",
                ["text"] = "Hello",
                ["typingSpeed"] = 10
            };

            // Act
            var result = InputSystemHandler.SimulateKeyboardInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("type", resultJson["action"].ToString());
            Assert.AreEqual("Hello", resultJson["text"].ToString());
        }

        [Test]
        public void SimulateKeyboardInput_KeyCombo_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "combo",
                ["keys"] = new JArray { "LeftCtrl", "C" }
            };

            // Act
            var result = InputSystemHandler.SimulateKeyboardInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("combo", resultJson["action"].ToString());
        }

        [Test]
        public void SimulateKeyboardInput_BatchedActions_Success()
        {
            var parameters = new JObject
            {
                ["actions"] = new JArray
                {
                    new JObject { ["action"] = "press", ["key"] = "A" },
                    new JObject { ["action"] = "press", ["key"] = "D" }
                }
            };

            var result = InputSystemHandler.SimulateKeyboardInput(parameters);
            InputSystem.Update();

            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual(2, resultJson["totalActions"].ToObject<int>());
            Assert.IsTrue(keyboard.aKey.isPressed);
            Assert.IsTrue(keyboard.dKey.isPressed);

            InputSystemHandler.SimulateKeyboardInput(new JObject { ["action"] = "release", ["key"] = "A" });
            InputSystemHandler.SimulateKeyboardInput(new JObject { ["action"] = "release", ["key"] = "D" });
            InputSystem.Update();
        }

        #endregion

        #region Mouse Tests

        [Test]
        public void SimulateMouseInput_Move_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "move",
                ["x"] = 100,
                ["y"] = 200,
                ["absolute"] = true
            };

            // Act
            var result = InputSystemHandler.SimulateMouseInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("move", resultJson["action"].ToString());
            Assert.AreEqual(100, mouse.position.x.ReadValue());
            Assert.AreEqual(200, mouse.position.y.ReadValue());
        }

        [Test]
        public void SimulateMouseInput_Click_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "click",
                ["button"] = "left",
                ["clickCount"] = 1
            };

            // Act
            var result = InputSystemHandler.SimulateMouseInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("click", resultJson["action"].ToString());
            Assert.AreEqual("left", resultJson["button"].ToString());
        }

        [Test]
        public void SimulateMouseInput_Drag_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "drag",
                ["startX"] = 10,
                ["startY"] = 20,
                ["endX"] = 100,
                ["endY"] = 200,
                ["button"] = "left"
            };

            // Act
            var result = InputSystemHandler.SimulateMouseInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("drag", resultJson["action"].ToString());
        }

        [Test]
        public void SimulateMouseInput_Scroll_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "scroll",
                ["deltaX"] = 0,
                ["deltaY"] = 10
            };

            // Act
            var result = InputSystemHandler.SimulateMouseInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("scroll", resultJson["action"].ToString());
        }

        [Test]
        public void SimulateMouseInput_ButtonAction_Success()
        {
            var parameters = new JObject
            {
                ["action"] = "button",
                ["button"] = "left",
                ["buttonAction"] = "press"
            };

            var result = InputSystemHandler.SimulateMouseInput(parameters);
            Assert.NotNull(result);
            Assert.IsTrue(mouse.leftButton.isPressed);

            InputSystemHandler.SimulateMouseInput(new JObject
            {
                ["action"] = "button",
                ["button"] = "left",
                ["buttonAction"] = "release"
            });
            InputSystem.Update();
            Assert.IsFalse(mouse.leftButton.isPressed);
        }

        #endregion

        #region Gamepad Tests

        [Test]
        public void SimulateGamepadInput_Button_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "button",
                ["button"] = "a",
                ["buttonAction"] = "press"
            };

            // Act
            var result = InputSystemHandler.SimulateGamepadInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.IsTrue(gamepad.buttonSouth.isPressed);
        }

        [Test]
        public void SimulateGamepadInput_Stick_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "stick",
                ["stick"] = "left",
                ["x"] = 0.5f,
                ["y"] = 0.75f
            };

            // Act
            var result = InputSystemHandler.SimulateGamepadInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual(0.5f, gamepad.leftStick.x.ReadValue(), 0.01f);
            Assert.AreEqual(0.75f, gamepad.leftStick.y.ReadValue(), 0.01f);
        }

        [Test]
        public void SimulateGamepadInput_Trigger_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "trigger",
                ["trigger"] = "left",
                ["value"] = 0.8f
            };

            // Act
            var result = InputSystemHandler.SimulateGamepadInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual(0.8f, gamepad.leftTrigger.ReadValue(), 0.01f);
        }

        [Test]
        public void SimulateGamepadInput_DPad_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "dpad",
                ["direction"] = "up"
            };

            // Act
            var result = InputSystemHandler.SimulateGamepadInput(parameters);
            InputSystem.Update();

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual(Vector2.up, gamepad.dpad.ReadValue());
        }

        [UnityTest]
        public IEnumerator SimulateGamepadInput_ButtonHoldSeconds_Releases()
        {
            var parameters = new JObject
            {
                ["action"] = "button",
                ["button"] = "a",
                ["buttonAction"] = "press",
                ["holdSeconds"] = 0.05
            };

            InputSystemHandler.SimulateGamepadInput(parameters);
            InputSystem.Update();
            Assert.IsTrue(gamepad.buttonSouth.isPressed);

            double start = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - start < 0.1f)
            {
                yield return null;
            }

            InputSystem.Update();
            Assert.IsFalse(gamepad.buttonSouth.isPressed);
        }

        #endregion

        #region Touch Tests

        [Test]
        public void SimulateTouchInput_Tap_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "tap",
                ["x"] = 100,
                ["y"] = 200,
                ["touchId"] = 0
            };

            // Act
            var result = InputSystemHandler.SimulateTouchInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("tap", resultJson["action"].ToString());
        }

        [Test]
        public void SimulateTouchInput_Swipe_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "swipe",
                ["startX"] = 100,
                ["startY"] = 100,
                ["endX"] = 300,
                ["endY"] = 100,
                ["duration"] = 500,
                ["touchId"] = 0
            };

            // Act
            var result = InputSystemHandler.SimulateTouchInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("swipe", resultJson["action"].ToString());
        }

        [Test]
        public void SimulateTouchInput_Pinch_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "pinch",
                ["centerX"] = 200,
                ["centerY"] = 200,
                ["startDistance"] = 50,
                ["endDistance"] = 150
            };

            // Act
            var result = InputSystemHandler.SimulateTouchInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual("pinch", resultJson["action"].ToString());
        }

        #endregion

        #region Sequence and State Tests

        [Test]
        public void CreateInputSequence_Success()
        {
            // Arrange
            var parameters = new JObject
            {
                ["sequence"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "keyboard",
                        ["params"] = new JObject
                        {
                            ["action"] = "press",
                            ["key"] = "W"
                        }
                    },
                    new JObject
                    {
                        ["type"] = "mouse",
                        ["params"] = new JObject
                        {
                            ["action"] = "move",
                            ["x"] = 100,
                            ["y"] = 100,
                            ["absolute"] = true
                        }
                    }
                },
                ["delayBetween"] = 100
            };

            // Act
            var result = InputSystemHandler.CreateInputSequence(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["success"].ToObject<bool>());
            Assert.AreEqual(2, resultJson["totalSteps"].ToObject<int>());
        }

        [Test]
        public void GetCurrentInputState_Success()
        {
            // Arrange
            var parameters = new JObject();

            // Act
            var result = InputSystemHandler.GetCurrentInputState(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.NotNull(resultJson["activeDevices"]);
            Assert.NotNull(resultJson["keyboard"]);
            Assert.NotNull(resultJson["mouse"]);
            Assert.NotNull(resultJson["gamepad"]);
            Assert.NotNull(resultJson["touchscreen"]);
        }

        #endregion

        #region Error Cases

        [Test]
        public void SimulateKeyboardInput_InvalidAction_ReturnsError()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "invalid_action"
            };

            // Act
            var result = InputSystemHandler.SimulateKeyboardInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["error"].ToString().Contains("Unknown action"));
        }

        [Test]
        public void SimulateKeyboardInput_MissingKey_ReturnsError()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "press"
                // Missing "key" parameter
            };

            // Act
            var result = InputSystemHandler.SimulateKeyboardInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["error"].ToString().Contains("key is required"));
        }

        [Test]
        public void SimulateMouseInput_InvalidButton_ReturnsError()
        {
            // Arrange
            var parameters = new JObject
            {
                ["action"] = "click",
                ["button"] = "invalid_button"
            };

            // Act
            var result = InputSystemHandler.SimulateMouseInput(parameters);

            // Assert
            Assert.NotNull(result);
            var resultJson = JObject.FromObject(result);
            Assert.IsTrue(resultJson["error"].ToString().Contains("Invalid mouse button"));
        }

        #endregion
    }
}
#endif
