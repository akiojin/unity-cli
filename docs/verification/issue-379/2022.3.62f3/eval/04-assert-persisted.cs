var failures = new System.Collections.Generic.List<string>();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/Generated/E2E/Tilemap379.unity") failures.Add("active scene is " + scene.path);
if (scene.isDirty) failures.Add("scene is dirty");
// importer
var importers = new System.Collections.Generic.List<object>();
foreach (var path in new string[] { "Assets/Tiles379/grass379.png", "Assets/Tiles379/edge379.png" })
{
    var imp = AssetImporter.GetAtPath(path) as TextureImporter;
    var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
    if (imp == null || spr == null) { failures.Add("importer/sprite missing " + path); continue; }
    if (imp.textureType != TextureImporterType.Sprite) failures.Add(path + " type " + imp.textureType);
    if (imp.spriteImportMode != SpriteImportMode.Single) failures.Add(path + " mode " + imp.spriteImportMode);
    if (imp.spritePixelsPerUnit != 16f) failures.Add(path + " ppu " + imp.spritePixelsPerUnit);
    if (imp.filterMode != FilterMode.Point) failures.Add(path + " filter " + imp.filterMode);
    if (imp.mipmapEnabled) failures.Add(path + " mipmaps enabled");
    if (imp.textureCompression != TextureImporterCompression.Uncompressed) failures.Add(path + " compression " + imp.textureCompression);
    if (spr.rect.width != 16f || spr.rect.height != 16f) failures.Add(path + " rect " + spr.rect);
    importers.Add(new { path, type = imp.textureType.ToString(), mode = imp.spriteImportMode.ToString(), ppu = imp.spritePixelsPerUnit, filter = imp.filterMode.ToString(), mipmaps = imp.mipmapEnabled, compression = imp.textureCompression.ToString(), rectW = spr.rect.width, rectH = spr.rect.height, texFormat = spr.texture.format.ToString(), texMipCount = spr.texture.mipmapCount, spritePacked = spr.packed });
}
// atlas
var atlasPath = "Assets/Tiles379/Terrain379.spriteatlas";
var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(atlasPath);
var packableNames = new System.Collections.Generic.List<string>();
object atlasInfo = null;
if (atlas == null) failures.Add("atlas missing");
else
{
    foreach (var p in UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(atlas)) packableNames.Add(AssetDatabase.GetAssetPath(p));
    var ps = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(atlas);
    var ts = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(atlas);
    if (packableNames.Count != 2 || !packableNames.Contains("Assets/Tiles379/grass379.png") || !packableNames.Contains("Assets/Tiles379/edge379.png")) failures.Add("atlas packables mismatch");
    if (ps.enableRotation) failures.Add("atlas rotation enabled");
    if (ts.filterMode != FilterMode.Point) failures.Add("atlas filter " + ts.filterMode);
    if (ts.generateMipMaps) failures.Add("atlas mipmaps enabled");
    atlasInfo = new { atlasPath, packables = packableNames.ToArray(), ps.enableRotation, ps.enableTightPacking, ps.padding, filterMode = ts.filterMode.ToString(), ts.generateMipMaps };
}
// tile assets
var tile = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.Tile>("Assets/Tiles379/Grass379.asset");
var ruleTile = AssetDatabase.LoadAssetAtPath<UnityEngine.RuleTile>("Assets/Tiles379/GrassRule379.asset");
if (tile == null || tile.sprite == null || tile.sprite.name != "grass379") failures.Add("Tile asset or sprite reference lost");
object ruleInfo = null;
if (ruleTile == null) failures.Add("RuleTile asset missing");
else
{
    if (ruleTile.m_DefaultSprite == null || ruleTile.m_DefaultSprite.name != "grass379") failures.Add("RuleTile default sprite lost");
    if (ruleTile.m_TilingRules.Count != 1) failures.Add("RuleTile rule count " + ruleTile.m_TilingRules.Count);
    else
    {
        var r = ruleTile.m_TilingRules[0];
        if (r.m_NeighborPositions.Count != 1 || r.m_Neighbors.Count != 1) failures.Add("rule neighbor list lengths " + r.m_NeighborPositions.Count + "/" + r.m_Neighbors.Count);
        else if (r.m_NeighborPositions[0] != new Vector3Int(1, 0, 0) || r.m_Neighbors[0] != UnityEngine.RuleTile.TilingRuleOutput.Neighbor.This) failures.Add("rule neighbor content mismatch");
        if (r.m_Sprites == null || r.m_Sprites.Length != 1 || r.m_Sprites[0] == null || r.m_Sprites[0].name != "edge379") failures.Add("rule sprite lost");
        ruleInfo = new { defaultSprite = ruleTile.m_DefaultSprite == null ? null : ruleTile.m_DefaultSprite.name, rules = ruleTile.m_TilingRules.Count, neighborPositions = r.m_NeighborPositions.Count, neighbors = r.m_Neighbors.Count, pos0 = r.m_NeighborPositions.Count > 0 ? r.m_NeighborPositions[0].ToString() : null, neighbor0 = r.m_Neighbors.Count > 0 ? r.m_Neighbors[0] : -1, ruleSprite = r.m_Sprites.Length > 0 && r.m_Sprites[0] != null ? r.m_Sprites[0].name : null };
    }
}
// hierarchy and cells
var gridGo = GameObject.Find("/Grid");
var mapGo = GameObject.Find("/Grid/Tilemap");
var cells = new System.Collections.Generic.List<object>();
object mapInfo = null;
if (gridGo == null || mapGo == null || gridGo.GetComponent<Grid>() == null) failures.Add("Grid/Tilemap hierarchy missing");
else
{
    var map = mapGo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
    var rend = mapGo.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();
    if (map == null || rend == null) failures.Add("Tilemap/TilemapRenderer component missing");
    else
    {
        var expected = new object[][] {
            new object[] { new Vector3Int(0, 0, 0), "Assets/Tiles379/Grass379.asset", "grass379" },
            new object[] { new Vector3Int(1, 0, 0), "Assets/Tiles379/Grass379.asset", "grass379" },
            new object[] { new Vector3Int(2, 0, 0), "Assets/Tiles379/Grass379.asset", "grass379" },
            new object[] { new Vector3Int(0, 2, 0), "Assets/Tiles379/GrassRule379.asset", "edge379" },
            new object[] { new Vector3Int(1, 2, 0), "Assets/Tiles379/GrassRule379.asset", "grass379" },
            new object[] { new Vector3Int(3, 2, 0), "Assets/Tiles379/GrassRule379.asset", "grass379" } };
        foreach (var e in expected)
        {
            var c = (Vector3Int)e[0]; var t = map.GetTile(c); var s = map.GetSprite(c);
            var tp = t == null ? null : AssetDatabase.GetAssetPath(t); var sn = s == null ? null : s.name;
            if (tp != (string)e[1]) failures.Add("cell " + c + " tile " + tp);
            if (sn != (string)e[2]) failures.Add("cell " + c + " sprite " + sn);
            cells.Add(new { cell = c.ToString(), tile = tp, sprite = sn });
        }
        if (map.GetTile(new Vector3Int(2, 2, 0)) != null) failures.Add("gap cell (2,2) unexpectedly painted");
        var painted = 0; foreach (var p in map.cellBounds.allPositionsWithin) if (map.HasTile(p)) painted++;
        if (painted != 6) failures.Add("painted cell count " + painted);
        var grid = gridGo.GetComponent<Grid>();
        if (grid.cellSize != new Vector3(1, 1, 1) && grid.cellSize != new Vector3(1, 1, 0)) failures.Add("cell size " + grid.cellSize);
        if (rend.sharedMaterial == null || rend.sharedMaterial.shader.name != "Sprites/Default") failures.Add("tilemap material not Sprites/Default");
        mapInfo = new { gridInstanceId = gridGo.GetInstanceID(), mapInstanceId = mapGo.GetInstanceID(), cellSize = grid.cellSize.ToString(), paintedCells = painted, usedTiles = map.GetUsedTilesCount(), cellBounds = map.cellBounds.ToString(), material = rend.sharedMaterial == null ? null : rend.sharedMaterial.name, shader = rend.sharedMaterial == null ? null : rend.sharedMaterial.shader.name, localPosition = mapGo.transform.localPosition.ToString(), localScale = mapGo.transform.localScale.ToString() };
    }
}
// camera
object camInfo = null;
var cam = Camera.main;
if (cam == null) failures.Add("Main Camera missing");
else
{
    var ppcs = cam.GetComponents<UnityEngine.U2D.PixelPerfectCamera>();
    if (ppcs.Length != 1) failures.Add("PixelPerfectCamera count " + ppcs.Length);
    else
    {
        var ppc = ppcs[0];
        if (ppc.assetsPPU != 16) failures.Add("assetsPPU " + ppc.assetsPPU);
        if (ppc.refResolutionX != 320 || ppc.refResolutionY != 180) failures.Add("ref resolution " + ppc.refResolutionX + "x" + ppc.refResolutionY);
        if (!cam.orthographic) failures.Add("camera not orthographic");
        if (cam.transform.position != new Vector3(2f, 1.5f, -10f)) failures.Add("camera position " + cam.transform.position);
        camInfo = new { cameraInstanceId = cam.gameObject.GetInstanceID(), type = ppc.GetType().FullName, ppc.assetsPPU, ppc.refResolutionX, ppc.refResolutionY, ppc.enabled, orthographic = cam.orthographic, orthographicSize = cam.orthographicSize, position = cam.transform.position.ToString(), near = cam.nearClipPlane, far = cam.farClipPlane, cullingMask = cam.cullingMask, clearFlags = cam.clearFlags.ToString() };
    }
}
// recheck atlas (new path, created by corrected skill example)
object recheckAtlasInfo = null;
var rcPath = "Assets/Tiles379/TerrainRecheck379.spriteatlas";
var rcAtlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(rcPath);
if (rcAtlas == null) failures.Add("recheck atlas missing");
else
{
    var rcPack = new System.Collections.Generic.List<string>();
    foreach (var p in UnityEditor.U2D.SpriteAtlasExtensions.GetPackables(rcAtlas)) rcPack.Add(AssetDatabase.GetAssetPath(p));
    var rps = UnityEditor.U2D.SpriteAtlasExtensions.GetPackingSettings(rcAtlas);
    var rts = UnityEditor.U2D.SpriteAtlasExtensions.GetTextureSettings(rcAtlas);
    if (rcPack.Count != 1 || rcPack[0] != "Assets/Tiles379/grass379.png") failures.Add("recheck atlas packables " + string.Join(",", rcPack.ToArray()));
    if (rps.enableRotation) failures.Add("recheck atlas rotation enabled");
    if (rts.filterMode != FilterMode.Point) failures.Add("recheck atlas filter " + rts.filterMode);
    if (rts.generateMipMaps) failures.Add("recheck atlas mipmaps enabled");
    recheckAtlasInfo = new { rcPath, packables = rcPack.ToArray(), rps.enableRotation, rps.enableTightPacking, rps.padding, filterMode = rts.filterMode.ToString(), rts.generateMipMaps };
}
return new { passed = failures.Count == 0, recheckAtlas = recheckAtlasInfo, failures = failures.ToArray(), isPlaying = EditorApplication.isPlaying, scenePath = scene.path, sceneDirty = scene.isDirty, importers = importers.ToArray(), atlas = atlasInfo, tileSprite = tile == null || tile.sprite == null ? null : tile.sprite.name, ruleTile = ruleInfo, tilemap = mapInfo, cells = cells.ToArray(), camera = camInfo, spritePackerMode = EditorSettings.spritePackerMode.ToString() };
