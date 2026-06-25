using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class GameObjectHandlerTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("GameObjectHandlerTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeGameObject = null;

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        private static string PathOf(GameObject obj)
        {
            return GameObjectHandler.GetGameObjectPath(obj);
        }

        [Test]
        public void CreateGameObject_ShouldCreateChildAndFindByExactName()
        {
            var rootPath = PathOf(_root);
            var parameters = new JObject
            {
                ["name"] = "CreatedChild",
                ["parentPath"] = rootPath,
                ["position"] = new JObject { ["x"] = 1.5, ["y"] = 2.0, ["z"] = -3.0 },
                ["scale"] = new JObject { ["x"] = 2.0, ["y"] = 2.0, ["z"] = 2.0 }
            };

            var created = ToJObject(GameObjectHandler.CreateGameObject(parameters));

            Assert.IsNull(created["error"]);
            Assert.AreEqual("/GameObjectHandlerTestRoot/CreatedChild", created.Value<string>("path"));
            Assert.AreEqual(1.5, created["position"]?["x"]?.Value<double>(), 0.0001);
            Assert.AreEqual(2.0, created["scale"]?["x"]?.Value<double>(), 0.0001);
            Assert.AreEqual("CreatedChild", Selection.activeGameObject?.name);

            var found = ToJObject(GameObjectHandler.FindGameObjects(new JObject
            {
                ["name"] = "CreatedChild",
                ["exactMatch"] = true
            }));

            Assert.IsNull(found["error"]);
            Assert.AreEqual(1, found.Value<int>("count"));
            Assert.AreEqual("/GameObjectHandlerTestRoot/CreatedChild", found["objects"]?[0]?["path"]?.Value<string>());
        }

        [Test]
        public void ModifyGameObject_ShouldUpdateTransformAndAppearInHierarchyAndDetails()
        {
            var child = new GameObject("EditableChild");
            child.transform.SetParent(_root.transform);
            var childPath = PathOf(child);

            var modified = ToJObject(GameObjectHandler.ModifyGameObject(new JObject
            {
                ["path"] = childPath,
                ["name"] = "RenamedChild",
                ["position"] = new JObject { ["x"] = 4.0, ["y"] = 5.0, ["z"] = 6.0 },
                ["rotation"] = new JObject { ["x"] = 0.0, ["y"] = 90.0, ["z"] = 0.0 },
                ["scale"] = new JObject { ["x"] = 1.0, ["y"] = 2.0, ["z"] = 3.0 }
            }));

            Assert.IsNull(modified["error"]);
            Assert.IsTrue(modified.Value<bool>("modified"));
            Assert.AreEqual("RenamedChild", modified.Value<string>("name"));
            Assert.AreEqual("/GameObjectHandlerTestRoot/RenamedChild", modified.Value<string>("path"));
            Assert.AreEqual(4.0, modified["position"]?["x"]?.Value<double>(), 0.0001);
            Assert.AreEqual(3.0, modified["scale"]?["z"]?.Value<double>(), 0.0001);

            var hierarchy = ToJObject(GameObjectHandler.GetHierarchy(new JObject
            {
                ["rootPath"] = PathOf(_root),
                ["includeTransform"] = true,
                ["maxDepth"] = 1
            }));

            Assert.IsNull(hierarchy["error"]);
            Assert.AreEqual(1, hierarchy.Value<int>("objectCount"));
            Assert.AreEqual("RenamedChild", hierarchy["hierarchy"]?[0]?["name"]?.Value<string>());
            Assert.AreEqual(4.0, hierarchy["hierarchy"]?[0]?["transform"]?["position"]?["x"]?.Value<double>(), 0.0001);

            var details = ToJObject(SceneAnalysisHandler.GetGameObjectDetails(new JObject
            {
                ["path"] = "/GameObjectHandlerTestRoot/RenamedChild",
                ["includeComponents"] = true
            }));

            Assert.IsNull(details["error"]);
            Assert.AreEqual("RenamedChild", details.Value<string>("name"));
            Assert.AreEqual("/GameObjectHandlerTestRoot/RenamedChild", details.Value<string>("path"));
            Assert.IsTrue(details["components"]!.Any(component =>
                component["type"]?.Value<string>() == "Transform"));
        }

        [Test]
        public void DeleteGameObject_ShouldDeletePathAndPathsTargets()
        {
            var first = new GameObject("DeleteOne");
            first.transform.SetParent(_root.transform);
            var second = new GameObject("DeleteTwo");
            second.transform.SetParent(_root.transform);

            var firstPath = PathOf(first);
            var secondPath = PathOf(second);

            var deleted = ToJObject(GameObjectHandler.DeleteGameObject(new JObject
            {
                ["path"] = firstPath,
                ["paths"] = new JArray(secondPath)
            }));

            Assert.IsNull(deleted["error"]);
            Assert.AreEqual(2, deleted.Value<int>("deletedCount"));
            Assert.AreEqual(0, deleted.Value<int>("notFoundCount"));
            Assert.IsNull(GameObjectHandler.FindGameObjectByPath(firstPath));
            Assert.IsNull(GameObjectHandler.FindGameObjectByPath(secondPath));
        }

        [Test]
        public void InvalidRequests_ShouldReturnErrors()
        {
            var missingModifyPath = ToJObject(GameObjectHandler.ModifyGameObject(new JObject
            {
                ["name"] = "MissingPath"
            }));
            StringAssert.Contains("path is required", missingModifyPath.Value<string>("error"));

            var missingDeleteTarget = ToJObject(GameObjectHandler.DeleteGameObject(new JObject()));
            StringAssert.Contains("Either 'path' or 'paths' parameter is required", missingDeleteTarget.Value<string>("error"));

            var invalidParent = ToJObject(GameObjectHandler.CreateGameObject(new JObject
            {
                ["name"] = "Orphan",
                ["parentPath"] = "/MissingParent"
            }));
            StringAssert.Contains("Parent GameObject not found", invalidParent.Value<string>("error"));
        }
    }
}
