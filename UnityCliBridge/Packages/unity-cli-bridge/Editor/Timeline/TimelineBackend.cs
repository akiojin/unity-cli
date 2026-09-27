using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Timeline
{
    public static class TimelineBackend
    {
        private sealed class InvalidRequest : Exception
        {
            public readonly string Code;
            public InvalidRequest(string code, string message) : base(message) { Code = code; }
        }

        public static object GetTimeline(JObject parameters) => Handle(() =>
        {
            var asset = Resolve(parameters, false, out var director);
            return Snapshot(asset, director);
        });

        public static object ManageTimeline(JObject parameters) => Handle(() => Manage(parameters));

        private static object Handle(Func<object> operation)
        {
            try { return operation(); }
            catch (InvalidRequest error) { return new { error = error.Message, code = error.Code }; }
            catch (Exception error) { return new { error = error.Message, code = "TIMELINE_ERROR" }; }
        }

        private static JObject Manage(JObject p)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "EDIT_MODE_REQUIRED", "Timeline mutations require EditMode.");
            var action = Text(p, "action");
            var actions = new[] { "create_asset", "assign_director", "create_track", "delete_track", "add_clip", "update_clip", "remove_clip", "set_binding", "clear_binding", "evaluate" };
            Require(actions.Contains(action), "INVALID_ACTION", "Unknown Timeline action: " + action);
            // Reject non-finite numbers even when supplied on an action that does not consume them.
            foreach (var field in new[] { "start", "duration", "time" })
                if (p[field] != null) Number(p, field, field == "duration");

            if (action == "create_asset")
            {
                var path = AssetPath(Text(p, "assetPath"));
                Require(path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase), "INVALID_ASSET_PATH", "Timeline assetPath must end in .playable.");
                Require(AssetDatabase.IsValidFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')), "INVALID_ASSET_PATH", "The parent Assets folder must already exist.");
                Require(!File.Exists(path) && AssetDatabase.LoadMainAssetAtPath(path) == null, "ASSET_EXISTS", "Refusing to overwrite an existing asset.");
                var created = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(created, path);
                Undo.RegisterCreatedObjectUndo(created, "Create Timeline");
                AssetDatabase.SaveAssetIfDirty(created);
                var result = Snapshot(created, null);
                result["action"] = action;
                return result;
            }

            var needsDirector = new[] { "assign_director", "set_binding", "clear_binding", "evaluate" }.Contains(action);
            var asset = Resolve(p, action == "assign_director", out var director);
            Require(!needsDirector || director != null, "DIRECTOR_REQUIRED", "directorPath is required for this action.");
            AnimationTrack track = null;
            TimelineClip clip = null;
            AnimationClip animation = null;
            Animator animator = null;
            double start = 0, duration = 0, time = 0;
            int clipIndex = -1;
            string trackName = null;

            if (action == "create_track")
            {
                Require(p["trackType"] == null || Text(p, "trackType") == "AnimationTrack", "UNSUPPORTED_TRACK", "Only AnimationTrack is supported.");
                trackName = Text(p, "trackName");
            }
            if (new[] { "delete_track", "add_clip", "update_clip", "remove_clip", "set_binding", "clear_binding" }.Contains(action))
            {
                var id = Text(p, "trackId");
                var selected = AllTracks(asset).FirstOrDefault(t => TrackId(t) == id);
                Require(selected != null, "TRACK_NOT_FOUND", "trackId does not belong to this Timeline asset.");
                track = SupportedTrack(asset, selected);
            }
            if (action == "set_binding")
            {
                animator = SceneObject(Text(p, "animatorPath")).GetComponent<Animator>();
                Require(animator != null, "ANIMATOR_NOT_FOUND", "animatorPath must identify a GameObject with an Animator.");
            }
            if (action == "add_clip")
            {
                animation = LoadAnimation(Text(p, "animationClipPath"));
                start = Number(p, "start", false);
                duration = Number(p, "duration", true);
                ValidEnd(start, duration);
            }
            if (action == "update_clip" || action == "remove_clip")
            {
                Require(p["clipIndex"]?.Type == JTokenType.Integer, "INVALID_CLIP_INDEX", "clipIndex must be an integer.");
                var rawIndex = p["clipIndex"].Value<long>();
                var clips = track.GetClips().ToArray();
                Require(rawIndex >= 0 && rawIndex < clips.Length, "CLIP_NOT_FOUND", "clipIndex is outside this track.");
                clipIndex = (int)rawIndex;
                clip = clips[clipIndex];
                Require(clip.asset is AnimationPlayableAsset, "UNSUPPORTED_CLIP", "Only AnimationPlayableAsset clips can be edited.");
                var expected = p["expectedClip"] as JObject;
                Require(expected != null, "EXPECTED_CLIP_REQUIRED", "expectedClip from get_timeline is required.");
                var expectedPath = Text(expected, "animationClipPath");
                var expectedStart = Number(expected, "start", false);
                var expectedDuration = Number(expected, "duration", true);
                Require(expectedPath == ClipPath(clip) && expectedStart == clip.start && expectedDuration == clip.duration,
                    "STALE_CLIP", "The clip changed. Query get_timeline again before editing.");
                if (action == "update_clip")
                {
                    Require(p["start"] != null || p["duration"] != null || p["animationClipPath"] != null,
                        "INVALID_PARAMETERS", "update_clip requires start, duration, or animationClipPath.");
                    start = p["start"] == null ? clip.start : Number(p, "start", false);
                    duration = p["duration"] == null ? clip.duration : Number(p, "duration", true);
                    ValidEnd(start, duration);
                    if (p["animationClipPath"] != null) animation = LoadAnimation(Text(p, "animationClipPath"));
                }
            }
            if (action == "evaluate")
            {
                time = Number(p, "time", false);
                foreach (var root in asset.GetRootTracks()) SupportedTrack(asset, root);
            }

            // All input and target validation is complete before any mutation or Undo record.
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Timeline " + action);
            bool assetChanged = false;
            switch (action)
            {
                case "assign_director":
                    Undo.RecordObject(director, "Assign Timeline");
                    director.playableAsset = asset;
                    DirtyScene(director);
                    break;
                case "create_track":
                    Undo.RegisterCompleteObjectUndo(asset, "Create Timeline track");
                    track = asset.CreateTrack<AnimationTrack>(null, trackName);
                    track.trackOffset = TrackOffset.ApplySceneOffsets;
                    Undo.RegisterCreatedObjectUndo(track, "Create Timeline track");
                    assetChanged = true;
                    break;
                case "delete_track":
                    if (director != null)
                    {
                        Undo.RecordObject(director, "Clear deleted Timeline track binding");
                        director.ClearGenericBinding(track);
                        DirtyScene(director);
                    }
                    Undo.RegisterCompleteObjectUndo(asset, "Delete Timeline track");
                    asset.DeleteTrack(track);
                    assetChanged = true;
                    break;
                case "add_clip":
                    Undo.RegisterCompleteObjectUndo(track, "Add Timeline clip");
                    clip = track.CreateClip(animation);
                    clip.start = start;
                    clip.duration = duration;
                    clipIndex = Array.IndexOf(track.GetClips().ToArray(), clip);
                    assetChanged = true;
                    break;
                case "update_clip":
                    Undo.RegisterCompleteObjectUndo(track, "Update Timeline clip");
                    clip.start = start;
                    clip.duration = duration;
                    if (animation != null)
                    {
                        Undo.RecordObject(clip.asset, "Update Timeline animation");
                        ((AnimationPlayableAsset)clip.asset).clip = animation;
                        EditorUtility.SetDirty(clip.asset);
                    }
                    assetChanged = true;
                    break;
                case "remove_clip":
                    Undo.RegisterCompleteObjectUndo(track, "Remove Timeline clip");
                    asset.DeleteClip(clip);
                    assetChanged = true;
                    break;
                case "set_binding":
                case "clear_binding":
                    Undo.RecordObject(director, "Change Timeline binding");
                    if (action == "set_binding") director.SetGenericBinding(track, animator);
                    else director.ClearGenericBinding(track);
                    DirtyScene(director);
                    break;
                case "evaluate":
                    Undo.RecordObject(director, "Evaluate Timeline");
                    foreach (var boundTrack in asset.GetOutputTracks())
                        if (director.GetGenericBinding(boundTrack) is Animator boundAnimator)
                            Undo.RegisterFullObjectHierarchyUndo(boundAnimator.gameObject, "Evaluate Timeline");
                    director.time = time;
                    director.Evaluate();
                    DirtyScene(director);
                    foreach (var boundTrack in asset.GetOutputTracks())
                        if (director.GetGenericBinding(boundTrack) is Animator boundAnimator)
                            DirtyScene(boundAnimator);
                    break;
            }
            if (assetChanged)
            {
                if (track != null) EditorUtility.SetDirty(track);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            Undo.CollapseUndoOperations(undoGroup);
            var snapshot = Snapshot(asset, director);
            snapshot["action"] = action;
            if (action == "create_track") snapshot["trackId"] = TrackId(track);
            if (action == "add_clip" || action == "update_clip") snapshot["clipIndex"] = clipIndex;
            if (action == "evaluate") snapshot["time"] = director.time;
            return snapshot;
        }

        private static TimelineAsset Resolve(JObject p, bool assigning, out PlayableDirector director)
        {
            director = null;
            if (p["directorPath"] != null)
            {
                director = SceneObject(Text(p, "directorPath")).GetComponent<PlayableDirector>();
                Require(director != null, "DIRECTOR_NOT_FOUND", "directorPath must identify a GameObject with a PlayableDirector.");
            }
            TimelineAsset asset = null;
            if (p["assetPath"] != null)
            {
                asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetPath(Text(p, "assetPath")));
                Require(asset != null, "TIMELINE_NOT_FOUND", "assetPath does not identify a TimelineAsset.");
            }
            Require(asset != null || director != null, "INVALID_PARAMETERS", "assetPath or directorPath is required.");
            if (asset == null) asset = director.playableAsset as TimelineAsset;
            Require(asset != null, "TIMELINE_NOT_FOUND", "The director has no TimelineAsset.");
            AssetPath(AssetDatabase.GetAssetPath(asset));
            Require(assigning || director == null || director.playableAsset == asset, "DIRECTOR_ASSET_MISMATCH", "directorPath and assetPath refer to different Timeline assets.");
            return asset;
        }

        private static AnimationTrack SupportedTrack(TimelineAsset asset, TrackAsset track)
        {
            Require(track.GetType() == typeof(AnimationTrack) && asset.GetRootTracks().Contains(track),
                "UNSUPPORTED_TRACK", "Only top-level AnimationTrack edits are supported.");
            var animation = (AnimationTrack)track;
            Require(animation.infiniteClip == null && !track.GetChildTracks().Any(),
                "UNSUPPORTED_TRACK", "Infinite clips and override/subtracks are not supported.");
            return animation;
        }

        private static IEnumerable<TrackAsset> AllTracks(TimelineAsset asset)
        {
            foreach (var root in asset.GetRootTracks())
                foreach (var track in Descendants(root)) yield return track;
        }

        private static IEnumerable<TrackAsset> Descendants(TrackAsset track)
        {
            yield return track;
            foreach (var child in track.GetChildTracks())
                foreach (var descendant in Descendants(child)) yield return descendant;
        }

        private static JObject Snapshot(TimelineAsset asset, PlayableDirector director)
        {
            var tracks = new JArray();
            foreach (var track in AllTracks(asset))
            {
                var clips = new JArray();
                int index = 0;
                foreach (var clip in track.GetClips())
                    clips.Add(new JObject { ["index"] = index++, ["animationClipPath"] = ClipPath(clip), ["start"] = clip.start, ["duration"] = clip.duration });
                var binding = director != null ? director.GetGenericBinding(track) : null;
                var boundObject = binding as GameObject ?? (binding as Component)?.gameObject;
                tracks.Add(new JObject { ["trackId"] = TrackId(track), ["name"] = track.name, ["type"] = track.GetType().Name,
                    ["binding"] = boundObject != null ? HierarchyPath(boundObject.transform) : null, ["clips"] = clips });
            }
            return new JObject { ["assetPath"] = AssetDatabase.GetAssetPath(asset),
                ["directorPath"] = director != null ? HierarchyPath(director.transform) : null, ["tracks"] = tracks };
        }

        private static string TrackId(TrackAsset track)
        {
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(track, out string guid, out long localId),
                "INVALID_TRACK_ID", "Track is not a persisted asset.");
            return guid + ":" + localId;
        }

        private static string ClipPath(TimelineClip clip) =>
            clip.asset is AnimationPlayableAsset playable && playable.clip != null ? AssetDatabase.GetAssetPath(playable.clip) : null;

        private static AnimationClip LoadAnimation(string path)
        {
            var animation = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetPath(path));
            Require(animation != null && !animation.legacy, "INVALID_ANIMATION_CLIP", "animationClipPath must reference a non-legacy AnimationClip.");
            return animation;
        }

        private static string AssetPath(string path)
        {
            Require(path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains('\\') &&
                !path.Split('/').Any(segment => segment == ".." || segment == "." || segment.Length == 0),
                "INVALID_ASSET_PATH", "Use a project-relative path under Assets without traversal.");
            return path;
        }

        private static GameObject SceneObject(string path)
        {
            var canonical = "/" + path.TrimStart('/');
            var matches = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        if (HierarchyPath(transform) == canonical) matches.Add(transform.gameObject);
            }
            Require(matches.Count > 0, "GAMEOBJECT_NOT_FOUND", "No scene GameObject matches the full hierarchy path: " + path);
            Require(matches.Count == 1, "AMBIGUOUS_OBJECT", "The full hierarchy path is not unique across loaded scenes: " + path);
            return matches[0];
        }

        private static string HierarchyPath(Transform transform) =>
            transform.parent == null ? "/" + transform.name : HierarchyPath(transform.parent) + "/" + transform.name;

        private static void DirtyScene(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        private static string Text(JObject p, string field)
        {
            Require(p[field]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(p[field].Value<string>()),
                "INVALID_PARAMETERS", field + " must be a non-empty string.");
            return p[field].Value<string>();
        }

        private static double Number(JObject p, string field, bool positive)
        {
            var token = p[field];
            Require(token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer),
                "INVALID_TIME", field + " must be a finite number.");
            var value = token.Value<double>();
            // TimelineClip clamps silently above 1,000,000 seconds. Reject instead of changing the request.
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value <= 1000000 && (positive ? value > 0 : value >= 0),
                "INVALID_TIME", field + (positive ? " must be finite, greater than zero, and at most 1000000." : " must be finite, non-negative, and at most 1000000."));
            return value;
        }

        private static void ValidEnd(double start, double duration) =>
            Require(!double.IsInfinity(start + duration), "INVALID_TIME", "Clip end must be finite.");

        private static void Require(bool condition, string code, string message)
        {
            if (!condition) throw new InvalidRequest(code, message);
        }
    }
}
