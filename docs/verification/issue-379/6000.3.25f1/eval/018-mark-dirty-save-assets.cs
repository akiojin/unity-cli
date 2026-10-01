var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/Generated/E2E/Tilemap379.unity") throw new Exception("unexpected scene " + scene.path);
var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>("Assets/Tiles/TerrainRecheck.spriteatlas");
EditorUtility.SetDirty(atlas);
AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
return new { scene = scene.path, isDirty = scene.isDirty };
