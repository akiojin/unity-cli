var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.isDirty) throw new Exception("Scene dirty; refusing to exit without saving");
if (EditorApplication.isPlaying) throw new Exception("Still in Play");
AssetDatabase.SaveAssets();
EditorApplication.Exit(0);
return true;
