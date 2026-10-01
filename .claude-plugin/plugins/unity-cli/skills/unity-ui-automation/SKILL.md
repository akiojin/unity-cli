---
name: unity-ui-automation
description: Automate Unity UI inspection and interaction with unity-cli. Use when the user asks to find a UI element by name or type, inspect button or input state, click a UI element, set a UI value, or run a short UI interaction sequence during testing. Do not use for scene authoring or general play-mode control without UI targeting; use `unity-scene-create` or `unity-playmode-testing` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.1
  category: ui
  triggers:
    - ui
    - canvas
    - button
    - click
    - input-field
  siblings:
    - unity-localization
    - unity-ui-toolkit-build
    - unity-playmode-testing
    - unity-scene-create
---

# UI Automation

Find, inspect, and interact with uGUI / UI Toolkit elements via `unity-cli`. This skill is the UI-focused complement to `unity-playmode-testing` (full runtime control).

## Use When

- The user wants to locate UI elements by name or type.
- The task requires inspecting UI state before or after interaction.
- The user wants to click UI elements, set values, or run a short UI input sequence.

## Do Not Use When

- Configure Locale/String Table assets and verify localized UI text: `unity-localization`.

- Build a complete UXML/USS UI Toolkit screen and verify its interactions: use `unity-ui-toolkit-build`.

- The task is general Play Mode setup with no UI targeting requirement; use `unity-playmode-testing`.
- The request authors UI prefabs or scene hierarchy; use `unity-scene-create` or `unity-prefab-workflow`.

## Preferred Flow

1. Find the target with `namePattern` or `elementType`.
2. Inspect current state before acting (visibility, interactability).
3. Perform one interaction at a time and re-check state when sequence matters.
4. Combine with `unity-playmode-testing` only when the UI depends on runtime state.

```bash
unity-cli raw find_ui_elements --json '{"namePattern":"Start","includeInactive":true}'
unity-cli raw get_ui_element_state --json '{"elementPath":"/Canvas/StartButton"}'
unity-cli raw click_ui_element --json '{"elementPath":"/Canvas/StartButton"}'
unity-cli raw set_ui_element_value --json '{"elementPath":"/Canvas/NameInput","value":"Player1"}'
```

## Examples

- "Find the start button and click it."
- "Read the current value of `/Canvas/NameInput` and then set it to `Player1`."
- "Run a short slider interaction sequence in the active UI."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [ui-test-flow.md](references/ui-test-flow.md): safer locate-inspect-interact sequence for UI testing.
