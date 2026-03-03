using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityCliBridge.Handlers;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class SceneHandlerTests
    {
        private string testSceneFolder = "Assets/TestScenes";

        [SetUp]
        public void Setup()
        {
            // Create test folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(testSceneFolder))
            {
                AssetDatabase.CreateFolder("Assets", "TestScenes");
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test scenes
            if (AssetDatabase.IsValidFolder(testSceneFolder))
            {
                AssetDatabase.DeleteAsset(testSceneFolder);
            }
            
            // Remove any test scenes from build settings
            var buildScenes = EditorBuildSettings.scenes.ToList();
            buildScenes.RemoveAll(s => s.path.Contains("TestScene"));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void CreateScene_ShouldWorkWithMinimalParameters()
        {
            const string sceneName = "TestScene_Auto";
            var expectedPath = $"Assets/Scenes/{sceneName}.unity";

            var parameters = new JObject
            {
                ["sceneName"] = sceneName
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual(sceneName, result.Value<string>("sceneName"));
            var path = result.Value<string>("path");
            Assert.AreEqual(expectedPath, path);
            Assert.IsTrue(result.Value<bool?>("isLoaded") ?? false);

            // Verify scene was created
            Assert.IsTrue(File.Exists(path));

            // Clean up
            AssetDatabase.DeleteAsset(path);
        }

        [Test]
        public void CreateScene_ShouldWorkWithCustomPath()
        {
            var parameters = new JObject
            {
                ["sceneName"] = "CustomScene",
                ["path"] = testSceneFolder + "/"
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual("CustomScene", result.Value<string>("sceneName"));
            var path = result.Value<string>("path");
            Assert.AreEqual(testSceneFolder + "/CustomScene.unity", path);
            
            // Verify scene was created
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void CreateScene_ShouldNotLoadScene_WhenLoadSceneIsFalse()
        {
            var currentScenePath = SceneManager.GetActiveScene().path;
            
            var parameters = new JObject
            {
                ["sceneName"] = "UnloadedScene",
                ["path"] = testSceneFolder + "/",
                ["loadScene"] = false
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.IsFalse(result.Value<bool?>("isLoaded") ?? false);
            
            // Verify current scene didn't change
            Assert.AreEqual(currentScenePath, SceneManager.GetActiveScene().path);
        }

        [Test]
        public void CreateScene_ShouldAddToBuildSettings_WhenRequested()
        {
            var parameters = new JObject
            {
                ["sceneName"] = "BuildScene",
                ["path"] = testSceneFolder + "/",
                ["addToBuildSettings"] = true
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.IsTrue(result.Value<int?>("sceneIndex") >= 0);
            
            // Verify scene is in build settings
            var buildScenes = EditorBuildSettings.scenes;
            Assert.IsTrue(buildScenes.Any(s => s.path == result.Value<string>("path")));
        }

        [Test]
        public void CreateScene_ShouldFailForEmptySceneName()
        {
            var parameters = new JObject
            {
                ["sceneName"] = ""
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Scene name cannot be empty", result.Value<string>("error"));
        }

        [Test]
        public void CreateScene_ShouldFailForInvalidSceneName()
        {
            var parameters = new JObject
            {
                ["sceneName"] = "Invalid/Scene/Name"
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("invalid characters", result.Value<string>("error"));
        }

        [Test]
        public void CreateScene_ShouldFailForExistingScene()
        {
            // Create a scene first
            var scenePath = testSceneFolder + "/ExistingScene.unity";
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, scenePath);

            var parameters = new JObject
            {
                ["sceneName"] = "ExistingScene",
                ["path"] = testSceneFolder + "/"
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("already exists", result.Value<string>("error"));
        }

        [Test]
        public void CreateScene_ShouldFailForInvalidPath()
        {
            var parameters = new JObject
            {
                ["sceneName"] = "TestScene",
                ["path"] = "../InvalidPath/"
            };

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Invalid path", result.Value<string>("error"));
        }

        [Test]
        public void CreateScene_ShouldHandleMissingParameters()
        {
            var parameters = new JObject();

            var result = ToJObject(SceneHandler.CreateScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Scene name", result.Value<string>("error"));
        }
    }
}
