using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace UnityCliBridge.TestScenes
{
    public sealed class UGuiTestSceneController : MonoBehaviour
    {
        private int clickCount;

        private Text status;
        private Button button;
        private InputField inputField;
        private Toggle toggle;
        private Slider slider;
        private Dropdown dropdown;

        private void Awake()
        {
            EnsureEventSystem();
            BindReferences();
            BindEvents();
            UpdateStatus();
        }

        private static void EnsureEventSystem()
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

        private void BindReferences()
        {
            var panel = GameObject.Find("/Canvas/UGUI_Panel");
            if (panel == null) return;

            var t = panel.transform;
            status = t.Find("UGUI_StatusText")?.GetComponent<Text>();
            button = t.Find("UGUI_Button")?.GetComponent<Button>();
            inputField = t.Find("UGUI_InputField")?.GetComponent<InputField>();
            toggle = t.Find("UGUI_Toggle")?.GetComponent<Toggle>();
            slider = t.Find("UGUI_Slider")?.GetComponent<Slider>();
            dropdown = t.Find("UGUI_Dropdown")?.GetComponent<Dropdown>();
        }

        private void BindEvents()
        {
            if (button != null)
            {
                button.onClick.AddListener(OnClicked);
            }

            if (inputField != null)
            {
                inputField.onValueChanged.AddListener(_ => UpdateStatus());
            }

            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(_ => UpdateStatus());
            }

            if (slider != null)
            {
                slider.onValueChanged.AddListener(_ => UpdateStatus());
            }

            if (dropdown != null)
            {
                dropdown.onValueChanged.AddListener(_ => UpdateStatus());
            }
        }

        private void OnClicked()
        {
            clickCount++;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (status == null) return;

            status.text =
                $"UGUI clicks={clickCount}\n" +
                $"Input='{inputField?.text}'\n" +
                $"Toggle={toggle?.isOn}\n" +
                $"Slider={slider?.value}\n" +
                $"Dropdown={dropdown?.value}";
        }
    }
}

