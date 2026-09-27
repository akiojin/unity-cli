using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace UnityCliBridge.TestScenes
{
    // Test inputs only: all Timeline editing and evaluation is exercised through the CLI.
    public static class UnityCliTimelineE2EFixture
    {
        public const string Root = "Assets/Scenes/Generated/E2E/Timeline";

        [MenuItem("Tools/Unity CLI/Timeline/Generate E2E Fixture")]
        public static void Generate()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var existing = SceneManager.GetSceneAt(i);
                if (existing.isDirty && !existing.path.StartsWith(Root + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Save unrelated dirty scenes before Timeline E2E.");
            }
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("TimelineDirector").AddComponent<PlayableDirector>();
            new GameObject("TimelineActor").AddComponent<Animator>();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Motion.anim");
            if (clip == null)
            {
                clip = new AnimationClip { name = "Timeline E2E Motion" };
                AssetDatabase.CreateAsset(clip, Root + "/Motion.anim");
            }
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0, 0, 2, 10));
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            EditorSceneManager.SaveScene(scene, Root + "/TimelineTest.unity");
        }

        [MenuItem("Tools/Unity CLI/Timeline/Reload E2E Assets")]
        public static void ReloadAssets()
        {
            var asset = GameObject.Find("TimelineDirector").GetComponent<PlayableDirector>().playableAsset;
            var path = AssetDatabase.GetAssetPath(asset);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Resources.UnloadAsset(asset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(Root + "/TimelineTest.unity");
        }

        [MenuItem("Tools/Unity CLI/Timeline/Generate Unsupported Track")]
        public static void GenerateUnsupportedTrack()
        {
            // Reflection keeps the test project compilable without com.unity.timeline.
            var timelineType = Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline", true);
            var audioType = Type.GetType("UnityEngine.Timeline.AudioTrack, Unity.Timeline", true);
            var asset = ScriptableObject.CreateInstance(timelineType);
            var path = Root + "/Unsupported.playable";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            timelineType.GetMethods().Single(m => m.Name == "CreateTrack" && !m.IsGenericMethod &&
                m.GetParameters().Length == 3).Invoke(asset, new object[] { audioType, null, "Audio" });
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
