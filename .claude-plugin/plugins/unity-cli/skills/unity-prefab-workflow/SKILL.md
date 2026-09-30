---
name: unity-prefab-workflow
description: Manage Unity prefab assets with unity-cli. Use when the user asks to create or instantiate a prefab or Variant, list or apply/revert instance overrides, unpack a prefab, open prefab edit mode, save changes, or update prefab asset properties. Do not use for general scene object editing; use `unity-gameobject-edit` or `unity-scene-create` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.4.0
  category: prefabs
  triggers:
    - prefab
    - instantiate
    - edit-mode
    - asset
  siblings:
    - unity-gameobject-edit
    - unity-scene-create
    - unity-asset-management
---

# Prefab Workflow

Create, open, edit, and instantiate prefab assets via `unity-cli`. This skill owns Prefab edit mode, Variant inheritance, override apply/revert, and unpacking; ordinary scene-instance field edits belong to `unity-gameobject-edit`.

## Use When

- The user wants to create or update a prefab asset.
- The task requires entering prefab edit mode and saving changes back to the asset.
- The user wants to instantiate prefab assets into a scene with specific transforms.
- The user wants to create a Variant, inspect/apply/revert overrides, or unpack a Prefab connection.

## Do Not Use When

- The request edits ordinary scene objects with no prefab asset involved; use `unity-gameobject-edit`.
- The task is bootstrapping a fresh scene; use `unity-scene-create`.
- The request is about asset import settings or materials; use `unity-asset-management`.

## Preferred Flow

1. `open_prefab` to enter edit mode (or `create_prefab` from a scene object).
2. Apply edits via `add_component`, `modify_component`, or `set_component_field`.
3. `save_prefab` to persist changes back to the asset.
4. `exit_prefab_mode` to return to scene authoring.

```bash
unity-cli raw create_prefab --json '{"gameObjectPath":"/Player","prefabPath":"Assets/Prefabs/Player.prefab"}'
unity-cli raw open_prefab --json '{"prefabPath":"Assets/Prefabs/Player.prefab"}'
unity-cli raw save_prefab --json '{}'
unity-cli raw exit_prefab_mode --json '{}'
unity-cli raw instantiate_prefab --json '{"prefabPath":"Assets/Prefabs/Player.prefab","position":{"x":0,"y":0,"z":0}}'
```

## Variant and Override Flow

1. Instantiate the base with `instantiate_prefab`, then use `create_prefab` on
   that connected root with a new asset path. Unity creates a Variant and connects
   the instance to it. Do not unpack the base instance before saving the Variant.
2. List `get_prefab_overrides` on the scene instance root. It returns property,
   object, added/removed component overrides and `applyTargets`.
3. Use `manage_prefab_overrides` with `action: apply|revert` and
   `scope: all|property|object|added_component|removed_component`. Apply requires an
   explicit `assetPath` (Variant or base). Revert restores the immediate source.
4. For individual scopes use the returned `instanceId`; also supply `propertyPath`
   for a property or `assetComponentId` for a removed component. Refresh the list
   after mutations or reloads; IDs are session-local. An object added only to a
   Variant cannot be applied to a base where it does not exist.
5. Save the scene after reverting or unpacking. Use `unpack_prefab` with
   `Completely` when no Prefab connections should remain. `Outermost` preserves
   nested connections and a Variant's base connection; inspect the returned
   `isPartOfPrefabInstance` and `connectedObjectCount`.

```bash
unity-cli raw instantiate_prefab --json '{"prefabPath":"Assets/Prefabs/Player.prefab","name":"ArmoredPlayer"}'
unity-cli raw create_prefab --json '{"gameObjectPath":"/ArmoredPlayer","prefabPath":"Assets/Prefabs/ArmoredPlayer.prefab"}'
unity-cli raw get_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer"}'
unity-cli raw manage_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer","action":"apply","scope":"all","assetPath":"Assets/Prefabs/ArmoredPlayer.prefab"}'
unity-cli raw manage_prefab_overrides --json '{"gameObjectPath":"/ArmoredPlayer","action":"revert","scope":"all"}'
unity-cli raw unpack_prefab --json '{"gameObjectPath":"/ArmoredPlayer","mode":"Completely"}'
```

## Examples

- "Create a prefab from `/Player` and save it under `Assets/Prefabs/Player.prefab`."
- "Open an existing prefab, change a field, save it, and exit prefab mode."
- "Instantiate a prefab at the origin for a test scene."
- "Create a Variant that inherits Player defaults but keeps its own collider override."
- "Apply only this component override to the Variant and preserve the base Prefab."
- "Unpack this hierarchy completely and verify no Prefab connections remain."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [prefab-edit-mode.md](references/prefab-edit-mode.md): safe sequence for edit mode, scene handoff, and instantiation checks.
