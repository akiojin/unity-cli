---
name: unity-input-system
description: Configure Unity Input System action maps, actions, bindings, and control schemes using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# Input System Configuration

Manage Input Action Assets: action maps, actions, bindings, and control schemes.

## Action Maps

```bash
unity-cli raw create_action_map --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay"}'
unity-cli raw remove_action_map --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay"}'
```

## Actions

```bash
unity-cli raw add_input_action --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump","actionType":"Button"}'
unity-cli raw remove_input_action --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump"}'
```

## Bindings

```bash
unity-cli raw add_input_binding --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump","path":"<Keyboard>/space"}'
unity-cli raw create_composite_binding --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Move","compositeType":"2DVector","bindings":{"up":"<Keyboard>/w","down":"<Keyboard>/s","left":"<Keyboard>/a","right":"<Keyboard>/d"}}'
unity-cli raw remove_input_binding --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump","bindingIndex":0}'
unity-cli raw remove_all_bindings --json '{"assetPath":"Assets/Input/Controls.inputactions","mapName":"Gameplay","actionName":"Jump"}'
```

## Analysis & Control

```bash
unity-cli raw analyze_input_actions_asset --json '{"assetPath":"Assets/Input/Controls.inputactions"}'
unity-cli raw get_input_actions_state --json '{}'
unity-cli raw manage_control_schemes --json '{"assetPath":"Assets/Input/Controls.inputactions","action":"list"}'
```

## Tips

- `actionType` values: `Button`, `Value`, `PassThrough`.
- Use `analyze_input_actions_asset` to audit before builds.
