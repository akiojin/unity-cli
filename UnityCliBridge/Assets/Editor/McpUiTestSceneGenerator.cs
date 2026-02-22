using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UITK = UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace UnityMCPServer.TestScenes
{
    internal static class McpUiTestSceneGenerator
    {
        private const string MenuRoot = "Tools/MCP/UI Tests/";

        private const string ScenesFolder = "Assets/Scenes";

        private const string UiTestRootFolder = "Assets/McpUiTest";
        private const string UiToolkitFolder = "Assets/McpUiTest/UITK";
        private const string UiToolkitUxmlPath = "Assets/McpUiTest/UITK/MCP_UITK_Test.uxml";
        private const string UiToolkitPanelSettingsPath = "Assets/McpUiTest/UITK/MCP_UITK_TestPanelSettings.asset";

        private const string UGuiScenePath = "Assets/Scenes/MCP_UI_UGUI_TestScene.unity";
        private const string UiToolkitScenePath = "Assets/Scenes/MCP_UI_UITK_TestScene.unity";
        private const string ImguiScenePath = "Assets/Scenes/MCP_UI_IMGUI_TestScene.unity";

        [MenuItem(MenuRoot + "Generate All UI Test Scenes")]
        private static void GenerateAllUiScenes()
        {
            GenerateUGuiScene();
            GenerateUiToolkitScene();
            GenerateImguiScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem(MenuRoot + "Generate UGUI Test Scene")]
        private static void GenerateUGuiScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureMainCamera();
            EnsureEventSystem();

            var canvasGo = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var resources = new DefaultControls.Resources();

            var panelGo = DefaultControls.CreatePanel(resources);
            panelGo.name = "UGUI_Panel";
            panelGo.transform.SetParent(canvasGo.transform, false);

            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0, 1);
            panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot = new Vector2(0, 1);
            panelRt.anchoredPosition = new Vector2(20, -20);
            panelRt.sizeDelta = new Vector2(520, 720);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var statusGo = DefaultControls.CreateText(resources);
            statusGo.name = "UGUI_StatusText";
            statusGo.transform.SetParent(panelGo.transform, false);
            var statusText = statusGo.GetComponent<Text>();
            if (statusText != null)
            {
                statusText.fontSize = 22;
                statusText.text = "UGUI Status";
            }

            var buttonGo = DefaultControls.CreateButton(resources);
            buttonGo.name = "UGUI_Button";
            buttonGo.transform.SetParent(panelGo.transform, false);
            var buttonText = buttonGo.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = "UGUI Button";
            }

            var inputGo = DefaultControls.CreateInputField(resources);
            inputGo.name = "UGUI_InputField";
            inputGo.transform.SetParent(panelGo.transform, false);

            var toggleGo = DefaultControls.CreateToggle(resources);
            toggleGo.name = "UGUI_Toggle";
            toggleGo.transform.SetParent(panelGo.transform, false);

            var sliderGo = DefaultControls.CreateSlider(resources);
            sliderGo.name = "UGUI_Slider";
            sliderGo.transform.SetParent(panelGo.transform, false);
            var slider = sliderGo.GetComponent<Slider>();
            if (slider != null)
            {
                slider.minValue = 0;
                slider.maxValue = 10;
                slider.value = 5;
            }

            var dropdownGo = DefaultControls.CreateDropdown(resources);
            dropdownGo.name = "UGUI_Dropdown";
            dropdownGo.transform.SetParent(panelGo.transform, false);
            var dropdown = dropdownGo.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                dropdown.options = new System.Collections.Generic.List<Dropdown.OptionData>
                {
                    new Dropdown.OptionData("Option A"),
                    new Dropdown.OptionData("Option B"),
                    new Dropdown.OptionData("Option C"),
                };
                dropdown.value = 0;
            }

            new GameObject("UGUI_TestController", typeof(McpUGuiTestSceneController));

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolderExists(ScenesFolder);
            EditorSceneManager.SaveScene(scene, UGuiScenePath);
        }

        [MenuItem(MenuRoot + "Generate UI Toolkit Test Scene")]
        private static void GenerateUiToolkitScene()
        {
            EnsureUiToolkitAssets();

            var visualTree = AssetDatabase.LoadAssetAtPath<UITK.VisualTreeAsset>(UiToolkitUxmlPath);
            var panelSettings = AssetDatabase.LoadAssetAtPath<UITK.PanelSettings>(UiToolkitPanelSettingsPath);
            if (visualTree == null || panelSettings == null)
            {
                Debug.LogError("Failed to load UI Toolkit test assets. Re-run generator after AssetDatabase refresh.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureMainCamera();

            var root = new GameObject("UITK");
            var docGo = new GameObject("UIDocument");
            docGo.transform.SetParent(root.transform, false);

            var doc = docGo.AddComponent<UITK.UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = visualTree;
            docGo.AddComponent<McpUiToolkitTestSceneController>();

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolderExists(ScenesFolder);
            EditorSceneManager.SaveScene(scene, UiToolkitScenePath);
        }

        [MenuItem(MenuRoot + "Generate IMGUI Test Scene")]
        private static void GenerateImguiScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureMainCamera();

            new GameObject("IMGUI", typeof(McpImguiTestPanel));

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolderExists(ScenesFolder);
            EditorSceneManager.SaveScene(scene, ImguiScenePath);
        }

        private static void EnsureUiToolkitAssets()
        {
            EnsureFolderExists(UiTestRootFolder);
            EnsureFolderExists(UiToolkitFolder);

            // Create UXML on disk (then import) so it becomes a VisualTreeAsset.
            var fullUxmlPath = Path.Combine(Application.dataPath, "McpUiTest/UITK/MCP_UITK_Test.uxml");
            if (!File.Exists(fullUxmlPath))
            {
                File.WriteAllText(fullUxmlPath, GetUiToolkitUxmlContent());
            }

            AssetDatabase.ImportAsset(UiToolkitUxmlPath);

            var panelSettings = AssetDatabase.LoadAssetAtPath<UITK.PanelSettings>(UiToolkitPanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<UITK.PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, UiToolkitPanelSettingsPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static string GetUiToolkitUxmlContent()
        {
            return
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n" +
                "  <ui:VisualElement name=\"UITK_Container\">\n" +
                "    <ui:Label name=\"UITK_Status\" text=\"UITK Status\" />\n" +
                "    <ui:Button name=\"UITK_Button\" text=\"UITK Button\" />\n" +
                "    <ui:Toggle name=\"UITK_Toggle\" label=\"UITK Toggle\" value=\"false\" />\n" +
                "    <ui:Slider name=\"UITK_Slider\" lowValue=\"0\" highValue=\"10\" value=\"5\" label=\"UITK Slider\" />\n" +
                "    <ui:TextField name=\"UITK_TextField\" label=\"UITK TextField\" value=\"\" />\n" +
                "    <ui:DropdownField name=\"UITK_Dropdown\" label=\"UITK Dropdown\" />\n" +
                "  </ui:VisualElement>\n" +
                "</ui:UXML>\n";
        }

        private static void EnsureMainCamera()
        {
            var existing = GameObject.FindWithTag("MainCamera");
            if (existing != null) return;

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<Camera>();
            cameraGo.transform.position = new Vector3(0, 1, -10);
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            var standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                Object.DestroyImmediate(standalone);
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

        private static void EnsureFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            var name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}

