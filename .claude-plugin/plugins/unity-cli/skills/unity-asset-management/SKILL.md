---
name: unity-asset-management
description: Manage Unity assets and import metadata with unity-cli. Use when the user asks to refresh the asset database, inspect asset info, create or modify a material, create an animation clip or sprite atlas, update import settings, or analyze asset dependencies before moving or deleting files. Do not use for Addressables groups or content builds; use `unity-addressables`. Do not use for scene object edits; use `unity-gameobject-edit`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.1
  category: assets
  triggers:
    - asset
    - material
    - import
    - animation
    - sprite
    - dependency
  siblings:
    - unity-addressables
    - unity-prefab-workflow
    - unity-gameobject-edit
    - unity-editor-tools
---

# Asset Management

Manage the Unity Asset Database, materials, animation clips, sprite atlases, import settings, and asset dependency analysis. This skill is the file/asset complement to `unity-addressables` (which handles groups and content builds).

## Use When

- The user wants to inspect, refresh, move, or otherwise manage project assets.
- The user wants to create or update materials.
- The user wants to author AnimationClip or SpriteAtlas assets.
- The user needs import settings or dependency analysis before file changes.

## Do Not Use When

- The task is Addressables groups or content builds; use `unity-addressables`.
- The request is about scene-instance edits; use `unity-gameobject-edit`.
- The work happens inside prefab edit mode; use `unity-prefab-workflow`.

## Preferred Flow

1. Inspect the target asset with `manage_asset_database` using `{"action":"get_asset_info","assetPath":"..."}` before changing it.
2. Run `analyze_asset_dependencies` before deleting, moving, or changing shared assets.
3. Apply import or material changes with the narrowest possible payload.
4. Call `refresh_assets` after any out-of-editor file change.

```bash
unity-cli raw manage_asset_database --json '{"action":"get_asset_info","assetPath":"Assets/Textures/hero.png"}'
unity-cli raw manage_asset_database --json '{"action":"refresh"}'
unity-cli raw create_material --json '{"materialPath":"Assets/Materials/HeroMat.mat","shader":"Standard"}'
unity-cli raw create_animation_clip --json '{"clipPath":"Assets/Animations/Hero.anim","spritePaths":["Assets/Sprites/Hero/idle_0.png"],"frameRate":12,"loopTime":true}'
unity-cli raw analyze_asset_dependencies --json '{"action":"get_dependencies","assetPath":"Assets/Prefabs/Player.prefab","recursive":true}'
```

## Examples

- "Refresh the asset database and inspect `Assets/Textures/hero.png`."
- "Create a material for the player and tint it red."
- "Generate an animation clip from a set of sprite frames."
- "Check which assets depend on `Player.prefab` before moving it."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [asset-safety.md](references/asset-safety.md): dependency analysis, import changes, and material updates that touch many assets.
