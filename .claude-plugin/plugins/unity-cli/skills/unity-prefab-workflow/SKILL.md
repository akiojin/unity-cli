---
name: unity-prefab-workflow
description: Create, edit, instantiate, and manage Unity Prefabs using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# Prefab Lifecycle

Create, open, edit, and instantiate Prefabs.

## Commands

```bash
# Create prefab from scene object
unity-cli raw create_prefab --json '{"gameObjectPath":"/Player","assetPath":"Assets/Prefabs/Player.prefab"}'

# Prefab editing mode
unity-cli raw open_prefab --json '{"assetPath":"Assets/Prefabs/Player.prefab"}'
unity-cli raw save_prefab --json '{}'
unity-cli raw exit_prefab_mode --json '{}'

# Instantiate
unity-cli raw instantiate_prefab --json '{"assetPath":"Assets/Prefabs/Player.prefab","position":{"x":0,"y":0,"z":0}}'

# Modify prefab properties
unity-cli raw modify_prefab --json '{"assetPath":"Assets/Prefabs/Player.prefab","modifications":{"name":"UpdatedPlayer"}}'
```

## Workflow

1. `open_prefab` to enter edit mode
2. Make changes (add components, modify properties)
3. `save_prefab` to persist
4. `exit_prefab_mode` to return to scene

## Tips

- Always `exit_prefab_mode` before switching scenes.
- Use `instantiate_prefab` with `position`/`rotation` for placement.
