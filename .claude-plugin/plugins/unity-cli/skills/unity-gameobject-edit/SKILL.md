---
name: unity-gameobject-edit
description: Edit GameObjects, modify or remove components, manage tags and layers in Unity.
allowed-tools: Bash, Read, Grep, Glob
---

# GameObject & Component Editing

Modify existing GameObjects and their components.

## Commands

```bash
# Modify GameObject properties
unity-cli raw modify_gameobject --json '{"gameObjectPath":"/Player","name":"Hero","isActive":true}'
unity-cli raw delete_gameobject --json '{"gameObjectPath":"/OldObject"}'

# Component operations
unity-cli raw modify_component --json '{"gameObjectPath":"/Player","componentType":"Rigidbody","properties":{"mass":2.0}}'
unity-cli raw set_component_field --json '{"gameObjectPath":"/Player","componentType":"Transform","fieldPath":"position","value":{"x":0,"y":1,"z":0}}'
unity-cli raw remove_component --json '{"gameObjectPath":"/Player","componentType":"BoxCollider"}'
unity-cli raw list_components --json '{"gameObjectPath":"/Player"}'
unity-cli raw get_component_types --json '{}'

# Tags & layers
unity-cli raw manage_tags --json '{"action":"add","tagName":"Enemy"}'
unity-cli raw manage_layers --json '{"action":"list"}'
```

## Tips

- `set_component_field` supports dot-separated `fieldPath` for nested fields.
- Save the scene after destructive edits.
