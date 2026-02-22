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

        private void OnGUI()
        {
            const int x = 20;
            const int w = 360;
            const int h = 30;
            const int gap = 8;
            int y = 20;

            GUI.Label(
                new Rect(x, y, w, 90),
                $"IMGUI clicks={clickCount}\nToggle={toggleValue}\nSlider={sliderValue:0.00}\nText='{textValue}'"
            );
            y += 100;

            var buttonRect = new Rect(x, y, w, h);
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Button",
                controlType: "Button",
                rect: buttonRect,
                isInteractable: true,
                getValue: () => clickCount,
                onClick: () => clickCount++
            );
            if (GUI.Button(buttonRect, "IMGUI Button"))
            {
                clickCount++;
            }
            y += h + gap;

            var toggleRect = new Rect(x, y, w, h);
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Toggle",
                controlType: "Toggle",
                rect: toggleRect,
                isInteractable: true,
                getValue: () => toggleValue,
                setValue: token => toggleValue = token != null && token.ToObject<bool>()
            );
            toggleValue = GUI.Toggle(toggleRect, toggleValue, "IMGUI Toggle");
            y += h + gap;

            var sliderRect = new Rect(x, y, w, h);
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/Slider",
                controlType: "Slider",
                rect: sliderRect,
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
            sliderValue = GUI.HorizontalSlider(sliderRect, sliderValue, 0f, 1f);
            y += h + gap;

            var textRect = new Rect(x, y, w, h);
            ImguiControlRegistry.RegisterControl(
                controlId: "IMGUI/TextField",
                controlType: "TextField",
                rect: textRect,
                isInteractable: true,
                getValue: () => textValue,
                setValue: token => textValue = token?.ToString() ?? string.Empty
            );
            textValue = GUI.TextField(textRect, textValue);
        }
    }
}
