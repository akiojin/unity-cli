---
name: unity-ui-toolkit-build
description: Build UI Toolkit screens with unity-cli. Use when the user asks to author UXML and USS, configure PanelSettings and UIDocument, bind C# callbacks, and verify the screen in Play Mode. Do not use for interacting with existing UI only; use `unity-ui-automation`. For scene-only creation use `unity-scene-create`, C#-only edits use `unity-csharp-edit`, and runtime capture alone use `unity-playmode-testing`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: ui
  triggers:
    - ui-toolkit
    - uxml
    - uss
    - uidocument
  siblings:
    - unity-ui-automation
    - unity-scene-create
    - unity-csharp-edit
    - unity-asset-management
    - unity-editor-tools
    - unity-playmode-testing
---

# UI Toolkit Build

Take a runtime screen from UXML/USS through a saved UIDocument and C# binding
to observed input, state changes, and Game-view captures. Use the existing
tools and delegated skills; no separate UI authoring backend is required.

## Use When

- Build a UI Toolkit screen with UXML/USS, PanelSettings, and UIDocument.
- Create a button/text-field form and bind its callback to a visible label.
- Complete the build → Play → input → inspect → capture loop in an existing project.

## Do Not Use When

- Only inspect/click/set an existing UI: `unity-ui-automation`.
- Build a uGUI Canvas/prefab or a scene without UI Toolkit: `unity-scene-create`.
- Only edit the binding script: `unity-csharp-edit`.
- Only refresh/import assets: `unity-asset-management`.
- Only evaluate C# or inspect Editor diagnostics: `unity-editor-tools`.
- Only enter Play, simulate input, or capture media: `unity-playmode-testing`.

## Preferred Flow

1. Read the runtime checklist, pin the project/port, and confirm `system ping`.
   Inspect scene dirty state and console before mutation; preserve unrelated work.
   Confirm the target screen, paths, named controls, and expected post-click text.
2. Delegate scene creation/loading to `unity-scene-create`. For an acceptance
   fixture use `Assets/Scenes/Generated/E2E/`; reuse the requested scene otherwise.
3. Follow the construction recipe. Delegate short Editor evaluations to
   `unity-editor-tools` to write UXML/USS source, import it, create PanelSettings
   via AssetDatabase, and assign a UIDocument. Never hand-edit `.asset`, `.unity`,
   `.prefab`, or `.meta` YAML. Refuse accidental overwrite of existing assets.
4. Delegate the MonoBehaviour binding to `unity-csharp-edit`, using CLI C# write
   tools with `refresh:false` and `waitForCompile:false`; run the refresh and
   compilation checks as separate calls in step 5. Register callbacks in OnEnable and unregister in OnDisable. Use stable,
   unique UXML names, validate queries, and support Unity 2022.3 as well as Unity 6.
5. Call `refresh_assets`, wait for compilation/domain reload with
   `get_compilation_state`, and reconnect. If a write fails after saving a file,
   inspect that file and diagnostics before retrying; do not duplicate the write.
   Inspect diagnostics; attach
   the compiled binding component, save assets and scene, then reload to check
   persistence. Do not infer compilation success from a write response alone.
6. Delegate Play to `unity-playmode-testing` and use the construction reference's
   Editor-eval Game capture recipe for UI Toolkit overlays. Wait until the
   document has a ready panel. Delegate discovery/state/input to
   `unity-ui-automation`; use the returned `uitk:/Object#element` paths, not uGUI
   hierarchy paths or screen coordinates guessed from an image.
7. Capture the initial Game view and label state. Set the text field, read its
   value back, click the button, and assert the label text changed to the expected
   value. Capture the resulting Game view. Inspect both images, console errors,
   and structured state; an interaction success response alone is insufficient.
8. Exit Play, preserve the saved screen, and report paths and evidence. If an
   existing tool cannot perform a required operation, preserve the failing call
   and report the gap for a separate Issue; do not add bridge features here or
   claim a mocked/manual edit passed the automated loop.

Use a stable request ID for each mutating eval. After a timeout query
`editor eval-status` before retrying; timeout does not cancel execution.

## Examples

- "Create a UI Toolkit sign-in form and verify its Submit button updates a label."
- "UXML と USS で名前入力画面を作り、Play 中のクリック結果まで確認して。"

For a new test scene, then a discovered document named `ToolkitDemo`:

```bash
unity-cli raw create_scene --json '{"sceneName":"ToolkitDemo","path":"Assets/Scenes/Generated/E2E/"}'
unity-cli raw find_ui_elements --json '{"namePattern":"submit","uiSystem":"uitk"}'
unity-cli raw get_ui_element_state --json '{"elementPath":"uitk:/ToolkitDemo#status"}'
unity-cli raw set_ui_element_value --json '{"elementPath":"uitk:/ToolkitDemo#player-name","value":"Ada"}'
unity-cli raw click_ui_element --json '{"elementPath":"uitk:/ToolkitDemo#submit"}'
unity-cli raw get_ui_element_state --json '{"elementPath":"uitk:/ToolkitDemo#status"}'
```

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [construction.md](references/construction.md): source assets, Editor setup, binding, and expected checks.
- [C# post-write pipeline issue #420](https://github.com/akiojin/unity-cli/issues/420): independent refresh/compile calls avoid this CLI failure.
- [Game capture issue #422](https://github.com/akiojin/unity-cli/issues/422): use the Editor-eval ScreenCapture recipe for UI overlays.
