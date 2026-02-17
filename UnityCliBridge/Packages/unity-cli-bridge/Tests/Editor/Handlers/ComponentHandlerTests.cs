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
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
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
