var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (EditorApplication.isPlaying) throw new Exception("still playing");
if (s.isDirty) throw new Exception("scene dirty; refusing to exit: " + s.path);
AssetDatabase.SaveAssets();
EditorApplication.delayCall += () => EditorApplication.Exit(0);
return new { scene = s.path, s.isDirty, exit = "scheduled EditorApplication.Exit(0)" };
