#if ENABLE_INPUT_SYSTEM && UNITY_INPUT_SYSTEM_PACKAGE
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Handlers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Processors;

namespace UnityCliBridge.Tests
{
    public class GamepadStickValueTests
    {
        private Gamepad gamepad;
        private float originalMin;
        private float originalMax;
        private Gamepad previousCurrent;
        private IDictionary activeDevices;
        private object previousTrackedGamepad;
        private Vector2 previousLeftSnapshot;
        private Vector2 previousRightSnapshot;

        private static FieldInfo HandlerField(string name) => typeof(InputSystemHandler)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            originalMin = InputSystem.settings.defaultDeadzoneMin;
            originalMax = InputSystem.settings.defaultDeadzoneMax;
            previousCurrent = Gamepad.current;
            activeDevices = (IDictionary)HandlerField("activeDevices").GetValue(null);
            previousTrackedGamepad = activeDevices["gamepad"];
            previousLeftSnapshot = (Vector2)HandlerField("simulatedGamepadLeftStick").GetValue(null);
            previousRightSnapshot = (Vector2)HandlerField("simulatedGamepadRightStick").GetValue(null);
            gamepad = InputSystem.AddDevice<Gamepad>();
            activeDevices["gamepad"] = gamepad;
        }

        [TearDown]
        public void TearDown()
        {
            InputSystem.settings.defaultDeadzoneMin = originalMin;
            InputSystem.settings.defaultDeadzoneMax = originalMax;
            if (gamepad != null && gamepad.added)
                InputSystem.RemoveDevice(gamepad);
            activeDevices.Remove("gamepad");
            if (previousTrackedGamepad != null)
                activeDevices["gamepad"] = previousTrackedGamepad;
            HandlerField("simulatedGamepadLeftStick").SetValue(null, previousLeftSnapshot);
            HandlerField("simulatedGamepadRightStick").SetValue(null, previousRightSnapshot);
            if (previousCurrent != null && previousCurrent.added)
                previousCurrent.MakeCurrent();
        }

        private static IEnumerable<TestCaseData> StickCases()
        {
            var values = new[]
            {
                Vector2.zero, new Vector2(0.5f, 0), new Vector2(0, -0.5f),
                new Vector2(0.5f, 0.75f), new Vector2(-0.5f, -0.75f),
                new Vector2(-0.5f, 0.75f), new Vector2(1, 1),
                new Vector2(-1, -1), new Vector2(2, -2), new Vector2(0.01f, -0.01f)
            };
            foreach (var stick in new[] { "left", "right" })
            foreach (var value in values)
            foreach (var customDeadzone in new[] { false, true })
                yield return new TestCaseData(stick, value.x, value.y, customDeadzone);
        }

        [TestCaseSource(nameof(StickCases))]
        public void StickInput_MatchesProcessedAxes(string stick, float x, float y, bool customDeadzone)
        {
            InputSystem.settings.defaultDeadzoneMin = customDeadzone ? 0.2f : 0.125f;
            InputSystem.settings.defaultDeadzoneMax = customDeadzone ? 0.8f : 0.925f;
            var result = JObject.FromObject(InputSystemHandler.SimulateGamepadInput(new JObject
            {
                ["action"] = "stick", ["stick"] = stick, ["x"] = x, ["y"] = y
            }));
            InputSystem.Update();

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var control = stick == "left" ? gamepad.leftStick : gamepad.rightStick;
            var expected = new Vector2(Mathf.Clamp(x, -1, 1), Mathf.Clamp(y, -1, 1));
            Assert.That(control.x.ReadValue(), Is.EqualTo(expected.x).Within(0.0001f), "processed x");
            Assert.That(control.y.ReadValue(), Is.EqualTo(expected.y).Within(0.0001f), "processed y");
            Assert.That(result["value"].Value<float>("x"), Is.EqualTo(expected.x));
            Assert.That(result["value"].Value<float>("y"), Is.EqualTo(expected.y));
            var state = JObject.FromObject(InputSystemHandler.GetCurrentInputState(new JObject()));
            Assert.That(state["gamepad"]["sticks"][stick].Value<float>("x"), Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(state["gamepad"]["sticks"][stick].Value<float>("y"), Is.EqualTo(expected.y).Within(0.0001f));

            // The vector retains Unity's radial deadzone; it is not the axis-value contract.
            var expectedVector = new StickDeadzoneProcessor().Process(control.ReadUnprocessedValue());
            Assert.That(Vector2.Distance(control.ReadValue(), expectedVector), Is.LessThan(0.0001f));
        }
    }
}
#endif
