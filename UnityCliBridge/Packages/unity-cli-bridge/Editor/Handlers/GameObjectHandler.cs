using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityCliBridge.Logging;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles GameObject-related operations
    /// </summary>
    public static class GameObjectHandler
    {
        /// <summary>
        /// Creates a new GameObject based on parameters
        /// </summary>
        public static object CreateGameObject(JObject parameters)
        {
            try
            {
                if (Application.isPlaying)
                {
                    return new { error = "CreateGameObject is blocked during Play Mode", code = "PLAY_MODE_BLOCKED" };
                }
                // Parse parameters
                string name = parameters["name"]?.ToString() ?? "GameObject";
                string primitiveType = parameters["primitiveType"]?.ToString();
                
                // Parse transform
                var position = ParseVector3(parameters["position"]) ?? Vector3.zero;
                var rotation = ParseVector3(parameters["rotation"]) ?? Vector3.zero;
                var scale = ParseVector3(parameters["scale"]) ?? Vector3.one;
                
                // Parse parent
                string parentPath = parameters["parentPath"]?.ToString();
                GameObject parent = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parent = FindGameObjectByPath(parentPath);
                    if (parent == null)
                    {
                        return new { error = $"Parent GameObject not found: {parentPath}" };
                    }
                }
                
                // Create GameObject
                GameObject newObject;
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    newObject = CreatePrimitive(primitiveType);
                    if (newObject == null)
                    {
                        return new { error = $"Unknown primitive type: {primitiveType}" };
                    }
                }
                else
                {
                    newObject = new GameObject();
                }
                
                // Set properties
                newObject.name = name;
                newObject.transform.position = position;
                newObject.transform.rotation = Quaternion.Euler(rotation);
                newObject.transform.localScale = scale;
                
                // Set parent
                if (parent != null)
                {
                    newObject.transform.SetParent(parent.transform, true);
                }
                
                // Set tag
                string tag = parameters["tag"]?.ToString();
                if (!string.IsNullOrEmpty(tag))
                {
                    try
                    {
                        newObject.tag = tag;
                    }
                    catch (Exception)
                    {
                        McpLogger.LogWarning("GameObjectHandler", $"Invalid tag: {tag}");
                    }
                }
                
                // Set layer
                int? layer = parameters["layer"]?.ToObject<int>();
                if (layer.HasValue && layer.Value >= 0 && layer.Value < 32)
                {
                    newObject.layer = layer.Value;
                }
                
                // Register undo
                Undo.RegisterCreatedObjectUndo(newObject, $"Create {name}");
                
                // Select the new object
                Selection.activeGameObject = newObject;
                
                // Return info about created object
                return new
                {
                    id = newObject.GetInstanceID(),
                    name = newObject.name,
                    path = GetGameObjectPath(newObject),
                    position = new { x = position.x, y = position.y, z = position.z },
                    rotation = new { x = rotation.x, y = rotation.y, z = rotation.z },
                    scale = new { x = scale.x, y = scale.y, z = scale.z },
                    tag = newObject.tag,
                    layer = newObject.layer,
                    isActive = newObject.activeSelf
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to create GameObject: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Creates a primitive GameObject
        /// </summary>
        private static GameObject CreatePrimitive(string type)
        {
            switch (type.ToLower())
            {
                case "cube":
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);
                case "sphere":
                    return GameObject.CreatePrimitive(PrimitiveType.Sphere);
                case "cylinder":
                    return GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                case "capsule":
                    return GameObject.CreatePrimitive(PrimitiveType.Capsule);
                case "plane":
                    return GameObject.CreatePrimitive(PrimitiveType.Plane);
                case "quad":
                    return GameObject.CreatePrimitive(PrimitiveType.Quad);
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// Parses a Vector3 from JToken
        /// </summary>
        private static Vector3? ParseVector3(JToken token)
        {
            if (token == null) return null;
            
            try
            {
                if (token is JObject obj)
                {
                    float x = obj["x"]?.ToObject<float>() ?? 0;
                    float y = obj["y"]?.ToObject<float>() ?? 0;
                    float z = obj["z"]?.ToObject<float>() ?? 0;
                    return new Vector3(x, y, z);
                }
            }
            catch
            {
                // Invalid format
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets the full hierarchy path of a GameObject
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            
            string path = obj.name;
            Transform parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return "/" + path;
        }

        /// <summary>
        /// Finds a GameObject by path, supporting both scene and prefab mode
        /// </summary>
        public static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            
            // Check if we're in prefab mode
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                // In prefab mode, search from prefab root
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                
                // If the path is the root itself
                if (path == "/" + prefabRoot.name || path == prefabRoot.name)
                {
                    return prefabRoot;
                }
                
                // If the path starts with the root name, strip it
                string searchPath = path;
                if (searchPath.StartsWith("/"))
                {
                    searchPath = searchPath.Substring(1);
                }
                
                // If path starts with prefab root name, get the relative path
                string rootPrefix = prefabRoot.name + "/";
                if (searchPath.StartsWith(rootPrefix))
                {
                    searchPath = searchPath.Substring(rootPrefix.Length);
                    Transform found = prefabRoot.transform.Find(searchPath);
                    return found != null ? found.gameObject : null;
                }
                else if (searchPath == prefabRoot.name)
                {
                    return prefabRoot;
                }
                else
                {
                    // Try direct search under prefab root
                    Transform found = prefabRoot.transform.Find(searchPath);
                    return found != null ? found.gameObject : null;
                }
            }
            else
            {
                // In scene mode, use regular GameObject.Find
                return GameObject.Find(path);
            }
        }
        
        /// <summary>
        /// Modifies an existing GameObject
        /// </summary>
        public static object ModifyGameObject(JObject parameters)
        {
            try
            {
                if (Application.isPlaying)
                {
                    bool runtime = parameters["runtime"]?.ToObject<bool>() ?? false;
                    if (!runtime)
                    {
                        return new { error = "ModifyGameObject is blocked during Play Mode without runtime:true", code = "PLAY_MODE_BLOCKED" };
                    }
                    // Disallow structural changes during Play
                    if (parameters.ContainsKey("parentPath") || parameters.ContainsKey("layer") || parameters.ContainsKey("tag"))
                    {
                        return new { error = "Structural changes (parent/tag/layer) are blocked during Play Mode", code = "PLAY_MODE_BLOCKED" };
                    }
                }
                string path = parameters["path"]?.ToString();
                if (string.IsNullOrEmpty(path))
                {
                    return new { error = "GameObject path is required" };
                }
                
                // Find the GameObject
                GameObject obj = FindGameObjectByPath(path);
                if (obj == null)
                {
                    return new { error = $"GameObject not found: {path}" };
                }
                
                // Store original values for undo / runtime rollback
                var originalName = obj.name;
                var originalPosition = obj.transform.position;
                var originalRotation = obj.transform.rotation;
                var originalScale = obj.transform.localScale;
                var originalActive = obj.activeSelf;
                
                // Apply modifications
                bool modified = false;
                
                // Record runtime snapshot before first change
                if (Application.isPlaying)
                {
                    UnityCliBridge.Helpers.RuntimeChangeJournal.Record(obj);
                }

                // Name (non-structural; allow in Play when runtime:true)
                string newName = parameters["name"]?.ToString();
                if (!string.IsNullOrEmpty(newName) && newName != obj.name)
                {
                    obj.name = newName;
                    modified = true;
                }
                
                // Transform
                var position = ParseVector3(parameters["position"]);
                if (position.HasValue)
                {
                    obj.transform.position = position.Value;
                    modified = true;
                }
                
                var rotation = ParseVector3(parameters["rotation"]);
                if (rotation.HasValue)
                {
                    obj.transform.rotation = Quaternion.Euler(rotation.Value);
                    modified = true;
                }
                
                var scale = ParseVector3(parameters["scale"]);
                if (scale.HasValue)
                {
                    obj.transform.localScale = scale.Value;
                    modified = true;
                }
                
                // Active state
                bool? active = parameters["active"]?.ToObject<bool>();
                if (active.HasValue && active.Value != obj.activeSelf)
                {
                    obj.SetActive(active.Value);
                    modified = true;
                }
                
                // Tag/Layer changes: allowed only in Edit Mode (already blocked earlier in Play)
                if (!Application.isPlaying)
                {
                    string tag = parameters["tag"]?.ToString();
                    if (!string.IsNullOrEmpty(tag) && tag != obj.tag)
                    {
                        try { obj.tag = tag; modified = true; }
                        catch (Exception) { McpLogger.LogWarning("GameObjectHandler", $"Invalid tag: {tag}"); }
                    }

                    int? layer = parameters["layer"]?.ToObject<int>();
                    if (layer.HasValue && layer.Value >= 0 && layer.Value < 32 && layer.Value != obj.layer)
                    { obj.layer = layer.Value; modified = true; }
                }
                
                // Parent changes only in Edit Mode
                if (!Application.isPlaying)
                {
                    string parentPath = parameters["parentPath"]?.ToString();
                    if (parameters.ContainsKey("parentPath"))
                    {
                        GameObject newParent = null;
                        if (!string.IsNullOrEmpty(parentPath))
                        {
                            newParent = FindGameObjectByPath(parentPath);
                            if (newParent == null)
                            {
                                return new { error = $"Parent GameObject not found: {parentPath}" };
                            }
                        }
                        if (obj.transform.parent != (newParent ? newParent.transform : null))
                        {
                            obj.transform.SetParent(newParent ? newParent.transform : null, true);
                            modified = true;
                        }
                    }
                }
                
                if (modified)
                {
                    // Register undo
                    Undo.RecordObject(obj, "Modify GameObject");
                    Undo.RecordObject(obj.transform, "Modify GameObject Transform");
                    
                    // Mark as dirty for saving
                    EditorUtility.SetDirty(obj);
                }
                
                // Return updated info
                return new
                {
                    id = obj.GetInstanceID(),
                    name = obj.name,
                    path = GetGameObjectPath(obj),
                    position = new { x = obj.transform.position.x, y = obj.transform.position.y, z = obj.transform.position.z },
                    rotation = new { x = obj.transform.rotation.eulerAngles.x, y = obj.transform.rotation.eulerAngles.y, z = obj.transform.rotation.eulerAngles.z },
                    scale = new { x = obj.transform.localScale.x, y = obj.transform.localScale.y, z = obj.transform.localScale.z },
                    tag = obj.tag,
                    layer = obj.layer,
                    isActive = obj.activeSelf,
                    modified = modified
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to modify GameObject: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Finds GameObjects based on search criteria
        /// </summary>
        public static object FindGameObjects(JObject parameters)
        {
            try
            {
                string name = parameters["name"]?.ToString();
                string tag = parameters["tag"]?.ToString();
                int? layer = parameters["layer"]?.ToObject<int>();
                bool exactMatch = parameters["exactMatch"]?.ToObject<bool>() ?? false;
                
                List<GameObject> results = new List<GameObject>();
                
                // Get all GameObjects in scene
                GameObject[] allObjects;
                // Check if we're in prefab mode
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                {
                    // In prefab mode, get all objects from prefab root
                    allObjects = prefabStage.prefabContentsRoot.GetComponentsInChildren<Transform>(true)
                        .Select(t => t.gameObject)
                        .ToArray();
                }
                else
                {
                    // In scene mode, use FindObjectsOfType
                    allObjects = GameObject.FindObjectsOfType<GameObject>(true);
                }
                
                foreach (var obj in allObjects)
                {
                    bool matches = true;
                    
                    // Check name
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (exactMatch)
                        {
                            matches &= obj.name == name;
                        }
                        else
                        {
                            matches &= obj.name.Contains(name, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    
                    // Check tag
                    if (!string.IsNullOrEmpty(tag))
                    {
                        matches &= obj.CompareTag(tag);
                    }
                    
                    // Check layer
                    if (layer.HasValue)
                    {
                        matches &= obj.layer == layer.Value;
                    }
                    
                    if (matches)
                    {
                        results.Add(obj);
                    }
                }
                
                // Convert results to data
                var resultData = results.Select(obj => new
                {
                    id = obj.GetInstanceID(),
                    name = obj.name,
                    path = GetGameObjectPath(obj),
                    tag = obj.tag,
                    layer = obj.layer,
                    isActive = obj.activeSelf,
                    transform = new
                    {
                        position = new { x = obj.transform.position.x, y = obj.transform.position.y, z = obj.transform.position.z },
                        rotation = new { x = obj.transform.rotation.eulerAngles.x, y = obj.transform.rotation.eulerAngles.y, z = obj.transform.rotation.eulerAngles.z },
                        scale = new { x = obj.transform.localScale.x, y = obj.transform.localScale.y, z = obj.transform.localScale.z }
                    }
                }).ToList();
                
                return new
                {
                    count = resultData.Count,
                    objects = resultData
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to find GameObjects: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Deletes GameObject(s)
        /// </summary>
        public static object DeleteGameObject(JObject parameters)
        {
            try
            {
                if (Application.isPlaying)
                {
                    return new { error = "DeleteGameObject is blocked during Play Mode", code = "PLAY_MODE_BLOCKED" };
                }
                string path = parameters["path"]?.ToString();
                string[] paths = parameters["paths"]?.ToObject<string[]>();
                bool includeChildren = parameters["includeChildren"]?.ToObject<bool>() ?? true;
                
                // Validate input
                if (string.IsNullOrEmpty(path) && (paths == null || paths.Length == 0))
                {
                    return new { error = "Either 'path' or 'paths' parameter is required" };
                }
                
                // Collect all paths
                List<string> allPaths = new List<string>();
                if (!string.IsNullOrEmpty(path))
                {
                    allPaths.Add(path);
                }
                if (paths != null)
                {
                    allPaths.AddRange(paths);
                }
                
                // Find and delete GameObjects
                List<string> deleted = new List<string>();
                List<string> notFound = new List<string>();
                
                foreach (string objPath in allPaths)
                {
                    GameObject obj = FindGameObjectByPath(objPath);
                    if (obj != null)
                    {
                        deleted.Add(objPath);
                        Undo.DestroyObjectImmediate(obj);
                    }
                    else
                    {
                        notFound.Add(objPath);
                    }
                }
                
                return new
                {
                    deletedCount = deleted.Count,
                    deleted = deleted,
                    notFound = notFound,
                    notFoundCount = notFound.Count
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to delete GameObject: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Gets the scene hierarchy
        /// </summary>
        // Helper class for tracking object count during hierarchy traversal
        private class ObjectCounter
        {
            public int MaxObjects { get; set; }
            public int CurrentCount { get; set; }
        }

        public static object GetHierarchy(JObject parameters)
        {
            try
            {
                string rootPath = parameters["rootPath"]?.ToString();
                bool includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? true;
                int maxDepth = parameters["maxDepth"]?.ToObject<int>() ?? 0;
                bool includeComponents = parameters["includeComponents"]?.ToObject<bool>() ?? false;
                bool includeTransform = parameters["includeTransform"]?.ToObject<bool>() ?? false;
                bool includeTags = parameters["includeTags"]?.ToObject<bool>() ?? false;
                bool includeLayers = parameters["includeLayers"]?.ToObject<bool>() ?? false;
                bool nameOnly = parameters["nameOnly"]?.ToObject<bool>() ?? false;
                int maxObjects = parameters["maxObjects"]?.ToObject<int>() ?? 100;
                
                // Build hierarchy with object count tracking
                List<object> hierarchy = new List<object>();
                var objectCounter = new ObjectCounter { MaxObjects = maxObjects, CurrentCount = 0 };
                
                // Check if a specific root path is provided
                if (!string.IsNullOrEmpty(rootPath))
                {
                    // Find the specified GameObject
                    GameObject rootObject = FindGameObjectByPath(rootPath);
                    if (rootObject == null)
                    {
                        return new { error = $"GameObject not found at path: {rootPath}" };
                    }
                    
                    McpLogger.Log("GameObjectHandler", $"GetHierarchy: Using rootPath={rootPath}, maxObjects={maxObjects}, getting children at depth 0");
                    
                    // When rootPath is specified, start from its children (depth 0 = direct children)
                    foreach (Transform child in rootObject.transform)
                    {
                        if (!includeInactive && !child.gameObject.activeInHierarchy)
                            continue;
                        
                        // Check if we've hit the object limit
                        if (objectCounter.MaxObjects > 0 && objectCounter.CurrentCount >= objectCounter.MaxObjects)
                        {
                            McpLogger.Log("GameObjectHandler", $"GetHierarchy: Hit object limit at {objectCounter.CurrentCount} objects");
                            break;
                        }
                        
                        var node = BuildHierarchyNode(child.gameObject, 0, maxDepth, includeInactive, includeComponents, includeTransform, includeTags, includeLayers, nameOnly, objectCounter);
                        if (node != null)
                        {
                            hierarchy.Add(node);
                            McpLogger.Log("GameObjectHandler", $"GetHierarchy: Added child node: {child.name}, Current count: {objectCounter.CurrentCount}");
                        }
                        else
                        {
                            McpLogger.Log("GameObjectHandler", $"GetHierarchy: Node was null for: {child.name}, Breaking loop");
                            break;
                        }
                    }
                }
                else
                {
                    // Check if we're in prefab mode
                    var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

                    if (prefabStage != null)
                    {
                        // In prefab mode, use prefab root
                        GameObject prefabRoot = prefabStage.prefabContentsRoot;
                        McpLogger.Log("GameObjectHandler", $"GetHierarchy: Prefab mode - using prefab root: {prefabRoot.name}");
                        
                        var node = BuildHierarchyNode(prefabRoot, 0, maxDepth, includeInactive, includeComponents, includeTransform, includeTags, includeLayers, nameOnly, objectCounter);
                        if (node != null)
                        {
                            hierarchy.Add(node);
                        }
                    }
                    else
                    {
                        // Get root GameObjects from the scene
                        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                        McpLogger.Log("GameObjectHandler", $"GetHierarchy: No rootPath, using scene roots. maxObjects={maxObjects}, rootObjects.Length={rootObjects.Length}");
                        
                        foreach (var root in rootObjects)
                        {
                            if (!includeInactive && !root.activeInHierarchy)
                                continue;
                            
                            // Check if we've hit the object limit
                            if (objectCounter.MaxObjects > 0 && objectCounter.CurrentCount >= objectCounter.MaxObjects)
                            {
                                McpLogger.Log("GameObjectHandler", $"GetHierarchy: Hit object limit at {objectCounter.CurrentCount} objects");
                                break;
                            }

                            var node = BuildHierarchyNode(root, 0, maxDepth, includeInactive, includeComponents, includeTransform, includeTags, includeLayers, nameOnly, objectCounter);
                            if (node != null)
                            {
                                hierarchy.Add(node);
                                McpLogger.Log("GameObjectHandler", $"GetHierarchy: Added root node: {root.name}, Current count: {objectCounter.CurrentCount}");
                            }
                            else
                            {
                                McpLogger.Log("GameObjectHandler", $"GetHierarchy: Node was null for: {root.name}, Breaking loop");
                                break;
                            }
                        }
                    }
                }
                
                // Prepare result with appropriate scene/prefab name
                var prefabStageResult = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                var result = new Dictionary<string, object>
                {
                    ["sceneName"] = prefabStageResult != null ? 
                        System.IO.Path.GetFileNameWithoutExtension(prefabStageResult.assetPath) : 
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    ["isPrefabMode"] = prefabStageResult != null,
                    ["objectCount"] = hierarchy.Count,
                    ["hierarchy"] = hierarchy
                };
                
                // Add truncation info if we hit the limit
                if (objectCounter.MaxObjects > 0 && objectCounter.CurrentCount >= objectCounter.MaxObjects)
                {
                    result["truncated"] = true;
                    result["maxObjectsReached"] = objectCounter.MaxObjects;
                    result["message"] = $"Hierarchy truncated at {objectCounter.MaxObjects} objects to prevent response size overflow";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to get hierarchy: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Builds a hierarchy node for a GameObject
        /// </summary>
        private static object BuildHierarchyNode(GameObject obj, int currentDepth, int maxDepth, bool includeInactive, bool includeComponents, bool includeTransform, bool includeTags, bool includeLayers, bool nameOnly, ObjectCounter counter)
        {
            // Increment counter first
            counter.CurrentCount++;
            
            // Check object limit after incrementing
            if (counter.MaxObjects > 0 && counter.CurrentCount > counter.MaxObjects)
            {
                // Decrement counter since we're not processing this object
                counter.CurrentCount--;
                return null;
            }
            
            // Name only mode - minimal data
            if (nameOnly)
            {
                var minimalNode = new Dictionary<string, object>
                {
                    ["name"] = obj.name,
                    ["path"] = GetGameObjectPath(obj)
                };
                
                // Add children if within depth limit and object limit
                if (maxDepth < 0 || currentDepth < maxDepth)
                {
                    List<object> children = new List<object>();
                    foreach (Transform child in obj.transform)
                    {
                        if (!includeInactive && !child.gameObject.activeInHierarchy)
                            continue;
                        
                        // Check object limit before adding child
                        if (counter.MaxObjects > 0 && counter.CurrentCount >= counter.MaxObjects)
                            break;
                            
                        var childNode = BuildHierarchyNode(child.gameObject, currentDepth + 1, maxDepth, includeInactive, includeComponents, includeTransform, includeTags, includeLayers, nameOnly, counter);
                        if (childNode != null)
                        {
                            children.Add(childNode);
                        }
                    }
                    
                    if (children.Count > 0)
                    {
                        minimalNode["children"] = children;
                    }
                }
                
                return minimalNode;
            }
            
            // Standard mode - build node with requested data
            var node = new Dictionary<string, object>
            {
                ["name"] = obj.name,
                ["path"] = GetGameObjectPath(obj),
                ["isActive"] = obj.activeSelf
            };
            
            // Add tag if requested
            if (includeTags)
            {
                node["tag"] = obj.tag;
            }
            
            // Add layer if requested
            if (includeLayers)
            {
                node["layer"] = obj.layer;
            }
            
            // Add transform if requested
            if (includeTransform)
            {
                node["transform"] = new
                {
                    position = new { x = obj.transform.position.x, y = obj.transform.position.y, z = obj.transform.position.z },
                    rotation = new { x = obj.transform.rotation.eulerAngles.x, y = obj.transform.rotation.eulerAngles.y, z = obj.transform.rotation.eulerAngles.z },
                    scale = new { x = obj.transform.localScale.x, y = obj.transform.localScale.y, z = obj.transform.localScale.z }
                };
            }
            
            // Add components if requested
            if (includeComponents)
            {
                var components = obj.GetComponents<Component>();
                var componentList = new List<string>();
                foreach (var comp in components)
                {
                    if (comp != null)
                    {
                        componentList.Add(comp.GetType().Name);
                    }
                }
                node["components"] = componentList;
            }
            
            // Add children if within depth limit and object limit
            if (maxDepth < 0 || currentDepth < maxDepth)
            {
                List<object> children = new List<object>();
                foreach (Transform child in obj.transform)
                {
                    if (!includeInactive && !child.gameObject.activeInHierarchy)
                        continue;
                    
                    // Check object limit before adding child
                    if (counter.MaxObjects > 0 && counter.CurrentCount >= counter.MaxObjects)
                        break;
                        
                    var childNode = BuildHierarchyNode(child.gameObject, currentDepth + 1, maxDepth, includeInactive, includeComponents, includeTransform, includeTags, includeLayers, nameOnly, counter);
                    if (childNode != null)
                    {
                        children.Add(childNode);
                    }
                }
                
                if (children.Count > 0)
                {
                    node["children"] = children;
                }
            }
            
            return node;
        }
    }
}
