# Tilemap authoring recipe

## Table of Contents

- Sprite import and atlas
- Tile assets and painting
- Pixel Perfect and persistence

Use `unity-cli editor eval '<code>' --mode statements --request-id <unique-id>
--output json` for the C# below. Adapt paths to the requested project and inspect
existing assets first. Require `state: completed`; transport success does not
imply evaluation success. On a timeout query `editor eval-status <id>` and inspect
the actual assets before retrying. Never rerun a mutation blindly with a new ID.
Use fully qualified names outside the implicit System/UnityEngine/UnityEditor
namespaces. Do not edit `.asset`, `.unity` or `.meta` YAML.

## Sprite import and atlas

The import tool's `textureType: Sprite` sets Single mode. Do not send this setting
to an existing sliced Multiple texture unless conversion is intended. PPU and
Sprite Mode are not import-tool settings; unknown keys may be silently ignored.
After the narrow import-tool call, a Single sprite example is:

```csharp
var path = "Assets/Tiles/grass.png";
var importer = AssetImporter.GetAtPath(path) as TextureImporter;
if (importer == null) throw new Exception("TextureImporter missing");
importer.textureType = TextureImporterType.Sprite;
importer.spriteImportMode = SpriteImportMode.Single;
importer.spritePixelsPerUnit = 16;
importer.SaveAndReimport();
var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
if (sprite == null) throw new Exception("Sprite missing after import");
return new { importer.spritePixelsPerUnit, mode = importer.spriteImportMode.ToString(),
    filter = importer.filterMode.ToString(), width = sprite.rect.width,
    height = sprite.rect.height };
```

For Multiple, preserve existing slices through the installed Sprite Editor data
provider API (`UnityEditor.U2D.Sprites` in `com.unity.2d.sprite`); inspect that
version's API before changing slice rectangles, names and IDs. Load all subassets
and choose the exact Sprite name; never silently choose the first slice. Verify
each slice survives reimport. A 16×16 sprite at 16 PPU spans one unit cell.

Use `create_sprite_atlas` for the atlas, then read it with
`AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>` and
`UnityEditor.U2D.SpriteAtlasExtensions.GetPackables`. Inspect actual texture and
packing settings through the same extensions; for pixel art use Point, no
mipmaps, no rotation and appropriate padding. Do not use `overwrite:true` merely
because a retry finds an existing atlas.
Atlas creation alone does not enable runtime packing. Inspect the project's
Sprite Packer mode and the rendered sprite's `packed` value before claiming
the atlas is in use; report an asset-only atlas check when packing is disabled.

## Tile assets and painting

Create missing folders through `AssetDatabase.CreateFolder` before creating assets.
The following minimal Tile creates no runtime script:

```csharp
var path = "Assets/Tiles/Grass.asset";
if (AssetDatabase.LoadMainAssetAtPath(path) != null)
    throw new Exception("Inspect existing tile before changing it");
var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Tiles/grass.png");
if (sprite == null) throw new Exception("Sprite missing");
var tile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
tile.sprite = sprite;
tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
AssetDatabase.CreateAsset(tile, path);
AssetDatabase.SaveAssets();
return tile;
```

On a new empty scene, create a `Grid` GameObject and a child GameObject with
`UnityEngine.Tilemaps.Tilemap` and `UnityEngine.Tilemaps.TilemapRenderer` components.
Set local position to zero and scale to one. For an existing scene locate the
explicit hierarchy path first. Paint with `map.SetTile(new Vector3Int(x,y,0),tile)`.
Read each expected cell with `map.GetTile`, plus `GetSprite` for rendered sprite
identity. Set a compatible material: Built-in `Sprites/Default`, or a sprite
material compatible with the project's existing URP renderer. Check shader
availability; do not change render pipeline settings to hide missing materials.

For RuleTile, confirm the package/type resolves before compiling a snippet that
references `UnityEngine.RuleTile`. Create a RuleTile asset, assign
`m_DefaultSprite`, then populate `m_TilingRules` with `RuleTile.TilingRule` objects.
For example, a rule for an identical tile immediately to the right uses matching
entries `m_NeighborPositions = [new Vector3Int(1,0,0)]` and
`m_Neighbors = [RuleTile.TilingRule.Neighbor.This]`, plus `m_Sprites = [edgeSprite]`
(construct typed lists/arrays, not C# collection expressions on older Editors).
Replace the default neighbor lists rather than appending to them.
Keep neighbor-list lengths equal. Test both matching and nonmatching cells,
verify `GetSprite`, then save/reopen and repeat. A default-only RuleTile is a
static fallback, not evidence that neighbor matching works.

## Pixel Perfect and persistence

Discover the loaded type before adding a camera component. Standalone typically
provides `UnityEngine.U2D.PixelPerfectCamera`; URP provides
`UnityEngine.Rendering.Universal.PixelPerfectCamera`. Do not assume one based
solely on Editor version. Use the installed package's public properties (or
reflection after resolving that exact type). Configure `assetsPPU`,
`refResolutionX`, `refResolutionY`, and inspect their values. For a Built-in
example with the standalone package:

```csharp
var camera = Camera.main;
if (camera == null) throw new Exception("Target camera missing");
camera.orthographic = true;
camera.transform.position = new Vector3(1.5f, 0.5f, -10);
var pixel = camera.GetComponent<UnityEngine.U2D.PixelPerfectCamera>();
if (pixel == null) pixel = camera.gameObject.AddComponent<UnityEngine.U2D.PixelPerfectCamera>();
pixel.assetsPPU = 16;
pixel.refResolutionX = 320;
pixel.refResolutionY = 180;
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
return new { pixel.assetsPPU, pixel.refResolutionX, pixel.refResolutionY };
```

Save dirty assets with `EditorUtility.SetDirty` and `AssetDatabase.SaveAssets`.
Mark and save the scene after painting as well as camera changes. Use generated
E2E scenes under `Assets/Scenes/Generated/E2E/` when verifying this repository.
Reopen the saved scene in Edit Mode and require:

- Sprite import PPU/filter/mode and atlas packables match the request.
- Grid/Tilemap hierarchy exists and every expected cell references its saved Tile.
- Tile/RuleTile sprite references and camera component/settings persist.
- Play state is true, compilation errors are zero, and Game capture really uses
  `captureMode:game` with OS fallback disabled. Inspect the returned capture
  source and image: some bridge versions render Main Camera rather than the
  final Game-view backbuffer. State that limitation; a camera render does not
  prove final UI or post-processing output (see [#422](https://github.com/akiojin/unity-cli/issues/422)).
- Stop returns to Edit Mode with the saved scene still intact.

Official API references (not plugin skill sources):

- [Tilemap.SetTile](https://docs.unity3d.com/ScriptReference/Tilemaps.Tilemap.SetTile.html)
- [RuleTile API](https://docs.unity3d.com/Packages/com.unity.2d.tilemap.extras@3.0/api/UnityEngine.RuleTile.html)
- [Pixel Perfect settings](https://docs.unity3d.com/Packages/com.unity.2d.pixel-perfect@5.0/manual/components.html)
