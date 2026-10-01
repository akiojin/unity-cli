---
name: unity-input-system
description: Configure Unity Input System assets with unity-cli. Use when the user asks to create or remove an action map, add or remove an action or binding, set up a composite binding, inspect an input actions asset, or manage a control scheme. Do not use for runtime input simulation during tests; use `unity-playmode-testing`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: input
  triggers:
    - input
    - action-map
    - binding
    - composite
    - control-scheme
  siblings:
    - unity-playmode-testing
---

# Input System

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Author Input Action Assets: action maps, actions, bindings, control schemes, and composites. This skill owns asset authoring; runtime input simulation belongs to `unity-playmode-testing`.

## Use When

- The user wants to author or update `.inputactions` assets.
- The task is about action maps, actions, bindings, or control schemes.
- The user wants to inspect the structure of an input actions asset before modifying it.

## Do Not Use When

- The user wants to simulate keyboard, mouse, gamepad, or touch input during tests; use `unity-playmode-testing`.
- The task is only to verify that the Input System package is installed; use `unity-editor-tools`.

## Preferred Flow

1. Decide which asset path and action map the change belongs to.
2. Create or inspect the action map before adding actions or bindings.
3. Add actions first, then bindings, then control schemes or composites.
4. Run `analyze_input_actions_asset` after non-trivial edits.

```bash
unity-cli raw create_action_map --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay"}'
unity-cli raw add_input_action --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump","actionType":"Button"}'
unity-cli raw add_input_binding --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump","path":"<Keyboard>/space"}'
unity-cli raw analyze_input_actions_asset --json '{"assetPath":"Assets/Input/Controls.inputactions"}'
```

## Examples

- "Create a `Gameplay` action map with `Jump` and keyboard bindings."
- "Add a 2D movement composite to the `Move` action."
- "Inspect this input actions asset and tell me whether it is missing control schemes."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [input-actions-playbook.md](references/input-actions-playbook.md): sequencing for action maps, composites, and control schemes.
