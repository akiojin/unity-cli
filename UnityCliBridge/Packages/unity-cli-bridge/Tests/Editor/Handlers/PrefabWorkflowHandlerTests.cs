using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Tests
{
    public class PrefabWorkflowHandlerTests
    {
        private const string Folder = "Assets/Scenes/Generated/E2E/PrefabUnit";
        private const string Base = Folder + "/Base.prefab";
        private const string Variant = Folder + "/Variant.prefab";
        private GameObject instance;

        [SetUp]
        public void SetUp()
        {
            System.IO.Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var source = new GameObject("PrefabUnit");
            source.AddComponent<BoxCollider>();
            source.AddComponent<SphereCollider>();
            PrefabUtility.SaveAsPrefabAsset(source, Base);
            Object.DestroyImmediate(source);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base));
        }

        [TearDown]
        public void TearDown()
        {
            if (instance != null) Object.DestroyImmediate(instance);
            AssetDatabase.DeleteAsset(Folder);
        }

        // Reflection keeps RED a missing-feature assertion, rather than a compiler failure.
        private static JObject Call(string method, JObject request)
        {
            var type = typeof(UnityCliBridge.Handlers.AssetManagementHandler).Assembly.GetType("UnityCliBridge.Handlers.PrefabWorkflowHandler");
            Assert.IsNotNull(type, "Prefab workflow handler must be available");
            var entry = type.GetMethod(method);
            Assert.IsNotNull(entry, method);
            return JObject.FromObject(entry.Invoke(null, new object[] { request }));
        }

        private static void Ok(JObject value)
        {
            Assert.IsNull(value["error"], value.ToString());
            Assert.IsTrue(value.Value<bool>("success"), value.ToString());
        }

        private static void CreateVariant()
        {
            var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base));
            source.name = "VariantSource" + Guid.NewGuid().ToString("N");
            try
            {
                Ok(JObject.FromObject(UnityCliBridge.Handlers.AssetManagementHandler.CreatePrefab(new JObject
                {
                    ["gameObjectPath"] = "/" + source.name, ["prefabPath"] = Variant
                })));
            }
            finally { Object.DestroyImmediate(source); }
        }

        private JObject Request(string action, string scope, Object target = null, string assetPath = Base)
        {
            var result = new JObject { ["gameObjectPath"] = "/" + instance.name, ["action"] = action, ["scope"] = scope };
            if (target != null) result["instanceId"] = target.GetInstanceID();
            if (action == "apply") result["assetPath"] = assetPath;
            return result;
        }

        private void EditBox(float value)
        {
            var component = instance.GetComponent<BoxCollider>();
            component.size = new Vector3(value, 1, 1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        [Test]
        public void VariantHasSourceConnectionAndProtectsExistingAssets()
        {
            CreateVariant();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Variant);
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(asset));
            Assert.AreEqual(Base, AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(asset)));
            var duplicate = JObject.FromObject(UnityCliBridge.Handlers.AssetManagementHandler.CreatePrefab(new JObject
            {
                ["gameObjectPath"] = "/" + instance.name, ["prefabPath"] = Variant
            }));
            StringAssert.Contains("already exists", duplicate.Value<string>("error"));
        }

        [Test]
        public void PrefabWritesAreBlockedInPlayMode()
        {
            foreach (string name in new[] { "manage_prefab_overrides", "unpack_prefab" })
                Assert.IsFalse(UnityCliBridge.Helpers.PlayModeCommandPolicy.IsAllowed(name), name);
            Assert.IsTrue(UnityCliBridge.Helpers.PlayModeCommandPolicy.IsAllowed("get_prefab_overrides"));
        }

        [Test]
        public void ListingIncludesPropertyAndAddedAndRemovedComponents()
        {
            EditBox(3);
            var added = instance.AddComponent<CapsuleCollider>();
            Object.DestroyImmediate(instance.GetComponent<SphereCollider>());
            var result = Call("GetOverrides", new JObject { ["gameObjectPath"] = "/" + instance.name });
            Ok(result);
            Assert.IsTrue(result["properties"].Any(p => p.Value<string>("propertyPath") == "m_Size.x"));
            Assert.IsTrue(result["addedComponents"].Any(p => p.Value<int>("instanceId") == added.GetInstanceID()));
            Assert.AreEqual("UnityEngine.SphereCollider", result["removedComponents"][0].Value<string>("type"));
            Assert.AreNotEqual(0, result["removedComponents"][0].Value<int>("assetComponentId"));
        }

        [TestCase("apply")]
        [TestCase("revert")]
        public void PropertyOperationDoesNotTouchSiblingOverride(string action)
        {
            EditBox(3);
            var box = instance.GetComponent<BoxCollider>();
            box.isTrigger = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            var request = Request(action, "property", box);
            request["propertyPath"] = "m_Size.x";
            Ok(Call("ManageOverrides", request));
            Assert.AreEqual(action == "apply" ? 3 : 1, AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<BoxCollider>().size.x);
            Assert.AreEqual(action == "apply" ? 3 : 1, box.size.x);
            Assert.IsTrue(box.isTrigger);
            Assert.IsFalse(AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<BoxCollider>().isTrigger);
        }

        [TestCase("apply")]
        [TestCase("revert")]
        public void AddedComponentCanBeManagedIndividually(string action)
        {
            var added = instance.AddComponent<CapsuleCollider>();
            EditBox(3);
            Ok(Call("ManageOverrides", Request(action, "added_component", added)));
            Assert.AreEqual(action == "apply", AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<CapsuleCollider>() != null);
            Assert.AreEqual(action == "apply", instance.GetComponent<CapsuleCollider>() != null);
            Assert.AreEqual(3, instance.GetComponent<BoxCollider>().size.x);
        }

        [TestCase("apply")]
        [TestCase("revert")]
        public void RemovedComponentCanBeManagedIndividually(string action)
        {
            var assetComponent = AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<SphereCollider>();
            Object.DestroyImmediate(instance.GetComponent<SphereCollider>());
            var request = Request(action, "removed_component", instance);
            request["assetComponentId"] = assetComponent.GetInstanceID();
            Ok(Call("ManageOverrides", request));
            Assert.AreEqual(action == "revert", AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<SphereCollider>() != null);
            Assert.AreEqual(action == "revert", instance.GetComponent<SphereCollider>() != null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ApplyAllCanTargetVariantOrBase(bool toVariant)
        {
            CreateVariant();
            Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Variant));
            EditBox(4);
            instance.AddComponent<CapsuleCollider>();
            Object.DestroyImmediate(instance.GetComponent<SphereCollider>());
            Ok(Call("ManageOverrides", Request("apply", "all", assetPath: toVariant ? Variant : Base)));
            Assert.AreEqual(4, AssetDatabase.LoadAssetAtPath<GameObject>(Variant).GetComponent<BoxCollider>().size.x);
            Assert.AreEqual(toVariant ? 1 : 4, AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<BoxCollider>().size.x);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Variant).GetComponent<CapsuleCollider>());
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(Variant).GetComponent<SphereCollider>());
        }

        [Test]
        public void RevertAllRestoresInstance()
        {
            EditBox(4);
            instance.AddComponent<CapsuleCollider>();
            Object.DestroyImmediate(instance.GetComponent<SphereCollider>());
            Ok(Call("ManageOverrides", Request("revert", "all")));
            Assert.AreEqual(1, instance.GetComponent<BoxCollider>().size.x);
            Assert.IsNull(instance.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(instance.GetComponent<SphereCollider>());
        }

        [Test]
        public void ApplyAllToBaseIncludesAddedAndRemovedGameObjects()
        {
            var contents = PrefabUtility.LoadPrefabContents(Base);
            try
            {
                new GameObject("RemoveMe").transform.SetParent(contents.transform);
                PrefabUtility.SaveAsPrefabAsset(contents, Base);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            CreateVariant();
            Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Variant));
            Object.DestroyImmediate(instance.transform.Find("RemoveMe").gameObject);
            new GameObject("AddedChild").transform.SetParent(instance.transform);
            Ok(Call("ManageOverrides", Request("apply", "all", assetPath: Base)));
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(Base);
            Assert.IsNull(saved.transform.Find("RemoveMe"));
            Assert.IsNotNull(saved.transform.Find("AddedChild"));
            Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
        }

        [Test]
        public void InvalidScopeTargetAndDestinationAreRejectedWithoutChangingAsset()
        {
            EditBox(4);
            Assert.AreEqual("INVALID_ARGUMENT", Call("ManageOverrides", Request("apply", "typo")).Value<string>("code"));
            var unrelated = new GameObject("Other");
            try
            {
                Assert.AreEqual("INVALID_TARGET", Call("ManageOverrides", Request("apply", "object", unrelated)).Value<string>("code"));
            }
            finally { Object.DestroyImmediate(unrelated); }
            var request = Request("apply", "all", assetPath: Folder + "/Missing.prefab");
            Assert.AreEqual("INVALID_APPLY_TARGET", Call("ManageOverrides", request).Value<string>("code"));
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(Base).GetComponent<BoxCollider>().size.x);
        }

        [Test]
        public void DuplicateComponentTypesAreSelectedByIdentity()
        {
            var first = instance.AddComponent<CapsuleCollider>();
            var second = instance.AddComponent<CapsuleCollider>();
            Ok(Call("ManageOverrides", Request("revert", "added_component", second)));
            Assert.IsTrue(first != null);
            Assert.IsTrue(second == null);
            Assert.AreEqual(1, instance.GetComponents<CapsuleCollider>().Length);
        }

        [Test]
        public void InactiveInstanceCanBeListedAndAmbiguousPathsAreRejected()
        {
            instance.SetActive(false);
            Ok(Call("GetOverrides", new JObject { ["gameObjectPath"] = "/" + instance.name }));
            var duplicate = new GameObject(instance.name);
            try
            {
                Assert.AreEqual("INVALID_TARGET", Call("GetOverrides", new JObject { ["gameObjectPath"] = "/" + instance.name }).Value<string>("code"));
            }
            finally { Object.DestroyImmediate(duplicate); }
        }

        [Test]
        public void DefaultRootPositionCannotBeAppliedAsAProperty()
        {
            instance.transform.position = new Vector3(4, 0, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            var request = Request("apply", "property", instance.transform);
            request["propertyPath"] = "m_LocalPosition.x";
            Assert.AreEqual("DEFAULT_OVERRIDE", Call("ManageOverrides", request).Value<string>("code"));
            Assert.AreEqual(Vector3.zero, AssetDatabase.LoadAssetAtPath<GameObject>(Base).transform.localPosition);
        }

        [Test]
        public void RevertRejectsAssetDestinationAndStaleProperty()
        {
            EditBox(4);
            var request = Request("revert", "property", instance.GetComponent<BoxCollider>());
            request["propertyPath"] = "missing";
            Assert.AreEqual("OVERRIDE_NOT_FOUND", Call("ManageOverrides", request).Value<string>("code"));
            request["assetPath"] = Base;
            Assert.AreEqual("INVALID_ARGUMENT", Call("ManageOverrides", request).Value<string>("code"));
            Assert.AreEqual(4, instance.GetComponent<BoxCollider>().size.x);
        }

        [TestCase("Outermost", true)]
        [TestCase("Completely", false)]
        public void UnpackVariantReportsItsBaseConnection(string mode, bool baseRemains)
        {
            CreateVariant();
            Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Variant));
            var result = Call("Unpack", new JObject { ["gameObjectPath"] = "/" + instance.name, ["mode"] = mode });
            Ok(result);
            Assert.AreEqual(baseRemains, result.Value<bool>("isPartOfPrefabInstance"));
            Assert.AreEqual(baseRemains ? 1 : 0, result.Value<int>("connectedObjectCount"));
            if (baseRemains) Assert.AreEqual(Base, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
        }

        [TestCase("Outermost", true)]
        [TestCase("Completely", false)]
        public void UnpackReportsRemainingNestedConnection(string mode, bool nestedRemains)
        {
            var root = new GameObject("NestedRoot");
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base), root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Nested.prefab");
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Nested.prefab"));
            var result = Call("Unpack", new JObject { ["gameObjectPath"] = "/" + instance.name, ["mode"] = mode });
            Ok(result);
            Assert.IsFalse(PrefabUtility.IsPartOfPrefabInstance(instance));
            Assert.AreEqual(nestedRemains, PrefabUtility.IsPartOfPrefabInstance(instance.transform.GetChild(0).gameObject));
            Assert.AreEqual(nestedRemains ? 1 : 0, result.Value<int>("connectedObjectCount"));
        }
    }
}
