using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Scene Analysis operations - deep inspection of GameObjects and components
    /// </summary>
    public static class SceneAnalysisHandler
    {
        /// <summary>
        /// Gets detailed information about a specific GameObject
        /// </summary>
        public static object GetGameObjectDetails(JObject parameters)
        {
            try
            {
                // Get parameters
                var gameObjectName = parameters["gameObjectName"]?.ToString();
                var path = parameters["path"]?.ToString();
                var includeChildren = parameters["includeChildren"]?.ToObject<bool>() ?? false;
                var includeComponents = parameters["includeComponents"]?.ToObject<bool>() ?? true;
                var includeMaterials = parameters["includeMaterials"]?.ToObject<bool>() ?? false;
                var maxDepth = parameters["maxDepth"]?.ToObject<int>() ?? 3;

                // Validate input
                if (string.IsNullOrEmpty(gameObjectName) && string.IsNullOrEmpty(path))
                {
                    return new { error = "Either gameObjectName or path must be provided" };
                }

                // Find the GameObject
                GameObject targetObject = null;
                
                if (!string.IsNullOrEmpty(path))
                {
                    targetObject = GameObject.Find(path);
                }
                else if (!string.IsNullOrEmpty(gameObjectName))
                {
                    // Try to find by name
                    targetObject = GameObject.Find(gameObjectName);
                    
                    // If not found in active objects, search all
                    if (targetObject == null)
                    {
                        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                        targetObject = allObjects.FirstOrDefault(go => go.name == gameObjectName);
                    }
                }

                if (targetObject == null)
                {
                    var identifier = !string.IsNullOrEmpty(path) ? path : gameObjectName;
                    return new { error = $"GameObject not found: {identifier}" };
                }

                // Build the result
                var result = new Dictionary<string, object>();
                result["name"] = targetObject.name;
                result["path"] = GetGameObjectPath(targetObject);
                result["isActive"] = targetObject.activeSelf;
                result["isStatic"] = targetObject.isStatic;
                result["tag"] = targetObject.tag;
                result["layer"] = LayerMask.LayerToName(targetObject.layer);

                // Transform information
                var transform = targetObject.transform;
                result["transform"] = new
                {
                    position = SerializeVector3(transform.localPosition),
                    rotation = SerializeVector3(transform.localEulerAngles),
                    scale = SerializeVector3(transform.localScale),
                    worldPosition = SerializeVector3(transform.position)
                };

                // Components
                if (includeComponents)
                {
                    var components = new List<object>();
                    foreach (var component in targetObject.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        
                        var componentData = SerializeComponent(component, includeMaterials);
                        if (componentData != null)
                        {
                            components.Add(componentData);
                        }
                    }
                    result["components"] = components;
                }

                // Children
                if (includeChildren && maxDepth > 0)
                {
                    var children = new List<object>();
                    foreach (Transform child in transform)
                    {
                        var childData = GetChildDetails(child.gameObject, maxDepth - 1, includeComponents, includeMaterials);
                        children.Add(childData);
                    }
                    result["children"] = children;
                }

                // Prefab information
                var prefabInfo = new Dictionary<string, object>();
                var prefabAssetType = PrefabUtility.GetPrefabAssetType(targetObject);
                var prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(targetObject);
                
                prefabInfo["isPrefab"] = prefabAssetType != PrefabAssetType.NotAPrefab;
                prefabInfo["isInstance"] = prefabInstanceStatus == PrefabInstanceStatus.Connected;
                
                if (prefabInfo["isPrefab"].Equals(true))
                {
                    var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(targetObject);
                    if (prefabAsset != null)
                    {
                        prefabInfo["prefabPath"] = AssetDatabase.GetAssetPath(prefabAsset);
                    }
                }
                
                result["prefabInfo"] = prefabInfo;

                // Generate summary
                var componentCount = includeComponents ? ((List<object>)result["components"]).Count : 0;
                var childCount = includeChildren ? transform.childCount : 0;
                var summary = $"GameObject \"{targetObject.name}\"";
                
                if (componentCount > 0)
                {
                    summary += $" with {componentCount} component{(componentCount != 1 ? "s" : "")}";
                }
                
                if (childCount > 0)
                {
                    summary += $" and {childCount} child{(childCount != 1 ? "ren" : "")}";
                }
                
                summary += $" at {result["path"]}";
                result["summary"] = summary;

                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to get GameObject details: {ex.Message}" };
            }
        }

        /// <summary>
        /// Serializes a component with its properties
        /// </summary>
        private static Dictionary<string, object> SerializeComponent(Component component, bool includeMaterials)
        {
            if (component == null) return null;

            var componentData = new Dictionary<string, object>();
            componentData["type"] = component.GetType().Name;

            // Check if component can be enabled/disabled
            var behaviour = component as Behaviour;
            if (behaviour != null)
            {
                componentData["enabled"] = behaviour.enabled;
            }
            else
            {
                var renderer = component as Renderer;
                if (renderer != null)
                {
                    componentData["enabled"] = renderer.enabled;
                }
                else
                {
                    componentData["enabled"] = true;
                }
            }

            // Get properties
            var properties = new Dictionary<string, object>();
            
            // Handle specific component types
            switch (component)
            {
                case Transform t:
                    properties["position"] = SerializeVector3(t.localPosition);
                    properties["rotation"] = SerializeVector3(t.localEulerAngles);
                    properties["scale"] = SerializeVector3(t.localScale);
                    break;
                    
                case MeshRenderer mr:
                    properties["shadowCastingMode"] = mr.shadowCastingMode.ToString();
                    properties["receiveShadows"] = mr.receiveShadows;
                    if (includeMaterials && mr.sharedMaterials != null)
                    {
                        properties["materials"] = mr.sharedMaterials
                            .Where(m => m != null)
                            .Select(m => m.name)
                            .ToArray();
                    }
                    break;
                    
                case Light light:
                    properties["type"] = light.type.ToString();
                    properties["color"] = SerializeColor(light.color);
                    properties["intensity"] = light.intensity;
                    properties["range"] = light.range;
                    properties["shadows"] = light.shadows.ToString();
                    break;
                    
                case Camera cam:
                    properties["fieldOfView"] = cam.fieldOfView;
                    properties["nearClipPlane"] = cam.nearClipPlane;
                    properties["farClipPlane"] = cam.farClipPlane;
                    properties["depth"] = cam.depth;
                    properties["clearFlags"] = cam.clearFlags.ToString();
                    break;
                    
                case Collider col:
                    properties["isTrigger"] = col.isTrigger;
                    if (col is BoxCollider box)
                    {
                        properties["center"] = SerializeVector3(box.center);
                        properties["size"] = SerializeVector3(box.size);
                    }
                    else if (col is SphereCollider sphere)
                    {
                        properties["center"] = SerializeVector3(sphere.center);
                        properties["radius"] = sphere.radius;
                    }
                    else if (col is CapsuleCollider capsule)
                    {
                        properties["center"] = SerializeVector3(capsule.center);
                        properties["radius"] = capsule.radius;
                        properties["height"] = capsule.height;
                        properties["direction"] = capsule.direction;
                    }
                    break;
                    
                case Rigidbody rb:
                    properties["mass"] = rb.mass;
                    properties["drag"] = rb.linearDamping;
                    properties["angularDrag"] = rb.angularDamping;
                    properties["useGravity"] = rb.useGravity;
                    properties["isKinematic"] = rb.isKinematic;
                    break;
                    
                default:
                    // For other components, try to get some basic properties
                    var type = component.GetType();
                    var publicProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    
                    foreach (var prop in publicProperties.Take(10)) // Limit to prevent too much data
                    {
                        try
                        {
                            if (prop.CanRead && IsSerializableType(prop.PropertyType))
                            {
                                var value = prop.GetValue(component);
                                if (value != null)
                                {
                                    properties[prop.Name] = SerializeValue(value);
                                }
                            }
                        }
                        catch
                        {
                            // Skip properties that throw exceptions
                        }
                    }
                    break;
            }

            if (properties.Count > 0)
            {
                componentData["properties"] = properties;
            }

            return componentData;
        }

        /// <summary>
        /// Gets child GameObject details recursively
        /// </summary>
        private static Dictionary<string, object> GetChildDetails(GameObject child, int remainingDepth, bool includeComponents, bool includeMaterials)
        {
            var childData = new Dictionary<string, object>();
            childData["name"] = child.name;
            childData["path"] = GetGameObjectPath(child);
            childData["isActive"] = child.activeSelf;

            if (includeComponents)
            {
                var components = new List<object>();
                foreach (var component in child.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var componentData = new Dictionary<string, object>
                    {
                        ["type"] = component.GetType().Name
                    };
                    components.Add(componentData);
                }
                childData["components"] = components;
            }

            if (remainingDepth > 0 && child.transform.childCount > 0)
            {
                var children = new List<object>();
                foreach (Transform grandchild in child.transform)
                {
                    var grandchildData = GetChildDetails(grandchild.gameObject, remainingDepth - 1, includeComponents, includeMaterials);
                    children.Add(grandchildData);
                }
                childData["children"] = children;
            }

            return childData;
        }

        /// <summary>
        /// Gets the full path of a GameObject
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            var path = "/" + obj.name;
            var parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            
            return path;
        }

        /// <summary>
        /// Checks if a type can be serialized
        /// </summary>
        private static bool IsSerializableType(Type type)
        {
            return type.IsPrimitive || 
                   type == typeof(string) || 
                   type == typeof(Vector3) || 
                   type == typeof(Vector2) || 
                   type == typeof(Color) ||
                   type == typeof(Quaternion) ||
                   type.IsEnum;
        }

        /// <summary>
        /// Serializes a value to a JSON-friendly format
        /// </summary>
        private static object SerializeValue(object value, HashSet<object> processedObjects = null)
        {
            // Initialize processed objects set for circular reference detection
            if (processedObjects == null)
                processedObjects = new HashSet<object>();

            // Handle null values
            if (value == null)
                return new { type = "null", value = "null" };

            // Check for circular references in Unity Objects
            if (value is UnityEngine.Object unityObj)
            {
                if (processedObjects.Contains(value))
                    return new { type = "CircularReference", objectType = value.GetType().Name };
                processedObjects.Add(value);
            }

            // Handle specific types
            switch (value)
            {
                case GameObject go:
                    return new { 
                        type = "GameObject", 
                        name = go.name, 
                        instanceId = go.GetInstanceID(),
                        path = GetGameObjectPath(go)
                    };
                    
                case Transform transform:
                    return new {
                        type = "Transform",
                        position = SerializeVector3(transform.position),
                        rotation = SerializeVector3(transform.eulerAngles),
                        localScale = SerializeVector3(transform.localScale)
                    };
                    
                case Component comp:
                    return new { 
                        type = "Component", 
                        componentType = comp.GetType().Name,
                        gameObject = comp.gameObject.name
                    };
                    
                case UnityEngine.Object obj:
                    var assetPath = AssetDatabase.GetAssetPath(obj);
                    return new { 
                        type = "UnityObject",
                        objectType = obj.GetType().Name,
                        name = obj.name,
                        assetPath = string.IsNullOrEmpty(assetPath) ? null : assetPath
                    };
                    
                case IList list:
                    var items = new List<object>();
                    int maxItems = Math.Min(list.Count, 10); // Limit to 10 items
                    for (int i = 0; i < maxItems; i++)
                    {
                        items.Add(SerializeValue(list[i], processedObjects));
                    }
                    return new { 
                        type = "Array", 
                        elementType = list.GetType().GetElementType()?.Name ?? "unknown",
                        count = list.Count, 
                        items = items,
                        truncated = list.Count > maxItems
                    };
                    
                case Vector3 v:
                    return SerializeVector3(v);
                case Vector2 v:
                    return new { x = v.x, y = v.y };
                case Vector4 v:
                    return new { x = v.x, y = v.y, z = v.z, w = v.w };
                case Color c:
                    return SerializeColor(c);
                case Quaternion q:
                    return new { x = q.x, y = q.y, z = q.z, w = q.w, eulerAngles = SerializeVector3(q.eulerAngles) };
                case Bounds b:
                    return new { center = SerializeVector3(b.center), size = SerializeVector3(b.size) };
                case Rect r:
                    return new { x = r.x, y = r.y, width = r.width, height = r.height };
                case Enum e:
                    return e.ToString();
                case bool b:
                    return b;
                case string s:
                    return s;
                case float f:
                    return f;
                case double d:
                    return d;
                case int i:
                    return i;
                case long l:
                    return l;
                default:
                    // For unknown types, return type information
                    return new { type = value.GetType().Name, value = value.ToString() };
            }
        }

        /// <summary>
        /// Serializes a Vector3
        /// </summary>
        private static Dictionary<string, float> SerializeVector3(Vector3 vector)
        {
            return new Dictionary<string, float>
            {
                ["x"] = vector.x,
                ["y"] = vector.y,
                ["z"] = vector.z
            };
        }

        /// <summary>
        /// Serializes a Color
        /// </summary>
        private static Dictionary<string, float> SerializeColor(Color color)
        {
            return new Dictionary<string, float>
            {
                ["r"] = color.r,
                ["g"] = color.g,
                ["b"] = color.b,
                ["a"] = color.a
            };
        }

        /// <summary>
        /// Analyzes the current scene and returns statistics
        /// </summary>
        public static object AnalyzeSceneContents(JObject parameters)
        {
            try
            {
                // Get parameters
                var includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? true;
                var groupByType = parameters["groupByType"]?.ToObject<bool>() ?? true;
                var includePrefabInfo = parameters["includePrefabInfo"]?.ToObject<bool>() ?? true;
                var includeMemoryInfo = parameters["includeMemoryInfo"]?.ToObject<bool>() ?? false;

                // Get current scene
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!scene.IsValid())
                {
                    return new { error = "No active scene loaded" };
                }

                var result = new Dictionary<string, object>();
                result["sceneName"] = scene.name;

                // Get all GameObjects
                var allObjects = includeInactive 
                    ? Resources.FindObjectsOfTypeAll<GameObject>()
                        .Where(go => go.scene == scene)
                        .ToArray()
                    : GameObject.FindObjectsOfType<GameObject>();

                // Basic statistics
                var statistics = new Dictionary<string, object>();
                statistics["totalGameObjects"] = allObjects.Length;
                statistics["activeGameObjects"] = allObjects.Count(go => go.activeInHierarchy);
                statistics["rootObjects"] = scene.GetRootGameObjects().Length;

                // Prefab info
                if (includePrefabInfo)
                {
                    var prefabInstances = allObjects
                        .Where(go => PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected)
                        .ToArray();
                    
                    var uniquePrefabs = prefabInstances
                        .Select(go => PrefabUtility.GetCorrespondingObjectFromSource(go))
                        .Where(prefab => prefab != null)
                        .Select(prefab => AssetDatabase.GetAssetPath(prefab))
                        .Distinct()
                        .Count();

                    statistics["prefabInstances"] = prefabInstances.Length;
                    statistics["uniquePrefabs"] = uniquePrefabs;
                }

                result["statistics"] = statistics;

                // Component distribution
                if (groupByType)
                {
                    var componentDistribution = new Dictionary<string, object>();
                    var scriptComponents = new Dictionary<string, int>();

                    foreach (var go in allObjects)
                    {
                        foreach (var component in go.GetComponents<Component>())
                        {
                            if (component == null) continue;

                            var typeName = component.GetType().Name;
                            
                            // Group MonoBehaviours separately
                            if (component is MonoBehaviour && !(component is UnityEngine.EventSystems.UIBehaviour))
                            {
                                if (!scriptComponents.ContainsKey(typeName))
                                    scriptComponents[typeName] = 0;
                                scriptComponents[typeName]++;
                            }
                            else
                            {
                                if (!componentDistribution.ContainsKey(typeName))
                                    componentDistribution[typeName] = 0;
                                componentDistribution[typeName] = (int)componentDistribution[typeName] + 1;
                            }
                        }
                    }

                    if (scriptComponents.Count > 0)
                    {
                        componentDistribution["Scripts"] = scriptComponents;
                    }

                    result["componentDistribution"] = componentDistribution;
                }

                // Rendering statistics
                var rendering = new Dictionary<string, object>();
                var renderers = allObjects
                    .SelectMany(go => go.GetComponents<Renderer>())
                    .Where(r => r != null && r.enabled)
                    .ToArray();

                var uniqueMaterials = renderers
                    .SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null)
                    .Distinct()
                    .ToArray();

                var uniqueTextures = uniqueMaterials
                    .SelectMany(m => GetTexturesFromMaterial(m))
                    .Where(t => t != null)
                    .Distinct()
                    .Count();

                var meshFilters = allObjects
                    .SelectMany(go => go.GetComponents<MeshFilter>())
                    .Where(mf => mf != null && mf.sharedMesh != null)
                    .ToArray();

                var uniqueMeshes = meshFilters
                    .Select(mf => mf.sharedMesh)
                    .Distinct()
                    .ToArray();

                rendering["materials"] = uniqueMaterials.Length;
                rendering["textures"] = uniqueTextures;
                rendering["meshes"] = uniqueMeshes.Length;
                rendering["vertices"] = uniqueMeshes.Sum(m => m.vertexCount);
                rendering["triangles"] = uniqueMeshes.Sum(m => m.triangles.Length / 3);

                result["rendering"] = rendering;

                // Lighting statistics
                var lighting = new Dictionary<string, object>();
                var lights = allObjects
                    .SelectMany(go => go.GetComponents<Light>())
                    .Where(l => l != null && l.enabled)
                    .ToArray();

                lighting["directionalLights"] = lights.Count(l => l.type == LightType.Directional);
                lighting["pointLights"] = lights.Count(l => l.type == LightType.Point);
                lighting["spotLights"] = lights.Count(l => l.type == LightType.Spot);
                lighting["areaLights"] = lights.Count(l => l.type == LightType.Rectangle);
                lighting["realtimeLights"] = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime || l.lightmapBakeType == LightmapBakeType.Mixed);
                lighting["bakedLights"] = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Baked);

                result["lighting"] = lighting;

                // Memory info (if requested)
                if (includeMemoryInfo)
                {
                    var memoryInfo = new Dictionary<string, object>();
                    
                    // Get texture memory
                    var textureMemory = uniqueMaterials
                        .SelectMany(m => GetTexturesFromMaterial(m))
                        .Where(t => t != null)
                        .Distinct()
                        .Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));

                    // Get mesh memory
                    var meshMemory = uniqueMeshes
                        .Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));

                    memoryInfo["totalMemoryMB"] = (textureMemory + meshMemory) / (1024f * 1024f);
                    memoryInfo["textureMemoryMB"] = textureMemory / (1024f * 1024f);
                    memoryInfo["meshMemoryMB"] = meshMemory / (1024f * 1024f);

                    result["memoryInfo"] = memoryInfo;
                }

                // Generate summary
                var objectCount = allObjects.Length;
                var rendererCount = renderers.Length;
                var lightCount = lights.Length;
                
                string summary;
                if (objectCount == 0)
                {
                    summary = "Scene is empty";
                }
                else
                {
                    summary = $"Scene contains {objectCount} GameObject{(objectCount != 1 ? "s" : "")}";
                    if (rendererCount > 0)
                    {
                        summary += $" with {rendererCount} renderer{(rendererCount != 1 ? "s" : "")}";
                    }
                    if (lightCount > 0)
                    {
                        summary += $" and {lightCount} light{(lightCount != 1 ? "s" : "")}";
                    }
                }

                result["summary"] = summary;

                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to analyze scene: {ex.Message}" };
            }
        }

        /// <summary>
        /// Gets all property values of a specific component
        /// </summary>
        public static object GetComponentValues(JObject parameters)
        {
            try
            {
                // Get parameters
                var gameObjectName = parameters["gameObjectName"]?.ToString();
                var componentType = parameters["componentType"]?.ToString();
                var componentIndex = parameters["componentIndex"]?.ToObject<int>() ?? 0;
                var includePrivateFields = parameters["includePrivateFields"]?.ToObject<bool>() ?? false;
                var includeInherited = parameters["includeInherited"]?.ToObject<bool>() ?? true;

                // Validate input
                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return new { error = "gameObjectName is required" };
                }

                if (string.IsNullOrEmpty(componentType))
                {
                    return new { error = "componentType is required" };
                }

                if (componentIndex < 0)
                {
                    return new { error = "componentIndex must be non-negative" };
                }

                // Find the GameObject
                var targetObject = GameObject.Find(gameObjectName);
                if (targetObject == null)
                {
                    // Try to find in all objects including inactive
                    var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                    targetObject = allObjects.FirstOrDefault(go => go.name == gameObjectName);
                }

                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectName}" };
                }

                // Find the component
                Component targetComponent = null;
                var components = targetObject.GetComponents<Component>();
                var matchingComponents = new List<Component>();

                foreach (var component in components)
                {
                    if (component == null) continue;
                    if (component.GetType().Name == componentType)
                    {
                        matchingComponents.Add(component);
                    }
                }

                if (matchingComponents.Count == 0)
                {
                    return new { error = $"Component not found: {componentType} on GameObject \"{gameObjectName}\"" };
                }

                if (componentIndex >= matchingComponents.Count)
                {
                    return new { error = $"Component index {componentIndex} out of range. GameObject has {matchingComponents.Count} {componentType} component(s)" };
                }

                targetComponent = matchingComponents[componentIndex];

                // Build result
                var result = new Dictionary<string, object>();
                result["gameObject"] = gameObjectName;
                result["componentType"] = componentType;
                result["componentIndex"] = componentIndex;

                // Check if component can be enabled/disabled
                var behaviour = targetComponent as Behaviour;
                if (behaviour != null)
                {
                    result["enabled"] = behaviour.enabled;
                }
                else
                {
                    var renderer = targetComponent as Renderer;
                    if (renderer != null)
                    {
                        result["enabled"] = renderer.enabled;
                    }
                    else
                    {
                        result["enabled"] = true;
                    }
                }

                // Get properties
                var properties = new Dictionary<string, object>();
                var type = targetComponent.GetType();
                var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
                
                if (includePrivateFields)
                {
                    bindingFlags |= BindingFlags.NonPublic;
                }

                if (!includeInherited)
                {
                    bindingFlags |= BindingFlags.DeclaredOnly;
                }

                // Get properties
                var propertyInfos = type.GetProperties(bindingFlags);
                foreach (var prop in propertyInfos)
                {
                    try
                    {
                        if (prop.CanRead && !prop.GetIndexParameters().Any())
                        {
                            var value = prop.GetValue(targetComponent);
                            // Include null values as well
                            var propData = SerializePropertyValue(prop, value);
                            if (propData != null)
                            {
                                properties[prop.Name] = propData;
                            }
                        }
                    }
                    catch
                    {
                        // Skip properties that throw exceptions
                    }
                }

                // Get SerializeField attributes (always include these)
                var serializeFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => f.GetCustomAttribute<SerializeField>() != null && 
                           !f.Name.Contains("<") && !f.Name.Contains(">"));
                
                foreach (var field in serializeFields)
                {
                    try
                    {
                        var value = field.GetValue(targetComponent);
                        var fieldData = SerializeFieldValue(field, value);
                        if (fieldData != null)
                        {
                            properties["[SF]" + field.Name] = fieldData;
                        }
                    }
                    catch (Exception ex)
                    {
                        properties["[SF]" + field.Name] = new { error = ex.Message };
                    }
                }

                // Get public fields
                var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.Name.Contains("<") && !f.Name.Contains(">"));
                
                foreach (var field in publicFields)
                {
                    try
                    {
                        var value = field.GetValue(targetComponent);
                        var fieldData = SerializeFieldValue(field, value);
                        if (fieldData != null)
                        {
                            properties[field.Name] = fieldData;
                        }
                    }
                    catch (Exception ex)
                    {
                        properties[field.Name] = new { error = ex.Message };
                    }
                }

                // Get additional private fields if requested
                if (includePrivateFields)
                {
                    var privateFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(f => f.GetCustomAttribute<SerializeField>() == null &&
                               !f.Name.Contains("<") && !f.Name.Contains(">"));
                    
                    foreach (var field in privateFields)
                    {
                        try
                        {
                            var value = field.GetValue(targetComponent);
                            var fieldData = SerializeFieldValue(field, value);
                            if (fieldData != null)
                            {
                                properties["_" + field.Name] = fieldData;
                            }
                        }
                        catch
                        {
                            // Skip fields that throw exceptions
                        }
                    }
                }

                result["properties"] = properties;
                
                // Debug logging
                BridgeLogger.Log("SceneAnalysis", $"GetComponentValues - Properties count: {properties.Count}");
                if (properties.Count > 0)
                {
                    var firstKey = properties.Keys.First();
                    BridgeLogger.Log("SceneAnalysis", $"First property key: {firstKey}");
                    var firstValue = properties[firstKey];
                    BridgeLogger.Log("SceneAnalysis", $"First property value type: {firstValue?.GetType().Name ?? "null"}");
                }

                // Generate summary with detailed property names for debugging
                var propertyCount = properties.Count;
                var propertyNames = string.Join(", ", properties.Keys.Take(5));
                if (properties.Count > 5)
                {
                    propertyNames += $"... and {properties.Count - 5} more";
                }
                
                var summary = $"{componentType} component on \"{gameObjectName}\"";
                if (matchingComponents.Count > 1)
                {
                    summary += $" (index {componentIndex})";
                }
                summary += $" - {propertyCount} propert{(propertyCount != 1 ? "ies" : "y")}";
                
                if (propertyCount > 0)
                {
                    summary += $": {propertyNames}";
                }
                
                result["summary"] = summary;
                result["propertyCount"] = propertyCount;
                result["hasProperties"] = propertyCount > 0;

                // Add debug info
                result["debug"] = new Dictionary<string, object>
                {
                    ["propertiesType"] = properties.GetType().Name,
                    ["propertiesCount"] = properties.Count,
                    ["firstPropertyKey"] = properties.Count > 0 ? properties.Keys.First() : null
                };

                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to get component values: {ex.Message}" };
            }
        }

        /// <summary>
        /// Serializes a property value with type information
        /// </summary>
        private static Dictionary<string, object> SerializePropertyValue(PropertyInfo prop, object value)
        {
            var result = new Dictionary<string, object>();
            var propType = prop.PropertyType;

            try
            {
                // Serialize the value with circular reference protection
                result["value"] = SerializeValue(value);
                result["type"] = GetTypeName(propType);

                // Add range information for numeric types
                if (propType == typeof(float) || propType == typeof(int))
                {
                    var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
                    if (rangeAttr != null)
                    {
                        result["range"] = new { min = rangeAttr.min, max = rangeAttr.max };
                    }
                }

                // Add options for enum types
                if (propType.IsEnum)
                {
                    result["options"] = Enum.GetNames(propType);
                }

                // Add tooltip if available
                var tooltipAttr = prop.GetCustomAttribute<TooltipAttribute>();
                if (tooltipAttr != null)
                {
                    result["tooltip"] = tooltipAttr.tooltip;
                }
            }
            catch (Exception ex)
            {
                result["error"] = $"Failed to serialize: {ex.Message}";
                result["type"] = GetTypeName(propType);
            }

            return result;
        }

        /// <summary>
        /// Serializes a field value with type information
        /// </summary>
        private static Dictionary<string, object> SerializeFieldValue(FieldInfo field, object value)
        {
            var result = new Dictionary<string, object>();
            var fieldType = field.FieldType;

            try
            {
                // Serialize the value with circular reference protection
                result["value"] = SerializeValue(value);
                result["type"] = GetTypeName(fieldType);

                // Add range information for numeric types
                if (fieldType == typeof(float) || fieldType == typeof(int))
                {
                    var rangeAttr = field.GetCustomAttribute<RangeAttribute>();
                    if (rangeAttr != null)
                    {
                        result["range"] = new { min = rangeAttr.min, max = rangeAttr.max };
                    }
                }

                // Add options for enum types
                if (fieldType.IsEnum)
                {
                    result["options"] = Enum.GetNames(fieldType);
                }

                // Add tooltip if available
                var tooltipAttr = field.GetCustomAttribute<TooltipAttribute>();
                if (tooltipAttr != null)
                {
                    result["tooltip"] = tooltipAttr.tooltip;
                }

                // Mark if it's a SerializeField
                if (field.GetCustomAttribute<SerializeField>() != null)
                {
                    result["serialized"] = true;
                }

                // Mark if it's HideInInspector
                if (field.GetCustomAttribute<HideInInspector>() != null)
                {
                    result["hiddenInInspector"] = true;
                }
            }
            catch (Exception ex)
            {
                result["error"] = $"Failed to serialize: {ex.Message}";
                result["type"] = GetTypeName(fieldType);
            }

            return result;
        }

        /// <summary>
        /// Gets a friendly name for a type
        /// </summary>
        private static string GetTypeName(Type type)
        {
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector3)) return "Vector3";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Color)) return "Color";
            if (type == typeof(Quaternion)) return "Quaternion";
            if (type.IsEnum) return type.Name;
            if (type.IsArray) return GetTypeName(type.GetElementType()) + "[]";
            return type.Name;
        }

        /// <summary>
        /// Gets all textures from a material
        /// </summary>
        private static IEnumerable<Texture> GetTexturesFromMaterial(Material material)
        {
            if (material == null) yield break;

            var shader = material.shader;
            if (shader == null) yield break;

            var propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    var propertyName = ShaderUtil.GetPropertyName(shader, i);
                    var texture = material.GetTexture(propertyName);
                    if (texture != null)
                    {
                        yield return texture;
                    }
                }
            }
        }

        /// <summary>
        /// Finds all GameObjects that have a specific component type
        /// </summary>
        public static object FindByComponent(JObject parameters)
        {
            try
            {
                // Get parameters
                var componentType = parameters["componentType"]?.ToString();
                var includeInactive = parameters["includeInactive"]?.ToObject<bool>() ?? true;
                var searchScope = parameters["searchScope"]?.ToString() ?? "scene";
                var matchExactType = parameters["matchExactType"]?.ToObject<bool>() ?? true;

                // Validate input
                if (string.IsNullOrEmpty(componentType))
                {
                    return new { error = "componentType is required" };
                }

                // Validate searchScope
                if (searchScope != "scene" && searchScope != "prefabs" && searchScope != "all")
                {
                    return new { error = "Invalid searchScope. Must be one of: scene, prefabs, all" };
                }

                // Get the component type
                var targetType = GetTypeByName(componentType);
                if (targetType == null)
                {
                    return new { error = $"Invalid component type: {componentType}" };
                }

                if (!typeof(Component).IsAssignableFrom(targetType))
                {
                    return new { error = $"{componentType} is not a Component type" };
                }

                var results = new List<Dictionary<string, object>>();
                var sceneCount = 0;
                var prefabCount = 0;

                // Search in scene
                if (searchScope == "scene" || searchScope == "all")
                {
                    var sceneObjects = includeInactive
                        ? Resources.FindObjectsOfTypeAll<GameObject>()
                            .Where(go => go.scene.IsValid() && go.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                            .ToArray()
                        : GameObject.FindObjectsOfType<GameObject>();

                    foreach (var go in sceneObjects)
                    {
                        var components = matchExactType
                            ? go.GetComponents(targetType).Where(c => c != null && c.GetType() == targetType).ToArray()
                            : go.GetComponents(targetType);

                        if (components.Length > 0)
                        {
                            var result = new Dictionary<string, object>();
                            result["gameObject"] = go.name;
                            result["path"] = GetGameObjectPath(go);
                            result["componentCount"] = components.Length;
                            result["isActive"] = go.activeInHierarchy;

                            if (!matchExactType)
                            {
                                // Include actual component types found
                                result["componentTypes"] = components
                                    .Select(c => c.GetType().Name)
                                    .Distinct()
                                    .ToArray();
                            }

                            if (searchScope == "all")
                            {
                                result["location"] = "scene";
                            }

                            results.Add(result);
                            sceneCount++;
                        }
                    }
                }

                // Search in prefabs
                if (searchScope == "prefabs" || searchScope == "all")
                {
                    var guids = AssetDatabase.FindAssets("t:Prefab");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        
                        if (prefab == null) continue;

                        var components = matchExactType
                            ? prefab.GetComponentsInChildren(targetType, includeInactive)
                                .Where(c => c != null && c.GetType() == targetType)
                                .ToArray()
                            : prefab.GetComponentsInChildren(targetType, includeInactive);

                        if (components.Length > 0)
                        {
                            var result = new Dictionary<string, object>();
                            result["gameObject"] = prefab.name;
                            result["path"] = path;
                            result["componentCount"] = components.Length;
                            result["isActive"] = prefab.activeSelf;

                            if (!matchExactType)
                            {
                                // Include actual component types found
                                result["componentTypes"] = components
                                    .Select(c => c.GetType().Name)
                                    .Distinct()
                                    .ToArray();
                            }

                            if (searchScope == "all")
                            {
                                result["location"] = "prefab";
                            }

                            results.Add(result);
                            prefabCount++;
                        }
                    }
                }

                // Sort results by path
                results = results.OrderBy(r => r["path"].ToString()).ToList();

                // Count active objects
                var activeCount = results.Count(r => (bool)r["isActive"]);

                // Build result
                var finalResult = new Dictionary<string, object>();
                finalResult["componentType"] = componentType;
                finalResult["searchScope"] = searchScope;
                finalResult["results"] = results;
                finalResult["totalFound"] = results.Count;
                finalResult["activeCount"] = activeCount;

                if (searchScope == "all")
                {
                    finalResult["sceneCount"] = sceneCount;
                    finalResult["prefabCount"] = prefabCount;
                }

                // Generate summary
                string summary;
                if (results.Count == 0)
                {
                    summary = $"No GameObjects found with {componentType} component";
                }
                else
                {
                    var typeText = matchExactType ? componentType : $"{componentType}-derived";
                    
                    if (searchScope == "scene")
                    {
                        summary = $"Found {results.Count} GameObject{(results.Count != 1 ? "s" : "")} with {typeText} component";
                    }
                    else if (searchScope == "prefabs")
                    {
                        summary = $"Found {results.Count} prefab{(results.Count != 1 ? "s" : "")} with {typeText} component{(matchExactType ? "" : "s")}";
                    }
                    else // all
                    {
                        summary = $"Found {results.Count} GameObject{(results.Count != 1 ? "s" : "")} with {typeText} component ({sceneCount} in scene, {prefabCount} in prefabs)";
                    }

                    if (activeCount < results.Count)
                    {
                        summary += $" ({activeCount} active)";
                    }
                }

                finalResult["summary"] = summary;

                return finalResult;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to find GameObjects by component: {ex.Message}" };
            }
        }

        /// <summary>
        /// Gets a Type by its name
        /// </summary>
        private static Type GetTypeByName(string typeName)
        {
            // First try UnityEngine types
            var unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (unityType != null) return unityType;

            // Try UnityEngine.CoreModule
            unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule");
            if (unityType != null) return unityType;

            // Try without namespace
            var type = Type.GetType(typeName);
            if (type != null) return type;

            // Search all assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// Finds all references to and from a GameObject
        /// </summary>
        public static object GetObjectReferences(JObject parameters)
        {
            try
            {
                // Get parameters
                var gameObjectName = parameters["gameObjectName"]?.ToString();
                var includeAssetReferences = parameters["includeAssetReferences"]?.ToObject<bool>() ?? true;
                var includeHierarchyReferences = parameters["includeHierarchyReferences"]?.ToObject<bool>() ?? true;
                var searchInPrefabs = parameters["searchInPrefabs"]?.ToObject<bool>() ?? false;

                // Validate input
                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return new { error = "gameObjectName is required" };
                }

                // Find the target GameObject
                GameObject targetObject = GameObject.Find(gameObjectName);
                bool isPrefab = false;
                string targetPath = null;

                if (targetObject == null)
                {
                    // Try to find in all objects including inactive
                    var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                    targetObject = allObjects.FirstOrDefault(go => go.name == gameObjectName && go.scene.IsValid());
                }

                if (targetObject == null && searchInPrefabs)
                {
                    // Try to find as prefab
                    var guids = AssetDatabase.FindAssets($"t:Prefab {gameObjectName}");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null && prefab.name == gameObjectName)
                        {
                            targetObject = prefab;
                            isPrefab = true;
                            targetPath = path;
                            break;
                        }
                    }
                }

                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectName}" };
                }

                if (!isPrefab)
                {
                    targetPath = GetGameObjectPath(targetObject);
                }

                // Lists to store references
                var referencedBy = new List<Dictionary<string, object>>();
                var referencesTo = new List<Dictionary<string, object>>();
                var circularReferences = new HashSet<string>();
                
                // Statistics
                int searchedObjects = 0;
                int searchedPrefabs = 0;
                int assetReferences = 0;
                int prefabInstances = 0;

                // Get all components on target object for analysis
                var targetComponents = targetObject.GetComponents<Component>();

                // Search for references TO this object (who references it)
                var objectsToSearch = new List<GameObject>();
                
                // Add scene objects
                var sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>()
                    .Where(go => go.scene.IsValid() && go != targetObject)
                    .ToArray();
                objectsToSearch.AddRange(sceneObjects);

                // Search in scene objects
                foreach (var go in objectsToSearch)
                {
                    searchedObjects++;
                    var components = go.GetComponents<Component>();
                    
                    foreach (var component in components)
                    {
                        if (component == null) continue;

                        var componentRefs = FindReferencesInComponent(component, targetObject, targetComponents);
                        foreach (var reference in componentRefs)
                        {
                            var refData = new Dictionary<string, object>();
                            refData["gameObject"] = go.name;
                            refData["path"] = GetGameObjectPath(go);
                            refData["component"] = component.GetType().Name;
                            refData["property"] = reference.propertyName;
                            refData["referenceType"] = reference.referenceType;
                            
                            referencedBy.Add(refData);
                            
                            // Check for circular reference
                            if (referencesTo.Any(r => r["gameObject"].ToString() == go.name))
                            {
                                circularReferences.Add(go.name);
                            }
                        }
                    }
                }

                // Search in prefabs if requested
                if (searchInPrefabs)
                {
                    var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                    foreach (var guid in prefabGuids)
                    {
                        searchedPrefabs++;
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        
                        if (prefab == null || prefab == targetObject) continue;

                        // Check if this is an instance of our target prefab
                        if (isPrefab && PrefabUtility.GetCorrespondingObjectFromSource(prefab) == targetObject)
                        {
                            prefabInstances++;
                        }

                        var allPrefabObjects = prefab.GetComponentsInChildren<Transform>(true)
                            .Select(t => t.gameObject)
                            .ToArray();

                        foreach (var go in allPrefabObjects)
                        {
                            var components = go.GetComponents<Component>();
                            
                            foreach (var component in components)
                            {
                                if (component == null) continue;

                                var prefabRefs = FindReferencesInComponent(component, targetObject, targetComponents);
                                foreach (var reference in prefabRefs)
                                {
                                    var refData = new Dictionary<string, object>();
                                    refData["gameObject"] = prefab.name;
                                    refData["path"] = path;
                                    refData["component"] = component.GetType().Name;
                                    refData["property"] = reference.propertyName;
                                    refData["referenceType"] = reference.referenceType;
                                    refData["location"] = "prefab";
                                    
                                    referencedBy.Add(refData);
                                }
                            }
                        }
                    }
                }

                // Find references FROM this object (what it references)
                foreach (var component in targetComponents)
                {
                    if (component == null) continue;

                    var type = component.GetType();
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    foreach (var field in fields)
                    {
                        var fieldValue = field.GetValue(component);
                        if (fieldValue == null) continue;

                        // Check for GameObject references
                        if (fieldValue is GameObject referencedGO && referencedGO != targetObject)
                        {
                            var refData = new Dictionary<string, object>();
                            refData["gameObject"] = referencedGO.name;
                            refData["path"] = GetGameObjectPath(referencedGO);
                            refData["component"] = component.GetType().Name;
                            refData["property"] = field.Name;
                            refData["referenceType"] = "direct";
                            
                            referencesTo.Add(refData);
                            
                            // Check for circular reference
                            if (referencedBy.Any(r => r["gameObject"].ToString() == referencedGO.name))
                            {
                                circularReferences.Add(referencedGO.name);
                            }
                        }
                        // Check for Component references
                        else if (fieldValue is Component referencedComp && referencedComp.gameObject != targetObject)
                        {
                            var refData = new Dictionary<string, object>();
                            refData["gameObject"] = referencedComp.gameObject.name;
                            refData["path"] = GetGameObjectPath(referencedComp.gameObject);
                            refData["component"] = component.GetType().Name;
                            refData["property"] = field.Name;
                            refData["referenceType"] = referencedComp is Transform ? "transform" : "component";
                            
                            referencesTo.Add(refData);
                        }
                        // Check for asset references
                        else if (includeAssetReferences && fieldValue is UnityEngine.Object assetRef && 
                                !(assetRef is GameObject) && !(assetRef is Component))
                        {
                            var assetPath = AssetDatabase.GetAssetPath(assetRef);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                var refData = new Dictionary<string, object>();
                                refData["gameObject"] = assetRef.name;
                                refData["path"] = null;
                                refData["component"] = component.GetType().Name;
                                refData["property"] = field.Name;
                                refData["referenceType"] = "asset";
                                refData["assetPath"] = assetPath;
                                
                                referencesTo.Add(refData);
                                assetReferences++;
                            }
                        }
                    }
                }

                // Include hierarchy references if requested
                if (includeHierarchyReferences && !isPrefab)
                {
                    // Parent reference
                    if (targetObject.transform.parent != null)
                    {
                        var refData = new Dictionary<string, object>();
                        refData["gameObject"] = targetObject.transform.parent.name;
                        refData["path"] = GetGameObjectPath(targetObject.transform.parent.gameObject);
                        refData["component"] = "Transform";
                        refData["property"] = "parent";
                        refData["referenceType"] = "hierarchy";
                        
                        referencesTo.Add(refData);
                    }

                    // Child references
                    foreach (Transform child in targetObject.transform)
                    {
                        var refData = new Dictionary<string, object>();
                        refData["gameObject"] = child.name;
                        refData["path"] = GetGameObjectPath(child.gameObject);
                        refData["component"] = "Transform";
                        refData["property"] = "child";
                        refData["referenceType"] = "hierarchy";
                        
                        referencedBy.Add(refData);
                    }
                }

                // Build result
                var result = new Dictionary<string, object>();
                result["targetObject"] = gameObjectName;
                result["targetPath"] = targetPath;
                if (isPrefab)
                {
                    result["isPrefab"] = true;
                }

                var references = new Dictionary<string, object>();
                references["referencedBy"] = referencedBy;
                references["referencesTo"] = referencesTo;
                result["references"] = references;

                var stats = new Dictionary<string, object>();
                stats["totalReferencedBy"] = referencedBy.Count;
                stats["totalReferencesTo"] = referencesTo.Count;
                stats["componentCount"] = targetComponents.Length;
                stats["searchedObjects"] = searchedObjects;
                
                if (searchInPrefabs)
                {
                    stats["searchedPrefabs"] = searchedPrefabs;
                }
                
                if (isPrefab && prefabInstances > 0)
                {
                    stats["prefabInstances"] = prefabInstances;
                }
                
                if (assetReferences > 0)
                {
                    stats["assetReferences"] = assetReferences;
                }
                
                if (circularReferences.Count > 0)
                {
                    stats["circularReferences"] = circularReferences.ToArray();
                }
                
                result["stats"] = stats;

                // Generate summary
                string summary;
                if (referencedBy.Count == 0 && referencesTo.Count == 0)
                {
                    summary = $"{gameObjectName} has no references";
                }
                else
                {
                    var parts = new List<string>();
                    
                    if (referencedBy.Count > 0)
                    {
                        var byText = isPrefab && searchInPrefabs ? 
                            $"{referencedBy.Count} object{(referencedBy.Count != 1 ? "s" : "")}" :
                            $"{referencedBy.Count} object{(referencedBy.Count != 1 ? "s" : "")}";
                        parts.Add($"referenced by {byText}");
                    }
                    
                    if (referencesTo.Count > 0)
                    {
                        if (assetReferences > 0 && referencesTo.Count == assetReferences)
                        {
                            parts.Add($"references {assetReferences} asset{(assetReferences != 1 ? "s" : "")}");
                        }
                        else
                        {
                            parts.Add($"references {referencesTo.Count} object{(referencesTo.Count != 1 ? "s" : "")}");
                        }
                    }
                    
                    summary = $"{gameObjectName} is {string.Join(" and ", parts)}";
                    
                    if (isPrefab && prefabInstances > 0)
                    {
                        summary += $" ({prefabInstances} instance{(prefabInstances != 1 ? "s" : "")} in scenes)";
                    }
                    
                    if (circularReferences.Count > 0)
                    {
                        summary += " (circular reference detected)";
                    }
                }
                
                result["summary"] = summary;

                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to get object references: {ex.Message}" };
            }
        }

        /// <summary>
        /// Finds references to target object in a component
        /// </summary>
        private static List<(string propertyName, string referenceType)> FindReferencesInComponent(
            Component component, GameObject targetObject, Component[] targetComponents)
        {
            var references = new List<(string propertyName, string referenceType)>();
            
            var type = component.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            foreach (var field in fields)
            {
                try
                {
                    var fieldValue = field.GetValue(component);
                    if (fieldValue == null) continue;

                    // Direct GameObject reference
                    if (ReferenceEquals(fieldValue, targetObject))
                    {
                        references.Add((field.Name, "direct"));
                    }
                    // Component reference
                    else if (fieldValue is Component comp && targetComponents.Contains(comp))
                    {
                        references.Add((field.Name, comp is Transform ? "transform" : "component"));
                    }
                    // Array/List of GameObjects
                    else if (fieldValue is GameObject[] goArray)
                    {
                        if (goArray.Contains(targetObject))
                        {
                            references.Add((field.Name, "array"));
                        }
                    }
                    else if (fieldValue is List<GameObject> goList)
                    {
                        if (goList.Contains(targetObject))
                        {
                            references.Add((field.Name, "list"));
                        }
                    }
                    // Array/List of Components
                    else if (fieldValue is Component[] compArray)
                    {
                        if (compArray.Any(c => c != null && targetComponents.Contains(c)))
                        {
                            references.Add((field.Name, "array"));
                        }
                    }
                    else if (fieldValue is IList list && list.Count > 0 && list[0] is Component)
                    {
                        foreach (Component item in list)
                        {
                            if (item != null && targetComponents.Contains(item))
                            {
                                references.Add((field.Name, "list"));
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Skip fields that throw exceptions
                }
            }
            
            return references;
        }
    }
}