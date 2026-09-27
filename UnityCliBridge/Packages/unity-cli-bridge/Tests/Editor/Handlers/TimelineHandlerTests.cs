using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace UnityCliBridge.Tests.Editor.Handlers
{
    public class TimelineHandlerTests
    {
        private const string Folder = "Assets/__TimelineHandlerTests";
        private const string Asset = Folder + "/Sequence.playable";
        private GameObject directorObject;
        private GameObject actor;

        private static JObject Call(string method, object parameters)
        {
            var type = typeof(UnityCliBridge.Handlers.AnimatorStateHandler).Assembly
                .GetType("UnityCliBridge.Handlers.TimelineHandler");
            Assert.NotNull(type, "Timeline facade must exist even without the optional package.");
            return JObject.FromObject(type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { JObject.FromObject(parameters) }));
        }

        private static JObject Manage(object parameters) => Call("ManageTimeline", parameters);
        private static JObject Get(object parameters) => Call("GetTimeline", parameters);
        private static void Ok(JObject result) => Assert.IsNull(result["error"], result.ToString());

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets", "__TimelineHandlerTests");
            directorObject = new GameObject("TimelineTestDirector");
            directorObject.AddComponent<PlayableDirector>();
            actor = new GameObject("TimelineTestActor");
            actor.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(directorObject);
            UnityEngine.Object.DestroyImmediate(actor);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void OptionalPackageAbsent_ReturnsDedicatedError()
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") != null)
                Assert.Ignore("This contract runs in the package-absent project.");
            Assert.AreEqual("TIMELINE_PACKAGE_MISSING", Get(new { assetPath = Asset })["code"]?.Value<string>());
            Assert.AreEqual("TIMELINE_PACKAGE_MISSING", Manage(new { action = "create_asset", assetPath = Asset })["code"]?.Value<string>());
        }

        [Test]
        public void AnimationWorkflow_EvaluatesAndRejectsStaleClipWithoutMutation()
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null)
                Assert.Ignore("Timeline package required for animation workflow.");
            Ok(Manage(new { action = "create_asset", assetPath = Asset }));
            Ok(Manage(new { action = "assign_director", assetPath = Asset, directorPath = "TimelineTestDirector" }));
            var track = Manage(new { action = "create_track", assetPath = Asset, trackName = "Movement", trackType = "AnimationTrack" });
            Ok(track);
            var id = track["trackId"].Value<string>();
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 2, 10));
            AssetDatabase.CreateAsset(clip, Folder + "/Move.anim");
            Ok(Manage(new { action = "add_clip", assetPath = Asset, trackId = id, animationClipPath = Folder + "/Move.anim", start = 0, duration = 2 }));
            Ok(Manage(new { action = "set_binding", directorPath = "TimelineTestDirector", trackId = id, animatorPath = "TimelineTestActor" }));
            Ok(Manage(new { action = "evaluate", directorPath = "TimelineTestDirector", time = 1 }));
            Assert.That(actor.transform.localPosition.x, Is.EqualTo(5).Within(0.05));
            Ok(Manage(new { action = "evaluate", directorPath = "TimelineTestDirector", time = 0.5 }));
            Assert.That(actor.transform.localPosition.x, Is.EqualTo(2.5).Within(0.05));
            var before = Get(new { assetPath = Asset });
            var rejected = Manage(new { action = "update_clip", assetPath = Asset, trackId = id, clipIndex = 0,
                expectedClip = new { animationClipPath = Folder + "/Move.anim", start = 8, duration = 2 }, start = 3 });
            Assert.AreEqual("STALE_CLIP", rejected["code"]?.Value<string>());
            Assert.AreEqual(before.ToString(), Get(new { assetPath = Asset }).ToString());
            var expected = before["tracks"][0]["clips"][0];
            Ok(Manage(new { action = "update_clip", assetPath = Asset, trackId = id, clipIndex = 0, expectedClip = expected, start = 2, duration = 3 }));
            Ok(Manage(new { action = "evaluate", directorPath = "TimelineTestDirector", time = 3 }));
            Assert.That(actor.transform.localPosition.x, Is.EqualTo(5).Within(0.05), "Evaluation must use the edited clip, even after a graph was built.");
            var after = Get(new { assetPath = Asset });
            Assert.AreEqual(id, after["tracks"][0]["trackId"]?.Value<string>());
            Ok(Manage(new { action = "remove_clip", assetPath = Asset, trackId = id, clipIndex = 0, expectedClip = after["tracks"][0]["clips"][0] }));
            Ok(Manage(new { action = "clear_binding", directorPath = "TimelineTestDirector", trackId = id }));
            Ok(Manage(new { action = "delete_track", assetPath = Asset, trackId = id }));
            Assert.IsEmpty(Get(new { assetPath = Asset })["tracks"]);
        }

        [Test]
        public void DeleteTrack_ClearsBindingOnSuppliedDirector()
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null)
                Assert.Ignore("Timeline package required.");
            Ok(Manage(new { action = "create_asset", assetPath = Asset }));
            Ok(Manage(new { action = "assign_director", assetPath = Asset, directorPath = "TimelineTestDirector" }));
            var created = Manage(new { action = "create_track", assetPath = Asset, trackName = "Movement" });
            Ok(created);
            Ok(Manage(new { action = "set_binding", directorPath = "TimelineTestDirector", trackId = created["trackId"], animatorPath = "TimelineTestActor" }));
            Ok(Manage(new { action = "delete_track", directorPath = "TimelineTestDirector", trackId = created["trackId"] }));
            var serialized = new SerializedObject(directorObject.GetComponent<PlayableDirector>());
            Assert.Zero(serialized.FindProperty("m_SceneBindings").arraySize, "Deleting a track must remove its director binding.");
        }

        [TestCase("Packages/Sequence.playable")]
        [TestCase("Assets/../Sequence.playable")]
        public void Create_RejectsUnsafeAssetPaths(string path)
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null)
                Assert.Ignore("Timeline package required.");
            Assert.IsNotNull(Manage(new { action = "create_asset", assetPath = path })["error"]);
        }

        [TestCase(-1.0, 1.0)]
        [TestCase(0.0, 0.0)]
        [TestCase(double.NaN, 1.0)]
        [TestCase(0.0, double.PositiveInfinity)]
        [TestCase(1000001.0, 1.0)]
        public void AddClip_InvalidTimesLeaveAssetUnchanged(double start, double duration)
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null)
                Assert.Ignore("Timeline package required.");
            Ok(Manage(new { action = "create_asset", assetPath = Asset }));
            var created = Manage(new { action = "create_track", assetPath = Asset, trackName = "Movement" });
            Ok(created);
            var before = Get(new { assetPath = Asset }).ToString();
            var rejected = Manage(new { action = "add_clip", assetPath = Asset, trackId = created["trackId"],
                animationClipPath = Folder + "/Missing.anim", start, duration });
            Assert.AreEqual("INVALID_TIME", rejected["code"]?.Value<string>());
            Assert.AreEqual(before, Get(new { assetPath = Asset }).ToString());
        }

        [Test]
        public void CreateAndAssign_RejectOverwriteAndAmbiguousDirectorWithoutMutation()
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null)
                Assert.Ignore("Timeline package required.");
            Ok(Manage(new { action = "create_asset", assetPath = Asset }));
            var guid = AssetDatabase.AssetPathToGUID(Asset);
            Assert.AreEqual("ASSET_EXISTS", Manage(new { action = "create_asset", assetPath = Asset })["code"]?.Value<string>());
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(Asset));
            var duplicate = new GameObject("TimelineTestDirector");
            try
            {
                duplicate.AddComponent<PlayableDirector>();
                Assert.AreEqual("AMBIGUOUS_OBJECT", Manage(new { action = "assign_director", assetPath = Asset,
                    directorPath = "/TimelineTestDirector" })["code"]?.Value<string>());
                Assert.IsNull(directorObject.GetComponent<PlayableDirector>().playableAsset);
            }
            finally { UnityEngine.Object.DestroyImmediate(duplicate); }
        }
    }
}
