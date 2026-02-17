---
name: unity-ui-automation
description: Find, inspect, and interact with Unity UI elements for automated testing using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# UI Element Automation

Find, inspect, and interact with UI elements (uGUI / UI Toolkit).

## Commands

```bash
# Find UI elements
unity-cli raw find_ui_elements --json '{"query":"Button","searchType":"name"}'
unity-cli raw find_ui_elements --json '{"query":"UnityEngine.UI.Button","searchType":"type"}'

# Inspect state
unity-cli raw get_ui_element_state --json '{"elementPath":"/Canvas/StartButton"}'

# Interact
unity-cli raw click_ui_element --json '{"elementPath":"/Canvas/StartButton"}'
unity-cli raw set_ui_element_value --json '{"elementPath":"/Canvas/NameInput","value":"Player1"}'
unity-cli raw simulate_ui_input --json '{"elementPath":"/Canvas/Slider","inputType":"drag","value":0.75}'
```

## Tips

- Use `searchType` `name` or `type` to narrow results.
- `get_ui_element_state` returns visibility, interactability, and current value.
- Combine with PlayMode testing for end-to-end UI tests.
