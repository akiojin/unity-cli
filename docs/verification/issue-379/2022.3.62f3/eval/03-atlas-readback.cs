var p = "Assets/Tiles379/TerrainRecheck379.spriteatlas";
var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(p);
if (atlas == null) throw new Exception("atlas missing");
var packables = new System.Collections.Generic.List<string>();
foreach (var o in UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas)) packables.Add(AssetDatabase.GetAssetPath(o) + " (" + o.GetType().Name + ")");
var ps = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(atlas);
var ts = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(atlas);
var plat = UnityEditor.U2D.SpriteAtlasExtensions.GetPlatformSettings(atlas, "DefaultTexturePlatform");
return new { path = p, packables = packables.ToArray(), ps.enableRotation, ps.enableTightPacking, ps.padding, ps.blockOffset, filterMode = ts.filterMode.ToString(), ts.generateMipMaps, ts.readable, ts.sRGB, defaultCompression = plat.textureCompression.ToString(), spriteCount = atlas.spriteCount, spritePackerMode = EditorSettings.spritePackerMode.ToString(), isVariant = atlas.isVariant };
