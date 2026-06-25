using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class ComponentHandlerTests
    {
        private const string TestAssetFolder = "Assets/ComponentHandlerTests";
        private GameObject _root;
        private Func<bool> _playModeDetectorBackup;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ComponentTestRoot");
            _playModeDetectorBackup = ComponentHandler.PlayModeDetector;
            ComponentHandler.PlayModeDetector = () => false;
        }

        [TearDown]
        public void TearDown()
        {
            ComponentHandler.PlayModeDetector = _playModeDetectorBackup;
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            if (AssetDatabase.IsValidFolder(TestAssetFolder))
            {
                AssetDatabase.DeleteAsset(TestAssetFolder);
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        [Test]
        public void AddModifyListAndRemoveComponent_ShouldManageComponentLifecycle()
        {
            var child = new GameObject("Lifecycle");
            child.transform.SetParent(_root.transform);
            var path = GameObjectHandler.GetGameObjectPath(child);

            var added = ToJObject(ComponentHandler.AddComponent(new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = "Light",
                ["properties"] = new JObject
                {
                    ["intensity"] = 2.5,
                    ["range"] = 6.0,
                    ["color"] = new JObject { ["r"] = 0.25, ["g"] = 0.5, ["b"] = 0.75, ["a"] = 1.0 }
                }
            }));

            Assert.IsNull(added["error"]);
            Assert.IsTrue(added.Value<bool>("success"));
            Assert.AreEqual("Light", added.Value<string>("componentType"));
            Assert.AreEqual(2.5f, child.GetComponent<Light>().intensity, 0.0001f);
            Assert.AreEqual(0.75f, child.GetComponent<Light>().color.b, 0.0001f);

            var modified = ToJObject(ComponentHandler.ModifyComponent(new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = "Light",
                ["properties"] = new JObject
                {
                    ["intensity"] = 4.0,
                    ["range"] = 12.0
                }
            }));

            Assert.IsNull(modified["error"]);
            Assert.IsTrue(modified.Value<bool>("success"));
            Assert.AreEqual(4.0f, child.GetComponent<Light>().intensity, 0.0001f);
            Assert.AreEqual(12.0f, child.GetComponent<Light>().range, 0.0001f);

            var listed = ToJObject(ComponentHandler.ListComponents(new JObject
            {
                ["gameObjectPath"] = path,
                ["includeProperties"] = true
            }));

            Assert.IsNull(listed["error"]);
            Assert.GreaterOrEqual(listed.Value<int>("componentCount"), 2);
            Assert.IsTrue(listed["components"]!.Any(component =>
                component["type"]?.Value<string>() == "Light" &&
                component["properties"]?["intensity"]?.Value<double>() == 4.0));

            var removed = ToJObject(ComponentHandler.RemoveComponent(new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = "Light"
            }));

            Assert.IsNull(removed["error"]);
            Assert.IsTrue(removed.Value<bool>("removed"));
            Assert.IsNull(child.GetComponent<Light>());
        }

        [Test]
        public void GetComponentTypes_ShouldSearchOnlyAddableTypes()
        {
            var result = ToJObject(ComponentHandler.GetComponentTypes(new JObject
            {
                ["search"] = "Light",
                ["onlyAddable"] = true
            }));

            Assert.IsNull(result["error"]);
            Assert.IsTrue(result.Value<bool>("onlyAddable"));
            Assert.Greater(result.Value<int>("totalCount"), 0);
            Assert.IsTrue(result["componentTypes"]!.Any(componentType =>
                componentType.Value<string>() == "Light"));
            Assert.IsTrue(result["categories"]!.Any(category =>
                category.Value<string>() == "Rendering" || category.Value<string>() == "Other"));
        }

        [Test]
        public void SceneAnalysisComponentTools_ShouldReadValuesAndFindByComponent()
        {
            var child = new GameObject("ComponentAnalysisTarget");
            child.transform.SetParent(_root.transform);
            child.AddComponent<SerializedFieldTestComponent>();

            var values = ToJObject(SceneAnalysisHandler.GetComponentValues(new JObject
            {
                ["gameObjectName"] = "ComponentAnalysisTarget",
                ["componentType"] = nameof(SerializedFieldTestComponent),
                ["includePrivateFields"] = true
            }));

            Assert.IsNull(values["error"]);
            Assert.AreEqual(nameof(SerializedFieldTestComponent), values.Value<string>("componentType"));
            Assert.IsTrue(values.Value<bool>("hasProperties"));
            Assert.AreEqual(1.5, values["properties"]?["[SF]_hiddenFloat"]?["value"]?.Value<double>(), 0.0001);

            var found = ToJObject(SceneAnalysisHandler.FindByComponent(new JObject
            {
                ["componentType"] = nameof(SerializedFieldTestComponent),
                ["searchScope"] = "scene",
                ["includeInactive"] = true
            }));

            Assert.IsNull(found["error"]);
            Assert.GreaterOrEqual(found.Value<int>("totalFound"), 1);
            Assert.IsTrue(found["results"]!.Any(entry =>
                entry["gameObject"]?.Value<string>() == "ComponentAnalysisTarget" &&
                entry["path"]?.Value<string>() == "/ComponentTestRoot/ComponentAnalysisTarget"));
        }

        [Test]
        public void SetComponentField_PrefabAssetShouldSaveFieldValue()
        {
            if (!AssetDatabase.IsValidFolder(TestAssetFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ComponentHandlerTests");
            }

            const string prefabPath = TestAssetFolder + "/LightPrefab.prefab";
            var source = new GameObject("LightPrefab");
            var light = source.AddComponent<Light>();
            light.intensity = 1.0f;
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            UnityEngine.Object.DestroyImmediate(source);

            var result = ToJObject(ComponentHandler.SetComponentField(new JObject
            {
                ["prefabAssetPath"] = prefabPath,
                ["componentType"] = "Light",
                ["fieldPath"] = "m_Intensity",
                ["value"] = 7.25,
                ["valueType"] = "float"
            }));

            Assert.IsNull(result["error"]);
            Assert.AreEqual("prefabAsset", result.Value<string>("scope"));
            Assert.IsFalse(result.Value<bool>("requiresSave"));
            Assert.AreEqual(7.25, result["appliedValue"]?.Value<double>(), 0.0001);

            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(savedPrefab);
            Assert.AreEqual(7.25f, savedPrefab.GetComponent<Light>().intensity, 0.0001f);
        }

        [Test]
        public void SetComponentField_ShouldUpdatePrivateSerializeField()
        {
            var child = new GameObject("Child");
            child.transform.SetParent(_root.transform);
            var component = child.AddComponent<SerializedFieldTestComponent>();

            string path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_hiddenFloat",
                ["value"] = 5.75,
                ["valueType"] = "float"
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));

            Assert.IsNull(result["error"]);
            Assert.AreEqual("scene", result.Value<string>("scope"));
            Assert.AreEqual(5.75f, component.HiddenFloat, 0.0001f);
            Assert.AreEqual(5.75, result["appliedValue"]?.Value<double>(), 0.0001);
        }

        [Test]
        public void SetComponentField_ShouldUpdateNestedStructField()
        {
            var child = new GameObject("Nested");
            child.transform.SetParent(_root.transform);
            var component = child.AddComponent<SerializedFieldTestComponent>();

            string path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_settings.count",
                ["value"] = 42,
                ["valueType"] = "int"
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));

            Assert.IsNull(result["error"]);
            Assert.AreEqual(42, component.Settings.count);
            Assert.AreEqual(42, result["appliedValue"]?.Value<int>());
        }

        [Test]
        public void SetComponentField_ShouldUpdateArrayElement()
        {
            var child = new GameObject("Array");
            child.transform.SetParent(_root.transform);
            var component = child.AddComponent<SerializedFieldTestComponent>();

            string path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_weights[1]",
                ["value"] = 99,
                ["valueType"] = "int"
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));

            Assert.IsNull(result["error"]);
            Assert.AreEqual(99, component.Weights[1]);
            Assert.AreEqual(99, result["appliedValue"]?.Value<int>());
        }

        [Test]
        public void SetComponentField_DryRunShouldNotApplyValue()
        {
            var child = new GameObject("DryRun");
            child.transform.SetParent(_root.transform);
            var component = child.AddComponent<SerializedFieldTestComponent>();

            string path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_hiddenFloat",
                ["value"] = 9.0,
                ["valueType"] = "float",
                ["dryRun"] = true
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));

            Assert.IsNull(result["error"]);
            Assert.IsTrue(result.Value<bool>("dryRun"));
            Assert.AreEqual(1.5f, component.HiddenFloat, 0.0001f); // original value
            Assert.AreEqual(9.0, result["previewValue"]?.Value<double>(), 0.0001);
        }

        [Test]
        public void SetComponentField_ShouldBlockPlayModeWithoutRuntime()
        {
            ComponentHandler.PlayModeDetector = () => true;
            var child = new GameObject("PlayBlocked");
            child.transform.SetParent(_root.transform);
            child.AddComponent<SerializedFieldTestComponent>();
            var path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_hiddenFloat",
                ["value"] = 2.0
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));
            Assert.IsNotNull(result["error"]);
            StringAssert.Contains("Play Mode", result["error"]?.ToString());
        }

        [Test]
        public void SetComponentField_RuntimeTrueAllowsPlayModeChange()
        {
            ComponentHandler.PlayModeDetector = () => true;
            var child = new GameObject("RuntimeOK");
            child.transform.SetParent(_root.transform);
            var component = child.AddComponent<SerializedFieldTestComponent>();
            var path = GameObjectHandler.GetGameObjectPath(child);

            var parameters = new JObject
            {
                ["gameObjectPath"] = path,
                ["componentType"] = typeof(SerializedFieldTestComponent).FullName,
                ["fieldPath"] = "_hiddenFloat",
                ["value"] = 3.5,
                ["runtime"] = true
            };

            var result = ToJObject(ComponentHandler.SetComponentField(parameters));
            Assert.IsNull(result["error"]);
            Assert.AreEqual(3.5f, component.HiddenFloat, 0.0001f);
            var notes = result["notes"]?.ToObject<string[]>();
            Assert.IsNotNull(notes);
            Assert.IsTrue(notes!.Any(n => n.Contains("Play Mode")));
        }

        private class SerializedFieldTestComponent : MonoBehaviour
        {
            [Serializable]
            public struct SettingsData
            {
                public int count;
                public Vector3 offset;
            }

            [SerializeField] private float _hiddenFloat = 1.5f;
            [SerializeField] private SettingsData _settings = new SettingsData { count = 3, offset = new Vector3(1f, 2f, 3f) };
            [SerializeField] private List<int> _weights = new List<int> { 2, 4, 6 };

            public float HiddenFloat => _hiddenFloat;
            public SettingsData Settings => _settings;
            public IReadOnlyList<int> Weights => _weights;
        }
    }
}
