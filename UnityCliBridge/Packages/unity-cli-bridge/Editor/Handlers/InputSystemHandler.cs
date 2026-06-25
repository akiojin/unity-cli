#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

// Conditionally include Input System namespaces only if available
// This prevents compilation errors when the package is not installed
#pragma warning disable CS0234 // Type or namespace does not exist
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
#pragma warning restore CS0234

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Input System simulation operations for LLM control
    /// </summary>
    public static class InputSystemHandler
    {
        private const string VirtualMouseName = "UnityCliVirtualMouse";
        private const string VirtualKeyboardName = "UnityCliVirtualKeyboard";
        private const string VirtualGamepadName = "UnityCliVirtualGamepad";
        private const string VirtualTouchscreenName = "UnityCliVirtualTouchscreen";

        private static Dictionary<string, InputDevice> activeDevices = new Dictionary<string, InputDevice>();
        private static List<InputEventPtr> queuedEvents = new List<InputEventPtr>();
        private static bool isSimulationActive = false;

        private class ScheduledRelease
        {
            public double ReleaseTime;
            public int MinimumFrame;
            public int ReleaseFrame;
            public Action Callback;
        }

        private static readonly List<ScheduledRelease> scheduledReleases = new List<ScheduledRelease>();
        private static readonly System.Reflection.MethodInfo InputSystemUpdateWithType = typeof(InputSystem).GetMethod(
            "Update",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null,
            new[] { typeof(InputUpdateType) },
            null);

        // Virtual keyboard management
        private static Keyboard virtualKeyboard;
        private static HashSet<Key> pressedKeys = new HashSet<Key>();
        private static string simulatedTypedText = string.Empty;
        private static Vector2 simulatedMousePosition;
        private static bool simulatedMouseLeftButton;
        private static bool simulatedMouseRightButton;
        private static bool simulatedMouseMiddleButton;
        private static Vector2 simulatedMouseScroll;
        private static bool simulatedGamepadButtonA;
        private static bool simulatedGamepadButtonB;
        private static bool simulatedGamepadButtonX;
        private static bool simulatedGamepadButtonY;
        private static bool simulatedGamepadStart;
        private static bool simulatedGamepadSelect;
        private static bool simulatedGamepadLeftShoulder;
        private static bool simulatedGamepadRightShoulder;
        private static bool simulatedGamepadLeftStickButton;
        private static bool simulatedGamepadRightStickButton;
        private static Vector2 simulatedGamepadLeftStick;
        private static Vector2 simulatedGamepadRightStick;
        private static float simulatedGamepadLeftTrigger;
        private static float simulatedGamepadRightTrigger;
        private static Vector2 simulatedGamepadDpad;
        private static readonly List<object> simulatedActiveTouches = new List<object>();
        private static int simulatedTouchPressCount;
        private static int simulatedTouchReleaseCount;
        private static int simulatedTouchMaxSimultaneous;
        private static string simulatedTouchLast = "none";

        static InputSystemHandler()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            EditorApplication.update -= ProcessScheduledReleases;
            EditorApplication.update += ProcessScheduledReleases;
        }

        /// <summary>
        /// Simulates keyboard input
        /// </summary>
        public static object SimulateKeyboardInput(JObject parameters)
        {
            try
            {
                return HandleBatchedActions(parameters, HandleKeyboardAction, "keyboard");
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in SimulateKeyboardInput: {e.Message}");
                return new { error = $"Failed to simulate keyboard input: {e.Message}" };
            }
        }

        private static object HandleKeyboardAction(JObject parameters)
        {
            ProcessScheduledReleases();
            string action = parameters?["action"]?.ToString();

            if (string.IsNullOrEmpty(action))
            {
                return new { error = "action is required (press, release, type, combo)" };
            }

            var keyboard = GetVirtualKeyboard();

            switch (action.ToLowerInvariant())
            {
                case "press":
                    return SimulateKeyPress(keyboard, parameters);

                case "release":
                    return SimulateKeyRelease(keyboard, parameters);

                case "type":
                    return SimulateTextInput(keyboard, parameters);

                case "combo":
                    return SimulateKeyCombo(keyboard, parameters);

                default:
                    return new { error = $"Unknown action: {action}" };
            }
        }

        /// <summary>
        /// Simulates mouse input
        /// </summary>
        public static object SimulateMouseInput(JObject parameters)
        {
            try
            {
                return HandleBatchedActions(parameters, HandleMouseAction, "mouse");
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in SimulateMouseInput: {e.Message}");
                return new { error = $"Failed to simulate mouse input: {e.Message}" };
            }
        }

        private static object HandleMouseAction(JObject parameters)
        {
            ProcessScheduledReleases();
            string action = parameters?["action"]?.ToString();

            if (string.IsNullOrEmpty(action))
            {
                return new { error = "action is required (move, click, drag, scroll, button)" };
            }

            var mouse = GetOrCreateDevice<Mouse>("mouse");

            switch (action.ToLowerInvariant())
            {
                case "move":
                    return SimulateMouseMove(mouse, parameters);

                case "click":
                    return SimulateMouseClick(mouse, parameters);

                case "drag":
                    return SimulateMouseDrag(mouse, parameters);

                case "scroll":
                    return SimulateMouseScroll(mouse, parameters);

                case "button":
                    return SimulateMouseButton(mouse, parameters);

                default:
                    return new { error = $"Unknown action: {action}" };
            }
        }

        /// <summary>
        /// Simulates gamepad input
        /// </summary>
        public static object SimulateGamepadInput(JObject parameters)
        {
            try
            {
                return HandleBatchedActions(parameters, HandleGamepadAction, "gamepad");
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in SimulateGamepadInput: {e.Message}");
                return new { error = $"Failed to simulate gamepad input: {e.Message}" };
            }
        }

        private static object HandleGamepadAction(JObject parameters)
        {
            ProcessScheduledReleases();
            string action = parameters?["action"]?.ToString();

            if (string.IsNullOrEmpty(action))
            {
                return new { error = "action is required (button, stick, trigger, dpad)" };
            }

            var gamepad = GetOrCreateDevice<Gamepad>("gamepad");

            switch (action.ToLowerInvariant())
            {
                case "button":
                    return SimulateGamepadButton(gamepad, parameters);

                case "stick":
                    return SimulateGamepadStick(gamepad, parameters);

                case "trigger":
                    return SimulateGamepadTrigger(gamepad, parameters);

                case "dpad":
                    return SimulateGamepadDPad(gamepad, parameters);

                default:
                    return new { error = $"Unknown action: {action}" };
            }
        }

        /// <summary>
        /// Simulates touch input
        /// </summary>
        public static object SimulateTouchInput(JObject parameters)
        {
            try
            {
                return HandleBatchedActions(parameters, HandleTouchAction, "touch");
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in SimulateTouchInput: {e.Message}");
                return new { error = $"Failed to simulate touch input: {e.Message}" };
            }
        }

        private static object HandleTouchAction(JObject parameters)
        {
            ProcessScheduledReleases();
            string action = parameters?["action"]?.ToString();

            if (string.IsNullOrEmpty(action))
            {
                return new { error = "action is required (tap, swipe, pinch, multi)" };
            }

            var touchscreen = GetOrCreateDevice<Touchscreen>("touchscreen");

            switch (action.ToLowerInvariant())
            {
                case "tap":
                    return SimulateTap(touchscreen, parameters);

                case "swipe":
                    return SimulateSwipe(touchscreen, parameters);

                case "pinch":
                    return SimulatePinch(touchscreen, parameters);

                case "multi":
                    return SimulateMultiTouch(touchscreen, parameters);

                default:
                    return new { error = $"Unknown action: {action}" };
            }
        }

        /// <summary>
        /// Creates a complex input sequence
        /// </summary>
        public static object CreateInputSequence(JObject parameters)
        {
            try
            {
                var sequence = parameters["sequence"]?.ToObject<JArray>();
                int delayBetween = parameters["delayBetween"]?.ToObject<int>() ?? 100;
                
                if (sequence == null || sequence.Count == 0)
                {
                    return new { error = "sequence is required and must not be empty" };
                }

                List<object> results = new List<object>();
                double startedAt = EditorApplication.timeSinceStartup;
                ProcessScheduledReleases();

                for (int index = 0; index < sequence.Count; index++)
                {
                    if (!(sequence[index] is JObject step))
                    {
                        results.Add(new
                        {
                            index,
                            success = false,
                            error = "Invalid sequence step format"
                        });
                        continue;
                    }

                    string inputType = step["type"]?.ToString();
                    var inputParams = step["params"] as JObject;
                    
                    if (string.IsNullOrEmpty(inputType) || inputParams == null)
                    {
                        results.Add(new
                        {
                            index,
                            type = inputType ?? string.Empty,
                            success = false,
                            error = "Invalid sequence step format"
                        });
                        continue;
                    }

                    object result;
                    switch (inputType.ToLowerInvariant())
                    {
                        case "keyboard":
                            result = SimulateKeyboardInput(inputParams);
                            break;
                        case "mouse":
                            result = SimulateMouseInput(inputParams);
                            break;
                        case "gamepad":
                            result = SimulateGamepadInput(inputParams);
                            break;
                        case "touch":
                            result = SimulateTouchInput(inputParams);
                            break;
                        default:
                            result = new { error = $"Unknown input type: {inputType}" };
                            break;
                    }

                    var resultToken = result == null ? JValue.CreateNull() : JToken.FromObject(result);
                    results.Add(new
                    {
                        index,
                        type = inputType,
                        success = resultToken["error"] == null,
                        result = resultToken
                    });

                    if (delayBetween > 0 && index < sequence.Count - 1)
                    {
                        WaitForMilliseconds(delayBetween);
                    }
                }

                ProcessScheduledReleases();
                bool success = results.All(r => !ResultHasError(r));
                int totalDurationMs = Mathf.Max(0, Mathf.RoundToInt((float)((EditorApplication.timeSinceStartup - startedAt) * 1000d)));

                return new
                {
                    success,
                    results = results,
                    totalSteps = sequence.Count,
                    delayBetween = delayBetween,
                    totalDurationMs = totalDurationMs
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in CreateInputSequence: {e.Message}");
                return new { error = $"Failed to create input sequence: {e.Message}" };
            }
        }

        /// <summary>
        /// Gets the current state of all input devices
        /// </summary>
        public static object GetCurrentInputState(JObject parameters)
        {
            try
            {
                ProcessScheduledReleases();
                var state = new
                {
                    simulationActive = isSimulationActive,
                    activeDevices = activeDevices.Keys.ToList(),
                    scheduledReleaseCount = scheduledReleases.Count,
                    keyboard = GetKeyboardState(),
                    mouse = GetMouseState(),
                    gamepad = GetGamepadState(),
                    touchscreen = GetTouchscreenState()
                };

                return state;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("InputSystemHandler", $"Error in GetCurrentInputState: {e.Message}");
                return new { error = $"Failed to get input state: {e.Message}" };
            }
        }

        #region Helper Methods

        /// <summary>
        /// Gets or creates the virtual keyboard device
        /// </summary>
        private static Keyboard GetVirtualKeyboard()
        {
            // Prefer any existing keyboard device (e.g., provided by tests)
            var existingKeyboard = InputSystem.devices
                .OfType<Keyboard>()
                .FirstOrDefault(k => k.added && !IsManagedVirtualKeyboard(k));
            if (existingKeyboard != null)
            {
                existingKeyboard.MakeCurrent();
                return existingKeyboard;
            }

            if (virtualKeyboard == null || !virtualKeyboard.added)
            {
                virtualKeyboard = InputSystem.devices
                    .OfType<Keyboard>()
                    .FirstOrDefault(k => k.added && IsManagedVirtualKeyboard(k));

                if (virtualKeyboard == null)
                {
                    virtualKeyboard = InputSystem.AddDevice<Keyboard>(VirtualKeyboardName);
                    BridgeLogger.Log("InputSystemHandler", "Created virtual keyboard device");
                }
            }

            virtualKeyboard.MakeCurrent();
            return virtualKeyboard;
        }

        private static bool IsManagedVirtualKeyboard(Keyboard keyboard)
        {
            if (keyboard == null || string.IsNullOrEmpty(keyboard.name))
            {
                return false;
            }

            return
                string.Equals(keyboard.name, VirtualKeyboardName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyboard.name, "VirtualKeyboard", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a KeyboardState with the currently pressed keys
        /// </summary>
        private static KeyboardState CreateKeyboardState()
        {
            // Convert HashSet to array for KeyboardState constructor
            var keysArray = new Key[pressedKeys.Count];
            pressedKeys.CopyTo(keysArray);
            return new KeyboardState(keysArray);
        }

        private static void ApplyStateChange<TState>(InputControl control, TState state)
            where TState : struct
        {
            InputState.Change(control, state, GetSimulationUpdateType());
        }

        private static void ApplyStateEvent(InputDevice device, InputEventPtr eventPtr)
        {
            InputState.Change(device, eventPtr, GetSimulationUpdateType());
        }

        private static InputUpdateType GetSimulationUpdateType()
        {
            return Application.isPlaying ? InputUpdateType.Dynamic : default;
        }

        private static void FlushQueuedEvents()
        {
            if (Application.isPlaying && InputSystemUpdateWithType != null)
            {
                InputSystemUpdateWithType.Invoke(null, new object[] { InputUpdateType.Dynamic });
                return;
            }

            InputSystem.Update();
        }

        private static void FlushQueuedEventsInEditMode()
        {
            if (!Application.isPlaying)
            {
                InputSystem.Update();
            }
        }

        private static T GetOrCreateDevice<T>(string deviceName) where T : InputDevice
        {
            if (activeDevices.TryGetValue(deviceName, out var cachedDevice))
            {
                if (cachedDevice is T typed && typed.added)
                {
                    return typed;
                }

                activeDevices.Remove(deviceName);
            }

            T device = null;
            if (Application.isPlaying)
            {
                device = InputSystem.AddDevice<T>(GetVirtualDeviceName<T>(deviceName));
                device.MakeCurrent();
            }

            if (device == null)
            {
                device = InputSystem.devices.OfType<T>().FirstOrDefault(d => d.added);
            }

            if (device == null)
            {
                device = InputSystem.AddDevice<T>();
            }

            activeDevices[deviceName] = device;
            return device;
        }

        private static string GetVirtualDeviceName<T>(string deviceName) where T : InputDevice
        {
            if (typeof(T) == typeof(Mouse))
            {
                return VirtualMouseName;
            }

            if (typeof(T) == typeof(Gamepad))
            {
                return VirtualGamepadName;
            }

            if (typeof(T) == typeof(Touchscreen))
            {
                return VirtualTouchscreenName;
            }

            return string.IsNullOrWhiteSpace(deviceName) ? typeof(T).Name : deviceName;
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                foreach (var key in activeDevices.Where(kvp => kvp.Value == device).Select(kvp => kvp.Key).ToList())
                {
                    activeDevices.Remove(key);
                }

                if (virtualKeyboard == device)
                {
                    virtualKeyboard = null;
                }

                if (device is Keyboard)
                {
                    pressedKeys.Clear();
                }
            }
        }

        private static object SimulateKeyPress(Keyboard keyboard, JObject parameters)
        {
            string keyName = parameters["key"]?.ToString();
            if (string.IsNullOrEmpty(keyName))
            {
                return new { error = "key is required" };
            }
            
            // Handle single character keys (w, a, s, d) by converting to uppercase
            if (keyName.Length == 1)
            {
                keyName = keyName.ToUpper();
            }
            
            if (!Enum.TryParse<Key>(keyName, true, out Key key))
            {
                return new { error = $"Invalid key: {keyName}" };
            }
            
            // Add key to pressed keys set
            if (pressedKeys.Add(key))
            {
                BridgeLogger.Log("InputSystemHandler", $"Simulating key press: {keyName} ({keyboard.name})");
                
                // Create new keyboard state with all pressed keys
                var keyboardState = CreateKeyboardState();
                
                ApplyStateChange(keyboard, keyboardState);

                if (TryGetHoldSeconds(parameters, out double holdSeconds))
                {
                    ScheduleRelease(holdSeconds, () =>
                    {
                        var releaseParams = new JObject
                        {
                            ["key"] = keyName
                        };
                        SimulateKeyRelease(GetVirtualKeyboard(), releaseParams);
                    });
                }
            }
            
            return new
            {
                success = true,
                action = "press",
                key = keyName,
                message = $"Key {keyName} pressed"
            };
        }

        private static object SimulateKeyRelease(Keyboard keyboard, JObject parameters)
        {
            string keyName = parameters["key"]?.ToString();
            if (string.IsNullOrEmpty(keyName))
            {
                return new { error = "key is required" };
            }
            
            // Handle single character keys (w, a, s, d) by converting to uppercase
            if (keyName.Length == 1)
            {
                keyName = keyName.ToUpper();
            }
            
            if (!Enum.TryParse<Key>(keyName, true, out Key key))
            {
                return new { error = $"Invalid key: {keyName}" };
            }
            
            // Remove key from pressed keys set
            if (pressedKeys.Remove(key))
            {
                BridgeLogger.Log("InputSystemHandler", $"Simulating key release: {keyName} ({keyboard.name})");
                
                // Create new keyboard state with remaining pressed keys
                var keyboardState = CreateKeyboardState();
                
                ApplyStateChange(keyboard, keyboardState);
            }
            
            return new
            {
                success = true,
                action = "release",
                key = keyName,
                message = $"Key {keyName} released"
            };
        }

        private static object SimulateTextInput(Keyboard keyboard, JObject parameters)
        {
            string text = parameters["text"]?.ToString();
            if (string.IsNullOrEmpty(text))
            {
                return new { error = "text is required" };
            }
            
            BridgeLogger.Log("InputSystemHandler", $"Simulating text input: \"{text}\" ({keyboard.name})");
            simulatedTypedText = text;
            
            // Use QueueTextEvent for text input (better for UI/TMP)
            foreach (char c in text)
            {
                InputSystem.QueueTextEvent(keyboard, c);
            }
            
            FlushQueuedEvents();
            
            return new
            {
                success = true,
                action = "type",
                text = text,
                message = $"Typed: {text}"
            };
        }

        private static object SimulateKeyCombo(Keyboard keyboard, JObject parameters)
        {
            var keys = parameters["keys"]?.ToObject<string[]>();
            if (keys == null || keys.Length == 0)
            {
                return new { error = "keys array is required" };
            }
            
            BridgeLogger.Log("InputSystemHandler", $"Simulating key combo: {string.Join("+", keys)} ({keyboard.name})");
            
            var normalizedKeys = new List<string>();

            // Press all keys
            foreach (string keyName in keys)
            {
                var actualKeyName = keyName.Length == 1 ? keyName.ToUpper() : keyName;
                if (Enum.TryParse<Key>(actualKeyName, true, out Key key))
                {
                    pressedKeys.Add(key);
                    normalizedKeys.Add(actualKeyName);
                }
            }
            
            var keyboardState = CreateKeyboardState();
            ApplyStateChange(keyboard, keyboardState);

            if (TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    foreach (string keyName in normalizedKeys)
                    {
                        var releaseParams = new JObject
                        {
                            ["key"] = keyName
                        };
                        SimulateKeyRelease(GetVirtualKeyboard(), releaseParams);
                    }
                });
            }
            else
            {
                foreach (string keyName in normalizedKeys)
                {
                    if (Enum.TryParse<Key>(keyName, true, out Key key))
                    {
                        pressedKeys.Remove(key);
                    }
                }

                keyboardState = CreateKeyboardState();
                ApplyStateChange(keyboard, keyboardState);
            }
            
            return new
            {
                success = true,
                action = "combo",
                keys = keys,
                message = $"Key combo: {string.Join("+", keys)}"
            };
        }

        private static object SimulateMouseMove(Mouse mouse, JObject parameters)
        {
            float x = parameters["x"]?.ToObject<float>() ?? 0;
            float y = parameters["y"]?.ToObject<float>() ?? 0;
            bool absolute = parameters["absolute"]?.ToObject<bool>() ?? true;
            
            Vector2 position = new Vector2(x, y);

            mouse.CopyState<MouseState>(out var mouseState);
            if (absolute)
            {
                mouseState.position = position;
                mouseState.delta = Vector2.zero;
            }
            else
            {
                mouseState.delta = position;
                mouseState.position += position;
            }

            simulatedMousePosition = mouseState.position;
            ApplyStateChange(mouse, mouseState);
            
            return new
            {
                success = true,
                action = "move",
                position = new { x, y },
                absolute = absolute,
                message = $"Mouse moved to ({x}, {y})"
            };
        }

        private static object SimulateMouseClick(Mouse mouse, JObject parameters)
        {
            string button = parameters["button"]?.ToString() ?? "left";
            int clickCount = parameters["clickCount"]?.ToObject<int>() ?? 1;

            if (!TryParseMouseButton(button, out var mouseButton))
            {
                return new { error = $"Invalid mouse button: {button}" };
            }

            for (int i = 0; i < clickCount; i++)
            {
                mouse.CopyState<MouseState>(out var pressState);
                pressState = pressState.WithButton(mouseButton, true);
                ApplyMouseButtonSnapshot(mouseButton, true);
                ApplyStateChange(mouse, pressState);

                mouse.CopyState<MouseState>(out var releaseState);
                releaseState = releaseState.WithButton(mouseButton, false);
                ApplyMouseButtonSnapshot(mouseButton, false);
                ApplyStateChange(mouse, releaseState);
            }
            
            return new
            {
                success = true,
                action = "click",
                button = button,
                clickCount = clickCount,
                message = $"Mouse {button} clicked {clickCount} time(s)"
            };
        }

        private static object SimulateMouseButton(Mouse mouse, JObject parameters)
        {
            string button = parameters["button"]?.ToString() ?? "left";
            string action = parameters["buttonAction"]?.ToString() ?? "press";

            if (!TryParseMouseButton(button, out var mouseButton))
            {
                return new { error = $"Invalid mouse button: {button}" };
            }

            float value = string.Equals(action, "release", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
            mouse.CopyState<MouseState>(out var mouseState);
            mouseState = mouseState.WithButton(mouseButton, value > 0f);
            ApplyMouseButtonSnapshot(mouseButton, value > 0f);
            ApplyStateChange(mouse, mouseState);

            if (!string.Equals(action, "release", StringComparison.OrdinalIgnoreCase) && TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    var releaseParams = new JObject
                    {
                        ["button"] = button,
                        ["buttonAction"] = "release"
                    };
                    SimulateMouseButton(GetOrCreateDevice<Mouse>("mouse"), releaseParams);
                });
            }

            return new
            {
                success = true,
                action = action,
                button,
                buttonAction = action,
                message = $"Mouse {button} {action}d"
            };
        }

        private static object SimulateMouseDrag(Mouse mouse, JObject parameters)
        {
            float startX = parameters["startX"]?.ToObject<float>() ?? 0;
            float startY = parameters["startY"]?.ToObject<float>() ?? 0;
            float endX = parameters["endX"]?.ToObject<float>() ?? 0;
            float endY = parameters["endY"]?.ToObject<float>() ?? 0;
            string button = parameters["button"]?.ToString() ?? "left";

            if (!TryParseMouseButton(button, out var mouseButton))
            {
                return new { error = $"Invalid mouse button: {button}" };
            }

            mouse.CopyState<MouseState>(out var startState);
            startState.position = new Vector2(startX, startY);
            startState.delta = Vector2.zero;
            simulatedMousePosition = startState.position;
            ApplyStateChange(mouse, startState);

            mouse.CopyState<MouseState>(out var pressState);
            pressState = pressState.WithButton(mouseButton, true);
            ApplyMouseButtonSnapshot(mouseButton, true);
            ApplyStateChange(mouse, pressState);

            mouse.CopyState<MouseState>(out var dragState);
            dragState.delta = new Vector2(endX - startX, endY - startY);
            dragState.position = new Vector2(endX, endY);
            simulatedMousePosition = dragState.position;
            ApplyStateChange(mouse, dragState);

            mouse.CopyState<MouseState>(out var releaseState);
            releaseState = releaseState.WithButton(mouseButton, false);
            ApplyMouseButtonSnapshot(mouseButton, false);
            ApplyStateChange(mouse, releaseState);
            
            return new
            {
                success = true,
                action = "drag",
                start = new { x = startX, y = startY },
                end = new { x = endX, y = endY },
                button = button,
                message = $"Mouse dragged from ({startX}, {startY}) to ({endX}, {endY})"
            };
        }

        private static object SimulateMouseScroll(Mouse mouse, JObject parameters)
        {
            float deltaX = parameters["deltaX"]?.ToObject<float>() ?? 0;
            float deltaY = parameters["deltaY"]?.ToObject<float>() ?? 0;

            mouse.CopyState<MouseState>(out var mouseState);
            mouseState.scroll = new Vector2(deltaX, deltaY);
            simulatedMouseScroll = mouseState.scroll;
            ApplyStateChange(mouse, mouseState);
            
            return new
            {
                success = true,
                action = "scroll",
                delta = new { x = deltaX, y = deltaY },
                message = $"Mouse scrolled by ({deltaX}, {deltaY})"
            };
        }

        private static ButtonControl GetMouseButton(Mouse mouse, string button)
        {
            switch (button.ToLower())
            {
                case "left":
                    return mouse.leftButton;
                case "right":
                    return mouse.rightButton;
                case "middle":
                    return mouse.middleButton;
                default:
                    return null;
            }
        }

        private static bool TryParseMouseButton(string button, out MouseButton mouseButton)
        {
            mouseButton = MouseButton.Left;
            switch ((button ?? string.Empty).ToLowerInvariant())
            {
                case "left":
                    mouseButton = MouseButton.Left;
                    return true;
                case "right":
                    mouseButton = MouseButton.Right;
                    return true;
                case "middle":
                    mouseButton = MouseButton.Middle;
                    return true;
                case "forward":
                    mouseButton = MouseButton.Forward;
                    return true;
                case "back":
                    mouseButton = MouseButton.Back;
                    return true;
                default:
                    return false;
            }
        }

        private static object SimulateGamepadButton(Gamepad gamepad, JObject parameters)
        {
            string buttonName = parameters["button"]?.ToString();
            string action = parameters["buttonAction"]?.ToString() ?? "press";
            
            if (string.IsNullOrEmpty(buttonName))
            {
                return new { error = "button is required" };
            }
            
            ButtonControl button = GetGamepadButton(gamepad, buttonName);
            if (button == null)
            {
                return new { error = $"Invalid gamepad button: {buttonName}" };
            }
            
            bool pressed = !string.Equals(action, "release", StringComparison.OrdinalIgnoreCase);
            gamepad.CopyState<GamepadState>(out var gamepadState);
            if (TryParseGamepadButton(buttonName, out var gamepadButton))
            {
                gamepadState = gamepadState.WithButton(gamepadButton, pressed);
                ApplyGamepadButtonSnapshot(gamepadButton, pressed);
            }
            else
            {
                return new { error = $"Invalid gamepad button: {buttonName}" };
            }

            ApplyStateChange(gamepad, gamepadState);

            if (!string.Equals(action, "release", StringComparison.OrdinalIgnoreCase) && TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    var releaseParams = new JObject
                    {
                        ["button"] = buttonName,
                        ["buttonAction"] = "release"
                    };
                    SimulateGamepadButton(GetOrCreateDevice<Gamepad>("gamepad"), releaseParams);
                });
            }
            
            return new
            {
                success = true,
                action = action,
                button = buttonName,
                message = $"Gamepad button {buttonName} {action}d"
            };
        }

        private static object SimulateGamepadStick(Gamepad gamepad, JObject parameters)
        {
            string stick = parameters["stick"]?.ToString() ?? "left";
            float x = parameters["x"]?.ToObject<float>() ?? 0;
            float y = parameters["y"]?.ToObject<float>() ?? 0;
            
            Vector2 desired = new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
            Vector2 afterStick = ApplyAxisDeadzoneInverse(desired);
            Vector2 raw = ApplyStickDeadzoneInverse(afterStick);

            gamepad.CopyState<GamepadState>(out var state);
            if (stick == "left")
            {
                state.leftStick = raw;
                simulatedGamepadLeftStick = new Vector2(x, y);
            }
            else
            {
                state.rightStick = raw;
                simulatedGamepadRightStick = new Vector2(x, y);
            }

            ApplyStateChange(gamepad, state);

            if (TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    var releaseParams = new JObject
                    {
                        ["stick"] = stick,
                        ["x"] = 0,
                        ["y"] = 0
                    };
                    SimulateGamepadStick(GetOrCreateDevice<Gamepad>("gamepad"), releaseParams);
                });
            }

            return new
            {
                success = true,
                action = "stick",
                stick = stick,
                value = new { x, y },
                message = $"Gamepad {stick} stick set to ({x}, {y})"
            };
        }

        private static Vector2 ApplyAxisDeadzoneInverse(Vector2 desired)
        {
            const float axisMin = 0.125f;

            if (desired == Vector2.zero)
            {
                return Vector2.zero;
            }

            float InverseComponent(float v)
            {
                var sign = Mathf.Sign(v);
                var magnitude = Mathf.Abs(v);
                if (magnitude <= 0f)
                {
                    return 0f;
                }
                var raw = magnitude * (1f - axisMin) + axisMin;
                return sign * Mathf.Clamp(raw, 0f, 1f);
            }

            return new Vector2(InverseComponent(desired.x), InverseComponent(desired.y));
        }

        private static Vector2 ApplyStickDeadzoneInverse(Vector2 desired)
        {
            const float stickMin = 0.125f;
            const float stickMax = 0.925f;

            var magnitude = desired.magnitude;
            if (magnitude <= Mathf.Epsilon)
            {
                return Vector2.zero;
            }

            var rawMagnitude = magnitude * (stickMax - stickMin) + stickMin;
            rawMagnitude = Mathf.Clamp(rawMagnitude, 0f, 1f);
            return desired.normalized * rawMagnitude;
        }

        private static object SimulateGamepadTrigger(Gamepad gamepad, JObject parameters)
        {
            string trigger = parameters["trigger"]?.ToString() ?? "left";
            float value = parameters["value"]?.ToObject<float>() ?? 0;
            
            value = Mathf.Clamp01(value);

            gamepad.CopyState<GamepadState>(out var gamepadState);
            if (trigger == "left")
            {
                gamepadState.leftTrigger = value;
                simulatedGamepadLeftTrigger = value;
            }
            else
            {
                gamepadState.rightTrigger = value;
                simulatedGamepadRightTrigger = value;
            }

            ApplyStateChange(gamepad, gamepadState);

            if (value > 0f && TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    var releaseParams = new JObject
                    {
                        ["trigger"] = trigger,
                        ["value"] = 0f
                    };
                    SimulateGamepadTrigger(GetOrCreateDevice<Gamepad>("gamepad"), releaseParams);
                });
            }
            
            return new
            {
                success = true,
                action = "trigger",
                trigger = trigger,
                value = value,
                message = $"Gamepad {trigger} trigger set to {value}"
            };
        }

        private static object SimulateGamepadDPad(Gamepad gamepad, JObject parameters)
        {
            string direction = parameters["direction"]?.ToString();
            
            if (string.IsNullOrEmpty(direction))
            {
                return new { error = "direction is required" };
            }
            
            Vector2 value = Vector2.zero;
            switch (direction.ToLower())
            {
                case "up":
                    value = Vector2.up;
                    break;
                case "down":
                    value = Vector2.down;
                    break;
                case "left":
                    value = Vector2.left;
                    break;
                case "right":
                    value = Vector2.right;
                    break;
                case "none":
                    value = Vector2.zero;
                    break;
                default:
                    return new { error = $"Invalid direction: {direction}" };
            }
            
            gamepad.CopyState<GamepadState>(out var gamepadState);
            gamepadState = gamepadState
                .WithButton(GamepadButton.DpadUp, false)
                .WithButton(GamepadButton.DpadDown, false)
                .WithButton(GamepadButton.DpadLeft, false)
                .WithButton(GamepadButton.DpadRight, false);

            switch (direction.ToLowerInvariant())
            {
                case "up":
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadUp, true);
                    simulatedGamepadDpad = Vector2.up;
                    break;
                case "down":
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadDown, true);
                    simulatedGamepadDpad = Vector2.down;
                    break;
                case "left":
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadLeft, true);
                    simulatedGamepadDpad = Vector2.left;
                    break;
                case "right":
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadRight, true);
                    simulatedGamepadDpad = Vector2.right;
                    break;
                case "none":
                    simulatedGamepadDpad = Vector2.zero;
                    break;
            }

            ApplyStateChange(gamepad, gamepadState);

            if (!string.Equals(direction, "none", StringComparison.OrdinalIgnoreCase) && TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                ScheduleRelease(holdSeconds, () =>
                {
                    var releaseParams = new JObject
                    {
                        ["direction"] = "none"
                    };
                    SimulateGamepadDPad(GetOrCreateDevice<Gamepad>("gamepad"), releaseParams);
                });
            }
            
            return new
            {
                success = true,
                action = "dpad",
                direction = direction,
                message = $"Gamepad D-Pad {direction}"
            };
        }

        private static object SimulateTap(Touchscreen touchscreen, JObject parameters)
        {
            float x = parameters["x"]?.ToObject<float>() ?? 0;
            float y = parameters["y"]?.ToObject<float>() ?? 0;
            int touchId = parameters["touchId"]?.ToObject<int>() ?? 0;
            
            var touch = touchscreen.touches[touchId];
            
            using (StateEvent.From(touchscreen, out var beginEvent))
            {
                touch.position.WriteValueIntoEvent(new Vector2(x, y), beginEvent);
                touch.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginEvent);
                ApplyStateEvent(touchscreen, beginEvent);
            }
            UpdateSimulatedTouch(touchId, new Vector2(x, y), UnityEngine.InputSystem.TouchPhase.Began);

            if (TryGetHoldSeconds(parameters, out double holdSeconds))
            {
                WaitForMilliseconds(Mathf.Max(1, Mathf.RoundToInt((float)(holdSeconds * 1000d))));
            }
            
            using (StateEvent.From(touchscreen, out var endEvent))
            {
                touch.position.WriteValueIntoEvent(new Vector2(x, y), endEvent);
                touch.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endEvent);
                ApplyStateEvent(touchscreen, endEvent);
            }
            UpdateSimulatedTouch(touchId, new Vector2(x, y), UnityEngine.InputSystem.TouchPhase.Ended);
            
            return new
            {
                success = true,
                action = "tap",
                position = new { x, y },
                touchId = touchId,
                message = $"Tap at ({x}, {y})"
            };
        }

        private static object SimulateSwipe(Touchscreen touchscreen, JObject parameters)
        {
            float startX = parameters["startX"]?.ToObject<float>() ?? 0;
            float startY = parameters["startY"]?.ToObject<float>() ?? 0;
            float endX = parameters["endX"]?.ToObject<float>() ?? 0;
            float endY = parameters["endY"]?.ToObject<float>() ?? 0;
            int duration = parameters["duration"]?.ToObject<int>() ?? 500;
            int touchId = parameters["touchId"]?.ToObject<int>() ?? 0;
            
            var touch = touchscreen.touches[touchId];
            
            using (StateEvent.From(touchscreen, out var beginEvent))
            {
                touch.position.WriteValueIntoEvent(new Vector2(startX, startY), beginEvent);
                touch.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginEvent);
                ApplyStateEvent(touchscreen, beginEvent);
            }
            UpdateSimulatedTouch(touchId, new Vector2(startX, startY), UnityEngine.InputSystem.TouchPhase.Began);

            if (duration > 1)
            {
                WaitForMilliseconds(Mathf.Max(1, duration / 2));
            }
            
            using (StateEvent.From(touchscreen, out var moveEvent))
            {
                touch.position.WriteValueIntoEvent(new Vector2(endX, endY), moveEvent);
                touch.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, moveEvent);
                ApplyStateEvent(touchscreen, moveEvent);
            }
            UpdateSimulatedTouch(touchId, new Vector2(endX, endY), UnityEngine.InputSystem.TouchPhase.Moved);

            if (duration > 1)
            {
                WaitForMilliseconds(Mathf.Max(1, duration - Mathf.Max(1, duration / 2)));
            }
            
            using (StateEvent.From(touchscreen, out var endEvent))
            {
                touch.position.WriteValueIntoEvent(new Vector2(endX, endY), endEvent);
                touch.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endEvent);
                ApplyStateEvent(touchscreen, endEvent);
            }
            UpdateSimulatedTouch(touchId, new Vector2(endX, endY), UnityEngine.InputSystem.TouchPhase.Ended);
            
            return new
            {
                success = true,
                action = "swipe",
                start = new { x = startX, y = startY },
                end = new { x = endX, y = endY },
                duration = duration,
                touchId = touchId,
                message = $"Swipe from ({startX}, {startY}) to ({endX}, {endY})"
            };
        }

        private static object SimulatePinch(Touchscreen touchscreen, JObject parameters)
        {
            float centerX = parameters["centerX"]?.ToObject<float>() ?? Screen.width / 2;
            float centerY = parameters["centerY"]?.ToObject<float>() ?? Screen.height / 2;
            float startDistance = parameters["startDistance"]?.ToObject<float>() ?? 100;
            float endDistance = parameters["endDistance"]?.ToObject<float>() ?? 200;
            
            // Simulate two-finger pinch
            var touch1 = touchscreen.touches[0];
            var touch2 = touchscreen.touches[1];
            
            // Calculate positions
            Vector2 center = new Vector2(centerX, centerY);
            Vector2 offset1Start = new Vector2(startDistance / 2, 0);
            Vector2 offset2Start = new Vector2(-startDistance / 2, 0);
            Vector2 offset1End = new Vector2(endDistance / 2, 0);
            Vector2 offset2End = new Vector2(-endDistance / 2, 0);
            
            using (StateEvent.From(touchscreen, out var beginEvent))
            {
                touch1.position.WriteValueIntoEvent(center + offset1Start, beginEvent);
                touch1.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginEvent);
                
                touch2.position.WriteValueIntoEvent(center + offset2Start, beginEvent);
                touch2.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Began, beginEvent);
                
                ApplyStateEvent(touchscreen, beginEvent);
            }
            UpdateSimulatedTouch(0, center + offset1Start, UnityEngine.InputSystem.TouchPhase.Began);
            UpdateSimulatedTouch(1, center + offset2Start, UnityEngine.InputSystem.TouchPhase.Began);
            
            using (StateEvent.From(touchscreen, out var moveEvent))
            {
                touch1.position.WriteValueIntoEvent(center + offset1End, moveEvent);
                touch1.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, moveEvent);
                
                touch2.position.WriteValueIntoEvent(center + offset2End, moveEvent);
                touch2.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Moved, moveEvent);
                
                ApplyStateEvent(touchscreen, moveEvent);
            }
            UpdateSimulatedTouch(0, center + offset1End, UnityEngine.InputSystem.TouchPhase.Moved);
            UpdateSimulatedTouch(1, center + offset2End, UnityEngine.InputSystem.TouchPhase.Moved);
            
            using (StateEvent.From(touchscreen, out var endEvent))
            {
                touch1.position.WriteValueIntoEvent(center + offset1End, endEvent);
                touch1.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endEvent);
                
                touch2.position.WriteValueIntoEvent(center + offset2End, endEvent);
                touch2.phase.WriteValueIntoEvent(UnityEngine.InputSystem.TouchPhase.Ended, endEvent);
                
                ApplyStateEvent(touchscreen, endEvent);
            }
            UpdateSimulatedTouch(0, center + offset1End, UnityEngine.InputSystem.TouchPhase.Ended);
            UpdateSimulatedTouch(1, center + offset2End, UnityEngine.InputSystem.TouchPhase.Ended);
            
            return new
            {
                success = true,
                action = "pinch",
                center = new { x = centerX, y = centerY },
                startDistance = startDistance,
                endDistance = endDistance,
                message = $"Pinch gesture from {startDistance} to {endDistance}"
            };
        }

        private static object SimulateMultiTouch(Touchscreen touchscreen, JObject parameters)
        {
            var touches = parameters["touches"]?.ToObject<JArray>();
            
            if (touches == null || touches.Count == 0)
            {
                return new { error = "touches array is required" };
            }
            
            List<object> results = new List<object>();
            
            for (int i = 0; i < touches.Count && i < touchscreen.touches.Count; i++)
            {
                var touchData = touches[i] as JObject;
                if (touchData != null)
                {
                    float x = touchData["x"]?.ToObject<float>() ?? 0;
                    float y = touchData["y"]?.ToObject<float>() ?? 0;
                    string phase = touchData["phase"]?.ToString() ?? "tap";
                    
                    var touch = touchscreen.touches[i];
                    
                    using (StateEvent.From(touchscreen, out var eventPtr))
                    {
                        touch.position.WriteValueIntoEvent(new Vector2(x, y), eventPtr);
                        
                        UnityEngine.InputSystem.TouchPhase touchPhase;
                        switch (phase.ToLower())
                        {
                            case "began":
                                touchPhase = UnityEngine.InputSystem.TouchPhase.Began;
                                // Note: isInProgress is automatically handled by the Input System based on phase
                                break;
                            case "moved":
                                touchPhase = UnityEngine.InputSystem.TouchPhase.Moved;
                                break;
                            case "ended":
                                touchPhase = UnityEngine.InputSystem.TouchPhase.Ended;
                                // Note: isInProgress is automatically handled by the Input System based on phase
                                break;
                            default:
                                touchPhase = UnityEngine.InputSystem.TouchPhase.Stationary;
                                break;
                        }
                        
                        touch.phase.WriteValueIntoEvent(touchPhase, eventPtr);
                        ApplyStateEvent(touchscreen, eventPtr);
                        UpdateSimulatedTouch(i, new Vector2(x, y), touchPhase);
                    }
                    
                    results.Add(new
                    {
                        touchId = i,
                        position = new { x, y },
                        phase = phase
                    });
                }
            }
            
            return new
            {
                success = true,
                action = "multi",
                touches = results,
                message = $"Multi-touch with {results.Count} touches"
            };
        }

        private static Key CharToKey(char c)
        {
            // Simple character to key mapping
            if (char.IsLetter(c))
            {
                string keyName = char.ToUpper(c).ToString();
                if (Enum.TryParse<Key>(keyName, out Key key))
                {
                    return key;
                }
            }
            else if (char.IsDigit(c))
            {
                string keyName = "Digit" + c;
                if (Enum.TryParse<Key>(keyName, out Key key))
                {
                    return key;
                }
            }
            else
            {
                switch (c)
                {
                    case ' ':
                        return Key.Space;
                    case '.':
                        return Key.Period;
                    case ',':
                        return Key.Comma;
                    case ';':
                        return Key.Semicolon;
                    case '/':
                        return Key.Slash;
                    case '\\':
                        return Key.Backslash;
                    case '-':
                        return Key.Minus;
                    case '=':
                        return Key.Equals;
                    case '\n':
                        return Key.Enter;
                    case '\t':
                        return Key.Tab;
                }
            }
            
            return Key.None;
        }

        private static object HandleBatchedActions(JObject parameters, Func<JObject, object> handler, string actionLabel)
        {
            var actionsArray = parameters?["actions"] as JArray;
            if (actionsArray == null || actionsArray.Count == 0)
            {
                ProcessScheduledReleases();
                return handler(parameters);
            }

            var results = new List<object>();
            foreach (var actionToken in actionsArray)
            {
                ProcessScheduledReleases();
                if (actionToken is JObject actionParams)
                {
                    results.Add(handler(actionParams));
                }
                else
                {
                    results.Add(new { error = $"Each {actionLabel} action entry must be an object" });
                }
            }

            bool success = results.All(r => !ResultHasError(r));

            return new
            {
                success,
                totalActions = results.Count,
                results
            };
        }

        private static bool ResultHasError(object result)
        {
            if (result == null)
            {
                return true;
            }

            try
            {
                var token = JToken.FromObject(result);
                return token?["error"] != null;
            }
            catch
            {
                return true;
            }
        }

        private static bool TryGetHoldSeconds(JObject parameters, out double seconds)
        {
            seconds = 0;
            if (parameters == null)
            {
                return false;
            }

            var holdToken = parameters["holdSeconds"];
            if (holdToken == null)
            {
                return false;
            }

            double parsed;
            try
            {
                parsed = holdToken.ToObject<double>();
            }
            catch
            {
                return false;
            }

            if (parsed <= 0)
            {
                return false;
            }

            seconds = parsed;
            return true;
        }

        private static void ScheduleRelease(double delaySeconds, Action releaseAction)
        {
            if (delaySeconds <= 0 || releaseAction == null)
            {
                return;
            }

            scheduledReleases.Add(new ScheduledRelease
            {
                ReleaseTime = EditorApplication.timeSinceStartup + delaySeconds,
                MinimumFrame = Application.isPlaying ? Time.frameCount + 1 : 0,
                ReleaseFrame = Application.isPlaying ? Time.frameCount + Mathf.Max(1, Mathf.CeilToInt((float)(delaySeconds * 60d))) : 0,
                Callback = releaseAction
            });
        }

        private static void WaitForMilliseconds(int delayMs)
        {
            if (delayMs <= 0)
            {
                ProcessScheduledReleases();
                return;
            }

            double target = EditorApplication.timeSinceStartup + (delayMs / 1000.0d);
            while (EditorApplication.timeSinceStartup < target)
            {
                ProcessScheduledReleases();

                int remainingMs = Mathf.Max(1, Mathf.CeilToInt((float)((target - EditorApplication.timeSinceStartup) * 1000d)));
                Thread.Sleep(Math.Min(remainingMs, 5));
            }

            ProcessScheduledReleases();
        }

        private static void ProcessScheduledReleases()
        {
            if (scheduledReleases.Count == 0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            for (int i = scheduledReleases.Count - 1; i >= 0; i--)
            {
                if (Application.isPlaying && Time.frameCount < scheduledReleases[i].MinimumFrame)
                {
                    continue;
                }

                bool reachedTime = now >= scheduledReleases[i].ReleaseTime;
                bool reachedFrame = Application.isPlaying && Time.frameCount >= scheduledReleases[i].ReleaseFrame;
                if (reachedTime || reachedFrame)
                {
                    try
                    {
                        scheduledReleases[i].Callback?.Invoke();
                    }
                    catch (Exception e)
                    {
                        BridgeLogger.LogError("InputSystemHandler", $"Error executing scheduled release: {e.Message}");
                    }

                    scheduledReleases.RemoveAt(i);
                }
            }
        }

        private static ButtonControl GetGamepadButton(Gamepad gamepad, string buttonName)
        {
            switch (buttonName.ToLower())
            {
                case "a":
                case "cross":
                    return gamepad.buttonSouth;
                case "b":
                case "circle":
                    return gamepad.buttonEast;
                case "x":
                case "square":
                    return gamepad.buttonWest;
                case "y":
                case "triangle":
                    return gamepad.buttonNorth;
                case "start":
                    return gamepad.startButton;
                case "select":
                    return gamepad.selectButton;
                case "leftshoulder":
                case "l1":
                    return gamepad.leftShoulder;
                case "rightshoulder":
                case "r1":
                    return gamepad.rightShoulder;
                case "leftstick":
                case "l3":
                    return gamepad.leftStickButton;
                case "rightstick":
                case "r3":
                    return gamepad.rightStickButton;
                default:
                    return null;
            }
        }

        private static bool TryParseGamepadButton(string buttonName, out GamepadButton gamepadButton)
        {
            gamepadButton = GamepadButton.South;
            switch ((buttonName ?? string.Empty).ToLowerInvariant())
            {
                case "a":
                case "cross":
                    gamepadButton = GamepadButton.South;
                    return true;
                case "b":
                case "circle":
                    gamepadButton = GamepadButton.East;
                    return true;
                case "x":
                case "square":
                    gamepadButton = GamepadButton.West;
                    return true;
                case "y":
                case "triangle":
                    gamepadButton = GamepadButton.North;
                    return true;
                case "start":
                    gamepadButton = GamepadButton.Start;
                    return true;
                case "select":
                    gamepadButton = GamepadButton.Select;
                    return true;
                case "leftshoulder":
                case "l1":
                    gamepadButton = GamepadButton.LeftShoulder;
                    return true;
                case "rightshoulder":
                case "r1":
                    gamepadButton = GamepadButton.RightShoulder;
                    return true;
                case "leftstick":
                case "l3":
                    gamepadButton = GamepadButton.LeftStick;
                    return true;
                case "rightstick":
                case "r3":
                    gamepadButton = GamepadButton.RightStick;
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyGamepadButtonSnapshot(GamepadButton gamepadButton, bool pressed)
        {
            switch (gamepadButton)
            {
                case GamepadButton.South:
                    simulatedGamepadButtonA = pressed;
                    break;
                case GamepadButton.East:
                    simulatedGamepadButtonB = pressed;
                    break;
                case GamepadButton.West:
                    simulatedGamepadButtonX = pressed;
                    break;
                case GamepadButton.North:
                    simulatedGamepadButtonY = pressed;
                    break;
                case GamepadButton.Start:
                    simulatedGamepadStart = pressed;
                    break;
                case GamepadButton.Select:
                    simulatedGamepadSelect = pressed;
                    break;
                case GamepadButton.LeftShoulder:
                    simulatedGamepadLeftShoulder = pressed;
                    break;
                case GamepadButton.RightShoulder:
                    simulatedGamepadRightShoulder = pressed;
                    break;
                case GamepadButton.LeftStick:
                    simulatedGamepadLeftStickButton = pressed;
                    break;
                case GamepadButton.RightStick:
                    simulatedGamepadRightStickButton = pressed;
                    break;
            }
        }

        private static void ApplyMouseButtonSnapshot(MouseButton mouseButton, bool pressed)
        {
            switch (mouseButton)
            {
                case MouseButton.Left:
                    simulatedMouseLeftButton = pressed;
                    break;
                case MouseButton.Right:
                    simulatedMouseRightButton = pressed;
                    break;
                case MouseButton.Middle:
                    simulatedMouseMiddleButton = pressed;
                    break;
            }
        }

        private static void UpdateSimulatedTouch(int touchId, Vector2 position, UnityEngine.InputSystem.TouchPhase phase)
        {
            simulatedActiveTouches.RemoveAll(touch =>
            {
                var token = JToken.FromObject(touch);
                return token["id"]?.ToObject<int>() == touchId;
            });

            if (phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                simulatedTouchPressCount++;
            }
            else if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                simulatedTouchReleaseCount++;
            }

            if (phase != UnityEngine.InputSystem.TouchPhase.None)
            {
                simulatedTouchLast = string.Format("{0}:{1}@{2:0.0},{3:0.0}", touchId, phase, position.x, position.y);
            }

            if (phase == UnityEngine.InputSystem.TouchPhase.None ||
                phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                return;
            }

            simulatedActiveTouches.Add(new
            {
                id = touchId,
                position = new { x = position.x, y = position.y },
                phase = phase.ToString()
            });

            simulatedTouchMaxSimultaneous = Mathf.Max(simulatedTouchMaxSimultaneous, simulatedActiveTouches.Count);
        }

        private static object GetKeyboardState()
        {
            var keyboards = InputSystem.devices
                .OfType<Keyboard>()
                .Where(device => device != null && device.added)
                .ToList();

            if (keyboards.Count == 0 && pressedKeys.Count == 0)
            {
                return null;
            }

            var currentlyPressed = keyboards
                .SelectMany(keyboard => keyboard.allKeys.Where(k => k != null && k.isPressed).Select(k => k.name))
                .Concat(pressedKeys.Select(key => key.ToString().ToLowerInvariant()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new
            {
                connected = true,
                pressedKeys = currentlyPressed,
                deviceCount = keyboards.Count,
                lastTypedText = simulatedTypedText
            };
        }

        private static object GetMouseState()
        {
            var mice = GetTrackedAndKnownDevices<Mouse>("mouse");
            bool hasSnapshot =
                simulatedMousePosition != Vector2.zero ||
                simulatedMouseLeftButton ||
                simulatedMouseRightButton ||
                simulatedMouseMiddleButton ||
                simulatedMouseScroll != Vector2.zero;
            if (mice.Count == 0 && !hasSnapshot)
            {
                return null;
            }

            var mouse = mice.Count > 0
                ? GetTrackedDevice<Mouse>("mouse") ?? mice.OrderByDescending(GetMouseActivityScore).First()
                : null;
            
            return new
            {
                connected = true,
                deviceCount = mice.Count,
                position = new
                {
                    x = mouse != null && mouse.position.ReadValue() != Vector2.zero ? mouse.position.x.ReadValue() : simulatedMousePosition.x,
                    y = mouse != null && mouse.position.ReadValue() != Vector2.zero ? mouse.position.y.ReadValue() : simulatedMousePosition.y
                },
                leftButton = (mouse != null && mouse.leftButton.isPressed) || simulatedMouseLeftButton,
                rightButton = (mouse != null && mouse.rightButton.isPressed) || simulatedMouseRightButton,
                middleButton = (mouse != null && mouse.middleButton.isPressed) || simulatedMouseMiddleButton,
                scroll = new
                {
                    x = mouse != null && mouse.scroll.ReadValue() != Vector2.zero ? mouse.scroll.x.ReadValue() : simulatedMouseScroll.x,
                    y = mouse != null && mouse.scroll.ReadValue() != Vector2.zero ? mouse.scroll.y.ReadValue() : simulatedMouseScroll.y
                }
            };
        }

        private static object GetGamepadState()
        {
            var gamepads = GetTrackedAndKnownDevices<Gamepad>("gamepad");
            bool hasSnapshot =
                simulatedGamepadButtonA ||
                simulatedGamepadButtonB ||
                simulatedGamepadButtonX ||
                simulatedGamepadButtonY ||
                simulatedGamepadStart ||
                simulatedGamepadSelect ||
                simulatedGamepadLeftShoulder ||
                simulatedGamepadRightShoulder ||
                simulatedGamepadLeftStickButton ||
                simulatedGamepadRightStickButton ||
                simulatedGamepadLeftStick != Vector2.zero ||
                simulatedGamepadRightStick != Vector2.zero ||
                simulatedGamepadLeftTrigger > 0f ||
                simulatedGamepadRightTrigger > 0f ||
                simulatedGamepadDpad != Vector2.zero;
            if (gamepads.Count == 0 && !hasSnapshot)
            {
                return null;
            }

            var gamepad = gamepads.Count > 0
                ? GetTrackedDevice<Gamepad>("gamepad") ?? gamepads.OrderByDescending(GetGamepadActivityScore).First()
                : null;
            
            return new
            {
                connected = true,
                deviceCount = gamepads.Count,
                buttons = new
                {
                    a = (gamepad != null && gamepad.buttonSouth.isPressed) || simulatedGamepadButtonA,
                    b = (gamepad != null && gamepad.buttonEast.isPressed) || simulatedGamepadButtonB,
                    x = (gamepad != null && gamepad.buttonWest.isPressed) || simulatedGamepadButtonX,
                    y = (gamepad != null && gamepad.buttonNorth.isPressed) || simulatedGamepadButtonY,
                    start = (gamepad != null && gamepad.startButton.isPressed) || simulatedGamepadStart,
                    select = (gamepad != null && gamepad.selectButton.isPressed) || simulatedGamepadSelect,
                    leftShoulder = (gamepad != null && gamepad.leftShoulder.isPressed) || simulatedGamepadLeftShoulder,
                    rightShoulder = (gamepad != null && gamepad.rightShoulder.isPressed) || simulatedGamepadRightShoulder,
                    leftStick = (gamepad != null && gamepad.leftStickButton.isPressed) || simulatedGamepadLeftStickButton,
                    rightStick = (gamepad != null && gamepad.rightStickButton.isPressed) || simulatedGamepadRightStickButton
                },
                sticks = new
                {
                    left = new
                    {
                        x = gamepad != null && Mathf.Abs(gamepad.leftStick.x.ReadValue()) > Mathf.Epsilon ? gamepad.leftStick.x.ReadValue() : simulatedGamepadLeftStick.x,
                        y = gamepad != null && Mathf.Abs(gamepad.leftStick.y.ReadValue()) > Mathf.Epsilon ? gamepad.leftStick.y.ReadValue() : simulatedGamepadLeftStick.y
                    },
                    right = new
                    {
                        x = gamepad != null && Mathf.Abs(gamepad.rightStick.x.ReadValue()) > Mathf.Epsilon ? gamepad.rightStick.x.ReadValue() : simulatedGamepadRightStick.x,
                        y = gamepad != null && Mathf.Abs(gamepad.rightStick.y.ReadValue()) > Mathf.Epsilon ? gamepad.rightStick.y.ReadValue() : simulatedGamepadRightStick.y
                    }
                },
                triggers = new
                {
                    left = gamepad != null && gamepad.leftTrigger.ReadValue() > 0f ? gamepad.leftTrigger.ReadValue() : simulatedGamepadLeftTrigger,
                    right = gamepad != null && gamepad.rightTrigger.ReadValue() > 0f ? gamepad.rightTrigger.ReadValue() : simulatedGamepadRightTrigger
                },
                dpad = new
                {
                    x = gamepad != null && Mathf.Abs(gamepad.dpad.x.ReadValue()) > Mathf.Epsilon ? gamepad.dpad.x.ReadValue() : simulatedGamepadDpad.x,
                    y = gamepad != null && Mathf.Abs(gamepad.dpad.y.ReadValue()) > Mathf.Epsilon ? gamepad.dpad.y.ReadValue() : simulatedGamepadDpad.y
                }
            };
        }

        private static object GetTouchscreenState()
        {
            var touchscreens = GetTrackedAndKnownDevices<Touchscreen>("touchscreen");
            if (touchscreens.Count == 0 &&
                simulatedActiveTouches.Count == 0 &&
                simulatedTouchPressCount == 0 &&
                simulatedTouchReleaseCount == 0)
            {
                return null;
            }

            var touchscreen = touchscreens.Count > 0
                ? GetTrackedDevice<Touchscreen>("touchscreen") ?? touchscreens.OrderByDescending(GetTouchActivityScore).First()
                : null;
            
            var activeTouches = new List<object>();
            for (int i = 0; touchscreen != null && i < touchscreen.touches.Count; i++)
            {
                var touch = touchscreen.touches[i];
                // Check if touch is active based on phase (Began, Moved, or Stationary)
                var currentPhase = touch.phase.ReadValue();
                if (currentPhase != UnityEngine.InputSystem.TouchPhase.None && 
                    currentPhase != UnityEngine.InputSystem.TouchPhase.Ended &&
                    currentPhase != UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    activeTouches.Add(new
                    {
                        id = i,
                        position = new { x = touch.position.x.ReadValue(), y = touch.position.y.ReadValue() },
                        phase = touch.phase.ReadValue().ToString()
                    });
                }
            }

            foreach (var touch in simulatedActiveTouches)
            {
                var token = JToken.FromObject(touch);
                var id = token["id"]?.ToObject<int?>();
                if (id == null)
                {
                    continue;
                }

                bool exists = activeTouches.Any(existing =>
                {
                    var existingToken = JToken.FromObject(existing);
                    return existingToken["id"]?.ToObject<int?>() == id;
                });

                if (!exists)
                {
                    activeTouches.Add(touch);
                }
            }
            
            return new
            {
                connected = true,
                deviceCount = touchscreens.Count,
                activeTouches = activeTouches,
                pressCount = simulatedTouchPressCount,
                releaseCount = simulatedTouchReleaseCount,
                maxSimultaneous = simulatedTouchMaxSimultaneous,
                lastTouch = simulatedTouchLast
            };
        }

        private static T GetTrackedDevice<T>(string deviceName) where T : InputDevice
        {
            if (!string.IsNullOrEmpty(deviceName) &&
                activeDevices.TryGetValue(deviceName, out var trackedDevice) &&
                trackedDevice is T typedTrackedDevice &&
                typedTrackedDevice.added)
            {
                return typedTrackedDevice;
            }

            return InputSystem.devices.OfType<T>().FirstOrDefault(device => device != null && device.added);
        }

        private static List<T> GetTrackedAndKnownDevices<T>(string deviceName) where T : InputDevice
        {
            var devices = new List<T>();
            var tracked = GetTrackedDevice<T>(deviceName);
            if (tracked != null)
            {
                devices.Add(tracked);
            }

            foreach (var device in InputSystem.devices.OfType<T>().Where(device => device != null && device.added))
            {
                if (!devices.Contains(device))
                {
                    devices.Add(device);
                }
            }

            return devices;
        }

        private static float GetMouseActivityScore(Mouse mouse)
        {
            if (mouse == null)
            {
                return float.MinValue;
            }

            var position = mouse.position.ReadValue();
            var scroll = mouse.scroll.ReadValue();
            float buttons =
                (mouse.leftButton.isPressed ? 1f : 0f) +
                (mouse.rightButton.isPressed ? 1f : 0f) +
                (mouse.middleButton.isPressed ? 1f : 0f);

            return position.sqrMagnitude + scroll.sqrMagnitude + (buttons * 1000f);
        }

        private static float GetGamepadActivityScore(Gamepad gamepad)
        {
            if (gamepad == null)
            {
                return float.MinValue;
            }

            float buttons =
                (gamepad.buttonSouth.isPressed ? 1f : 0f) +
                (gamepad.buttonEast.isPressed ? 1f : 0f) +
                (gamepad.buttonWest.isPressed ? 1f : 0f) +
                (gamepad.buttonNorth.isPressed ? 1f : 0f) +
                (gamepad.startButton.isPressed ? 1f : 0f) +
                (gamepad.selectButton.isPressed ? 1f : 0f);

            return
                gamepad.leftStick.ReadValue().sqrMagnitude +
                gamepad.rightStick.ReadValue().sqrMagnitude +
                gamepad.dpad.ReadValue().sqrMagnitude +
                gamepad.leftTrigger.ReadValue() +
                gamepad.rightTrigger.ReadValue() +
                (buttons * 1000f);
        }

        private static float GetTouchActivityScore(Touchscreen touchscreen)
        {
            if (touchscreen == null)
            {
                return float.MinValue;
            }

            float score = 0f;
            for (int i = 0; i < touchscreen.touches.Count; i++)
            {
                var touch = touchscreen.touches[i];
                var phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None)
                {
                    continue;
                }

                score += 1000f + touch.position.ReadValue().sqrMagnitude;
            }

            return score;
        }

        #endregion
    }
}
#endif
