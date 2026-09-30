using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Handlers
{
    /// <summary>Prefab inheritance and instance overrides, using public Editor APIs.</summary>
    public static class PrefabWorkflowHandler
    {
        private const InteractionMode Mode = InteractionMode.AutomatedAction;

        public static object GetOverrides(JObject parameters) => Execute(() =>
        {
            var root = Instance(parameters);
            var objects = PrefabUtility.GetObjectOverrides(root, true);
            var properties = new JArray();
            foreach (var item in objects)
            {
                using (var serialized = new SerializedObject(item.instanceObject))
                {
                    var property = serialized.GetIterator();
                    var visitedReferences = new HashSet<long>();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        // SerializeReference graphs can contain cycles; visit each object once.
                        enterChildren = property.propertyType != SerializedPropertyType.ManagedReference
                            || visitedReferences.Add(property.managedReferenceId);
                        if (!property.prefabOverride) continue;
                        properties.Add(new JObject
                        {
                            ["instanceId"] = item.instanceObject.GetInstanceID(),
                            ["type"] = item.instanceObject.GetType().FullName,
                            ["gameObjectPath"] = ObjectPath(item.instanceObject),
                            ["propertyPath"] = property.propertyPath,
                            ["propertyType"] = property.propertyType.ToString(),
                            ["isDefaultOverride"] = property.isDefaultOverride
                        });
                    }
                }
            }
            return new
            {
                success = true, gameObjectPath = ObjectPath(root),
                prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),
                applyTargets = SourcePaths(PrefabUtility.GetCorrespondingObjectFromSource(root)).ToArray(),
                properties,
                objects = objects.Select(o => Describe(o.instanceObject)).ToArray(),
                addedComponents = PrefabUtility.GetAddedComponents(root).Select(o => Describe(o.instanceComponent)).ToArray(),
                removedComponents = PrefabUtility.GetRemovedComponents(root).Select(o => new
                {
                    instanceId = o.containingInstanceGameObject.GetInstanceID(),
                    gameObjectPath = ObjectPath(o.containingInstanceGameObject),
                    assetComponentId = o.assetComponent.GetInstanceID(), type = o.assetComponent.GetType().FullName,
                    assetPath = AssetDatabase.GetAssetPath(o.assetComponent)
                }).ToArray()
            };
        });

        public static object ManageOverrides(JObject parameters) => Execute(() =>
        {
            string action = Required(parameters, "action");
            string scope = Required(parameters, "scope");
            Require(action == "apply" || action == "revert", "INVALID_ARGUMENT", "action must be apply or revert");
            Require(new[] { "all", "object", "property", "added_component", "removed_component" }.Contains(scope),
                "INVALID_ARGUMENT", "Unknown override scope");
            var root = Instance(parameters);
            bool apply = action == "apply";
            string assetPath = null;
            if (apply)
            {
                assetPath = Required(parameters, "assetPath");
                Require(SourcePaths(PrefabUtility.GetCorrespondingObjectFromSource(root)).Contains(assetPath)
                    && AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null,
                    "INVALID_APPLY_TARGET", "assetPath must be a source Prefab or Variant of this instance");
                Require(!PrefabUtility.IsPartOfImmutablePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)),
                    "INVALID_APPLY_TARGET", "The target Prefab is immutable");
            }
            else
            {
                Require(parameters["assetPath"] == null, "INVALID_ARGUMENT", "revert restores the immediate source; assetPath is only valid for apply");
            }

            if (scope == "all")
            {
                if (!apply) PrefabUtility.RevertPrefabInstance(root, Mode);
                else if (assetPath == PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root))
                    PrefabUtility.ApplyPrefabInstance(root, Mode);
                else
                {
                    // Validate every target before applying any override to an ancestor asset.
                    var overrides = AllOverrides(root);
                    foreach (var item in overrides) ValidateApplyTarget(item.GetAssetObject(), assetPath);
                    foreach (var item in overrides) item.Apply(assetPath, Mode);
                }
            }
            else
            {
                var target = EditorUtility.InstanceIDToObject(parameters.Value<int?>("instanceId") ?? 0);
                var targetObject = target as GameObject ?? (target as Component)?.gameObject;
                Require(targetObject != null && !EditorUtility.IsPersistent(targetObject)
                    && (targetObject == root || targetObject.transform.IsChildOf(root.transform)),
                    "INVALID_TARGET", "instanceId must identify an object within the selected Prefab instance");
                if (scope == "property")
                {
                    using (var serialized = new SerializedObject(target))
                    {
                        var property = serialized.FindProperty(Required(parameters, "propertyPath"));
                        Require(property != null && property.prefabOverride, "OVERRIDE_NOT_FOUND", "No property override exists at propertyPath");
                        if (apply)
                        {
                            Require(!property.isDefaultOverride, "DEFAULT_OVERRIDE", "Default root transform overrides cannot be applied");
                            ValidateApplyTarget(PrefabUtility.GetCorrespondingObjectFromSource(target), assetPath);
                            PrefabUtility.ApplyPropertyOverride(property, assetPath, Mode);
                        }
                        else PrefabUtility.RevertPropertyOverride(property, Mode);
                    }
                }
                else
                {
                    PrefabOverride selected = null;
                    if (scope == "object") selected = PrefabUtility.GetObjectOverrides(root, false).FirstOrDefault(o => o.instanceObject == target);
                    if (scope == "added_component") selected = PrefabUtility.GetAddedComponents(root).FirstOrDefault(o => o.instanceComponent == target);
                    if (scope == "removed_component") selected = PrefabUtility.GetRemovedComponents(root).FirstOrDefault(o =>
                        o.containingInstanceGameObject == targetObject && o.assetComponent.GetInstanceID() == parameters.Value<int?>("assetComponentId"));
                    Require(selected != null, "OVERRIDE_NOT_FOUND", "The specified override no longer exists; list overrides again");
                    if (apply)
                    {
                        ValidateApplyTarget(selected.GetAssetObject(), assetPath);
                        selected.Apply(assetPath, Mode);
                    }
                    else selected.Revert(Mode);
                }
            }
            if (apply) AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            return new { success = true, action, scope, gameObjectPath = ObjectPath(root), assetPath };
        }, true);

        public static object Unpack(JObject parameters) => Execute(() =>
        {
            string mode = Required(parameters, "mode");
            Require(mode == "Outermost" || mode == "Completely", "INVALID_ARGUMENT", "mode must be Outermost or Completely");
            var root = Instance(parameters);
            string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            PrefabUtility.UnpackPrefabInstance(root, mode == "Completely" ? PrefabUnpackMode.Completely : PrefabUnpackMode.OutermostRoot, Mode);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return new { success = true, mode, gameObjectPath = ObjectPath(root), sourcePrefabPath = sourcePath,
                isPartOfPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(root),
                connectedObjectCount = root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) };
        }, true);

        private static List<PrefabOverride> AllOverrides(GameObject root)
        {
            var result = new List<PrefabOverride>();
            result.AddRange(PrefabUtility.GetRemovedComponents(root));
            result.AddRange(PrefabUtility.GetRemovedGameObjects(root));
            result.AddRange(PrefabUtility.GetAddedGameObjects(root));
            result.AddRange(PrefabUtility.GetAddedComponents(root));
            result.AddRange(PrefabUtility.GetObjectOverrides(root, false));
            return result;
        }

        private static void ValidateApplyTarget(Object source, string assetPath) => Require(
            SourcePaths(source).Contains(assetPath), "INVALID_APPLY_TARGET",
            "An overridden object does not exist in the requested asset; apply to its owning Variant instead");

        private static IEnumerable<string> SourcePaths(Object source)
        {
            while (source != null)
            {
                yield return AssetDatabase.GetAssetPath(source);
                source = PrefabUtility.GetCorrespondingObjectFromSource(source);
            }
        }

        private static object Describe(Object target) => new
        {
            instanceId = target.GetInstanceID(), gameObjectPath = ObjectPath(target), type = target.GetType().FullName
        };

        private static string ObjectPath(Object target)
        {
            var gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null) return null;
            string path = gameObject.name;
            for (var parent = gameObject.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return "/" + path;
        }

        private static GameObject Instance(JObject parameters)
        {
            string path = "/" + Required(parameters, "gameObjectPath").TrimStart('/');
            var matches = Resources.FindObjectsOfTypeAll<GameObject>().Where(o => o.scene.IsValid() && o.scene.isLoaded
                && !EditorUtility.IsPersistent(o) && !EditorSceneManager.IsPreviewScene(o.scene) && ObjectPath(o) == path).ToArray();
            Require(matches.Length == 1, "INVALID_TARGET", "gameObjectPath must identify exactly one scene object (including inactive objects)");
            var root = matches[0];
            Require(PrefabUtility.IsAnyPrefabInstanceRoot(root), "NOT_PREFAB_INSTANCE_ROOT", "Select a Prefab instance root");
            return root;
        }

        private static string Required(JObject parameters, string field)
        {
            string value = parameters.Value<string>(field);
            Require(!string.IsNullOrWhiteSpace(value), "INVALID_ARGUMENT", field + " is required");
            return value;
        }

        private static void Require(bool condition, string code, string message)
        {
            if (!condition) throw new RequestException(code, message);
        }

        private static object Execute(Func<object> action, bool mutating = false)
        {
            try
            {
                Require(!mutating || !EditorApplication.isPlayingOrWillChangePlaymode, "PLAY_MODE_BLOCKED", "Prefab writes require Edit Mode");
                return action();
            }
            catch (RequestException error) { return new { success = false, error = error.Message, code = error.Code }; }
            catch (Exception error) { return new { success = false, error = error.Message, code = "PREFAB_OPERATION_FAILED" }; }
        }

        private sealed class RequestException : Exception
        {
            public string Code { get; }
            public RequestException(string code, string message) : base(message) { Code = code; }
        }
    }
}
