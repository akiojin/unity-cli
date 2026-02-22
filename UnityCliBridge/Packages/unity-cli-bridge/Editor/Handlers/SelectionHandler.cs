using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Editor selection operations
    /// </summary>
    public static class SelectionHandler
    {
        /// <summary>
        /// Handle selection operations (get, set, clear, get_details)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "get":
                        bool includeDetails = parameters["includeDetails"]?.ToObject<bool>() ?? false;
                        return GetSelection(includeDetails);
                    case "set":
                        var objectPaths = parameters["objectPaths"]?.ToObject<string[]>();
                        return SetSelection(objectPaths);
                    case "clear":
                        return ClearSelection();
                    case "get_details":
                        return GetSelectionDetails();
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("SelectionHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get current selection
        /// </summary>
        private static object GetSelection(bool includeDetails)
        {
            try
            {
                var selectedObjects = Selection.gameObjects;
                var selection = new List<object>();

                foreach (var go in selectedObjects)
                {
                    if (includeDetails)
                    {
                        selection.Add(GetDetailedObjectInfo(go));
                    }
                    else
                    {
                        selection.Add(new
                        {
                            path = GetGameObjectPath(go),
                            name = go.name
                        });
                    }
                }

                return new
                {
                    success = true,
                    action = "get",
                    selection = selection,
                    count = selection.Count
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("SelectionHandler", $"Error getting selection: {e.Message}");
                return new { error = $"Failed to get selection: {e.Message}" };
            }
        }

        /// <summary>
        /// Set selection to specific objects
        /// </summary>
        private static object SetSelection(string[] objectPaths)
        {
            try
            {
                if (objectPaths == null || objectPaths.Length == 0)
                {
                    return new { error = "No object paths provided" };
                }

                var selectedObjects = new List<GameObject>();
                var notFound = new List<string>();

                foreach (var path in objectPaths)
                {
                    var go = GameObject.Find(path.TrimStart('/'));
                    if (go != null)
                    {
                        selectedObjects.Add(go);
                    }
                    else
                    {
                        notFound.Add(path);
                    }
                }

                if (selectedObjects.Count == 0)
                {
                    return new { error = "No valid objects found to select" };
                }

                // Set the selection
                Selection.objects = selectedObjects.ToArray();

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "action", "set" },
                    { "selection", selectedObjects.Select(go => new {
                        path = GetGameObjectPath(go),
                        name = go.name
                    }).ToList() },
                    { "count", selectedObjects.Count }
                };

                if (notFound.Count > 0)
                {
                    result["notFound"] = notFound;
                    result["message"] = $"Selection set to {selectedObjects.Count} object(s). {notFound.Count} object(s) not found.";
                }
                else
                {
                    result["message"] = $"Selection set to {selectedObjects.Count} objects";
                }

                return result;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("SelectionHandler", $"Error setting selection: {e.Message}");
                return new { error = $"Failed to set selection: {e.Message}" };
            }
        }

        /// <summary>
        /// Clear current selection
        /// </summary>
        private static object ClearSelection()
        {
            try
            {
                int previousCount = Selection.gameObjects.Length;
                Selection.activeGameObject = null;
                Selection.objects = new UnityEngine.Object[0];

                return new
                {
                    success = true,
                    action = "clear",
                    previousCount = previousCount,
                    message = previousCount > 0 
                        ? $"Selection cleared. Previously had {previousCount} objects selected."
                        : "Selection was already empty"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("SelectionHandler", $"Error clearing selection: {e.Message}");
                return new { error = $"Failed to clear selection: {e.Message}" };
            }
        }

        /// <summary>
        /// Get detailed information about current selection
        /// </summary>
        private static object GetSelectionDetails()
        {
            try
            {
                var selectedObjects = Selection.gameObjects;
                
                if (selectedObjects.Length == 0)
                {
                    return new
                    {
                        success = true,
                        action = "get_details",
                        selection = new List<object>(),
                        count = 0,
                        message = "No objects selected"
                    };
                }

                var selection = new List<object>();
                int totalChildrenCount = 0;
                bool hasActiveObjects = false;
                bool hasInactiveObjects = false;

                foreach (var go in selectedObjects)
                {
                    var details = GetDetailedObjectInfo(go);
                    selection.Add(details);
                    
                    totalChildrenCount += go.transform.childCount;
                    if (go.activeSelf) hasActiveObjects = true;
                    else hasInactiveObjects = true;
                }

                return new
                {
                    success = true,
                    action = "get_details",
                    selection = selection,
                    count = selection.Count,
                    totalChildrenCount = totalChildrenCount,
                    hasActiveObjects = hasActiveObjects,
                    hasInactiveObjects = hasInactiveObjects
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("SelectionHandler", $"Error getting selection details: {e.Message}");
                return new { error = $"Failed to get selection details: {e.Message}" };
            }
        }

        /// <summary>
        /// Get detailed information about a GameObject
        /// </summary>
        private static object GetDetailedObjectInfo(GameObject go)
        {
            var components = go.GetComponents<Component>();
            var componentList = new List<object>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                
                var enabled = true;
                if (comp is Behaviour behaviour)
                {
                    enabled = behaviour.enabled;
                }
                else if (comp is Renderer renderer)
                {
                    enabled = renderer.enabled;
                }
                else if (comp is Collider collider)
                {
                    enabled = collider.enabled;
                }

                componentList.Add(new
                {
                    type = comp.GetType().Name,
                    enabled = enabled
                });
            }

            var result = new Dictionary<string, object>
            {
                { "path", GetGameObjectPath(go) },
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isActive", go.activeSelf },
                { "position", new { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z } },
                { "rotation", new { x = go.transform.eulerAngles.x, y = go.transform.eulerAngles.y, z = go.transform.eulerAngles.z } },
                { "scale", new { x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z } },
                { "childCount", go.transform.childCount }
            };

            // Basic component info for get action with details
            if (componentList.Count > 0)
            {
                result["components"] = componentList.Select(c => ((dynamic)c).type).ToList();
            }

            // Check if it's a prefab instance
            var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(go);
            if (prefabStatus != PrefabInstanceStatus.NotAPrefab)
            {
                result["isPrefabInstance"] = true;
                var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabAsset != null)
                {
                    result["prefabPath"] = AssetDatabase.GetAssetPath(prefabAsset);
                }
            }
            else
            {
                result["isPrefabInstance"] = false;
            }

            // Parent information
            if (go.transform.parent != null)
            {
                result["parentPath"] = GetGameObjectPath(go.transform.parent.gameObject);
            }
            else
            {
                result["parentPath"] = null;
            }

            return result;
        }

        /// <summary>
        /// Get the full path of a GameObject in the hierarchy
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "";

            string path = go.name;
            Transform parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return "/" + path;
        }
    }
}