using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Core;
using UnityCliBridge.Models;
using UnityCliBridge.Helpers;

namespace UnityCliBridge.Tests
{
    public class AnimationCurveHandlerTests
    {
        private const string Folder = "Assets/AnimationCurveHandlerTests";
        private const string ClipPath = Folder + "/Numeric.anim";
        private GameObject root;
        private static EditorCurveBinding X => EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets", "AnimationCurveHandlerTests");
            root = new GameObject("AnimationCurveTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(Folder);
        }

        private JObject Request(string operation = "set") => new JObject
        {
            ["clipPath"] = ClipPath, ["animationRoot"] = UnityCliBridge.Helpers.ObjectIdentity.GetInstanceId(root),
            ["binding"] = JObject.FromObject(new { path = "", component = "UnityEngine.Transform", property = "localPosition.x" }),
            ["operation"] = operation, ["createIfMissing"] = true,
            ["keys"] = JArray.FromObject(new[] { new { time = 0f, value = 0f }, new { time = 1f, value = 2f } })
        };

        // The animation routes complete synchronously. Keep the tests synchronous: the Test
        // Framework bundled with Unity 2022.3 (1.1.x) cannot run async Task tests.
        private static JObject Call(string name, JObject request)
        {
            var pending = BridgeCommandRouter.Handle(new Command
            { Id = "animation-test", Type = name, Parameters = request });
            Assert.IsTrue(pending.IsCompleted, name + " should complete synchronously");
            var response = JObject.Parse(pending.GetAwaiter().GetResult());
            Assert.AreEqual("success", (string)response["status"], response.ToString());
            return (JObject)response["result"];
        }

        private static void Success(JObject result)
        {
            Assert.IsNull(result["error"], result.ToString());
            Assert.IsTrue((bool)result["success"], result.ToString());
        }

        [Test]
        public void Set_LinearMidpointAndReimport_PreserveKeysAndTangents()
        {
            Success(Call("edit_animation_curve", Request()));
            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceUpdate);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var curve = AnimationUtility.GetEditorCurve(clip, X);
            Assert.AreEqual(1f, curve.Evaluate(.5f), 1e-5f);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(2f, curve.keys[1].value);
            Assert.AreEqual(AnimationUtility.TangentMode.Linear, AnimationUtility.GetKeyRightTangentMode(curve, 0));
            Assert.AreEqual(AnimationUtility.TangentMode.Linear, AnimationUtility.GetKeyLeftTangentMode(curve, 1));
            var result = Call("get_animation_curves", new JObject { ["clipPath"] = ClipPath });
            Success(result);
            Assert.AreEqual("m_LocalPosition.x", (string)result["curves"][0]["binding"]["property"]);
            Assert.AreEqual("Linear", (string)result["curves"][0]["keys"][0]["rightTangentMode"]);
        }

        [Test]
        public void UpsertRemove_PreserveOtherBindingsEventsAndSettings()
        {
            var clip = new AnimationClip { frameRate = 24 };
            AssetDatabase.CreateAsset(clip, ClipPath);
            var other = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y");
            AnimationUtility.SetEditorCurve(clip, other, AnimationCurve.Linear(0, 3, 1, 7));
            var sprite = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, sprite, new[] { new ObjectReferenceKeyframe { time = 0, value = null } });
            AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .25f, functionName = "Marker" } });
            var original = AnimationUtility.GetEditorCurve(clip, other).keys;
            Success(Call("edit_animation_curve", Request()));
            var edit = Request("upsert_keys");
            edit["keys"] = JArray.FromObject(new[] { new { time = 1f, value = 4f }, new { time = 2f, value = 8f } });
            Success(Call("edit_animation_curve", edit));
            Assert.AreEqual(4, AnimationUtility.GetEditorCurve(clip, X).keys[1].value);
            edit = Request("remove_keys");
            edit.Remove("keys"); edit["times"] = new JArray(2f);
            Success(Call("edit_animation_curve", edit));
            Assert.AreEqual(2, AnimationUtility.GetEditorCurve(clip, X).length);
            CollectionAssert.AreEqual(original, AnimationUtility.GetEditorCurve(clip, other).keys);
            Assert.AreEqual(1, AnimationUtility.GetObjectReferenceCurve(clip, sprite).Length);
            Assert.AreEqual("Marker", AnimationUtility.GetAnimationEvents(clip)[0].functionName);
            Assert.AreEqual(24, clip.frameRate);
            var get = Call("get_animation_curves", new JObject { ["clipPath"] = ClipPath });
            Assert.AreEqual(1, ((JArray)get["objectReferenceBindings"]).Count);
            edit = Request("remove_curve"); edit.Remove("keys");
            Success(Call("edit_animation_curve", edit));
            Assert.IsNull(AnimationUtility.GetEditorCurve(clip, X));
            CollectionAssert.AreEqual(original, AnimationUtility.GetEditorCurve(clip, other).keys);
        }

        [TestCase("Constant")]
        [TestCase("Auto")]
        [TestCase("ClampedAuto")]
        [TestCase("Free")]
        public void TangentModes_RoundTripAndOmittedUpdatesPreserve(string mode)
        {
            var request = Request();
            foreach (JObject key in (JArray)request["keys"])
            {
                key["leftTangentMode"] = mode; key["rightTangentMode"] = mode;
                key["inTangent"] = .75f; key["outTangent"] = 1.25f;
            }
            Success(Call("edit_animation_curve", request));
            request = Request("upsert_keys");
            request["keys"] = JArray.FromObject(new[] { new { time = 1f, value = 5f } });
            Success(Call("edit_animation_curve", request));
            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceUpdate);
            var curve = AnimationUtility.GetEditorCurve(AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath), X);
            Assert.AreEqual(mode, AnimationUtility.GetKeyLeftTangentMode(curve, 1).ToString());
            Assert.AreEqual(mode, AnimationUtility.GetKeyRightTangentMode(curve, 1).ToString());
            if (mode == "Free") Assert.AreEqual(.75f, curve.keys[1].inTangent);
        }

        [TestCase("binding")]
        [TestCase("path")]
        [TestCase("component")]
        [TestCase("root")]
        [TestCase("duplicate")]
        [TestCase("negative")]
        [TestCase("mode")]
        [TestCase("infinity")]
        [TestCase("packages")]
        [TestCase("extension")]
        public void InvalidInput_DoesNotCreateAsset(string invalid)
        {
            var request = Request();
            switch (invalid)
            {
                case "binding": request["binding"]["property"] = "missing"; break;
                case "path": request["binding"]["path"] = "MissingChild"; break;
                case "component": request["binding"]["component"] = "UnityEngine.Camera"; break;
                case "root": request["animationRoot"] = 0; break;
                case "duplicate": request["keys"][1]["time"] = 0; break;
                case "negative": request["keys"][1]["time"] = -1; break;
                case "mode": request["keys"][0]["leftTangentMode"] = "bogus"; break;
                case "infinity": request["keys"][0]["value"] = double.PositiveInfinity; break;
                case "packages": request["clipPath"] = "Packages/invalid.anim"; break;
                case "extension": request["clipPath"] = Folder + "/invalid.fbx"; break;
            }
            var result = Call("edit_animation_curve", request);
            Assert.IsNotNull(result["error"], result.ToString());
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(ClipPath));
        }

        [Test]
        public void MissingRemovalKey_DoesNotPartiallyWrite()
        {
            Success(Call("edit_animation_curve", Request()));
            var before = File.ReadAllBytes(ClipPath);
            var request = Request("remove_keys"); request.Remove("keys"); request["times"] = new JArray(0, 99);
            Assert.IsNotNull((Call("edit_animation_curve", request))["error"]);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ClipPath));
        }

        [Test]
        public void ChildComponentCurve_AndFilter()
        {
            var child = new GameObject("Child", typeof(Camera)); child.transform.SetParent(root.transform);
            var request = Request();
            request["binding"] = JObject.FromObject(new { path = "Child", component = "UnityEngine.Camera", property = "field of view" });
            Success(Call("edit_animation_curve", request));
            var result = Call("get_animation_curves", new JObject { ["clipPath"] = ClipPath, ["binding"] = request["binding"].DeepClone() });
            Assert.AreEqual(1, ((JArray)result["curves"]).Count);
        }

        [Test]
        public void PlayModePolicy_RejectsEditsAllowsReads()
        {
            Assert.IsFalse(PlayModeCommandPolicy.IsAllowed("edit_animation_curve"));
            Assert.IsTrue(PlayModeCommandPolicy.IsAllowed("get_animation_curves"));
        }

        [TestCase("m_Sprite", "UnityEngine.SpriteRenderer")]
        [TestCase("m_Enabled", "UnityEngine.BoxCollider")]
        public void NonNumericBinding_IsRejected(string property, string component)
        {
            root.AddComponent<SpriteRenderer>(); root.AddComponent<BoxCollider>();
            var request = Request();
            request["binding"]["component"] = component;
            request["binding"]["property"] = property;
            Assert.IsNotNull((Call("edit_animation_curve", request))["error"]);
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(ClipPath));
        }

        [Test]
        public void ReadOnlyClip_IsRejectedWithoutMutation()
        {
            Success(Call("edit_animation_curve", Request()));
            var before = File.ReadAllBytes(ClipPath);
            File.SetAttributes(ClipPath, File.GetAttributes(ClipPath) | FileAttributes.ReadOnly);
            try
            {
                Assert.IsNotNull((Call("edit_animation_curve", Request()))["error"]);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(ClipPath));
            }
            finally { File.SetAttributes(ClipPath, FileAttributes.Normal); }
        }

        [Test]
        public void InvalidLaterKey_IsAtomicForExistingClip()
        {
            Success(Call("edit_animation_curve", Request()));
            var before = File.ReadAllBytes(ClipPath);
            var request = Request("upsert_keys");
            request["keys"][0]["value"] = 123;
            request["keys"][1]["rightTangentMode"] = "bogus";
            Assert.IsNotNull((Call("edit_animation_curve", request))["error"]);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ClipPath));
            Assert.AreEqual(0, AnimationUtility.GetEditorCurve(AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath), X).keys[0].value);
        }

        [TestCase("upsert_keys")]
        [TestCase("remove_keys")]
        public void KeyEdits_RecalculateNeighboringLinearTangents(string operation)
        {
            var request = Request();
            request["keys"] = JArray.FromObject(new[] { new { time = 0f, value = 0f }, new { time = 1f, value = 2f }, new { time = 2f, value = 2f } });
            Success(Call("edit_animation_curve", request));
            request = Request(operation);
            if (operation == "remove_keys") { request.Remove("keys"); request["times"] = new JArray(1f); }
            else request["keys"] = JArray.FromObject(new[] { new { time = 1f, value = 1f } });
            Success(Call("edit_animation_curve", request));
            var curve = AnimationUtility.GetEditorCurve(AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath), X);
            Assert.AreEqual(.5f, curve.Evaluate(.5f), 1e-5f);
            Assert.AreEqual(1f, curve.keys[0].outTangent, 1e-5f);
        }

        [Test]
        public void ExistingUnimportedFile_IsNotOverwritten()
        {
            File.WriteAllText(ClipPath, "unimported asset must survive");
            var before = File.ReadAllBytes(ClipPath);
            Assert.IsNotNull((Call("edit_animation_curve", Request()))["error"]);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ClipPath));
        }
    }
}
