using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UITK = UnityEngine.UIElements;

namespace UnityMCPServer.TestScenes
{
    public sealed class McpUiToolkitTestSceneController : MonoBehaviour
    {
        private int clickCount;

        private UITK.UIDocument document;
        private UITK.Label status;
        private UITK.Toggle toggle;
        private UITK.Slider slider;
        private UITK.TextField textField;
        private UITK.DropdownField dropdown;

        private void Awake()
        {
            document = GetComponent<UITK.UIDocument>();
            StartCoroutine(BindNextFrame());
        }

        private IEnumerator BindNextFrame()
        {
            yield return null;

            if (document == null) yield break;

            var root = document.rootVisualElement;
            if (root == null) yield break;

            status = UnityEngine.UIElements.UQueryExtensions.Q<UITK.Label>(root, name: "UITK_Status");
            var button = UnityEngine.UIElements.UQueryExtensions.Q<UITK.Button>(root, name: "UITK_Button");
            toggle = UnityEngine.UIElements.UQueryExtensions.Q<UITK.Toggle>(root, name: "UITK_Toggle");
            slider = UnityEngine.UIElements.UQueryExtensions.Q<UITK.Slider>(root, name: "UITK_Slider");
            textField = UnityEngine.UIElements.UQueryExtensions.Q<UITK.TextField>(root, name: "UITK_TextField");
            dropdown = UnityEngine.UIElements.UQueryExtensions.Q<UITK.DropdownField>(root, name: "UITK_Dropdown");

            if (dropdown != null && (dropdown.choices == null || dropdown.choices.Count == 0))
            {
                dropdown.choices = new List<string> { "Option A", "Option B", "Option C" };
                dropdown.index = 0;
            }

            if (button != null)
            {
                button.clicked += () =>
                {
                    clickCount++;
                    UpdateStatus();
                };
            }

            if (toggle != null)
            {
                toggle.RegisterCallback<UITK.ChangeEvent<bool>>(_ => UpdateStatus());
            }

            if (slider != null)
            {
                slider.RegisterCallback<UITK.ChangeEvent<float>>(_ => UpdateStatus());
            }

            if (textField != null)
            {
                textField.RegisterCallback<UITK.ChangeEvent<string>>(_ => UpdateStatus());
            }

            if (dropdown != null)
            {
                dropdown.RegisterCallback<UITK.ChangeEvent<string>>(_ => UpdateStatus());
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (status == null) return;

            status.text =
                $"UITK clicks={clickCount}\n" +
                $"Toggle={toggle?.value}\n" +
                $"Slider={slider?.value}\n" +
                $"Text='{textField?.value}'\n" +
                $"Dropdown='{dropdown?.value}'";
        }
    }
}
