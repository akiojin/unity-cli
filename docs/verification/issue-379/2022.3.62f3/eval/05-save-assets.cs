AssetDatabase.SaveAssets();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return new { scene = scene.path, dirty = scene.isDirty, rootCount = scene.rootCount };
