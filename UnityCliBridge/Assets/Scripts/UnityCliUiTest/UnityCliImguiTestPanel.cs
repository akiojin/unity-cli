using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityCliBridge.Runtime.IMGUI;

namespace UnityCliBridge.TestScenes
{
    public sealed class UnityCliImguiTestPanel : MonoBehaviour
    {
        private int clickCount;
        private bool toggleValue;
        private float sliderValue = 0.5f;
        private string textValue = string.Empty;

        private void Update()
        {
            RegisterAutomationControls();
        }

        private void OnGUI()
        {
            const int x = 20;
            const int w = 360;
            int y = 20;

            GUI.Label(
                new Rect(x, y, w, 90),
                $"IMGUI clicks={clickCount}\nToggle={toggleValue}\nSlider={sliderValue:0.00}\nText='{textValue}'"
            );

            RegisterAutomationControls();

            var buttonRect = GetControlRect(0);
            if (GUI.Button(buttonRect, "IMGUI Button"))
            {
                clickCount++;
            }

            var toggleRect = GetControlRect(1);
            toggleValue = GUI.Toggle(toggleRect, toggleValue, "IMGUI Toggle");

            var sliderRect = GetControlRect(2);
            sliderValue = GUI.HorizontalSlider(sliderRect, sliderValue, 0f, 1f);

            var textRect = GetControlRect(3);
            textValue = GUI.TextField(textRect, textValue);
        }

        private void RegisterAutomationControls()
        {
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Button",
                controlType: "Button",
                rect: GetControlRect(0),
                isInteractable: true,
                getValue: () => clickCount,
                onClick: () => clickCount++
            );
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Toggle",
                controlType: "Toggle",
                rect: GetControlRect(1),
                isInteractable: true,
                getValue: () => toggleValue,
                setValue: token => toggleValue = token != null && token.ToObject<bool>()
            );
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Slider",
                controlType: "Slider",
                rect: GetControlRect(2),
                isInteractable: true,
                getValue: () => sliderValue,
                setValue: token =>
                {
                    if (token != null)
                    {
                        sliderValue = token.ToObject<float>();
                    }
                }
            );
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/TextField",
                controlType: "TextField",
                rect: GetControlRect(3),
                isInteractable: true,
                getValue: () => textValue,
                setValue: token => textValue = token?.ToString() ?? string.Empty
            );
        }

        private static Rect GetControlRect(int row)
        {
            const int x = 20;
            const int y = 120;
            const int w = 360;
            const int h = 30;
            const int gap = 8;
            return new Rect(x, y + row * (h + gap), w, h);
        }
    }
}
