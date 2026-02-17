---
name: unity-scene-create
description: Create and set up Unity scenes, GameObjects, and components using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# Scene & GameObject Creation

Create scenes, GameObjects, and attach components via `unity-cli`.

## Workflow

1. Create or load a scene
2. Create GameObjects (with optional primitive type)
3. Add components
4. Save the scene

## Commands

```bash
# Scene management
unity-cli scene create <name> --path Assets/Scenes/
unity-cli raw load_scene --json '{"scenePath":"Assets/Scenes/MyScene.unity"}'
unity-cli raw save_scene --json '{"scenePath":"Assets/Scenes/MyScene.unity"}'

# GameObject creation
unity-cli raw create_gameobject --json '{"name":"Player","primitiveType":"cube"}'
unity-cli raw create_gameobject --json '{"name":"Empty","parent":"/Canvas"}'

# Add components
unity-cli raw add_component --json '{"gameObjectPath":"/Player","componentType":"Rigidbody"}'
unity-cli raw add_component --json '{"gameObjectPath":"/Player","componentType":"BoxCollider"}'
```

## Tips

- Use `primitiveType` values: `cube`, `sphere`, `capsule`, `cylinder`, `plane`, `quad`.
- Always save the scene after bulk changes.
- Chain with `--output json` for automation.
