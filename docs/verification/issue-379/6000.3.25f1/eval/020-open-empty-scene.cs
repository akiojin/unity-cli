// Unload the authored scene (already saved, must not be dirty) so the next load reads it from disk.
var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (current.isDirty) throw new Exception("Scene still dirty; refusing to discard: " + current.path);
var empty = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
return new { previous = current.path, nowPath = empty.path, nowRootCount = empty.rootCount, gridStillLoaded = GameObject.Find("/Grid") != null };
