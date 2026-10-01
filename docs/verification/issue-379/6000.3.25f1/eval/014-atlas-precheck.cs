var p = "Assets/Tiles/TerrainRecheck.spriteatlas";
return new { path = p, mainAssetExists = AssetDatabase.LoadMainAssetAtPath(p) != null, guid = AssetDatabase.AssetPathToGUID(p), fileExists = System.IO.File.Exists(p),
    spritePackerMode = EditorSettings.spritePackerMode.ToString() };
