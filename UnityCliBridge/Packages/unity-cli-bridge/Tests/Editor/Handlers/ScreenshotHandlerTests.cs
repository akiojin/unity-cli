using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityCliBridge.Handlers;
using UnityCliBridge.Tests.Helpers;

namespace UnityCliBridge.Tests.Handlers
{
    public class ScreenshotHandlerTests
    {
        [UnityTest]
        public IEnumerator IncludeUi_RejectsUnfocusedGameViewWithoutChangingFocus()
        {
            return TaskTestUtility.Await(async () =>
            {
                var sceneView = EditorWindow.GetWindow<SceneView>();
                sceneView.Focus();
                await Task.Delay(100);
                var result = await Capture(new JObject { ["includeUI"] = true });
                Assert.AreEqual("GAME_VIEW_NOT_FOCUSED", result.Value<string>("code"));
                Assert.IsNotEmpty(result.Value<string>("error"));
                Assert.IsNull(result["path"]);
                Assert.AreSame(sceneView, EditorWindow.focusedWindow);
            });
        }

        [UnityTest]
        public IEnumerator DefaultCapture_RequiresFocusedGameView()
        {
            return TaskTestUtility.Await(async () =>
            {
                EditorWindow.GetWindow<SceneView>().Focus();
                var result = await Capture(new JObject());
                Assert.AreEqual("GAME_VIEW_NOT_FOCUSED", result.Value<string>("code"));
                Assert.IsNull(result["path"]);
            });
        }

        [UnityTest]
        public IEnumerator IncludeUi_RejectsOverlappingRequests()
        {
            return TaskTestUtility.Await(async () =>
            {
                if (Application.isBatchMode) Assert.Ignore("Requires a GUI Game View");
                var gameView = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
                gameView.Focus();
                var first = Capture(new JObject { ["includeUI"] = true });
                var second = await Capture(new JObject { ["includeUI"] = true });
                var completed = await first;
                var path = completed.Value<string>("path");
                if (path != null && File.Exists(path)) File.Delete(path);
                Assert.AreEqual("GAME_CAPTURE_BUSY", second.Value<string>("code"));
                Assert.IsNull(second["path"]);
                Assert.IsNull(completed["error"], completed.ToString());
            });
        }

        [UnityTest]
        public IEnumerator IncludeUi_RejectsFocusLostDuringCapture()
        {
            return TaskTestUtility.Await(async () =>
            {
                if (Application.isBatchMode) Assert.Ignore("Requires a GUI Game View");
                var gameView = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
                gameView.Focus();
                var pending = Capture(new JObject { ["includeUI"] = true });
                EditorWindow.GetWindow<SceneView>().Focus();
                var result = await pending;
                Assert.AreEqual("GAME_VIEW_NOT_FOCUSED", result.Value<string>("code"));
                Assert.IsNull(result["path"]);
            });
        }

        [UnityTest]
        public IEnumerator CameraOnly_CapturesWithoutGameViewFocus()
        {
            return TaskTestUtility.Await(async () =>
            {
                var go = new GameObject("ScreenshotTestCamera");
                string path = null;
                try
                {
                    var camera = go.AddComponent<Camera>();
                    go.tag = "MainCamera";
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.red;
                    EditorWindow.GetWindow<SceneView>().Focus();
                    var result = await Capture(new JObject
                    {
                        ["includeUI"] = false, ["width"] = 32, ["height"] = 24,
                        ["encodeAsBase64"] = true
                    });
                    path = result.Value<string>("path");
                    Assert.IsNull(result["error"], result.ToString());
                    Assert.AreEqual(32, result.Value<int>("width"));
                    Assert.AreEqual(24, result.Value<int>("height"));
                    Assert.IsFalse(result.Value<bool>("includeUI"));
                    Assert.IsTrue(File.Exists(path));
                    CollectionAssert.AreEqual(File.ReadAllBytes(path),
                        System.Convert.FromBase64String(result.Value<string>("base64Data")));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                    if (path != null && File.Exists(path)) File.Delete(path);
                }
            });
        }

        private static async Task<JObject> Capture(JObject parameters)
        {
            object result = ScreenshotHandler.CaptureScreenshot(parameters);
            if (result is Task<object> pending) result = await pending;
            return (JObject)result;
        }
    }
}
