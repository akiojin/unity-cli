using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.Editor
{
    public class RuntimeChangePersistenceTests
    {
        private const string RuntimeObjectName = "UnityCliBridge_PlayModeTemp";
        private const string EditObjectName = "UnityCliBridge_EditModeSeed";
        private const string TemporaryScenePath = "Assets/UnityCliBridge_RuntimeChangePersistenceTests.unity";
        private GameObject _cameraOwner;

        [SetUp]
        public void EnsureCamera()
        {
            DestroyNamedObject(RuntimeObjectName);
            DestroyNamedObject(EditObjectName);
            DeleteTemporarySceneAsset();

            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                _cameraOwner = new GameObject("PlayModeTestCamera");
                var camera = _cameraOwner.AddComponent<Camera>();
                _cameraOwner.tag = "MainCamera";
                mainCamera = camera;
            }

            AttachUniversalCameraDataIfAvailable(mainCamera);
        }

        [TearDown]
        public void CleanupCamera()
        {
            DestroyNamedObject(RuntimeObjectName);
            DestroyNamedObject(EditObjectName);
            DeleteTemporarySceneAsset();

            if (_cameraOwner != null)
            {
                Object.DestroyImmediate(_cameraOwner);
                _cameraOwner = null;
            }
        }

        [UnityTest]
        public IEnumerator GameObjectSpawnedInPlayMode_IsDestroyedAfterExit()
        {
            MarkActiveSceneDirtyForPlayModeBackup();
            yield return new EnterPlayMode();

            var runtimeObject = new GameObject(RuntimeObjectName);

            yield return null; // ensure at least one frame in Play Mode
            Assert.IsNotNull(GameObject.Find(RuntimeObjectName), "Object should exist during Play Mode");

            yield return new ExitPlayMode();
            Assert.IsNull(GameObject.Find(RuntimeObjectName), "Objects created in Play Mode must not leak back to Edit Mode");
        }

        [UnityTest]
        public IEnumerator EditModeObjectRestoresSerializedStateAfterPlayMode()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var initialPosition = new Vector3(1f, 2f, 3f);
            var playModePosition = new Vector3(4f, 5f, 6f);

            // Create a scene object with a known serialized value.
            var editModeObject = new GameObject(EditObjectName);
            editModeObject.transform.position = initialPosition;
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, TemporaryScenePath),
                "Temporary scene must be saved before entering Play Mode");

            yield return new EnterPlayMode();

            var playInstance = GameObject.Find(EditObjectName);
            Assert.IsNotNull(playInstance, "Edit-mode objects should appear in Play Mode scene");

            playInstance.transform.position = playModePosition;
            yield return null; // let the change run at least one frame

            Assert.AreEqual(playModePosition, playInstance.transform.position,
                "Play Mode change should apply while running");

            yield return new ExitPlayMode();

            var editModeInstance = GameObject.Find(EditObjectName);
            Assert.IsNotNull(editModeInstance, "Original edit-mode object should still exist");
            Assert.AreEqual(initialPosition, editModeInstance.transform.position,
                "Serialized value must remain unchanged after leaving Play Mode");

            Object.DestroyImmediate(editModeObject);
        }

        private static void DestroyNamedObject(string name)
        {
            var obj = GameObject.Find(name);
            while (obj != null)
            {
                Object.DestroyImmediate(obj);
                obj = GameObject.Find(name);
            }
        }

        private static void MarkActiveSceneDirtyForPlayModeBackup()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void DeleteTemporarySceneAsset()
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(TemporaryScenePath)))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(TemporaryScenePath);
            }
        }

        private static void AttachUniversalCameraDataIfAvailable(Camera camera)
        {
            if (camera == null) return;

            var type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (type == null)
            {
                return;
            }

            if (camera.gameObject.GetComponent(type) == null)
            {
                camera.gameObject.AddComponent(type);
            }
        }
    }
}
