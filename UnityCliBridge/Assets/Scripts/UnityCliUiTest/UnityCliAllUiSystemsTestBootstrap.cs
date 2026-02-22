using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UGUI = UnityEngine.UI;
using UITK = UnityEngine.UIElements;
using UnityCliBridge.Runtime.IMGUI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace UnityCliBridge.TestScenes
{
    public sealed class UnityCliAllUiSystemsTestBootstrap : MonoBehaviour
    {
        private int uguiClicks;
        private int uitkClicks;

        private UGUI.Text uguiStatus;
        private UITK.Label uitkStatus;
        private UITK.VisualElement uitkContainer;
        private UITK.Toggle uitkToggle;
        private UITK.Slider uitkSlider;
        private UITK.TextField uitkTextField;
        private UITK.DropdownField uitkDropdown;

        private void Awake()
        {
            EnsureEventSystem();
            CreateUGui();
            CreateImguiPanel();
            CreateUiToolkit();
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

        private void CreateUGui()
        {
            if (FindFirstObjectByType<Canvas>() != null)
            {
                return;
            }

            var canvasGo = new GameObject(
                "Canvas",
                typeof(Canvas),
                typeof(UGUI.CanvasScaler),
                typeof(UGUI.GraphicRaycaster)
            );

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<UGUI.CanvasScaler>();
            scaler.uiScaleMode = UGUI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var resources = new UGUI.DefaultControls.Resources();

            var panelGo = UGUI.DefaultControls.CreatePanel(resources);
            panelGo.name = "UGUI_Panel";
            panelGo.transform.SetParent(canvasGo.transform, false);

            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0, 1);
            panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot = new Vector2(0, 1);
            panelRt.anchoredPosition = new Vector2(20, -20);
            panelRt.sizeDelta = new Vector2(520, 720);

            var layout = panelGo.AddComponent<UGUI.VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panelGo.AddComponent<UGUI.ContentSizeFitter>();
            fitter.horizontalFit = UGUI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UGUI.ContentSizeFitter.FitMode.PreferredSize;

            var statusGo = UGUI.DefaultControls.CreateText(resources);
            statusGo.name = "UGUI_StatusText";
            statusGo.transform.SetParent(panelGo.transform, false);
            uguiStatus = statusGo.GetComponent<UGUI.Text>();
            uguiStatus.fontSize = 22;

            var buttonGo = UGUI.DefaultControls.CreateButton(resources);
            buttonGo.name = "UGUI_Button";
            buttonGo.transform.SetParent(panelGo.transform, false);
            var button = buttonGo.GetComponent<UGUI.Button>();
            button.onClick.AddListener(() =>
            {
                uguiClicks++;
                UpdateUguiStatus();
            });
            var buttonText = buttonGo.GetComponentInChildren<UGUI.Text>();
            if (buttonText != null)
            {
                buttonText.text = "UGUI Button";
            }

            var inputGo = UGUI.DefaultControls.CreateInputField(resources);
            inputGo.name = "UGUI_InputField";
            inputGo.transform.SetParent(panelGo.transform, false);
            var input = inputGo.GetComponent<UGUI.InputField>();
            input.onValueChanged.AddListener(_ => UpdateUguiStatus());

            var toggleGo = UGUI.DefaultControls.CreateToggle(resources);
            toggleGo.name = "UGUI_Toggle";
            toggleGo.transform.SetParent(panelGo.transform, false);
            var toggle = toggleGo.GetComponent<UGUI.Toggle>();
            toggle.onValueChanged.AddListener(_ => UpdateUguiStatus());

            var sliderGo = UGUI.DefaultControls.CreateSlider(resources);
            sliderGo.name = "UGUI_Slider";
            sliderGo.transform.SetParent(panelGo.transform, false);
            var slider = sliderGo.GetComponent<UGUI.Slider>();
            slider.minValue = 0;
            slider.maxValue = 10;
            slider.value = 5;
            slider.onValueChanged.AddListener(_ => UpdateUguiStatus());

            var dropdownGo = UGUI.DefaultControls.CreateDropdown(resources);
            dropdownGo.name = "UGUI_Dropdown";
            dropdownGo.transform.SetParent(panelGo.transform, false);
            var dropdown = dropdownGo.GetComponent<UGUI.Dropdown>();
            dropdown.options = new List<UGUI.Dropdown.OptionData>
            {
                new UGUI.Dropdown.OptionData("Option A"),
                new UGUI.Dropdown.OptionData("Option B"),
                new UGUI.Dropdown.OptionData("Option C")
            };
            dropdown.value = 0;
            dropdown.onValueChanged.AddListener(_ => UpdateUguiStatus());

            UpdateUguiStatus();
        }

        private void UpdateUguiStatus()
        {
            if (uguiStatus == null) return;

            var panel = GameObject.Find("/Canvas/UGUI_Panel");
            if (panel == null)
            {
                uguiStatus.text = "UGUI panel not found";
                return;
            }

            var input = panel.transform.Find("UGUI_InputField")?.GetComponent<UGUI.InputField>();
            var toggle = panel.transform.Find("UGUI_Toggle")?.GetComponent<UGUI.Toggle>();
            var slider = panel.transform.Find("UGUI_Slider")?.GetComponent<UGUI.Slider>();
            var dropdown = panel.transform.Find("UGUI_Dropdown")?.GetComponent<UGUI.Dropdown>();

            uguiStatus.text =
                $"UGUI clicks={uguiClicks}\n" +
                $"Input='{input?.text}'\n" +
                $"Toggle={toggle?.isOn}\n" +
                $"Slider={slider?.value}\n" +
                $"Dropdown={dropdown?.value}";
        }

        private void CreateUiToolkit()
        {
            var root = GameObject.Find("UITK") ?? new GameObject("UITK");
            var docGo = root.transform.Find("UIDocument")?.gameObject;
            if (docGo == null)
            {
                docGo = new GameObject("UIDocument");
                docGo.transform.SetParent(root.transform, false);
            }

            var doc = docGo.GetComponent<UITK.UIDocument>();
            if (doc == null)
            {
                doc = docGo.AddComponent<UITK.UIDocument>();
            }

            if (doc.panelSettings == null)
            {
                doc.panelSettings = ScriptableObject.CreateInstance<UITK.PanelSettings>();
            }

            StartCoroutine(BuildUiToolkitNextFrame(doc));
        }

        private IEnumerator BuildUiToolkitNextFrame(UITK.UIDocument doc)
        {
            yield return null;

            var root = doc.rootVisualElement;
            if (root == null)
            {
                yield break;
            }

            root.Clear();

            uitkContainer = new UITK.VisualElement { name = "UITK_Container" };
            uitkContainer.style.flexDirection = UITK.FlexDirection.Column;
            uitkContainer.style.paddingLeft = 10;
            uitkContainer.style.paddingTop = 10;
            root.Add(uitkContainer);

            uitkStatus = new UITK.Label("UITK Status") { name = "UITK_Status" };
            uitkContainer.Add(uitkStatus);

            var button = new UITK.Button(() =>
            {
                uitkClicks++;
                UpdateUitkStatus();
            })
            {
                name = "UITK_Button",
                text = "UITK Button"
            };
            uitkContainer.Add(button);

            uitkToggle = new UITK.Toggle("UITK Toggle") { name = "UITK_Toggle", value = false };
            uitkToggle.RegisterCallback<UITK.ChangeEvent<bool>>(_ => UpdateUitkStatus());
            uitkContainer.Add(uitkToggle);

            uitkSlider = new UITK.Slider("UITK Slider", 0, 10) { name = "UITK_Slider", value = 5 };
            uitkSlider.RegisterCallback<UITK.ChangeEvent<float>>(_ => UpdateUitkStatus());
            uitkContainer.Add(uitkSlider);

            uitkTextField = new UITK.TextField("UITK TextField") { name = "UITK_TextField", value = string.Empty };
            uitkTextField.RegisterCallback<UITK.ChangeEvent<string>>(_ => UpdateUitkStatus());
            uitkContainer.Add(uitkTextField);

            uitkDropdown = new UITK.DropdownField(
                "UITK Dropdown",
                new List<string> { "Option A", "Option B", "Option C" },
                0
            )
            {
                name = "UITK_Dropdown"
            };
            uitkDropdown.RegisterCallback<UITK.ChangeEvent<string>>(_ => UpdateUitkStatus());
            uitkContainer.Add(uitkDropdown);

            UpdateUitkStatus();
        }

        private void UpdateUitkStatus()
        {
            if (uitkStatus == null) return;

            uitkStatus.text =
                $"UITK clicks={uitkClicks}\n" +
                $"Toggle={uitkToggle?.value}\n" +
                $"Slider={uitkSlider?.value}\n" +
                $"Text='{uitkTextField?.value}'\n" +
                $"Dropdown='{uitkDropdown?.value}'";
        }

        private static void CreateImguiPanel()
        {
            if (FindFirstObjectByType<UnityCliImguiTestPanel>() != null)
            {
                return;
            }

            new GameObject("IMGUI_TestPanel", typeof(UnityCliImguiTestPanel));
        }
    }
}
