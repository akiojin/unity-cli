var p = "Assets/Tiles379/TerrainRecheck379.spriteatlas";
var all = new System.Collections.Generic.List<string>();
foreach (var g in AssetDatabase.FindAssets("t:SpriteAtlas")) all.Add(AssetDatabase.GUIDToAssetPath(g));
return new { exists = AssetDatabase.LoadMainAssetAtPath(p) != null, fileExists = System.IO.File.Exists(p), atlases = all.ToArray() };
