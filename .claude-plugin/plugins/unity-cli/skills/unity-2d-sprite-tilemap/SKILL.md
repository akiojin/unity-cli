---
name: unity-2d-sprite-tilemap
description: Build and verify a 2D sprite and Tilemap scene with unity-cli. Use when the user wants sprite import, Sprite Atlas, Tile or Rule Tile painting, and Pixel Perfect Camera verification as one workflow. Do not use for import or atlas edits alone; use `unity-asset-management`. For scene creation alone use `unity-scene-create`; for capture alone use `unity-playmode-testing`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.0
  category: assets
  triggers:
    - sprite
    - tilemap
    - pixel
  siblings:
    - unity-asset-management
    - unity-scene-create
    - unity-playmode-testing
---

# 2D Sprite and Tilemap

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Take sprite art through import, atlas and tile authoring, then verify the saved
scene in Play with a Pixel Perfect Camera. Use existing tools and Editor APIs.

## Use When

- The request spans sprite import, Tilemap construction and a visible runtime check.
- The user wants a Tile or Rule Tile level with matching sprite/camera PPU.
- A pixel-art workflow needs both saved asset checks and Game-view evidence.

## Do Not Use When

- Only change imports or create an atlas: `unity-asset-management`.
- Only create/load/save a scene: `unity-scene-create`.
- Only capture or simulate input in an existing level: `unity-playmode-testing`.
- Only install 2D packages: `unity-package-management`; only read console or
  evaluate a snippet: `unity-editor-tools`; only inspect UI: `unity-ui-automation`.

## Preferred Flow

1. Follow [runtime prerequisites](references/runtime-checklist.md). Select the
   project and explicit port consistently, ping, and inspect Editor state. Work
   in Edit Mode. Preserve existing scenes and shared assets; do not discard an
   unsaved scene. Establish source PNG paths, Sprite Mode, PPU, tile cell size,
   scene destination and reference resolution. For a new example use Single,
   16 PPU, unit cells and 320×180; preserve requested values.
2. Inspect resolved packages and the render pipeline. Built-in Tilemap needs
   `com.unity.modules.tilemap`. RuleTile needs compatible
   `com.unity.2d.tilemap.extras`; Pixel Perfect comes from either standalone
   `com.unity.2d.pixel-perfect` or URP. Use `unity-package-management` for missing
   dependencies, wait for compilation and ping again. Do not migrate pipelines
   or install a second PixelPerfectCamera provider just for this workflow.
3. Follow [the authoring recipe](references/tilemap-recipe.md). Import the supplied
   sprite with `manage_asset_import_settings`; use `editor eval` for PPU and
   Sprite Mode, which the import tool does not expose. For Multiple preserve
   slicing and select a named subasset. Verify actual importer values and
   sprite dimensions after reimport; JSON success alone is insufficient.
4. Create a Sprite Atlas through `create_sprite_atlas`, with Point filtering,
   mipmaps/rotation disabled for the pixel-art example. Inspect an existing
   atlas before changing it; do not silently overwrite. Create Tile assets via
   `ScriptableObject.CreateInstance` and `AssetDatabase.CreateAsset`. Use a
   RuleTile only when requested and its package/type is available; define
   actual neighbor rules rather than claiming a default sprite is autotiling.
5. Use `unity-scene-create` for the target scene. Add/reuse a Grid parent,
   Tilemap and TilemapRenderer, paint the requested cell coordinates through
   `Tilemap.SetTile`, and configure a pipeline-compatible sprite material.
   Keep stable paths/names and inspect on retries to avoid duplicate objects.
6. Configure an orthographic camera and the installed PixelPerfectCamera type.
   Match `assetsPPU` to sprite PPU and set the reference resolution. Position
   the camera to see the painted cells, with a valid clipping range and culling
   mask. Mark the scene dirty, save assets/scene, reopen that scene and read
   back the tiles, atlas packables, importer and camera settings.
7. Enter Play through `unity-playmode-testing`, confirm Play state, capture the
   Game view with `osFallback:false`, and inspect the image for visible tiles,
   crisp pixels, expected arrangement and absence of seams/missing materials.
   Verify console/compilation errors and tile state. Input is only applicable
   if the scene has an input-driven behavior; do not invent one for static art.
   Stop Play and confirm the saved authoring state is retained.
8. Report actual Editor/package versions, saved scene/Tile/atlas paths, imported
   PPU/filter, cell assertions and screenshot path. Distinguish visual evidence
   from state checks. If an existing tool/API cannot perform a required step,
   report the gap for a separate issue and link it; do not expand CLI/bridge
   functionality or hand-edit serialized Unity YAML here.

## Examples

- "Import grass.png, paint a small Tilemap and check it through a Pixel Perfect Camera."
- "スプライトの取り込みから Tile / Sprite Atlas を作り、Game 画面で確認して。"
- "Only make a Sprite Atlas" → `unity-asset-management`.

After selecting the project/port and creating the asset folder:

```bash
unity-cli raw manage_asset_import_settings --json '{"action":"modify","assetPath":"Assets/Tiles/grass.png","settings":{"textureType":"Sprite","filterMode":"Point","generateMipMaps":false,"compressionQuality":100}}'
unity-cli raw create_sprite_atlas --json '{"atlasPath":"Assets/Tiles/Terrain.spriteatlas","packables":["Assets/Tiles/grass.png"],"packingSettings":{"allowRotation":false},"textureSettings":{"filterMode":"Point","generateMipMaps":false}}'
unity-cli raw save_scene --json '{"scenePath":"Assets/Scenes/Terrain.unity"}'
unity-cli raw play_game --json '{}'
unity-cli raw get_editor_state --json '{}'
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720,"osFallback":false}'
unity-cli raw read_console --json '{"count":20}'
unity-cli raw stop_game --json '{}'
```

## References

- [runtime-checklist.md](references/runtime-checklist.md): CLI and Editor selection.
- [tilemap-recipe.md](references/tilemap-recipe.md): import, Tile/RuleTile, camera and persistence details.
