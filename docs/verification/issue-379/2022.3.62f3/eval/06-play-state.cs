var failures = new System.Collections.Generic.List<string>();
if (!EditorApplication.isPlaying) failures.Add("not in Play");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var mapGo = GameObject.Find("/Grid/Tilemap");
var map = mapGo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
var rend = mapGo.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>();
var cam = Camera.main;
var ppc = cam.GetComponent<UnityEngine.U2D.PixelPerfectCamera>();
var cells = new System.Collections.Generic.List<object>();
var expected = new object[][] {
    new object[] { new Vector3Int(0, 0, 0), "Grass379", "grass379" }, new object[] { new Vector3Int(1, 0, 0), "Grass379", "grass379" }, new object[] { new Vector3Int(2, 0, 0), "Grass379", "grass379" },
    new object[] { new Vector3Int(0, 2, 0), "GrassRule379", "edge379" }, new object[] { new Vector3Int(1, 2, 0), "GrassRule379", "grass379" }, new object[] { new Vector3Int(3, 2, 0), "GrassRule379", "grass379" } };
foreach (var e in expected)
{
    var c = (Vector3Int)e[0]; var t = map.GetTile(c); var s = map.GetSprite(c);
    if (t == null || t.name != (string)e[1]) failures.Add("cell " + c + " tile " + (t == null ? "null" : t.name));
    if (s == null || s.name != (string)e[2]) failures.Add("cell " + c + " sprite " + (s == null ? "null" : s.name));
    cells.Add(new { cell = c.ToString(), tile = t == null ? null : t.name, sprite = s == null ? null : s.name, spritePacked = s != null && s.packed, spriteTexture = s == null ? null : s.texture.name });
}
// Dynamic neighbour-rule check in Play (after save + domain reload); Play changes are discarded on Stop.
var ruleTile = map.GetTile(new Vector3Int(1, 2, 0));
var gap = new Vector3Int(2, 2, 0);
var rightBefore = map.GetSprite(new Vector3Int(1, 2, 0)).name;
map.SetTile(gap, ruleTile);
var rightWithNeighbor = map.GetSprite(new Vector3Int(1, 2, 0)).name;
var gapWithLoneNeighbor = map.GetSprite(gap).name;
map.SetTile(gap, null);
var rightAfterRemoval = map.GetSprite(new Vector3Int(1, 2, 0)).name;
if (rightBefore != "grass379") failures.Add("play nonmatch before: " + rightBefore);
if (rightWithNeighbor != "edge379") failures.Add("play match: " + rightWithNeighbor);
if (gapWithLoneNeighbor != "edge379") failures.Add("play gap-with-right-neighbor: " + gapWithLoneNeighbor);
if (rightAfterRemoval != "grass379") failures.Add("play nonmatch after removal: " + rightAfterRemoval);
var gv = UnityEditor.EditorWindow.focusedWindow;
return new { passed = failures.Count == 0, failures = failures.ToArray(), isPlaying = EditorApplication.isPlaying, isPaused = EditorApplication.isPaused, isCompiling = EditorApplication.isCompiling, scene = scene.path, frameCount = Time.frameCount, cells = cells.ToArray(), dynamicRule = new { rightBefore, rightWithNeighbor, gapWithLoneNeighbor, rightAfterRemoval }, material = rend.sharedMaterial.name, shader = rend.sharedMaterial.shader.name, shaderSupported = rend.sharedMaterial.shader.isSupported, rendererEnabled = rend.enabled, rendererVisible = rend.isVisible, ppc = new { ppc.assetsPPU, ppc.refResolutionX, ppc.refResolutionY, ppc.pixelRatio, ppc.enabled }, camera = new { cam.orthographic, cam.orthographicSize, position = cam.transform.position.ToString(), cam.pixelWidth, cam.pixelHeight }, screen = new { Screen.width, Screen.height } };
