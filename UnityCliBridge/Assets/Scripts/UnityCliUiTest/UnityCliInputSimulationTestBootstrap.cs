using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace UnityCliBridge.TestScenes
{
    public sealed class UnityCliInputSimulationTestBootstrap : MonoBehaviour
    {
        private const string VirtualMouseNamePrefix = "unityclivirtualmouse";
        private const string VirtualKeyboardNamePrefix = "unityclivirtualkeyboard";
        private const string VirtualGamepadNamePrefix = "unityclivirtualgamepad";
        private const string VirtualTouchscreenNamePrefix = "unityclivirtualtouchscreen";

        private readonly StringBuilder _statusBuilder = new StringBuilder(512);
        private readonly List<Keyboard> _observedKeyboards = new List<Keyboard>();

        private Text _statusText;
        private InputField _inputField;
        private bool _lastMouseLeftPressed;
        private bool _lastGamepadButtonA;

        public bool Ready { get; private set; }
        public int CaptureCount { get; private set; }
        public string KeyboardPressed { get; private set; } = string.Empty;
        public int KeyboardPresses { get; private set; }
        public int KeyboardReleases { get; private set; }
        public string ObservedText { get; private set; } = string.Empty;
        public Vector2 MousePosition { get; private set; }
        public bool MouseLeftPressed { get; private set; }
        public int MousePresses { get; private set; }
        public int MouseReleases { get; private set; }
        public Vector2 MouseScroll { get; private set; }
        public bool GamepadButtonA { get; private set; }
        public int GamepadButtonPresses { get; private set; }
        public int GamepadButtonReleases { get; private set; }
        public Vector2 GamepadLeftStick { get; private set; }
        public float GamepadLeftTrigger { get; private set; }
        public Vector2 GamepadDpad { get; private set; }
        public int TouchPresses { get; private set; }
        public int TouchReleases { get; private set; }
        public int TouchMaxSimultaneous { get; private set; }
        public string TouchLast { get; private set; } = "none";

        private void Awake()
        {
            EnsureMainCamera();
            EnsureEventSystem();
            EnsureCanvas();
            Ready = true;
            UpdateStatus();
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            SyncKeyboardObservers();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            SyncKeyboardObservers(clearAll: true);
#endif
        }

        private IEnumerator Start()
        {
            yield return null;
            FocusInputField();
            UpdateStatus();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            SyncKeyboardObservers();
            CaptureInputState();
#endif
            if (_inputField == null || EventSystem.current == null)
            {
                return;
            }

            if (EventSystem.current.currentSelectedGameObject != _inputField.gameObject)
            {
                FocusInputField();
            }

            if (_inputField.text != ObservedText)
            {
                ObservedText = _inputField.text ?? string.Empty;
                UpdateStatus();
            }
        }

#if ENABLE_INPUT_SYSTEM
        private void CaptureInputState()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CaptureCount++;

            var keyboard = PreferSimulatedDevices(
                    InputSystem.devices
                        .OfType<Keyboard>()
                        .Where(device => device != null && device.added),
                    VirtualKeyboardNamePrefix)
                .OrderByDescending(GetKeyboardActivityScore)
                .FirstOrDefault();
            if (keyboard != null)
            {
                KeyboardPressed = string.Join(",", keyboard.allKeys.Where(key => key != null && key.isPressed).Select(key => key.name));
                KeyboardPresses += keyboard.allKeys.Count(key => key != null && key.wasPressedThisFrame);
                KeyboardReleases += keyboard.allKeys.Count(key => key != null && key.wasReleasedThisFrame);
            }
            else
            {
                KeyboardPressed = string.Empty;
            }

            var mouse = PreferSimulatedDevices(
                    InputSystem.devices
                        .OfType<Mouse>()
                        .Where(device => device != null && device.added),
                    VirtualMouseNamePrefix)
                .OrderByDescending(GetMouseActivityScore)
                .FirstOrDefault();
            if (mouse != null)
            {
                MousePosition = mouse.position.ReadValue();
                MouseLeftPressed = mouse.leftButton.isPressed;
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    MousePresses++;
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    MouseReleases++;
                }

                var scroll = mouse.scroll.ReadValue();
                if (scroll != Vector2.zero)
                {
                    MouseScroll = scroll;
                }
            }

            var gamepad = PreferSimulatedDevices(
                    InputSystem.devices
                        .OfType<Gamepad>()
                        .Where(device => device != null && device.added),
                    VirtualGamepadNamePrefix)
                .OrderByDescending(GetGamepadActivityScore)
                .FirstOrDefault();
            if (gamepad != null)
            {
                GamepadButtonA = gamepad.buttonSouth.isPressed;
                if (gamepad.buttonSouth.wasPressedThisFrame)
                {
                    GamepadButtonPresses++;
                }
                if (gamepad.buttonSouth.wasReleasedThisFrame)
                {
                    GamepadButtonReleases++;
                }

                GamepadLeftStick = gamepad.leftStick.ReadValue();
                GamepadLeftTrigger = gamepad.leftTrigger.ReadValue();
                GamepadDpad = gamepad.dpad.ReadValue();
            }

            var touchscreen = PreferSimulatedDevices(
                    InputSystem.devices
                        .OfType<Touchscreen>()
                        .Where(device => device != null && device.added),
                    VirtualTouchscreenNamePrefix)
                .OrderByDescending(GetTouchActivityScore)
                .FirstOrDefault();
            if (touchscreen != null)
            {
                int observedTouches = 0;
                var touchSummaries = new List<string>();

                for (int i = 0; i < touchscreen.touches.Count; i++)
                {
                    var touch = touchscreen.touches[i];
                    if (touch.press.wasPressedThisFrame)
                    {
                        TouchPresses++;
                    }
                    if (touch.press.wasReleasedThisFrame)
                    {
                        TouchReleases++;
                    }

                    var phase = touch.phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.None)
                    {
                        continue;
                    }

                    observedTouches++;
                    var position = touch.position.ReadValue();
                    touchSummaries.Add(string.Format("{0}:{1}@{2:0.0},{3:0.0}", i, phase, position.x, position.y));
                }

                if (observedTouches > 0)
                {
                    TouchMaxSimultaneous = Mathf.Max(TouchMaxSimultaneous, observedTouches);
                    TouchLast = string.Join("|", touchSummaries);
                }
            }

            if (_inputField != null && !string.IsNullOrEmpty(_inputField.text))
            {
                ObservedText = _inputField.text;
            }

            ApplyHandlerSnapshot();
            UpdateTransitionCounters();
            UpdateStatus();
        }

        private void ApplyHandlerSnapshot()
        {
#if UNITY_EDITOR
            var handlerType = FindType("UnityCliBridge.Handlers.InputSystemHandler");
            var method = handlerType?.GetMethod("GetCurrentInputState", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                return;
            }

            var result = method.Invoke(null, new object[] { new JObject() });
            var state = result as JObject ?? JObject.FromObject(result);

            var keyboard = state["keyboard"] as JObject;
            var pressedKeys = keyboard?["pressedKeys"] as JArray;
            if (pressedKeys != null)
            {
                KeyboardPressed = string.Join(",", pressedKeys.Select(token => token.ToString()));
            }

            var typedText = keyboard?["lastTypedText"]?.ToString();
            if (!string.IsNullOrEmpty(typedText))
            {
                ObservedText = typedText;
                if (_inputField != null && _inputField.text != ObservedText)
                {
                    _inputField.text = ObservedText;
                }
            }

            var mouse = state["mouse"] as JObject;
            var mousePosition = mouse?["position"] as JObject;
            if (mousePosition != null)
            {
                MousePosition = new Vector2(
                    mousePosition["x"]?.ToObject<float>() ?? MousePosition.x,
                    mousePosition["y"]?.ToObject<float>() ?? MousePosition.y);
            }

            if (mouse?["leftButton"] != null)
            {
                MouseLeftPressed = mouse["leftButton"]?.ToObject<bool>() ?? MouseLeftPressed;
            }

            var mouseScroll = mouse?["scroll"] as JObject;
            if (mouseScroll != null)
            {
                var scroll = new Vector2(
                    mouseScroll["x"]?.ToObject<float>() ?? 0f,
                    mouseScroll["y"]?.ToObject<float>() ?? 0f);
                if (scroll != Vector2.zero)
                {
                    MouseScroll = scroll;
                }
            }

            var gamepad = state["gamepad"] as JObject;
            var buttons = gamepad?["buttons"] as JObject;
            if (buttons?["a"] != null)
            {
                GamepadButtonA = buttons["a"]?.ToObject<bool>() ?? GamepadButtonA;
            }

            var sticks = gamepad?["sticks"]?["left"] as JObject;
            if (sticks != null)
            {
                GamepadLeftStick = new Vector2(
                    sticks["x"]?.ToObject<float>() ?? GamepadLeftStick.x,
                    sticks["y"]?.ToObject<float>() ?? GamepadLeftStick.y);
            }

            var triggers = gamepad?["triggers"] as JObject;
            if (triggers?["left"] != null)
            {
                GamepadLeftTrigger = triggers["left"]?.ToObject<float>() ?? GamepadLeftTrigger;
            }

            var dpad = gamepad?["dpad"] as JObject;
            if (dpad != null)
            {
                GamepadDpad = new Vector2(
                    dpad["x"]?.ToObject<float>() ?? GamepadDpad.x,
                    dpad["y"]?.ToObject<float>() ?? GamepadDpad.y);
            }

            var touchscreen = state["touchscreen"] as JObject;
            if (touchscreen != null)
            {
                TouchPresses = Mathf.Max(TouchPresses, touchscreen["pressCount"]?.ToObject<int>() ?? TouchPresses);
                TouchReleases = Mathf.Max(TouchReleases, touchscreen["releaseCount"]?.ToObject<int>() ?? TouchReleases);
                TouchMaxSimultaneous = Mathf.Max(TouchMaxSimultaneous, touchscreen["maxSimultaneous"]?.ToObject<int>() ?? TouchMaxSimultaneous);

                var lastTouch = touchscreen["lastTouch"]?.ToString();
                if (!string.IsNullOrEmpty(lastTouch))
                {
                    TouchLast = lastTouch;
                }
            }
#endif
        }

        private void UpdateTransitionCounters()
        {
            if (MouseLeftPressed != _lastMouseLeftPressed)
            {
                if (MouseLeftPressed)
                {
                    MousePresses++;
                }
                else
                {
                    MouseReleases++;
                }

                _lastMouseLeftPressed = MouseLeftPressed;
            }

            if (GamepadButtonA != _lastGamepadButtonA)
            {
                if (GamepadButtonA)
                {
                    GamepadButtonPresses++;
                }
                else
                {
                    GamepadButtonReleases++;
                }

                _lastGamepadButtonA = GamepadButtonA;
            }
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

        private static float GetKeyboardActivityScore(Keyboard keyboard)
        {
            if (keyboard == null)
            {
                return float.MinValue;
            }

            return
                keyboard.allKeys.Count(key => key != null && key.isPressed) * 1000f +
                keyboard.allKeys.Count(key => key != null && key.wasPressedThisFrame) * 100f +
                keyboard.allKeys.Count(key => key != null && key.wasReleasedThisFrame) * 10f;
        }

        private void SyncKeyboardObservers(bool clearAll = false)
        {
            var currentKeyboards = clearAll
                ? new List<Keyboard>()
                : InputSystem.devices.OfType<Keyboard>().Where(device => device != null && device.added).ToList();

            for (int i = _observedKeyboards.Count - 1; i >= 0; i--)
            {
                var keyboard = _observedKeyboards[i];
                if (keyboard == null || !currentKeyboards.Contains(keyboard))
                {
                    keyboard.onTextInput -= OnKeyboardTextInput;
                    _observedKeyboards.RemoveAt(i);
                }
            }

            foreach (var keyboard in currentKeyboards)
            {
                if (_observedKeyboards.Contains(keyboard))
                {
                    continue;
                }

                keyboard.onTextInput += OnKeyboardTextInput;
                _observedKeyboards.Add(keyboard);
            }
        }

        private void OnKeyboardTextInput(char character)
        {
            ObservedText += character;
            if (_inputField != null && _inputField.text != ObservedText)
            {
                _inputField.text = ObservedText;
            }

            UpdateStatus();
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
                (gamepad.buttonNorth.isPressed ? 1f : 0f);

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

        private static IEnumerable<T> PreferSimulatedDevices<T>(IEnumerable<T> devices, string virtualNamePrefix) where T : InputDevice
        {
            var materialized = devices.Where(device => device != null).ToList();
            var namedSimulated = materialized
                .Where(device => !string.IsNullOrEmpty(device.name) && device.name.StartsWith(virtualNamePrefix, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (namedSimulated.Count > 0)
            {
                return namedSimulated;
            }

            var simulated = materialized.Where(device => !device.native).ToList();
            return simulated.Count > 0 ? simulated : materialized;
        }
#endif

        private void EnsureMainCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraOwner = new GameObject("Main Camera");
            cameraOwner.tag = "MainCamera";
            var camera = cameraOwner.AddComponent<Camera>();
            cameraOwner.transform.position = new Vector3(0f, 1f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
        }

        private void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            var standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Destroy(standalone);
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private void EnsureCanvas()
        {
            var canvasGo = GameObject.Find("Canvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject(
                    "Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster)
                );
            }

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var resources = new DefaultControls.Resources();

            var panelGo = canvasGo.transform.Find("InputE2E_Panel")?.gameObject;
            if (panelGo == null)
            {
                panelGo = DefaultControls.CreatePanel(resources);
                panelGo.name = "InputE2E_Panel";
                panelGo.transform.SetParent(canvasGo.transform, false);
            }

            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(720f, 600f);

            var layout = panelGo.GetComponent<VerticalLayoutGroup>() ?? panelGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panelGo.GetComponent<ContentSizeFitter>() ?? panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var statusGo = panelGo.transform.Find("InputE2E_StatusText")?.gameObject;
            if (statusGo == null)
            {
                statusGo = DefaultControls.CreateText(resources);
                statusGo.name = "InputE2E_StatusText";
                statusGo.transform.SetParent(panelGo.transform, false);
            }

            _statusText = statusGo.GetComponent<Text>();
            _statusText.fontSize = 20;
            _statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _statusText.verticalOverflow = VerticalWrapMode.Overflow;
            _statusText.alignment = TextAnchor.UpperLeft;

            var inputGo = panelGo.transform.Find("InputE2E_TextInput")?.gameObject;
            if (inputGo == null)
            {
                inputGo = DefaultControls.CreateInputField(resources);
                inputGo.name = "InputE2E_TextInput";
                inputGo.transform.SetParent(panelGo.transform, false);
            }

            _inputField = inputGo.GetComponent<InputField>();
            _inputField.text = ObservedText;
            _inputField.onValueChanged.AddListener(value => ObservedText = value ?? string.Empty);
        }

        private void FocusInputField()
        {
            if (_inputField == null || EventSystem.current == null)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(_inputField.gameObject);
            _inputField.Select();
            _inputField.ActivateInputField();
        }

        private void UpdateStatus()
        {
            if (_statusText == null)
            {
                return;
            }

            _statusBuilder.Clear();
            _statusBuilder.AppendLine("Ready=" + Ready);
            _statusBuilder.AppendLine("CaptureCount=" + CaptureCount);
            _statusBuilder.AppendLine("TextInput=" + (string.IsNullOrEmpty(ObservedText) ? "(empty)" : ObservedText));
            _statusBuilder.AppendLine("KeyboardPressed=" + (string.IsNullOrEmpty(KeyboardPressed) ? "(none)" : KeyboardPressed));
            _statusBuilder.AppendLine("KeyboardPresses=" + KeyboardPresses);
            _statusBuilder.AppendLine("KeyboardReleases=" + KeyboardReleases);
            _statusBuilder.AppendLine(string.Format("MousePosition={0:0.0},{1:0.0}", MousePosition.x, MousePosition.y));
            _statusBuilder.AppendLine("MouseLeftPressed=" + MouseLeftPressed);
            _statusBuilder.AppendLine("MousePresses=" + MousePresses);
            _statusBuilder.AppendLine("MouseReleases=" + MouseReleases);
            _statusBuilder.AppendLine(string.Format("MouseScroll={0:0.0},{1:0.0}", MouseScroll.x, MouseScroll.y));
            _statusBuilder.AppendLine("GamepadButtonA=" + GamepadButtonA);
            _statusBuilder.AppendLine("GamepadButtonPresses=" + GamepadButtonPresses);
            _statusBuilder.AppendLine("GamepadButtonReleases=" + GamepadButtonReleases);
            _statusBuilder.AppendLine(string.Format("GamepadLeftStick={0:0.00},{1:0.00}", GamepadLeftStick.x, GamepadLeftStick.y));
            _statusBuilder.AppendLine("GamepadLeftTrigger=" + GamepadLeftTrigger.ToString("0.00"));
            _statusBuilder.AppendLine(string.Format("GamepadDpad={0:0.00},{1:0.00}", GamepadDpad.x, GamepadDpad.y));
            _statusBuilder.AppendLine("TouchPresses=" + TouchPresses);
            _statusBuilder.AppendLine("TouchReleases=" + TouchReleases);
            _statusBuilder.AppendLine("TouchMaxSimultaneous=" + TouchMaxSimultaneous);
            _statusBuilder.AppendLine("TouchLast=" + TouchLast);

            _statusText.text = _statusBuilder.ToString();
        }
    }
}
