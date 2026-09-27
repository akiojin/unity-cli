#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && UNITY_INPUT_SYSTEM_PACKAGE
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests
{
    public class InputActionNotificationTests
    {
        private static readonly InputSettings.UpdateMode[] UpdateModes =
        {
            InputSettings.UpdateMode.ProcessEventsInDynamicUpdate,
            InputSettings.UpdateMode.ProcessEventsInFixedUpdate,
            InputSettings.UpdateMode.ProcessEventsManually
        };

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterTest()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EditorUpdates_DeliverKeyboardMouseAndGamepadPressAndRelease()
        {
            yield return new EnterPlayMode();
            var keyboardValues = new List<float>();
            var mouseValues = new List<float>();
            var gamepadValues = new List<float>();
            using (var settings = new SimulationSettingsScope())
            using (var keyboard = new InputAction("keyboard", InputActionType.PassThrough, "<Keyboard>/space"))
            using (var mouse = new InputAction("mouse", InputActionType.PassThrough, "<Mouse>/leftButton"))
            using (var gamepad = new InputAction("gamepad", InputActionType.PassThrough, "<Gamepad>/buttonSouth"))
            {
                keyboard.performed += context => keyboardValues.Add(context.ReadValue<float>());
                mouse.performed += context => mouseValues.Add(context.ReadValue<float>());
                gamepad.performed += context => gamepadValues.Add(context.ReadValue<float>());
                keyboard.Enable();
                mouse.Enable();
                gamepad.Enable();
                foreach (var updateMode in UpdateModes)
                {
                    InputSystem.settings.updateMode = updateMode;
                    foreach (var frameRate in new[] { 30, 120, -1 })
                    {
                        Application.targetFrameRate = frameRate;
                        keyboardValues.Clear();
                        mouseValues.Clear();
                        gamepadValues.Clear();
                        foreach (var action in new[] { "press", "release" })
                        {
                            UpdateEditorInput();
                            Assert.AreEqual(InputUpdateType.Editor, InputState.currentUpdateType);
                            AssertSuccess(InputSystemHandler.SimulateKeyboardInput(new JObject
                            {
                                ["action"] = action, ["key"] = "space"
                            }));
                            UpdateEditorInput();
                            AssertSuccess(InputSystemHandler.SimulateMouseInput(new JObject
                            {
                                ["action"] = "button", ["button"] = "left", ["buttonAction"] = action
                            }));
                            UpdateEditorInput();
                            AssertSuccess(InputSystemHandler.SimulateGamepadInput(new JObject
                            {
                                ["action"] = "button", ["button"] = "a", ["buttonAction"] = action
                            }));
                            yield return null;
                        }

                        foreach (var values in new[] { keyboardValues, mouseValues, gamepadValues })
                        {
                            CollectionAssert.Contains(values, 1f, $"InputAction must observe the press in {updateMode} at {frameRate} fps");
                            CollectionAssert.Contains(values, 0f, $"InputAction must observe the release in {updateMode} at {frameRate} fps");
                        }
                    }
                }
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EditorUpdates_DeliverScheduledKeyboardRelease()
        {
            yield return new EnterPlayMode();
            var values = new List<float>();
            using (var settings = new SimulationSettingsScope())
            using (var action = new InputAction("keyboard", InputActionType.PassThrough, "<Keyboard>/space"))
            {
                action.performed += context => values.Add(context.ReadValue<float>());
                action.Enable();
                foreach (var updateMode in UpdateModes)
                {
                    InputSystem.settings.updateMode = updateMode;
                    values.Clear();
                    UpdateEditorInput();
                    AssertSuccess(InputSystemHandler.SimulateKeyboardInput(new JObject
                    {
                        ["action"] = "press", ["key"] = "space", ["holdSeconds"] = 0.05
                    }));
                    var deadline = EditorApplication.timeSinceStartup + 2;
                    while (!values.Contains(0f) && EditorApplication.timeSinceStartup < deadline)
                    {
                        yield return null;
                        UpdateEditorInput();
                        // This public path processes releases just like EditorApplication.update.
                        InputSystemHandler.GetCurrentInputState(new JObject());
                    }
                    CollectionAssert.Contains(values, 1f, updateMode.ToString());
                    CollectionAssert.Contains(values, 0f, updateMode.ToString());
                }
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EditorUpdates_PreserveOtherGamepadControls()
        {
            yield return new EnterPlayMode();
            using (var settings = new SimulationSettingsScope())
            {
                foreach (var updateMode in UpdateModes)
                {
                    InputSystem.settings.updateMode = updateMode;
                    UpdateEditorInput();
                    AssertSuccess(InputSystemHandler.SimulateGamepadInput(new JObject
                    {
                        ["action"] = "button", ["button"] = "b", ["buttonAction"] = "press"
                    }));
                    AssertSuccess(InputSystemHandler.SimulateGamepadInput(new JObject
                    {
                        ["action"] = "stick", ["stick"] = "left", ["x"] = 0.5f, ["y"] = 0.75f
                    }));
                    var gamepad = Gamepad.current;
                    Assert.IsNotNull(gamepad);
                    var heldStick = gamepad.leftStick.ReadValue();
                    foreach (var action in new[] { "press", "release" })
                    {
                        UpdateEditorInput();
                        AssertSuccess(InputSystemHandler.SimulateGamepadInput(new JObject
                        {
                            ["action"] = "button", ["button"] = "a", ["buttonAction"] = action
                        }));
                        Assert.IsTrue(gamepad.buttonEast.isPressed, $"Held B lost in {updateMode} during A {action}");
                        Assert.AreEqual(heldStick, gamepad.leftStick.ReadValue(), $"Held stick lost in {updateMode} during A {action}");
                    }
                    AssertSuccess(InputSystemHandler.SimulateGamepadInput(new JObject
                    {
                        ["action"] = "button", ["button"] = "b", ["buttonAction"] = "release"
                    }));
                }
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EditorUpdates_DeliverTouchPhaseChanges()
        {
            yield return new EnterPlayMode();
            var phases = new List<UnityEngine.InputSystem.TouchPhase>();
            using (var settings = new SimulationSettingsScope())
            using (var action = new InputAction("touchPhase", InputActionType.PassThrough, "<Touchscreen>/touch0/phase"))
            {
                action.performed += context => phases.Add(context.ReadValue<UnityEngine.InputSystem.TouchPhase>());
                action.Enable();
                foreach (var updateMode in UpdateModes)
                {
                    InputSystem.settings.updateMode = updateMode;
                    phases.Clear();
                    foreach (var phase in new[] { "began", "ended" })
                    {
                        UpdateEditorInput();
                        AssertSuccess(InputSystemHandler.SimulateTouchInput(new JObject
                        {
                            ["action"] = "multi",
                            ["touches"] = new JArray(new JObject
                            {
                                ["x"] = 100, ["y"] = 200, ["phase"] = phase
                            })
                        }));
                    }
                    CollectionAssert.Contains(phases, UnityEngine.InputSystem.TouchPhase.Began, updateMode.ToString());
                    CollectionAssert.Contains(phases, UnityEngine.InputSystem.TouchPhase.Ended, updateMode.ToString());
                }
            }
            yield return new ExitPlayMode();
        }

        private sealed class SimulationSettingsScope : System.IDisposable
        {
            private readonly InputSettings.UpdateMode previousMode = InputSystem.settings.updateMode;
            private readonly InputSettings.EditorInputBehaviorInPlayMode previousRouting = InputSystem.settings.editorInputBehaviorInPlayMode;
            private readonly InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            private readonly int previousFrameRate = Application.targetFrameRate;
            private readonly int previousVSync = QualitySettings.vSyncCount;

            public SimulationSettingsScope()
            {
                QualitySettings.vSyncCount = 0;
                InputSystem.settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            }

            public void Dispose()
            {
                InputSystem.settings.updateMode = previousMode;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousRouting;
                InputSystem.settings.backgroundBehavior = previousBackground;
                Application.targetFrameRate = previousFrameRate;
                QualitySettings.vSyncCount = previousVSync;
            }
        }

        private static void UpdateEditorInput()
        {
            var update = typeof(InputSystem).GetMethod("Update",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            Assert.IsNotNull(update);
            update.Invoke(null, new object[] { InputUpdateType.Editor });
        }

        private static void AssertSuccess(object result)
        {
            var json = JObject.FromObject(result);
            Assert.IsNull(json["error"], json.ToString());
        }
    }
}
#endif
