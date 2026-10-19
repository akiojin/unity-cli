#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    // Samples in a real MonoBehaviour Update, independently of the handler's mirrored state.
    public sealed class TouchGestureUpdateProbe : MonoBehaviour
    {
        public struct Sample
        {
            public int Frame;
            public double Time;
            public int Slot;
            public bool Pressed;
            public bool PrimaryPressed;
            public Vector2 Position;
        }

        public readonly List<Sample> Samples = new List<Sample>();

        private void Update()
        {
            var screen = InputSystem.devices.OfType<Touchscreen>()
                .LastOrDefault(device => device.name.StartsWith("UnityCliVirtualTouchscreen", StringComparison.Ordinal));
            if (screen == null) return;
            for (var slot = 0; slot < screen.touches.Count; slot++)
            {
                var touch = screen.touches.FirstOrDefault(control => control.touchId.ReadValue() == slot + 1);
                Samples.Add(new Sample
                {
                    Frame = Time.frameCount,
                    Time = Time.realtimeSinceStartupAsDouble,
                    Slot = slot,
                    Pressed = touch != null && touch.press.isPressed,
                    PrimaryPressed = screen.primaryTouch.press.isPressed,
                    Position = touch == null ? Vector2.zero : touch.position.ReadValue()
                });
            }
        }
    }

    public sealed class TouchGesturePlayModeTests
    {
        private int previousFrameRate;
        private int previousVSync;
        private bool previousRunInBackground;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private InputSettings.UpdateMode previousUpdateMode;
        private float previousFixedDeltaTime;
        private HashSet<int> previousDevices;
        private GameObject probeObject;
        private TouchGestureUpdateProbe probe;
        private MethodInfo simulate;

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(Application.isPlaying);
            Application.logMessageReceived += ExpectBridgeConnectionLog;
            previousFrameRate = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
            previousRunInBackground = Application.runInBackground;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousUpdateMode = InputSystem.settings.updateMode;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            previousDevices = new HashSet<int>(InputSystem.devices.Select(device => device.deviceId));
            QualitySettings.vSyncCount = 0;
            var handler = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityCliBridge.Handlers.InputSystemHandler"))
                .FirstOrDefault(type => type != null);
            Assert.NotNull(handler, "The real Editor InputSystemHandler must be loaded.");
            simulate = handler.GetMethod("SimulateTouchInput", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(simulate);
            probeObject = new GameObject("TouchGestureUpdateProbe");
            probe = probeObject.AddComponent<TouchGestureUpdateProbe>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Let queued gestures finish before removing only devices created by this test.
            yield return WaitRealtime(0.7);
            if (probeObject != null) UnityEngine.Object.Destroy(probeObject);
            foreach (var device in InputSystem.devices.ToArray())
                if (!previousDevices.Contains(device.deviceId) && device is Touchscreen &&
                    device.name.StartsWith("UnityCliVirtualTouchscreen", StringComparison.Ordinal))
                    InputSystem.RemoveDevice(device);
            Application.targetFrameRate = previousFrameRate;
            QualitySettings.vSyncCount = previousVSync;
            Application.runInBackground = previousRunInBackground;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            InputSystem.settings.updateMode = previousUpdateMode;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            Application.logMessageReceived -= ExpectBridgeConnectionLog;
        }

        private static void ExpectBridgeConnectionLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log && System.Text.RegularExpressions.Regex.IsMatch(message,
                @"^\[unity-cli-bridge\] Client connected from 127\.0\.0\.1:\d+$"))
                LogAssert.Expect(type, message);
        }

        [UnityTest]
        public IEnumerator NativeTouchControl_IsVisibleToUpdateProbe()
        {
            var device = InputSystem.AddDevice<Touchscreen>("UnityCliVirtualTouchscreen");
            InputSystem.QueueStateEvent(device, new UnityEngine.InputSystem.LowLevel.TouchState
            {
                touchId = 1, position = new Vector2(45, 67), phase = UnityEngine.InputSystem.TouchPhase.Began
            });
            yield return WaitRealtime(0.2);
            Assert.That(probe.Samples.Any(sample => sample.Slot == 0 && sample.Pressed && sample.PrimaryPressed &&
                Vector2.Distance(sample.Position, new Vector2(45, 67)) < 0.1f), "Positive control: native input must reach the game Update.");
            InputSystem.QueueStateEvent(device, new UnityEngine.InputSystem.LowLevel.TouchState
            {
                touchId = 1, position = new Vector2(45, 67), phase = UnityEngine.InputSystem.TouchPhase.Ended
            });
            yield return WaitRealtime(0.1);
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
        }

        [UnityTest] public IEnumerator Tap_30Fps() { yield return VerifyTap(30); }
        [UnityTest] public IEnumerator Tap_120Fps() { yield return VerifyTap(120); }
        [UnityTest] public IEnumerator Tap_Uncapped() { yield return VerifyTap(-1); }
        [UnityTest] public IEnumerator Swipe_30Fps() { yield return VerifySwipe(30); }
        [UnityTest] public IEnumerator Swipe_120Fps() { yield return VerifySwipe(120); }
        [UnityTest] public IEnumerator Swipe_Uncapped() { yield return VerifySwipe(-1); }

        [UnityTest]
        public IEnumerator Tap_FixedInputUpdate()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            Time.fixedDeltaTime = 0.02f;
            yield return VerifyTap(120);
        }

        [UnityTest]
        public IEnumerator ZeroDurationSwipe_ExposesStartEndpointAndRelease()
        {
            Invoke(new JObject { ["action"] = "swipe", ["startX"] = 100, ["endX"] = 400, ["duration"] = 0 });
            yield return WaitRealtime(0.25);
            var pressed = probe.Samples.Where(sample => sample.Slot == 0 && sample.Pressed).ToList();
            Assert.That(pressed.Any(sample => sample.Position.x == 100));
            Assert.That(pressed.Any(sample => sample.Position.x == 400));
            Assert.Less(pressed.First(sample => sample.Position.x == 100).Frame,
                pressed.First(sample => sample.Position.x == 400).Frame);
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
        }

        [UnityTest]
        public IEnumerator SparseTouchId_HasOneLogicalStateEntry()
        {
            Invoke(new JObject { ["action"] = "tap", ["touchId"] = 7, ["x"] = 175, ["holdSeconds"] = 0.3 });
            yield return WaitRealtime(0.1);
            Assert.That(probe.Samples.Any(sample => sample.Slot == 7 && sample.Pressed && sample.Position.x == 175));
            var active = (JArray)GetTouchState()["activeTouches"];
            Assert.AreEqual(1, active.Count, active.ToString());
            Assert.AreEqual(7, active[0]["id"].Value<int>());
        }

        [UnityTest]
        public IEnumerator RemovingDevice_CancelsOnlyStartedGesture()
        {
            var before = GetTouchState();
            int presses = before?["pressCount"]?.Value<int>() ?? 0;
            int releases = before?["releaseCount"]?.Value<int>() ?? 0;
            Invoke(new JObject { ["action"] = "tap", ["holdSeconds"] = 0.3 });
            Invoke(new JObject { ["action"] = "tap", ["holdSeconds"] = 0.3 });
            yield return WaitRealtime(0.1);
            Assert.That(probe.Samples.Any(sample => sample.Slot == 0 && sample.Pressed));
            var device = InputSystem.devices.OfType<Touchscreen>().Last(screen => !previousDevices.Contains(screen.deviceId));
            InputSystem.RemoveDevice(device);
            yield return null;
            var after = GetTouchState();
            Assert.AreEqual(1, after["pressCount"].Value<int>() - presses);
            Assert.AreEqual(1, after["releaseCount"].Value<int>() - releases, "A queued gesture that never began must not count as released.");
        }

        private JObject GetTouchState()
        {
            var method = simulate.DeclaringType.GetMethod("GetCurrentInputState", BindingFlags.Public | BindingFlags.Static);
            var state = JObject.FromObject(method.Invoke(null, new object[] { new JObject() }));
            return state["touchscreen"] as JObject;
        }

        private IEnumerator VerifyTap(int frameRate)
        {
            Application.targetFrameRate = frameRate;
            yield return null;
            var started = Time.realtimeSinceStartupAsDouble;
            Invoke(new JObject { ["action"] = "tap", ["x"] = 100, ["y"] = 200, ["holdSeconds"] = 0.3 });
            Assert.Less(Time.realtimeSinceStartupAsDouble - started, 0.15, "Tap must enqueue without blocking Update.");
            yield return WaitRealtime(0.5);
            var pressed = probe.Samples.Where(sample => sample.Slot == 0 && sample.Pressed).ToList();
            Assert.GreaterOrEqual(pressed.Select(sample => sample.Frame).Distinct().Count(), 3,
                "Samples=" + probe.Samples.Count + "; devices=" + string.Join(";", InputSystem.devices.OfType<Touchscreen>()
                    .Select(device => device.name + " enabled=" + device.enabled + " slots=" + string.Join(",", device.touches
                        .Select(touch => touch.touchId.ReadValue() + ":" + touch.phase.ReadValue() + ":" + touch.press.ReadValue())))));
            Assert.That(pressed.All(sample => sample.PrimaryPressed), "Primary touch must be pressed during a single-finger tap.");
            Assert.GreaterOrEqual(pressed.Last().Time - pressed.First().Time, 0.2, "Requested hold must span real time.");
            Assert.That(pressed.All(sample => Vector2.Distance(sample.Position, new Vector2(100, 200)) < 0.1f));
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed, "Tap must release.");
        }

        private IEnumerator VerifySwipe(int frameRate)
        {
            Application.targetFrameRate = frameRate;
            yield return null;
            var started = Time.realtimeSinceStartupAsDouble;
            Invoke(new JObject { ["action"] = "swipe", ["startX"] = 100, ["startY"] = 200,
                ["endX"] = 400, ["endY"] = 200, ["duration"] = 500 });
            Assert.Less(Time.realtimeSinceStartupAsDouble - started, 0.15, "Swipe must enqueue without blocking Update.");
            yield return WaitRealtime(0.7);
            var pressed = probe.Samples.Where(sample => sample.Slot == 0 && sample.Pressed).ToList();
            Assert.GreaterOrEqual(pressed.Select(sample => sample.Frame).Distinct().Count(), 3);
            Assert.GreaterOrEqual(pressed.Last().Time - pressed.First().Time, 0.35);
            Assert.That(pressed.Any(sample => sample.Position.x > 110 && sample.Position.x < 390), "Update must observe intermediate positions.");
            Assert.That(pressed.Any(sample => Vector2.Distance(sample.Position, new Vector2(400, 200)) < 0.1f), "Update must observe the endpoint while pressed.");
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed, "Swipe must release.");
        }

        [UnityTest]
        public IEnumerator DefaultTap_IsObservedAndReleased()
        {
            Invoke(new JObject { ["action"] = "tap", ["x"] = 123, ["y"] = 234 });
            yield return WaitRealtime(0.25);
            Assert.That(probe.Samples.Any(sample => sample.Slot == 0 && sample.Pressed));
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
        }

        [UnityTest]
        public IEnumerator SameSlotGestures_AreQueuedInOrder()
        {
            Invoke(new JObject { ["action"] = "tap", ["x"] = 100, ["holdSeconds"] = 0.15 });
            Invoke(new JObject { ["action"] = "tap", ["x"] = 300, ["holdSeconds"] = 0.15 });
            yield return WaitRealtime(0.55);
            var pressed = probe.Samples.Where(sample => sample.Slot == 0 && sample.Pressed).ToList();
            Assert.That(pressed.Any(sample => sample.Position.x == 100));
            Assert.That(pressed.Any(sample => sample.Position.x == 300));
            Assert.Less(pressed.Last(sample => sample.Position.x == 100).Frame,
                pressed.First(sample => sample.Position.x == 300).Frame);
            Assert.That(probe.Samples.Any(sample => sample.Slot == 0 && !sample.Pressed &&
                sample.Frame > pressed.Last(first => first.Position.x == 100).Frame &&
                sample.Frame < pressed.First(second => second.Position.x == 300).Frame), "Queued gestures need an observable release frame.");
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
        }

        [UnityTest]
        public IEnumerator DistinctSlots_OverlapAndRelease()
        {
            Invoke(new JObject { ["action"] = "tap", ["touchId"] = 0, ["holdSeconds"] = 0.3 });
            Invoke(new JObject { ["action"] = "tap", ["touchId"] = 1, ["holdSeconds"] = 0.3 });
            yield return WaitRealtime(0.5);
            Assert.That(probe.Samples.Where(sample => sample.Pressed).GroupBy(sample => sample.Frame)
                .Any(frame => frame.Select(sample => sample.Slot).Distinct().Count() == 2));
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 1).Pressed);
        }

        [UnityTest]
        public IEnumerator RemovedDevice_DoesNotBreakSubsequentGesture()
        {
            Invoke(new JObject { ["action"] = "tap", ["holdSeconds"] = 0.3 });
            yield return null;
            var device = InputSystem.devices.OfType<Touchscreen>()
                .Last(screen => !previousDevices.Contains(screen.deviceId));
            InputSystem.RemoveDevice(device);
            yield return WaitRealtime(0.35);
            probe.Samples.Clear();
            Invoke(new JObject { ["action"] = "tap", ["x"] = 321, ["holdSeconds"] = 0.15 });
            yield return WaitRealtime(0.3);
            Assert.That(probe.Samples.Any(sample => sample.Pressed && sample.Position.x == 321));
            Assert.IsFalse(probe.Samples.Last(sample => sample.Slot == 0).Pressed);
            LogAssert.NoUnexpectedReceived();
        }

        private void Invoke(JObject parameters)
        {
            var result = JObject.FromObject(simulate.Invoke(null, new object[] { parameters }));
            Assert.IsNull(result["error"], result.ToString());
            Assert.IsTrue(result["success"]?.Value<bool>() == true, result.ToString());
        }

        private static IEnumerator WaitRealtime(double seconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        }
    }
}
#endif
