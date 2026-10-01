#!/usr/bin/env python3
"""Build eval/assert-state.template.cs from the original run's evidence/eval/56b-assert-after-reload.cs
(reused state assertions) plus recheck additions; then instantiate: make_assert.py <label> <phase> <checkNewAtlas> <expectPlaying>"""
import sys, pathlib
here = pathlib.Path(__file__).resolve().parent
src = (here.parent / "evidence/eval/56b-assert-after-reload.cs").read_text()
new_atlas = """// NEW recheck atlas (created by the corrected SKILL.md create_sprite_atlas example)
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
"""
def rep(s, a, b):
    assert a in s, a
    return s.replace(a, b, 1)
src = rep(src, "// Tile assets\n", new_atlas + "// Tile assets\n")
src = rep(src, 'expect("scene.isDirty", false, scene.isDirty);',
          'expect("scene.isDirty", false, scene.isDirty);\nexpect("EditorApplication.isPlaying", __EXPECT_PLAYING__, EditorApplication.isPlaying);')
src = rep(src, 'return new { phase = "after-reload-edit-mode"',
          'var s00 = map.GetSprite(new Vector3Int(0,0,0)); var s02 = map.GetSprite(new Vector3Int(0,2,0));\n'
          'expect("rendered sprites packed (atlas-only test expects false)", false, s00.packed || s02.packed);\n'
          'return new { phase = "__PHASE__", frameCount = Time.frameCount, rendererVisible = rend.isVisible, '
          'shaderSupported = rend.sharedMaterial.shader.isSupported, spritePacked = new { grass = s00.packed, edge = s02.packed }')
(here / "eval/assert-state.template.cs").write_text(src)
if len(sys.argv) == 5:
    label, phase, chk, play = sys.argv[1:]
    out = src.replace("__PHASE__", phase).replace("__CHECK_NEW_ATLAS__", chk).replace("__EXPECT_PLAYING__", play)
    (here / f"eval/{label}.cs").write_text(out)
    print("wrote", label)
