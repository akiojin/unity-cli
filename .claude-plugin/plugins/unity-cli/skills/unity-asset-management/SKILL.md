---
name: unity-asset-management
description: Manage Unity assets and import metadata with unity-cli. Use when the user asks to inspect or edit Timeline tracks/clips/bindings, create materials, animation clips or sprite atlases, refresh assets, update imports, or analyze dependencies. Do not use for Addressables builds; use `unity-addressables`. For Player builds or scene baking, use `unity-editor-tools`. For scene object edits, use `unity-gameobject-edit`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.2
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
    - unity-vfx-graph
---

# Asset Management

Manage the Unity Asset Database, materials, animation clips, sprite atlases, import settings, and asset dependency analysis. This skill is the file/asset complement to `unity-addressables` (which handles groups and content builds).

## Use When

- The user wants to inspect, refresh, move, or otherwise manage project assets.
- The user wants to create or update materials.
- The user wants to author Timeline, AnimationClip or SpriteAtlas assets, inspect Timeline tracks/clips/bindings, or edit numeric animation curves.
- The user needs import settings or dependency analysis before file changes.

## Do Not Use When

- The task is Addressables groups or content builds; use `unity-addressables`.
- The request is about scene-instance edits; use `unity-gameobject-edit`.
- The work happens inside prefab edit mode; use `unity-prefab-workflow`.

## Editor and Serialized Asset Safety

- When the target Editor is reachable, do not hand-edit `.unity`, `.prefab`, or `.asset` YAML. Use bridge tools so Unity maintains object references, prefab overrides, and its in-memory state consistently.
- A failed ping can be a sandbox false negative. Follow [Connection Recovery](../unity-cli-usage/references/runtime-checklist.md#connection-recovery); confirm the target project and connection with the user when sandbox restrictions prevent verification. Do not infer that the Editor is absent.
- Before using an offline fallback, explicitly state why the bridge is unavailable, which files are affected, and the alternative method. A timeout alone is not permission to edit YAML.

## Preferred Flow

1. Run `unity-cli system ping` for the target project before mutations (use `--project-path <project>` with multiple Editors). If it fails, follow Connection Recovery before continuing.

2. Inspect the target asset with `manage_asset_database` using `{"action":"get_asset_info","assetPath":"..."}` before changing it.
3. Run `analyze_asset_dependencies` before deleting, moving, or changing shared assets.
4. Apply import or material changes with the narrowest possible payload.
5. Call `refresh_assets` after any out-of-editor file change.

```bash
unity-cli raw manage_asset_database --json '{"action":"get_asset_info","assetPath":"Assets/Textures/hero.png"}'
unity-cli raw manage_asset_database --json '{"action":"refresh"}'
unity-cli raw create_material --json '{"materialPath":"Assets/Materials/HeroMat.mat","shader":"Standard"}'
unity-cli raw create_animation_clip --json '{"clipPath":"Assets/Animations/Hero.anim","spritePaths":["Assets/Sprites/Hero/idle_0.png"],"frameRate":12,"loopTime":true}'
unity-cli raw get_animation_curves --json '{"clipPath":"Assets/Animations/Move.anim"}'
unity-cli raw edit_animation_curve --json '{"clipPath":"Assets/Animations/Move.anim","animationRoot":12345,"binding":{"path":"","component":"UnityEngine.Transform","property":"localPosition.x"},"operation":"set","createIfMissing":true,"keys":[{"time":0,"value":0},{"time":1,"value":2}]}'
unity-cli raw analyze_asset_dependencies --json '{"action":"get_dependencies","assetPath":"Assets/Prefabs/Player.prefab","recursive":true}'
```

For numeric curves, replace `12345` with the actual animation root GameObject instance ID from scene inspection. `path` is relative to that root; `component` is fully qualified and `property` is the serialized binding name (Transform `localPosition.x` aliases `m_LocalPosition.x`). Only writable standalone `Assets/*.anim` clips are editable, outside Play Mode. Use `set` to replace one curve, `upsert_keys` to add/update exact times, `remove_keys` with `times`, or `remove_curve`. Other bindings remain intact. New keys default to Linear; omitted tangent settings on existing keys are retained. Use `leftTangentMode`/`rightTangentMode` and finite `inTangent`/`outTangent` for Free tangents. Inspect with `get_animation_curves` after editing; object-reference bindings are listed separately and cannot be numerically edited.

## Examples

### Timeline assets, tracks, clips and bindings

Requires Timeline installed, Edit Mode, and an existing writable parent folder.
Use a new asset path for creation, then inspect before subsequent edits.

```bash
unity-cli raw manage_timeline --json '{"action":"create_asset","assetPath":"Assets/Timelines/Intro.playable"}'
unity-cli raw manage_timeline --json '{"action":"create_track","assetPath":"Assets/Timelines/Intro.playable","trackName":"Movement","trackType":"AnimationTrack"}'
unity-cli raw get_timeline --json '{"assetPath":"Assets/Timelines/Intro.playable"}'
```

Use inspected stable `trackId` values (GUID:localID), not track names. Only top-level
AnimationTrack editing is supported. For `add_clip`, supply `animationClipPath`,
`start`, and positive `duration`; re-inspect before `update_clip`/`remove_clip` and
include `clipIndex` plus `expectedClip` from that snapshot. To bind or evaluate a
PlayableDirector, use full hierarchy paths for `directorPath`/`animatorPath`.
Inspect `unity-cli tool schema manage_timeline` for action-specific required fields.

- "Refresh the asset database and inspect `Assets/Textures/hero.png`."
- "Create a material for the player and tint it red."
- "Generate an animation clip from a set of sprite frames."
- "Check which assets depend on `Player.prefab` before moving it."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [asset-safety.md](references/asset-safety.md): dependency analysis, import changes, and material updates that touch many assets.
