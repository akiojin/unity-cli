using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityCliBridge.Handlers;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class LoadSceneTests
    {
        private string testSceneFolder = "Assets/TestScenes";
        private string testScenePath;
        private Scene originalScene;

        [SetUp]
        public void Setup()
        {
            // Save current scene state
            originalScene = SceneManager.GetActiveScene();
            
            // Create test folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(testSceneFolder))
            {
                AssetDatabase.CreateFolder("Assets", "TestScenes");
            }

            // Create a test scene
            testScenePath = testSceneFolder + "/LoadTestScene.unity";
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(testScene, testScenePath);
            
            // Add test scene to build settings
            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (!buildScenes.Any(s => s.path == testScenePath))
            {
                buildScenes.Add(new EditorBuildSettingsScene(testScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Remove test scene from build settings
            var buildScenes = EditorBuildSettings.scenes.ToList();
            buildScenes.RemoveAll(s => s.path.Contains("LoadTestScene"));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            
            // Clean up test scenes
            if (AssetDatabase.IsValidFolder(testSceneFolder))
            {
                AssetDatabase.DeleteAsset(testSceneFolder);
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void LoadScene_ShouldLoadByPath()
        {
            var parameters = new JObject
            {
                ["scenePath"] = testScenePath
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual("LoadTestScene", result.Value<string>("sceneName"));
            Assert.AreEqual(testScenePath, result.Value<string>("scenePath"));
            Assert.AreEqual("Single", result.Value<string>("loadMode"));
            Assert.IsTrue(result.Value<bool?>("isLoaded") ?? false);
            
            // Verify scene is actually loaded
            Assert.AreEqual("LoadTestScene", SceneManager.GetActiveScene().name);
        }

        [Test]
        public void LoadScene_ShouldLoadByName()
        {
            // Ensure scene is in build settings
            var parameters = new JObject
            {
                ["sceneName"] = "LoadTestScene"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual("LoadTestScene", result.Value<string>("sceneName"));
            Assert.IsTrue(result.Value<bool?>("isLoaded") ?? false);
        }

        [Test]
        public void LoadScene_ShouldLoadAdditively()
        {
            // First create another scene to have multiple scenes
            var additiveScenePath = testSceneFolder + "/AdditiveTestScene.unity";
            var additiveScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(additiveScene, additiveScenePath);
            
            var parameters = new JObject
            {
                ["scenePath"] = additiveScenePath,
                ["loadMode"] = "Additive"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual("AdditiveTestScene", result.Value<string>("sceneName"));
            Assert.AreEqual("Additive", result.Value<string>("loadMode"));
            Assert.IsTrue(result.Value<bool?>("isLoaded") ?? false);
            Assert.Greater(result.Value<int?>("activeSceneCount") ?? 0, 1);
            
            // Verify multiple scenes are loaded
            Assert.AreEqual(2, SceneManager.sceneCount);
        }

        [Test]
        public void LoadScene_ShouldFailForMissingParameters()
        {
            var parameters = new JObject();

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Either scenePath or sceneName must be provided", result.Value<string>("error"));
        }

        [Test]
        public void LoadScene_ShouldFailForBothParameters()
        {
            var parameters = new JObject
            {
                ["scenePath"] = testScenePath,
                ["sceneName"] = "LoadTestScene"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Provide either scenePath or sceneName, not both", result.Value<string>("error"));
        }

        [Test]
        public void LoadScene_ShouldFailForInvalidLoadMode()
        {
            var parameters = new JObject
            {
                ["scenePath"] = testScenePath,
                ["loadMode"] = "InvalidMode"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Invalid load mode", result.Value<string>("error"));
        }

        [Test]
        public void LoadScene_ShouldFailForNonExistentScenePath()
        {
            var parameters = new JObject
            {
                ["scenePath"] = "Assets/NonExistent/Scene.unity"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("Scene file not found", result.Value<string>("error"));
        }

        [Test]
        public void LoadScene_ShouldFailForSceneNotInBuildSettings()
        {
            // Create a scene not in build settings
            var notInBuildPath = testSceneFolder + "/NotInBuild.unity";
            var notInBuildScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(notInBuildScene, notInBuildPath);

            var parameters = new JObject
            {
                ["sceneName"] = "NotInBuild"
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            StringAssert.Contains("not in build settings", result.Value<string>("error"));
        }

        [Test]
        public void LoadScene_ShouldReturnPreviousSceneInfo()
        {
            // Load a known scene first
            EditorSceneManager.OpenScene(testScenePath);
            var previousSceneName = SceneManager.GetActiveScene().name;
            
            // Create another scene to load
            var newScenePath = testSceneFolder + "/NewTestScene.unity";
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, newScenePath);

            EditorSceneManager.OpenScene(testScenePath); // ensure previous scene is active

            var parameters = new JObject
            {
                ["scenePath"] = newScenePath
            };

            var result = ToJObject(SceneHandler.LoadScene(parameters));

            Assert.IsNotNull(result);
            Assert.IsNull(result.Value<string>("error"));
            Assert.AreEqual(previousSceneName, result.Value<string>("previousScene"));
        }
    }
}
