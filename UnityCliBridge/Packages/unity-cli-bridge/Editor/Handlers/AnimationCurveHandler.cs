using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Handlers
{
    /// <summary>Edits one validated numeric binding without resetting the clip.</summary>
    public static class AnimationCurveHandler
    {
        public static object GetAnimationCurves(JObject parameters)
        {
            try
            {
                var path = RequiredString(parameters, "clipPath");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) throw new ArgumentException("AnimationClip not found: " + path);
                EditorCurveBinding? filter = parameters["binding"] == null ? (EditorCurveBinding?)null : ParseBinding(parameters["binding"] as JObject);
                return new
                {
                    success = true, clipPath = path,
                    curves = AnimationUtility.GetCurveBindings(clip).Where(b => !filter.HasValue || Matches(b, filter.Value))
                        .Select(b => new { binding = BindingData(b), keys = KeyData(AnimationUtility.GetEditorCurve(clip, b)), isDiscrete = b.isDiscreteCurve }).ToArray(),
                    objectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .Where(b => !filter.HasValue || Matches(b, filter.Value)).Select(BindingData).ToArray()
                };
            }
            catch (Exception e) { return new { error = e.Message }; }
        }

        public static object EditAnimationCurve(JObject parameters)
        {
            AnimationClip created = null;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new ArgumentException("Animation curves cannot be edited in Play Mode");
                var path = RequiredString(parameters, "clipPath");
                ValidateClipPath(path);
                var operation = RequiredString(parameters, "operation");
                if (!new[] { "set", "upsert_keys", "remove_keys", "remove_curve" }.Contains(operation))
                    throw new ArgumentException("Unknown animation curve operation: " + operation);
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main == null && (File.Exists(path) || Directory.Exists(path)))
                    throw new ArgumentException("Existing asset cannot be loaded as a standalone AnimationClip: " + path);
                var clip = main as AnimationClip;
                if (main != null && clip == null) throw new ArgumentException("Asset is not an AnimationClip");
                if (clip != null && (!AssetDatabase.IsMainAsset(clip) || !AssetDatabase.IsOpenForEdit(clip)))
                    throw new ArgumentException("Only a writable standalone AnimationClip can be edited");
                var create = parameters["createIfMissing"]?.Value<bool>() ?? false;
                if (clip == null && (operation != "set" || !create))
                    throw new ArgumentException("AnimationClip not found; creation requires set and createIfMissing=true");
                var binding = ResolveBinding(parameters);
                var original = clip == null ? null : AnimationUtility.GetEditorCurve(clip, binding);
                if (operation != "set" && original == null)
                    throw new ArgumentException("Numeric curve does not exist for the requested binding");
                var curve = operation == "set" ? new AnimationCurve() : CloneCurve(original);
                if (operation == "set" || operation == "upsert_keys") ApplyKeys(curve, parameters["keys"] as JArray);
                else if (operation == "remove_keys") RemoveKeys(curve, parameters["times"] as JArray);
                else curve = null;

                // All request validation and curve edits happen before creating or dirtying assets.
                if (clip == null)
                {
                    EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                    clip = created = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
                    AssetDatabase.CreateAsset(clip, path);
                }
                Undo.RecordObject(clip, "Edit animation curve");
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                return new { success = true, clipPath = path, binding = BindingData(binding), keyCount = curve?.length ?? 0, operation };
            }
            catch (Exception e)
            {
                if (created != null)
                {
                    var path = AssetDatabase.GetAssetPath(created);
                    if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                    else UnityEngine.Object.DestroyImmediate(created);
                }
                return new { error = e.Message };
            }
        }

        private static string RequiredString(JObject parameters, string name, bool allowEmpty = false)
        {
            var token = parameters?[name];
            if (token?.Type != JTokenType.String || (!allowEmpty && string.IsNullOrWhiteSpace((string)token)))
                throw new ArgumentException(name + " must be " + (allowEmpty ? "a string" : "a nonempty string"));
            return (string)token;
        }

        private static EditorCurveBinding ParseBinding(JObject value)
        {
            var path = RequiredString(value, "path", true);
            if (path.StartsWith("/") || path.Contains("\\") || path.Split('/').Any(p => p == "." || p == ".."))
                throw new ArgumentException("binding.path must be relative to animationRoot");
            var component = RequiredString(value, "component");
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(component, false)).FirstOrDefault(t => t != null);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new ArgumentException("Unknown Component type: " + component);
            var property = RequiredString(value, "property");
            if (type == typeof(Transform))
                foreach (var name in new[] { "localPosition", "localRotation", "localScale" })
                    if (property.StartsWith(name + ".", StringComparison.Ordinal)) property = "m_" + char.ToUpperInvariant(name[0]) + property.Substring(1);
            return EditorCurveBinding.FloatCurve(path, type, property);
        }

        private static EditorCurveBinding ResolveBinding(JObject parameters)
        {
            if (parameters["animationRoot"]?.Type != JTokenType.Integer)
                throw new ArgumentException("animationRoot must be a GameObject instance ID");
            var root = EditorUtility.InstanceIDToObject(parameters["animationRoot"].Value<int>()) as GameObject;
            if (root == null) throw new ArgumentException("animationRoot GameObject not found");
            var requested = ParseBinding(parameters["binding"] as JObject);
            var target = requested.path.Length == 0 ? root.transform : root.transform.Find(requested.path);
            if (target == null) throw new ArgumentException("binding.path not found under animationRoot: " + requested.path);
            var matches = AnimationUtility.GetAnimatableBindings(target.gameObject, root).Where(b => Matches(b, requested)).ToArray();
            if (matches.Length == 0) throw new ArgumentException("Invalid animatable binding: " + requested.type.FullName + "." + requested.propertyName);
            var binding = matches[0];
            if (binding.isPPtrCurve || binding.isDiscreteCurve)
                throw new ArgumentException("Object-reference and discrete bindings do not support numeric curve editing");
            // Unity exposes some boolean properties as float bindings, without isDiscreteCurve.
            var component = target.GetComponent(binding.type);
            if (component != null)
            {
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.FindProperty(binding.propertyName);
                    if (property != null && (property.propertyType == SerializedPropertyType.Boolean ||
                        property.propertyType == SerializedPropertyType.Enum || property.propertyType == SerializedPropertyType.ObjectReference))
                        throw new ArgumentException("Binding is not a continuous numeric property: " + binding.propertyName);
                }
            }
            return binding;
        }

        private static bool Matches(EditorCurveBinding a, EditorCurveBinding b) =>
            a.path == b.path && a.type == b.type && a.propertyName == b.propertyName;

        private static object BindingData(EditorCurveBinding binding) =>
            new { path = binding.path, component = binding.type.FullName, property = binding.propertyName };

        private static object[] KeyData(AnimationCurve curve) => curve.keys.Select((key, i) => (object)new
        {
            time = key.time, value = key.value,
            leftTangentMode = AnimationUtility.GetKeyLeftTangentMode(curve, i).ToString(),
            rightTangentMode = AnimationUtility.GetKeyRightTangentMode(curve, i).ToString(),
            inTangent = FiniteOrNull(key.inTangent), outTangent = FiniteOrNull(key.outTangent)
        }).ToArray();

        private static float? FiniteOrNull(float value) => float.IsNaN(value) || float.IsInfinity(value) ? (float?)null : value;

        private static float Number(JToken token, string name, bool nonnegative = false)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new ArgumentException(name + " must be a finite number");
            var value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || (nonnegative && value < 0))
                throw new ArgumentException(name + " must be finite" + (nonnegative ? " and nonnegative" : ""));
            return value;
        }

        private static AnimationUtility.TangentMode Mode(JToken token)
        {
            if (token?.Type != JTokenType.String || !Enum.TryParse((string)token, out AnimationUtility.TangentMode mode) ||
                !new[] { "Linear", "Constant", "Auto", "ClampedAuto", "Free" }.Contains((string)token))
                throw new ArgumentException("Unknown tangent mode: " + token);
            return mode;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source) => source == null ? null :
            new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };

        private static void ApplyKeys(AnimationCurve curve, JArray keys)
        {
            if (keys == null || keys.Count == 0) throw new ArgumentException("keys must be a nonempty array");
            var times = new HashSet<float>();
            var modes = new Dictionary<float, (AnimationUtility.TangentMode left, AnimationUtility.TangentMode right)>();
            foreach (var item in keys)
            {
                if (!(item is JObject key)) throw new ArgumentException("Each key must be an object");
                var time = Number(key["time"], "time", true);
                if (!times.Add(time)) throw new ArgumentException("Duplicate key time: " + time);
                var value = Number(key["value"], "value");
                var index = Array.FindIndex(curve.keys, k => k.time == time);
                var frame = index < 0 ? new Keyframe(time, value) : curve.keys[index];
                var left = index < 0 ? AnimationUtility.TangentMode.Linear : AnimationUtility.GetKeyLeftTangentMode(curve, index);
                var right = index < 0 ? AnimationUtility.TangentMode.Linear : AnimationUtility.GetKeyRightTangentMode(curve, index);
                if (key["leftTangentMode"] != null) left = Mode(key["leftTangentMode"]);
                if (key["rightTangentMode"] != null) right = Mode(key["rightTangentMode"]);
                frame.value = value;
                if (key["inTangent"] != null) frame.inTangent = Number(key["inTangent"], "inTangent");
                if (key["outTangent"] != null) frame.outTangent = Number(key["outTangent"], "outTangent");
                // A reused Constant key carries Infinity; Free requires an explicit finite replacement.
                if ((left == AnimationUtility.TangentMode.Free && !FiniteOrNull(frame.inTangent).HasValue) ||
                    (right == AnimationUtility.TangentMode.Free && !FiniteOrNull(frame.outTangent).HasValue))
                    throw new ArgumentException("Free tangent mode requires finite tangent values");
                if (index < 0) curve.AddKey(frame); else curve.MoveKey(index, frame);
                modes[time] = (left, right);
            }
            // Apply interpolation after the final time ordering is known.
            for (var i = 0; i < curve.length; i++)
            {
                if (!modes.TryGetValue(curve.keys[i].time, out var mode)) continue;
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, mode.left);
                AnimationUtility.SetKeyRightTangentMode(curve, i, mode.right);
            }
            RecalculateTangents(curve);
        }

        private static void RemoveKeys(AnimationCurve curve, JArray times)
        {
            if (times == null || times.Count == 0) throw new ArgumentException("times must be a nonempty array");
            var requested = new HashSet<float>();
            foreach (var token in times)
            {
                var time = Number(token, "time", true);
                if (!requested.Add(time)) throw new ArgumentException("Duplicate removal time: " + time);
                if (!curve.keys.Any(k => k.time == time)) throw new ArgumentException("Key not found at time: " + time);
            }
            for (var i = curve.length - 1; i >= 0; i--) if (requested.Contains(curve.keys[i].time)) curve.RemoveKey(i);
            RecalculateTangents(curve);
        }

        private static void RecalculateTangents(AnimationCurve curve)
        {
            // Key insertion/removal does not update adjacent editor-mode tangents automatically.
            for (var i = 0; i < curve.length; i++)
            {
                var left = AnimationUtility.GetKeyLeftTangentMode(curve, i);
                var right = AnimationUtility.GetKeyRightTangentMode(curve, i);
                if (left != AnimationUtility.TangentMode.Free) AnimationUtility.SetKeyLeftTangentMode(curve, i, left);
                if (right != AnimationUtility.TangentMode.Free) AnimationUtility.SetKeyRightTangentMode(curve, i, right);
            }
        }

        private static void ValidateClipPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("\\") || path.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new ArgumentException("clipPath must be a standalone .anim path under Assets/");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new ArgumentException("AnimationClip is read-only");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException("Could not create asset folder: " + path);
        }
    }
}
