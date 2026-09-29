#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM && UNITY_INPUT_SYSTEM_PACKAGE
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    public class GamepadStickInputPlayModeTests
    {
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        private float previousDeadzoneMin;
        private float previousDeadzoneMax;
        private Gamepad previousCurrent;
        private InputDevice[] previousDevices;
        private IDictionary activeDevices;
        private object previousTrackedGamepad;
        private FieldInfo leftSnapshotField;
        private FieldInfo rightSnapshotField;
        private object previousLeftSnapshot;
        private object previousRightSnapshot;
        private MethodInfo simulateGamepadInput;
        private GameObject probeObject;

        [SetUp]
        public void SetUp()
        {
            previousDevices = InputSystem.devices.ToArray();
            previousCurrent = Gamepad.current;
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousDeadzoneMin = InputSystem.settings.defaultDeadzoneMin;
            previousDeadzoneMax = InputSystem.settings.defaultDeadzoneMax;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.defaultDeadzoneMin = 0.125f;
            InputSystem.settings.defaultDeadzoneMax = 0.925f;

            var handler = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityCliBridge.Handlers.InputSystemHandler"))
                .FirstOrDefault(type => type != null);
            Assert.That(handler, Is.Not.Null, "InputSystemHandler must be loaded for this regression test.");
            simulateGamepadInput = handler.GetMethod("SimulateGamepadInput", BindingFlags.Public | BindingFlags.Static);
            Assert.That(simulateGamepadInput, Is.Not.Null);
            var devicesField = handler.GetField("activeDevices", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(devicesField, Is.Not.Null);
            activeDevices = (IDictionary)devicesField.GetValue(null);
            previousTrackedGamepad = activeDevices["gamepad"];
            leftSnapshotField = handler.GetField("simulatedGamepadLeftStick", BindingFlags.NonPublic | BindingFlags.Static);
            rightSnapshotField = handler.GetField("simulatedGamepadRightStick", BindingFlags.NonPublic | BindingFlags.Static);
            previousLeftSnapshot = leftSnapshotField.GetValue(null);
            previousRightSnapshot = rightSnapshotField.GetValue(null);
            activeDevices.Remove("gamepad");
        }

        [TearDown]
        public void TearDown()
        {
            if (probeObject != null)
                UnityEngine.Object.DestroyImmediate(probeObject);

            if (activeDevices != null)
            {
                activeDevices.Remove("gamepad");
                if (previousTrackedGamepad != null)
                    activeDevices["gamepad"] = previousTrackedGamepad;
            }

            foreach (var device in InputSystem.devices.ToArray())
            {
                if (!previousDevices.Contains(device))
                    InputSystem.RemoveDevice(device);
            }

            if (previousCurrent != null && previousCurrent.added)
                previousCurrent.MakeCurrent();
            if (leftSnapshotField != null)
                leftSnapshotField.SetValue(null, previousLeftSnapshot);
            if (rightSnapshotField != null)
                rightSnapshotField.SetValue(null, previousRightSnapshot);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            InputSystem.settings.defaultDeadzoneMin = previousDeadzoneMin;
            InputSystem.settings.defaultDeadzoneMax = previousDeadzoneMax;
        }

        [UnityTest]
        public IEnumerator LeftStick_ProcessedAxesMatchRequestedValuesInGameUpdate()
        {
            return VerifyStickInGameUpdate("left");
        }

        [UnityTest]
        public IEnumerator RightStick_ProcessedAxesMatchRequestedValuesInGameUpdate()
        {
            return VerifyStickInGameUpdate("right");
        }

        private IEnumerator VerifyStickInGameUpdate(string stick)
        {
            Assert.That(Application.isPlaying, Is.True);
            probeObject = new GameObject("Issue246GameUpdateProbe");
            var probe = probeObject.AddComponent<GamepadStickGameUpdateProbe>();
            probe.ReadLeftStick = stick == "left";
            var samples = new[]
            {
                Vector2.zero,
                new Vector2(0.5f, 0f),
                new Vector2(0f, -0.5f),
                new Vector2(0.5f, 0.75f),
                new Vector2(-0.5f, -0.75f),
                new Vector2(-0.5f, 0.75f),
                new Vector2(0.5f, -0.75f),
                Vector2.one,
                new Vector2(2f, -2f),
                Vector2.zero
            };

            foreach (var sample in samples)
            {
                var result = JObject.FromObject(simulateGamepadInput.Invoke(null, new object[]
                {
                    new JObject
                    {
                        ["action"] = "stick", ["stick"] = stick,
                        ["x"] = sample.x, ["y"] = sample.y
                    }
                }));
                Assert.That(result["error"], Is.Null, result.ToString());
                probe.Gamepad = activeDevices["gamepad"] as Gamepad;
                Assert.That(probe.Gamepad, Is.Not.Null);

                // The probe reads in the game's Update after a real dynamic input update.
                // Do not call InputSystem.Update or query the handler's state snapshot here.
                var submittedFrame = Time.frameCount;
                for (var attempt = 0; attempt < 10 && probe.ObservedFrame <= submittedFrame; attempt++)
                    yield return null;

                Assert.That(probe.ObservedFrame, Is.GreaterThan(submittedFrame));
                Assert.That(probe.Axes.x, Is.EqualTo(Mathf.Clamp(sample.x, -1f, 1f)).Within(0.0001f),
                    $"{stick} x axis for requested {sample}, observed in game Update");
                Assert.That(probe.Axes.y, Is.EqualTo(Mathf.Clamp(sample.y, -1f, 1f)).Within(0.0001f),
                    $"{stick} y axis for requested {sample}, observed in game Update");
            }
        }
    }

    public class GamepadStickGameUpdateProbe : MonoBehaviour
    {
        public Gamepad Gamepad;
        public bool ReadLeftStick;
        public Vector2 Axes;
        public int ObservedFrame = -1;

        private void Update()
        {
            if (Gamepad == null || !Gamepad.added)
                return;
            var stick = ReadLeftStick ? Gamepad.leftStick : Gamepad.rightStick;
            Axes = new Vector2(stick.x.ReadValue(), stick.y.ReadValue());
            ObservedFrame = Time.frameCount;
        }
    }
}
#endif
