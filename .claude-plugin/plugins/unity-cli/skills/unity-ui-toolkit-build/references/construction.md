# Small runtime form

## Table of Contents

- UXML and USS
- Serialized setup through the Editor
- Binding script
- Game capture including the overlay

Use an unused folder such as `Assets/UI/ToolkitDemo`. These are independently
authored examples, using APIs shared by Unity 2022.3 and Unity 6. Adapt the paths
and screen content to the request. Inspect existing files before replacing them.

## UXML and USS

Write these source texts through `unity-editor-tools` using
`unity-cli editor eval '<statements>' --mode statements --request-id <unique-id>`
with `System.IO.Directory.CreateDirectory` / `System.IO.File.WriteAllText`.
Escape text as C# string literals and then shell-quote the entire expression;
never interpolate untrusted text into shell commands. Use absolute paths derived
from `UnityEngine.Application.dataPath`. Import via AssetDatabase afterward.

`Screen.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <Style src="Screen.uss" />
    <ui:VisualElement name="form" class="form">
        <ui:Label text="Welcome" name="status" />
        <ui:TextField label="Name" name="player-name" />
        <ui:Button text="Submit" name="submit" />
    </ui:VisualElement>
</ui:UXML>
```

`Screen.uss`:

```css
.form {
    padding-top: 32px;
    padding-left: 32px;
    padding-right: 32px;
    padding-bottom: 32px;
    width: 480px;
    background-color: rgb(28, 35, 48);
    color: rgb(240, 243, 250);
    font-size: 24px;
}
#status { margin-bottom: 20px; }
#player-name .unity-base-text-field__input { color: rgb(28, 35, 48); }
#submit { margin-top: 16px; height: 48px; color: rgb(28, 35, 48); }
```

## Serialized setup through the Editor

Import UXML and USS synchronously and confirm both load as `VisualTreeAsset`
and `StyleSheet`. Use fully qualified API names in eval statements:

```csharp
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UI/ToolkitDemo/Screen.uxml");
if (tree == null) throw new System.InvalidOperationException("UXML import failed");
var panel = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
UnityEditor.AssetDatabase.CreateAsset(panel, "Assets/UI/ToolkitDemo/Panel.asset");
var go = new UnityEngine.GameObject("ToolkitDemo");
var document = go.AddComponent<UnityEngine.UIElements.UIDocument>();
document.panelSettings = panel;
document.visualTreeAsset = tree;
UnityEditor.EditorUtility.SetDirty(document);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
return go.name;
```

This is for a new, unused path/object only. For existing assets load and modify
the requested object instead of duplicating it. Ensure PanelSettings has a theme
style sheet for legible built-in controls. If Unity does not assign its default
theme, import/create a `.tss` source containing
`@import url("unity-theme://default");` and assign the resulting
`ThemeStyleSheet` to `panel.themeStyleSheet` before saving.

## Binding script

Use `unity-csharp-edit` to write `Assets/Scripts/ToolkitDemoBinding.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ToolkitDemoBinding : MonoBehaviour
{
    private Button submit;
    private TextField playerName;
    private Label status;
    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        submit = root.Q<Button>("submit");
        playerName = root.Q<TextField>("player-name");
        status = root.Q<Label>("status");
        if (submit == null || playerName == null || status == null)
        {
            Debug.LogError("ToolkitDemo: required UXML controls missing");
            return;
        }
        submit.clicked += Submit;
    }
    private void OnDisable() { if (submit != null) submit.clicked -= Submit; }
    private void Submit() { status.text = "Hello, " + playerName.value + "!"; }
}
```

After compilation succeeds, add this component to the document object using
`add_component` and save/reload the scene. In Play, discover the controls, assert
initial label `Welcome`, input `Ada`, read back `Ada`, click Submit, then assert
`Hello, Ada!`. Capture actual Game images before/after using the recipe below,
check text visibility and layout, and inspect the console for errors. Preserve
UXML/USS, command responses and captures as evidence; restore Edit Mode afterward.

## Game capture including the overlay

`capture_screenshot` with `captureMode:game` can render only the camera even when
`includeUI:true`; a successful response is not proof that the UI is in the PNG.
Do not use its camera-only result or an OS fallback as evidence for this screen.
Use the existing `unity-editor-tools` eval with Unity's Game-frame capture API:

```csharp
UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
var captureDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), ".unity/capture");
System.IO.Directory.CreateDirectory(captureDir);
var path = System.IO.Path.Combine(captureDir, "toolkit-before.png");
if (System.IO.File.Exists(path)) throw new System.InvalidOperationException("Use a new capture filename");
UnityEngine.ScreenCapture.CaptureScreenshot(path);
return path;
```

Run while playing, after the panel has rendered. This queues capture for a
subsequent frame: wait for the file to exist and finish writing, then open it.
Use a different filename after the click. Keep Game view visible; if background
execution prevents frames, record `Application.runInBackground`, temporarily set
it to true via eval, and restore it after capturing. Verify the images actually
show `Welcome` and `Hello, Ada!`, alongside the matching structured UI state.
Do not implement a screenshot backend or modify the bridge to obtain evidence.
