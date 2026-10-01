var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return new { s.path, s.isDirty, isPlaying = EditorApplication.isPlaying };
