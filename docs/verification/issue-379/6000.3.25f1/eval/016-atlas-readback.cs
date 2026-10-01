var p = "Assets/Tiles/TerrainRecheck.spriteatlas";
var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(p);
if (atlas == null) throw new Exception("atlas missing");
var paths = new System.Collections.Generic.List<string>();
foreach (var o in UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas)) paths.Add(AssetDatabase.GetAssetPath(o));
var tex = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(atlas);
var pack = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(atlas);
var plat = UnityEditor.U2D.SpriteAtlasExtensions.GetPlatformSettings(atlas, "DefaultTexturePlatform");
var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Tiles/grass.png");
return new { path = p, guid = AssetDatabase.AssetPathToGUID(p), packables = paths.ToArray(),
    isVariant = UnityEditor.U2D.SpriteAtlasExtensions.IsIncludeInBuild(atlas),
    texture = new { filter = tex.filterMode.ToString(), tex.generateMipMaps, tex.readable, tex.sRGB, tex.anisoLevel },
    packing = new { pack.enableRotation, pack.enableTightPacking, pack.enableAlphaDilation, pack.padding, pack.blockOffset },
    platform = new { plat.format, compression = plat.textureCompression.ToString(), plat.maxTextureSize },
    spriteCountInAtlas = atlas.spriteCount, atlasCanBind = atlas.CanBindTo(sprite),
    spritePackerMode = EditorSettings.spritePackerMode.ToString(), grassSpritePacked = sprite.packed };
