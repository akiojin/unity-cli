---
name: unity-gameobject-edit
description: Edit existing GameObjects and components in Unity with unity-cli. Use when the user asks to rename objects, move or deactivate them, add components, change component fields, add or remove tags and layers, or delete objects from an existing scene. Do not use for new scene bootstrapping; use `unity-scene-create`. Do not use inside prefab edit mode; use `unity-prefab-workflow`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: scenes
  triggers:
    - gameobject
    - component
    - rename
    - tag
    - layer
  siblings:
    - unity-scene-create
    - unity-scene-inspect
    - unity-prefab-workflow
    - unity-asset-management
    - unity-csharp-edit
---

# GameObject Edit

Modify existing GameObjects and their components in an already-prepared scene. This skill is the destructive sibling of `unity-scene-inspect`.

## Use When

- The user wants to change an existing object's properties or hierarchy.
- The user wants to inspect, add, modify, or remove components on an existing object.
- The user needs tag or layer management for the current project.

## Do Not Use When

- The task is to create a brand-new scene from scratch; use `unity-scene-create`.
- The work is prefab asset editing rather than scene objects; use `unity-prefab-workflow`.
- The user only wants to read state without mutation; use `unity-scene-inspect`.

## Editor and Serialized Asset Safety

- When the target Editor is reachable, do not hand-edit `.unity`, `.prefab`, or `.asset` YAML. Use bridge tools so Unity maintains object references, prefab overrides, and its in-memory state consistently.
- A failed ping can be a sandbox false negative. Follow [Connection Recovery](../unity-cli-usage/references/runtime-checklist.md#connection-recovery); confirm the target project and connection with the user when sandbox restrictions prevent verification. Do not infer that the Editor is absent.
- Before using an offline fallback, explicitly state why the bridge is unavailable, which files are affected, and the alternative method. A timeout alone is not permission to edit YAML.

## Preferred Flow

1. Run `unity-cli system ping` for the target project before mutations (use `--project-path <project>` with multiple Editors). If it fails, follow Connection Recovery before continuing.

2. Inspect the exact `gameObjectPath` and current components before changing anything.
3. Add a missing component with `add_component`; use `modify_gameobject`, `modify_component`, or `set_component_field` for existing properties. Apply one mutation at a time.
4. Reserve `delete_gameobject` and `remove_component` for confirmed scopes.
5. Save the scene after destructive or bulk updates so reloads do not lose work.

```bash
unity-cli raw modify_gameobject --json '{"path":"/Player","name":"Hero","active":true}'
unity-cli raw add_component --json '{"gameObjectPath":"/Player","componentType":"Rigidbody"}'
unity-cli raw modify_component --json '{"gameObjectPath":"/Player","componentType":"Rigidbody","properties":{"mass":2.0}}'
unity-cli raw set_component_field --json '{"gameObjectPath":"/Player","componentType":"Transform","fieldPath":"position","value":{"x":0,"y":1,"z":0}}'
unity-cli raw remove_component --json '{"gameObjectPath":"/Player","componentType":"BoxCollider"}'
```

## Examples

- "Deactivate `/Player` and rename it to `Hero`."
- "Player に Rigidbody を付けて" → inspect `/Player`, then use `add_component` if it has no Rigidbody.
- "Change the Rigidbody mass on `/Player` and move it to (0,1,0)."
- "Remove the old collider from `/Obstacle` after listing its components."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [component-edit-safety.md](references/component-edit-safety.md): destructive-edit safety, nested field paths, tag/layer pitfalls.
