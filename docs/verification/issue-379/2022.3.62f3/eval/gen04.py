src=open("evidence/eval/12-assert-persisted.cs").read()
add='''// recheck atlas (new path, created by corrected skill example)
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
return new { passed = failures.Count == 0, recheckAtlas = recheckAtlasInfo,'''
assert "return new { passed = failures.Count == 0," in src
src=src.replace("return new { passed = failures.Count == 0,", add, 1)
open("evidence-recheck/eval/04-assert-persisted.cs","w").write(src)
