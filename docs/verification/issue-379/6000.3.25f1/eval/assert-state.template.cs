var checks = new System.Collections.Generic.List<object>();
int failed = 0;
System.Action<string, object, object> expect = (name, expected, actual) => {
    bool pass = object.Equals(expected, actual);
    if (!pass) failed++;
    checks.Add(new { name, expected, actual, pass });
};
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
expect("scene.path", "Assets/Scenes/Generated/E2E/Tilemap379.unity", scene.path);
expect("scene.isDirty", false, scene.isDirty);
expect("EditorApplication.isPlaying", __EXPECT_PLAYING__, EditorApplication.isPlaying);
// Importer
foreach (var path in new[] { "Assets/Tiles/grass.png", "Assets/Tiles/edge.png" }) {
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
    expect(path + " textureType", "Sprite", importer.textureType.ToString());
    expect(path + " spriteImportMode", "Single", importer.spriteImportMode.ToString());
    expect(path + " spritePixelsPerUnit", 16f, importer.spritePixelsPerUnit);
    expect(path + " filterMode", "Point", importer.filterMode.ToString());
    expect(path + " mipmapEnabled", false, importer.mipmapEnabled);
    expect(path + " textureCompression", "Uncompressed", importer.textureCompression.ToString());
    expect(path + " sprite.rect", "16x16", sprite.rect.width + "x" + sprite.rect.height);
    expect(path + " sprite.pixelsPerUnit", 16f, sprite.pixelsPerUnit);
    expect(path + " texture.filterMode", "Point", sprite.texture.filterMode.ToString());
    expect(path + " texture.mipmapCount", 1, sprite.texture.mipmapCount);
}
// Atlas
var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>("Assets/Tiles/Terrain.spriteatlas");
expect("atlas exists", true, atlas != null);
var packables = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas), p => AssetDatabase.GetAssetPath(p)));
expect("atlas packables", "Assets/Tiles/grass.png,Assets/Tiles/edge.png", string.Join(",", packables));
var atex = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(atlas);
var apack = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(atlas);
expect("atlas filterMode", "Point", atex.filterMode.ToString());
expect("atlas generateMipMaps", false, atex.generateMipMaps);
expect("atlas enableRotation", false, apack.enableRotation);
expect("atlas enableTightPacking", false, apack.enableTightPacking);
expect("atlas padding", 2, apack.padding);
// NEW recheck atlas (created by the corrected SKILL.md create_sprite_atlas example)
if (__CHECK_NEW_ATLAS__) {
    var natlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>("Assets/Tiles/TerrainRecheck.spriteatlas");
    expect("new atlas exists", true, natlas != null);
    if (natlas != null) {
        var nps = new System.Collections.Generic.List<string>();
        foreach (var p in UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(natlas)) nps.Add(AssetDatabase.GetAssetPath(p));
        expect("new atlas packables", "Assets/Tiles/grass.png", string.Join(",", nps.ToArray()));
        var ntex = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(natlas);
        var npack = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(natlas);
        expect("new atlas filterMode", "Point", ntex.filterMode.ToString());
        expect("new atlas generateMipMaps", false, ntex.generateMipMaps);
        expect("new atlas enableRotation", false, npack.enableRotation);
    }
}
// Tile assets
var tile = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.Tile>("Assets/Tiles/Grass.asset");
expect("Grass.asset sprite", "Assets/Tiles/grass.png", tile == null ? null : AssetDatabase.GetAssetPath(tile.sprite));
var rule = AssetDatabase.LoadAssetAtPath<UnityEngine.RuleTile>("Assets/Tiles/GrassRule.asset");
expect("GrassRule.asset defaultSprite", "Assets/Tiles/grass.png", rule == null ? null : AssetDatabase.GetAssetPath(rule.m_DefaultSprite));
expect("GrassRule rule count", 1, rule.m_TilingRules.Count);
expect("GrassRule rule neighborPositions", "(1, 0, 0)", string.Join(";", System.Linq.Enumerable.Select(rule.m_TilingRules[0].m_NeighborPositions, p => p.ToString())));
expect("GrassRule rule neighbors (This=1)", "1", string.Join(";", rule.m_TilingRules[0].m_Neighbors));
expect("GrassRule rule sprite", "Assets/Tiles/edge.png", AssetDatabase.GetAssetPath(rule.m_TilingRules[0].m_Sprites[0]));
// Hierarchy and cells
var gridGo = GameObject.Find("/Grid");
var mapGo = GameObject.Find("/Grid/Tilemap");
expect("/Grid has Grid", true, gridGo != null && gridGo.GetComponent<Grid>() != null);
expect("Grid count in scene", 1, UnityEngine.Object.FindObjectsByType<Grid>(FindObjectsSortMode.None).Length);
expect("Tilemap count in scene", 1, UnityEngine.Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None).Length);
var map = mapGo == null ? null : mapGo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
var rend = mapGo == null ? null : mapGo.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();
expect("/Grid/Tilemap has Tilemap+TilemapRenderer", true, map != null && rend != null);
expect("Grid cellSize", "(1.00, 1.00, 1.00)", gridGo.GetComponent<Grid>().cellSize.ToString());
expect("TilemapRenderer shader", "Sprites/Default", rend.sharedMaterial == null ? null : rend.sharedMaterial.shader.name);
System.Func<int, int, string> cell = (x, y) => {
    var c = new Vector3Int(x, y, 0); var t = map.GetTile(c); var s = map.GetSprite(c);
    return (t == null ? "null" : t.name) + "/" + (s == null ? "null" : s.name);
};
expect("cell (0,0) tile/sprite", "Grass/grass", cell(0, 0));
expect("cell (1,0) tile/sprite", "Grass/grass", cell(1, 0));
expect("cell (2,0) tile/sprite", "Grass/grass", cell(2, 0));
expect("cell (0,0) tile is saved asset", "Assets/Tiles/Grass.asset", AssetDatabase.GetAssetPath(map.GetTile(new Vector3Int(0, 0, 0))));
expect("rule cell (0,2) MATCH right=This -> alternate", "GrassRule/edge", cell(0, 2));
expect("rule cell (1,2) NONMATCH no right neighbour -> default", "GrassRule/grass", cell(1, 2));
expect("rule cell (0,2) tile is saved asset", "Assets/Tiles/GrassRule.asset", AssetDatabase.GetAssetPath(map.GetTile(new Vector3Int(0, 2, 0))));
expect("cell (1,1) empty", "null/null", cell(1, 1));
expect("cell (2,2) empty", "null/null", cell(2, 2));
expect("used cell count", 5, map.GetTilesRangeCount(new Vector3Int(-50, -50, 0), new Vector3Int(50, 50, 0)));
// Camera
var cam = Camera.main;
expect("Camera.main exists", true, cam != null);
expect("camera count", 1, UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
expect("camera orthographic", true, cam.orthographic);
expect("camera position", "(1.50, 1.50, -10.00)", cam.transform.position.ToString());
expect("camera cullingMask sees tilemap layer", true, (cam.cullingMask & (1 << mapGo.layer)) != 0);
expect("camera near<10<far", true, cam.nearClipPlane < 10f && cam.farClipPlane > 10f);
var ppcs = cam.GetComponents<UnityEngine.U2D.PixelPerfectCamera>();
expect("PixelPerfectCamera component count", 1, ppcs.Length);
var ppc = ppcs.Length > 0 ? ppcs[0] : null;
expect("PixelPerfectCamera type", "UnityEngine.U2D.PixelPerfectCamera", ppc == null ? null : ppc.GetType().FullName);
expect("PixelPerfectCamera enabled", true, ppc != null && ppc.enabled);
expect("PixelPerfectCamera assetsPPU", 16, ppc.assetsPPU);
expect("PixelPerfectCamera refResolutionX", 320, ppc.refResolutionX);
expect("PixelPerfectCamera refResolutionY", 180, ppc.refResolutionY);
var s00 = map.GetSprite(new Vector3Int(0,0,0)); var s02 = map.GetSprite(new Vector3Int(0,2,0));
expect("rendered sprites packed (atlas-only test expects false)", false, s00.packed || s02.packed);
return new { phase = "__PHASE__", frameCount = Time.frameCount, rendererVisible = rend.isVisible, shaderSupported = rend.sharedMaterial.shader.isSupported, spritePacked = new { grass = s00.packed, edge = s02.packed }, isPlaying = EditorApplication.isPlaying, total = checks.Count, failed,
    spritePackerMode = EditorSettings.spritePackerMode.ToString(),
    info = new { orthographicSize = cam.orthographicSize, pixelRatio = EditorApplication.isPlaying ? ppc.pixelRatio : -1, camPixelWidth = cam.pixelWidth, camPixelHeight = cam.pixelHeight,
        spriteTextureNames = map.GetSprite(new Vector3Int(0,0,0)).texture.name + "," + map.GetSprite(new Vector3Int(0,2,0)).texture.name },
    checks };
