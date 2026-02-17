using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.Editor
{
    public class RuntimeChangePersistenceTests
    {
        private const string RuntimeObjectName = "UnityCliBridge_PlayModeTemp";
        private const string EditObjectName = "UnityCliBridge_EditModeSeed";
        private GameObject _cameraOwner;

        [SetUp]
        public void EnsureCamera()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                _cameraOwner = new GameObject("PlayModeTestCamera");
                var camera = _cameraOwner.AddComponent<Camera>();
                _cameraOwner.tag = "MainCamera";
                mainCamera = camera;
            }

            AttachUniversalCameraDataIfAvailable(mainCamera);

            if (_cameraOwner != null)
            {
                Object.DontDestroyOnLoad(_cameraOwner);
            }
        }

        [TearDown]
        public void CleanupCamera()
        {
            if (_cameraOwner != null)
            {
                Object.DestroyImmediate(_cameraOwner);
                _cameraOwner = null;
            }
        }

        [UnityTest]
        public IEnumerator GameObjectSpawnedInPlayMode_IsDestroyedAfterExit()
        {
            yield return new EnterPlayMode();

            var runtimeObject = new GameObject(RuntimeObjectName);
            runtimeObject.AddComponent<MarkerComponent>().value = 99f;

            yield return null; // ensure at least one frame in Play Mode
            Assert.IsNotNull(GameObject.Find(RuntimeObjectName), "Object should exist during Play Mode");

            yield return new ExitPlayMode();
            Assert.IsNull(GameObject.Find(RuntimeObjectName), "Objects created in Play Mode must not leak back to Edit Mode");
        }

        [UnityTest]
        public IEnumerator EditModeObjectRestoresSerializedStateAfterPlayMode()
        {
            // Create a scene object with a known serialized value
            var editModeObject = new GameObject(EditObjectName);
            var marker = editModeObject.AddComponent<MarkerComponent>();
            marker.value = 1f;

            yield return new EnterPlayMode();

            var playInstance = GameObject.Find(EditObjectName);
            Assert.IsNotNull(playInstance, "Edit-mode objects should appear in Play Mode scene");

            var playMarker = playInstance.GetComponent<MarkerComponent>();
            playMarker.value = 42f;
            yield return null; // let the change run at least one frame

            Assert.AreEqual(42f, playMarker.value, "Play Mode change should apply while running");

            yield return new ExitPlayMode();

            var editModeInstance = GameObject.Find(EditObjectName);
            Assert.IsNotNull(editModeInstance, "Original edit-mode object should still exist");
            Assert.AreEqual(1f, editModeInstance.GetComponent<MarkerComponent>().value,
                "Serialized value must remain unchanged after leaving Play Mode");

            Object.DestroyImmediate(editModeObject);
        }

        private class MarkerComponent : MonoBehaviour
        {
            public float value;
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
