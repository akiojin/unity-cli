---
name: unity-scene-create
description: Create and bootstrap Unity scenes with unity-cli. Use when the user asks to create a new scene, load or save a scene, add starter GameObjects, or attach initial components while bootstrapping a level or test scene. Do not use for editing existing GameObjects in place; use `unity-gameobject-edit`. Do not use inside prefab edit mode; use `unity-prefab-workflow` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.1
  category: scenes
  triggers:
    - scene
    - bootstrap
    - create
    - gameobject
    - level
  siblings:
    - unity-project-bootstrap
    - unity-gameobject-edit
    - unity-prefab-workflow
    - unity-scene-inspect
    - unity-cli-usage
    - unity-ui-automation
---

# Scene Bootstrap

Create scenes, add starter GameObjects, and attach initial components via `unity-cli`. This skill owns greenfield scene authoring; it hands off to `unity-gameobject-edit` or `unity-prefab-workflow` once objects already exist.

## Use When

- The user wants to create a brand-new scene from scratch.
- The user asks to add initial GameObjects and components while bootstrapping a scene.
- The user needs help loading, saving, or organising a fresh scene authoring workflow.

## Do Not Use When

- The project itself does not exist yet and the request includes bridge setup; use `unity-project-bootstrap`.
- The request mainly mutates existing objects in an already-prepared scene; use `unity-gameobject-edit`.
- The work happens inside prefab edit mode; use `unity-prefab-workflow`.
- The user only wants to read or analyse a scene; use `unity-scene-inspect`.

## Editor and Serialized Asset Safety

- When the target Editor is reachable, do not hand-edit `.unity`, `.prefab`, or `.asset` YAML. Use bridge tools so Unity maintains object references, prefab overrides, and its in-memory state consistently.
- A failed ping can be a sandbox false negative. Follow [Connection Recovery](../unity-cli-usage/references/runtime-checklist.md#connection-recovery); confirm the target project and connection with the user when sandbox restrictions prevent verification. Do not infer that the Editor is absent.
- Before using an offline fallback, explicitly state why the bridge is unavailable, which files are affected, and the alternative method. A timeout alone is not permission to edit YAML.

## Preferred Flow

1. Run `unity-cli system ping` for the target project before mutations (use `--project-path <project>` with multiple Editors). If it fails, follow Connection Recovery before continuing.

2. Create or load a scene with `scene create` or `raw load_scene`.
3. Create GameObjects (optionally with a primitive type and `parentPath`).
4. Attach components via `add_component`.
5. Persist with `save_scene` after the bulk authoring is complete.

```bash
unity-cli scene create MainMenu --path Assets/Scenes/
unity-cli raw create_gameobject --json '{"name":"Player","primitiveType":"cube"}'
unity-cli raw add_component --json '{"gameObjectPath":"/Player","componentType":"Rigidbody"}'
unity-cli raw save_scene --json '{"scenePath":"Assets/Scenes/MainMenu.unity"}'
```

## Examples

- "Create a new gameplay scene and add a `Player` object with physics components."
- "Load `Assets/Scenes/TestScene.unity`, add a camera rig, then save it."
- "Bootstrap a simple empty scene for UI testing."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance selection prerequisites.
- [scene-bootstrap-patterns.md](references/scene-bootstrap-patterns.md): safe scene setup order and starter patterns.
