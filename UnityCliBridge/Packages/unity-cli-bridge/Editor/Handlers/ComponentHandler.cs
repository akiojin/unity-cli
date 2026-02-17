using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles component-related operations on GameObjects
    /// </summary>
    public static class ComponentHandler
    {
        public static Func<bool> PlayModeDetector = () => Application.isPlaying;

        /// <summary>
        /// Adds a component to a GameObject
        /// </summary>
        public static object AddComponent(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                string componentType = parameters["componentType"]?.ToString();
                JObject properties = parameters["properties"] as JObject;

                // Validate parameters
                if (string.IsNullOrEmpty(gameObjectPath))
                {
                    return new { error = "gameObjectPath is required" };
                }

                if (string.IsNullOrEmpty(componentType))
                {
                    return new { error = "componentType is required" };
                }

                // Find GameObject
                GameObject targetObject = GameObjectHandler.FindGameObjectByPath(gameObjectPath);
                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectPath}" };
                }

                // Resolve component type
                Type type = ResolveComponentType(componentType);
                if (type == null)
                {
                    return new { error = $"Component type not found: {componentType}" };
                }

                // Check if component already exists (for unique components)
                if (targetObject.GetComponent(type) != null && IsUniqueComponent(type))
                {
                    return new { error = $"GameObject already has component: {componentType}" };
                }

                // Add the component
                Component newComponent = targetObject.AddComponent(type);
                if (newComponent == null)
                {
                    return new { error = $"Failed to add component: {componentType}" };
                }

                // Apply properties if provided
                var appliedProperties = new List<string>();
                if (properties != null && properties.HasValues)
                {
                    foreach (var prop in properties.Properties())
                    {
                        if (TrySetComponentProperty(newComponent, prop.Name, prop.Value, out var errorMessage))
                        {
                            appliedProperties.Add(prop.Name);
                        }
                        else
                        {
                            return new { error = errorMessage ?? $"Property not found: {prop.Name}" };
                        }
                    }
                }

                // Register undo
                Undo.RegisterCreatedObjectUndo(newComponent, $"Add {componentType}");

                return new
                {
                    success = true,
                    componentType = type.Name,
                    gameObjectPath = gameObjectPath,
                    message = $"Component {type.Name} added successfully",
                    appliedProperties = appliedProperties.ToArray()
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in AddComponent: {ex.Message}");
                return new { error = $"Failed to add component: {ex.Message}" };
            }
        }

        /// <summary>
        /// Removes a component from a GameObject
        /// </summary>
        public static object RemoveComponent(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                string componentType = parameters["componentType"]?.ToString();
                int componentIndex = parameters["componentIndex"]?.ToObject<int>() ?? 0;

                // Validate parameters
                if (string.IsNullOrEmpty(gameObjectPath))
                {
                    return new { error = "gameObjectPath is required" };
                }

                if (string.IsNullOrEmpty(componentType))
                {
                    return new { error = "componentType is required" };
                }

                // Find GameObject
                GameObject targetObject = GameObjectHandler.FindGameObjectByPath(gameObjectPath);
                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectPath}" };
                }

                // Resolve component type
                Type type = ResolveComponentType(componentType);
                if (type == null)
                {
                    return new { error = $"Component type not found: {componentType}" };
                }

                // Special handling for Transform
                if (type == typeof(Transform))
                {
                    return new { error = "Cannot remove Transform component" };
                }

                // Get all components of the type
                Component[] components = targetObject.GetComponents(type);
                if (components.Length == 0)
                {
                    return new
                    {
                        success = true,
                        removed = false,
                        componentType = type.Name,
                        message = $"Component {type.Name} not found on GameObject"
                    };
                }

                // Check component index
                if (componentIndex >= components.Length)
                {
                    return new { error = $"Component index {componentIndex} out of range (found {components.Length} components)" };
                }

                // Remove the component
                Component componentToRemove = components[componentIndex];
                Undo.DestroyObjectImmediate(componentToRemove);

                return new
                {
                    success = true,
                    removed = true,
                    componentType = type.Name,
                    componentIndex = componentIndex,
                    message = $"Component {type.Name}[{componentIndex}] removed successfully"
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in RemoveComponent: {ex.Message}");
                return new { error = $"Failed to remove component: {ex.Message}" };
            }
        }

        /// <summary>
        /// Modifies properties of an existing component
        /// </summary>
        public static object ModifyComponent(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                string componentType = parameters["componentType"]?.ToString();
                int componentIndex = parameters["componentIndex"]?.ToObject<int>() ?? 0;
                JObject properties = parameters["properties"] as JObject;

                // Validate parameters
                if (string.IsNullOrEmpty(gameObjectPath))
                {
                    return new { error = "gameObjectPath is required" };
                }

                if (string.IsNullOrEmpty(componentType))
                {
                    return new { error = "componentType is required" };
                }

                if (properties == null || !properties.HasValues)
                {
                    return new { error = "properties is required and cannot be empty" };
                }

                // Find GameObject
                GameObject targetObject = GameObjectHandler.FindGameObjectByPath(gameObjectPath);
                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectPath}" };
                }

                // Resolve component type
                Type type = ResolveComponentType(componentType);
                if (type == null)
                {
                    return new { error = $"Component type not found: {componentType}" };
                }

                // Get component
                Component[] components = targetObject.GetComponents(type);
                if (components.Length == 0)
                {
                    return new { error = $"Component {type.Name} not found on GameObject" };
                }

                if (componentIndex >= components.Length)
                {
                    return new { error = $"Component index {componentIndex} out of range" };
                }

                Component component = components[componentIndex];

                // Record undo
                Undo.RecordObject(component, $"Modify {type.Name}");

                // Apply properties
                var modifiedProperties = new List<string>();
                foreach (var prop in properties.Properties())
                {
                    if (TrySetComponentProperty(component, prop.Name, prop.Value, out var errorMessage))
                    {
                        modifiedProperties.Add(prop.Name);
                    }
                    else
                    {
                        return new { error = errorMessage ?? $"Property not found: {prop.Name}" };
                    }
                }

                if (modifiedProperties.Count > 0)
                {
                    EditorUtility.SetDirty(component);
                }

                return new
                {
                    success = true,
                    componentType = type.Name,
                    componentIndex = componentIndex,
                    modifiedProperties = modifiedProperties.ToArray(),
                    message = $"Component {type.Name} properties updated"
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in ModifyComponent: {ex.Message}");
                return new { error = $"Failed to modify component: {ex.Message}" };
            }
        }

        /// <summary>
        /// Sets a serialized field value on a component (scene, prefab stage, or prefab asset)
        /// </summary>
        public static object SetComponentField(JObject parameters)
        {
            if (parameters == null)
            {
                return new { error = "Parameters are required" };
            }

            try
            {
                string componentTypeName = parameters["componentType"]?.ToString();
                string fieldPath = parameters["fieldPath"]?.ToString();
                if (string.IsNullOrEmpty(componentTypeName))
                {
                    return new { error = "componentType is required" };
                }

                if (string.IsNullOrEmpty(fieldPath))
                {
                    return new { error = "fieldPath is required" };
                }

                string scopeRaw = parameters["scope"]?.ToString();
                string scope = string.IsNullOrEmpty(scopeRaw) ? "auto" : scopeRaw.Trim();
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                string prefabAssetPath = parameters["prefabAssetPath"]?.ToString();
                string prefabObjectPath = parameters["prefabObjectPath"]?.ToString();
                string serializedPropertyPath = parameters["serializedPropertyPath"]?.ToString();
                string valueType = parameters["valueType"]?.ToString();
                string enumValue = parameters["enumValue"]?.ToString();
                int componentIndex = parameters["componentIndex"]?.ToObject<int>() ?? 0;
                bool dryRun = parameters["dryRun"]?.ToObject<bool>() ?? false;
                bool applyPrefabChanges = parameters["applyPrefabChanges"]?.ToObject<bool>() ?? true;
                bool createUndo = parameters["createUndo"]?.ToObject<bool>() ?? true;
                bool markSceneDirty = parameters["markSceneDirty"]?.ToObject<bool>() ?? true;
                bool runtime = parameters["runtime"]?.ToObject<bool>() ?? false;
                bool isInPlayMode = PlayModeDetector?.Invoke() ?? Application.isPlaying;
                JObject objectReferenceData = parameters["objectReference"] as JObject;
                bool hasValue = parameters.ContainsKey("value");
                JToken valueToken = hasValue ? parameters["value"] : null;

                if (componentIndex < 0)
                {
                    return new { error = "componentIndex must be non-negative" };
                }

                string normalizedScope = scope.ToLowerInvariant();
                string resolvedScope = normalizedScope;
                if (normalizedScope == "auto")
                {
                    resolvedScope = !string.IsNullOrEmpty(prefabAssetPath) ? "prefabasset" : "scene";
                }

                if (resolvedScope == "prefabasset" && string.IsNullOrEmpty(prefabAssetPath))
                {
                    return new { error = "prefabAssetPath is required when scope is prefabAsset" };
                }

                if (resolvedScope != "prefabasset" && string.IsNullOrEmpty(gameObjectPath))
                {
                    return new { error = "gameObjectPath is required when editing scene or prefab stage objects" };
                }

                if (!dryRun && !hasValue && (string.IsNullOrEmpty(valueType) || !string.Equals(valueType, "null", StringComparison.OrdinalIgnoreCase)))
                {
                    return new { error = "value is required unless dryRun is true" };
                }

                if (resolvedScope == "prefabasset" && !dryRun && !applyPrefabChanges)
                {
                    return new { error = "applyPrefabChanges must be true when modifying prefab assets (use dryRun for validation without saving)." };
                }

                if (isInPlayMode && !runtime && !dryRun)
                {
                    return new { error = "SetComponentField is blocked during Play Mode without runtime:true", code = "PLAY_MODE_BLOCKED" };
                }

                Type componentType = ResolveComponentType(componentTypeName);
                if (componentType == null)
                {
                    return new { error = $"Component type not found: {componentTypeName}" };
                }

                GameObject prefabRoot = null;
                GameObject targetObject = null;
                string finalScope = resolvedScope == "prefabasset" ? "prefabAsset" : resolvedScope;
                string resolvedGameObjectPath = gameObjectPath;
                bool prefabLoaded = false;

                try
                {
                    if (finalScope == "prefabAsset")
                    {
                        prefabRoot = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                        if (prefabRoot == null)
                        {
                            return new { error = $"Failed to load prefab asset: {prefabAssetPath}" };
                        }
                        prefabLoaded = true;

                        targetObject = ResolvePrefabObject(prefabRoot, prefabObjectPath);
                        if (targetObject == null)
                        {
                            return new { error = $"GameObject not found in prefab asset: {prefabObjectPath ?? prefabRoot.name}" };
                        }

                        resolvedGameObjectPath = BuildRelativePath(prefabRoot, targetObject);
                    }
                    else
                    {
                        targetObject = GameObjectHandler.FindGameObjectByPath(gameObjectPath);
                        if (targetObject == null)
                        {
                            return new { error = $"GameObject not found: {gameObjectPath}" };
                        }

                        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null && prefabStage.scene == targetObject.scene)
                        {
                            finalScope = "prefabStage";
                        }
                        else
                        {
                            finalScope = "scene";
                        }

                        resolvedGameObjectPath = GameObjectHandler.GetGameObjectPath(targetObject);
                    }

                    if (isInPlayMode && runtime && finalScope == "prefabAsset" && !dryRun)
                    {
                        return new { error = "Prefab asset edits are blocked during Play Mode" };
                    }

                    Component[] components = targetObject.GetComponents(componentType);
                    if (components.Length == 0)
                    {
                        return new { error = $"Component {componentType.Name} not found on GameObject" };
                    }

                    if (componentIndex >= components.Length)
                    {
                        return new { error = $"Component index {componentIndex} out of range (found {components.Length})" };
                    }

                    Component targetComponent = components[componentIndex];
                    var serializedObject = new SerializedObject(targetComponent);
                    serializedObject.Update();

                    string propertyPath = !string.IsNullOrEmpty(serializedPropertyPath)
                        ? serializedPropertyPath
                        : NormalizeSerializedPropertyPath(fieldPath);

                    SerializedProperty property = serializedObject.FindProperty(propertyPath);
                    if (property == null && propertyPath != fieldPath)
                    {
                        property = serializedObject.FindProperty(fieldPath);
                    }

                    if (property == null)
                    {
                        return new { error = $"Serialized property not found: {fieldPath}" };
                    }

                    var notes = new List<string>();
                    var previousValue = SerializePropertyValue(property);

                    var fieldResolution = ResolveFieldType(targetComponent.GetType(), fieldPath);
                    Type expectedValueType = fieldResolution?.FieldType;
                    if (property.isArray && fieldResolution?.ElementType != null)
                    {
                        expectedValueType = fieldResolution.ElementType;
                    }

                    object preparedValue;
                    UnityEngine.Object preparedReference;
                    int? preparedEnumIndex;
                    string prepareError;
                    var valueTypeHint = string.IsNullOrEmpty(valueType) ? "auto" : valueType;

                    if (!PrepareValue(property, expectedValueType, valueToken, valueTypeHint, enumValue, objectReferenceData, out preparedValue, out preparedReference, out preparedEnumIndex, out prepareError, notes))
                    {
                        return new { error = prepareError ?? $"Unable to prepare value for {fieldPath}" };
                    }

                    if (isInPlayMode && runtime)
                    {
                        notes.Add("Applied during Play Mode (runtime); values revert when exiting Play Mode.");
                    }

                    if (dryRun)
                    {
                        return new
                        {
                            success = true,
                            dryRun = true,
                            scope = finalScope,
                            componentType = componentType.Name,
                            componentIndex,
                            fieldPath,
                            serializedPropertyPath = property.propertyPath,
                            gameObjectPath = resolvedGameObjectPath,
                            prefabAssetPath,
                            previousValue,
                            previewValue = ConvertPreparedValue(property, preparedValue, preparedReference, preparedEnumIndex),
                            requiresSave = false,
                            notes = notes.Count > 0 ? notes.ToArray() : null
                        };
                    }

                    bool allowUndo = createUndo && !(isInPlayMode && runtime);
                    if (finalScope != "prefabAsset" && allowUndo)
                    {
                        Undo.RecordObject(targetComponent, $"Set {componentType.Name}.{fieldPath}");
                    }

                    string applyError;
                    if (!ApplyPreparedValue(property, preparedValue, preparedReference, preparedEnumIndex, out applyError))
                    {
                        return new { error = applyError ?? $"Failed to assign value to {fieldPath}" };
                    }

                    bool changed = serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();

                    var updatedProperty = serializedObject.FindProperty(property.propertyPath);
                    var appliedValue = SerializePropertyValue(updatedProperty);

                    if (!changed)
                    {
                        notes.Add("Serialized property already had the requested value");
                    }

                    if (finalScope != "prefabAsset")
                    {
                        if (!(isInPlayMode && runtime))
                        {
                            EditorUtility.SetDirty(targetComponent);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(targetComponent);
                        }
                        if (finalScope == "scene" && markSceneDirty && !isInPlayMode && targetComponent.gameObject.scene.IsValid())
                        {
                            EditorSceneManager.MarkSceneDirty(targetComponent.gameObject.scene);
                        }
                        else if (finalScope == "prefabStage")
                        {
                            notes.Add("Prefab stage changes will be saved when the stage is applied.");
                        }
                    }
                    else
                    {
                        if (!(isInPlayMode && runtime))
                        {
                            EditorUtility.SetDirty(targetComponent);
                        }
                    }

                    bool requiresSave = false;
                    if (finalScope == "prefabAsset")
                    {
                        if (applyPrefabChanges)
                        {
                            if (!PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabAssetPath, out bool saveSuccess) || !saveSuccess)
                            {
                                return new { error = $"Failed to save prefab asset: {prefabAssetPath}" };
                            }
                            notes.Add($"Prefab asset saved: {prefabAssetPath}");
                        }
                        requiresSave = !applyPrefabChanges;
                    }
                    else if (finalScope == "scene")
                    {
                        requiresSave = changed && markSceneDirty;
                    }
                    else if (finalScope == "prefabStage")
                    {
                        requiresSave = changed;
                    }

                    if (isInPlayMode && runtime)
                    {
                        requiresSave = false;
                    }

                    return new
                    {
                        success = true,
                        scope = finalScope,
                        componentType = componentType.Name,
                        componentIndex,
                        fieldPath,
                        serializedPropertyPath = updatedProperty.propertyPath,
                        gameObjectPath = resolvedGameObjectPath,
                        prefabAssetPath,
                        previousValue,
                        appliedValue,
                        requiresSave,
                        notes = notes.Count > 0 ? notes.ToArray() : null
                    };
                }
                finally
                {
                    if (prefabLoaded && prefabRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in SetComponentField: {ex}");
                return new { error = $"Failed to set component field: {ex.Message}" };
            }
        }

        /// <summary>
        /// Lists all components on a GameObject
        /// </summary>
        public static object ListComponents(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                bool includeProperties = parameters["includeProperties"]?.ToObject<bool>() ?? false;

                // Validate parameters
                if (string.IsNullOrEmpty(gameObjectPath))
                {
                    return new { error = "gameObjectPath is required" };
                }

                // Find GameObject
                GameObject targetObject = GameObjectHandler.FindGameObjectByPath(gameObjectPath);
                if (targetObject == null)
                {
                    return new { error = $"GameObject not found: {gameObjectPath}" };
                }

                // Get all components
                Component[] components = targetObject.GetComponents<Component>();
                var componentList = new List<object>();

                foreach (var component in components)
                {
                    if (component == null) continue;

                    var componentInfo = new Dictionary<string, object>
                    {
                        ["type"] = component.GetType().Name,
                        ["enabled"] = IsComponentEnabled(component)
                    };

                    // Include properties if requested
                    if (includeProperties)
                    {
                        var properties = GetComponentProperties(component);
                        if (properties.Count > 0)
                        {
                            componentInfo["properties"] = properties;
                        }
                    }

                    componentList.Add(componentInfo);
                }

                return new
                {
                    success = true,
                    gameObjectPath = gameObjectPath,
                    components = componentList,
                    componentCount = componentList.Count,
                    message = $"Found {componentList.Count} components"
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in ListComponents: {ex.Message}");
                return new { error = $"Failed to list components: {ex.Message}" };
            }
        }

        /// <summary>
        /// Gets available component types in the current Unity project.
        /// Intended for discovery/autocomplete in MCP clients.
        /// </summary>
        public static object GetComponentTypes(JObject parameters)
        {
            try
            {
                string categoryFilter = parameters["category"]?.ToString();
                string search = parameters["search"]?.ToString();
                bool onlyAddable = parameters["onlyAddable"]?.ToObject<bool>() ?? false;

                var types = TypeCache.GetTypesDerivedFrom<Component>();

                var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var componentTypes = new List<string>();

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (!type.IsClass || type.IsAbstract) continue;

                    if (onlyAddable)
                    {
                        // Unity disallows adding these built-in transform components via AddComponent.
                        if (type == typeof(Transform) || type == typeof(RectTransform))
                        {
                            continue;
                        }

                        // Exclude editor-only component-like types (best-effort).
                        if (!string.IsNullOrEmpty(type.Namespace) &&
                            type.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    var category = GetComponentTypeCategory(type);
                    categories.Add(category);

                    if (!string.IsNullOrEmpty(categoryFilter) &&
                        !string.Equals(category, categoryFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(search))
                    {
                        var name = type.Name ?? string.Empty;
                        var fullName = type.FullName ?? string.Empty;
                        if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                            fullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    componentTypes.Add(type.Name);
                }

                componentTypes.Sort(StringComparer.OrdinalIgnoreCase);

                return new
                {
                    componentTypes,
                    totalCount = componentTypes.Count,
                    categories = categories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray(),
                    searchTerm = string.IsNullOrEmpty(search) ? null : search,
                    onlyAddable = onlyAddable
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ComponentHandler", $"Error in GetComponentTypes: {ex.Message}");
                return new { error = $"Failed to get component types: {ex.Message}" };
            }
        }

        #region Helper Methods

        private static string GetComponentTypeCategory(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            var name = type.Name ?? string.Empty;

            if (ns.Contains(".UI") ||
                ns.Contains("UIElements") ||
                name.Contains("Canvas") ||
                name.Contains("RectTransform") ||
                name.Contains("EventSystem") ||
                name.Contains("Graphic") ||
                name.Contains("Button") ||
                name.Contains("Text"))
            {
                return "UI";
            }

            if (ns.Contains("Physics") ||
                name.Contains("Collider") ||
                name.Contains("Rigidbody") ||
                name.Contains("Joint") ||
                name.Contains("CharacterController"))
            {
                return "Physics";
            }

            if (ns.Contains("Rendering") ||
                name.Contains("Renderer") ||
                name.Contains("Camera") ||
                name.Contains("Light"))
            {
                return "Rendering";
            }

            return "Other";
        }

        /// <summary>
        /// Resolves a component type from string name
        /// </summary>
        public static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            // First try exact type name
            Type type = Type.GetType(typeName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
                return type;

            // Try with UnityEngine namespace
            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null && typeof(Component).IsAssignableFrom(type))
                return type;

            // Try with UnityEngine.UI namespace
            type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null && typeof(Component).IsAssignableFrom(type))
                return type;

            // Search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetTypes().FirstOrDefault(t =>
                    typeof(Component).IsAssignableFrom(t) &&
                    (string.Equals(t.FullName, typeName, StringComparison.Ordinal) ||
                     string.Equals(t.Name, typeName, StringComparison.Ordinal)));

                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>
        /// Checks if a component type allows only one instance per GameObject
        /// </summary>
        private static bool IsUniqueComponent(Type type)
        {
            // Most components can have multiple instances
            // These are the common unique ones:
            return type == typeof(Transform) ||
                   type == typeof(RectTransform) ||
                   type == typeof(Rigidbody) ||
                   type == typeof(Rigidbody2D) ||
                   type == typeof(Animator) ||
                   type == typeof(AudioListener);
        }

        /// <summary>
        /// Sets a property value on a component
        /// </summary>
        private static bool TrySetComponentProperty(Component component, string propertyName, JToken value, out string error)
        {
            error = null;

            try
            {
                Type type = component.GetType();

                // Try field first
                FieldInfo field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    object convertedValue = ConvertValue(value, field.FieldType);
                    field.SetValue(component, convertedValue);
                    return true;
                }

                // Try property
                PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    object convertedValue = ConvertValue(value, property.PropertyType);
                    property.SetValue(component, convertedValue);
                    return true;
                }

                // Handle nested properties (e.g., "constraints.freezePositionX")
                if (propertyName.Contains("."))
                {
                    return TrySetNestedProperty(component, propertyName, value, out error);
                }

                error = $"Property not found: {propertyName}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Invalid property value for '{propertyName}': {ex.Message}";
                McpLogger.LogWarning("ComponentHandler", $"Failed to set property {propertyName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets a nested property value
        /// </summary>
        private static bool TrySetNestedProperty(Component component, string propertyPath, JToken value, out string error)
        {
            error = null;
            string[] parts = propertyPath.Split('.');
            object current = component;
            Type currentType = component.GetType();

            // Navigate to the nested property
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var field = currentType.GetField(parts[i], BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    current = field.GetValue(current);
                    currentType = field.FieldType;
                    continue;
                }

                var prop = currentType.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    current = prop.GetValue(current);
                    currentType = prop.PropertyType;
                    continue;
                }

                error = $"Property not found: {propertyPath}";
                return false;
            }

            // Set the final property
            string finalProp = parts[parts.Length - 1];
            var finalField = currentType.GetField(finalProp, BindingFlags.Public | BindingFlags.Instance);
            if (finalField != null)
            {
                try
                {
                    object convertedValue = ConvertValue(value, finalField.FieldType);
                    finalField.SetValue(current, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Invalid property value for '{propertyPath}': {ex.Message}";
                    return false;
                }
            }

            var finalProperty = currentType.GetProperty(finalProp, BindingFlags.Public | BindingFlags.Instance);
            if (finalProperty != null && finalProperty.CanWrite)
            {
                try
                {
                    object convertedValue = ConvertValue(value, finalProperty.PropertyType);
                    finalProperty.SetValue(current, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Invalid property value for '{propertyPath}': {ex.Message}";
                    return false;
                }
            }

            error = $"Property not found: {propertyPath}";
            return false;
        }

        /// <summary>
        /// Converts a JSON value to the target type
        /// </summary>
        public static object ConvertValue(JToken value, Type targetType)
        {
            if (value == null || value.Type == JTokenType.Null)
                return null;

            // Handle Unity-specific types
            if (targetType == typeof(Vector3))
            {
                if (value.Type == JTokenType.Object)
                {
                    float x = value["x"]?.ToObject<float>() ?? 0f;
                    float y = value["y"]?.ToObject<float>() ?? 0f;
                    float z = value["z"]?.ToObject<float>() ?? 0f;
                    return new Vector3(x, y, z);
                }
            }
            else if (targetType == typeof(Vector2))
            {
                if (value.Type == JTokenType.Object)
                {
                    float x = value["x"]?.ToObject<float>() ?? 0f;
                    float y = value["y"]?.ToObject<float>() ?? 0f;
                    return new Vector2(x, y);
                }
            }
            else if (targetType == typeof(Vector4))
            {
                if (value.Type == JTokenType.Object)
                {
                    float x = value["x"]?.ToObject<float>() ?? 0f;
                    float y = value["y"]?.ToObject<float>() ?? 0f;
                    float z = value["z"]?.ToObject<float>() ?? 0f;
                    float w = value["w"]?.ToObject<float>() ?? 0f;
                    return new Vector4(x, y, z, w);
                }
            }
            else if (targetType == typeof(Vector2Int))
            {
                if (value.Type == JTokenType.Object)
                {
                    int x = value["x"]?.ToObject<int>() ?? 0;
                    int y = value["y"]?.ToObject<int>() ?? 0;
                    return new Vector2Int(x, y);
                }
            }
            else if (targetType == typeof(Vector3Int))
            {
                if (value.Type == JTokenType.Object)
                {
                    int x = value["x"]?.ToObject<int>() ?? 0;
                    int y = value["y"]?.ToObject<int>() ?? 0;
                    int z = value["z"]?.ToObject<int>() ?? 0;
                    return new Vector3Int(x, y, z);
                }
            }
            else if (targetType == typeof(Color))
            {
                if (value.Type == JTokenType.Object)
                {
                    float r = value["r"]?.ToObject<float>() ?? 0f;
                    float g = value["g"]?.ToObject<float>() ?? 0f;
                    float b = value["b"]?.ToObject<float>() ?? 0f;
                    float a = value["a"]?.ToObject<float>() ?? 1f;
                    return new Color(r, g, b, a);
                }
            }
            else if (targetType == typeof(Quaternion))
            {
                if (value.Type == JTokenType.Object)
                {
                    float x = value["x"]?.ToObject<float>() ?? 0f;
                    float y = value["y"]?.ToObject<float>() ?? 0f;
                    float z = value["z"]?.ToObject<float>() ?? 0f;
                    float w = value["w"]?.ToObject<float>() ?? 1f;
                    return new Quaternion(x, y, z, w);
                }
            }
            else if (targetType == typeof(Rect))
            {
                if (value.Type == JTokenType.Object)
                {
                    float x = value["x"]?.ToObject<float>() ?? 0f;
                    float y = value["y"]?.ToObject<float>() ?? 0f;
                    float width = value["width"]?.ToObject<float>() ?? 0f;
                    float height = value["height"]?.ToObject<float>() ?? 0f;
                    return new Rect(x, y, width, height);
                }
            }
            else if (targetType == typeof(RectInt))
            {
                if (value.Type == JTokenType.Object)
                {
                    int x = value["x"]?.ToObject<int>() ?? 0;
                    int y = value["y"]?.ToObject<int>() ?? 0;
                    int width = value["width"]?.ToObject<int>() ?? 0;
                    int height = value["height"]?.ToObject<int>() ?? 0;
                    return new RectInt(x, y, width, height);
                }
            }
            else if (targetType == typeof(Bounds))
            {
                if (value.Type == JTokenType.Object)
                {
                    var centerToken = value["center"] ?? value["position"] ?? value["min"];
                    var sizeToken = value["size"];
                    if (centerToken != null && sizeToken != null)
                    {
                        Vector3 center = (Vector3)ConvertValue(centerToken, typeof(Vector3));
                        Vector3 size = (Vector3)ConvertValue(sizeToken, typeof(Vector3));
                        return new Bounds(center, size);
                    }
                }
            }
            else if (targetType == typeof(BoundsInt))
            {
                if (value.Type == JTokenType.Object)
                {
                    var positionToken = value["position"] ?? value["min"];
                    var sizeToken = value["size"];
                    if (positionToken != null && sizeToken != null)
                    {
                        Vector3Int position = (Vector3Int)ConvertValue(positionToken, typeof(Vector3Int));
                        Vector3Int size = (Vector3Int)ConvertValue(sizeToken, typeof(Vector3Int));
                        return new BoundsInt(position, size);
                    }
                }
            }
            else if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, value.ToString(), true);
            }

            // Handle UnityEngine.Object-derived types (e.g., Material, Texture2D, Shader)
            else if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                // Expect an asset path string like "Assets/Materials/MyMat.mat"
                if (value.Type == JTokenType.String)
                {
                    string assetPath = value.ToString();
                    var obj = AssetDatabase.LoadAssetAtPath(assetPath, targetType);
                    return obj;
                }
            }

            // Use JSON.NET for other conversions
            try
            {
                return value.ToObject(targetType);
            }
            catch
            {
                // Fallback to basic conversion
                return Convert.ChangeType(value.ToString(), targetType);
            }
        }

        /// <summary>
        /// Checks if a component is enabled
        /// </summary>
        private static bool IsComponentEnabled(Component component)
        {
            // Handle Behaviour components (most Unity components)
            if (component is Behaviour behaviour)
                return behaviour.enabled;

            // Handle Renderer
            if (component is Renderer renderer)
                return renderer.enabled;

            // Handle Collider
            if (component is Collider collider)
                return collider.enabled;

            // Default to true for other components
            return true;
        }

        /// <summary>
        /// Gets properties of a component
        /// </summary>
        private static Dictionary<string, object> GetComponentProperties(Component component)
        {
            var properties = new Dictionary<string, object>();
            Type type = component.GetType();

            // Get common properties based on component type
            switch (component)
            {
                case Transform transform:
                    properties["position"] = new { x = transform.position.x, y = transform.position.y, z = transform.position.z };
                    properties["rotation"] = new { x = transform.eulerAngles.x, y = transform.eulerAngles.y, z = transform.eulerAngles.z };
                    properties["scale"] = new { x = transform.localScale.x, y = transform.localScale.y, z = transform.localScale.z };
                    break;

                case Rigidbody rb:
                    properties["mass"] = rb.mass;
                    properties["drag"] = rb.linearDamping;
                    properties["angularDrag"] = rb.angularDamping;
                    properties["useGravity"] = rb.useGravity;
                    properties["isKinematic"] = rb.isKinematic;
                    break;

                case BoxCollider box:
                    properties["isTrigger"] = box.isTrigger;
                    properties["center"] = new { x = box.center.x, y = box.center.y, z = box.center.z };
                    properties["size"] = new { x = box.size.x, y = box.size.y, z = box.size.z };
                    break;

                case Light light:
                    properties["type"] = light.type.ToString();
                    properties["color"] = new { r = light.color.r, g = light.color.g, b = light.color.b, a = light.color.a };
                    properties["intensity"] = light.intensity;
                    properties["range"] = light.range;
                    break;

                case Camera camera:
                    properties["fieldOfView"] = camera.fieldOfView;
                    properties["nearClipPlane"] = camera.nearClipPlane;
                    properties["farClipPlane"] = camera.farClipPlane;
                    properties["depth"] = camera.depth;
                    break;

                default:
                    // For other components, get first few public properties
                    var publicProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    int count = 0;
                    foreach (var prop in publicProperties.Where(p => p.CanRead).Take(10))
                    {
                        try
                        {
                            var value = prop.GetValue(component);
                            if (value != null && IsSerializableValue(value))
                            {
                                properties[prop.Name] = SerializeValue(value);
                                count++;
                                if (count >= 5) break; // Limit to 5 properties
                            }
                        }
                        catch { }
                    }
                    break;
            }

            return properties;
        }

        /// <summary>
        /// Checks if a value can be serialized
        /// </summary>
        private static bool IsSerializableValue(object value)
        {
            Type type = value.GetType();
            return type.IsPrimitive || 
                   type == typeof(string) || 
                   type == typeof(Vector3) || 
                   type == typeof(Vector2) ||
                   type == typeof(Color) ||
                   type == typeof(Quaternion);
        }

        /// <summary>
        /// Serializes a value for JSON
        /// </summary>
        private static object SerializeValue(object value)
        {
            if (value is Vector3 v3)
                return new { x = v3.x, y = v3.y, z = v3.z };
            if (value is Vector2 v2)
                return new { x = v2.x, y = v2.y };
            if (value is Color c)
                return new { r = c.r, g = c.g, b = c.b, a = c.a };
            if (value is Quaternion q)
                return new { x = q.x, y = q.y, z = q.z, w = q.w };
            return value;
        }

        private static GameObject ResolvePrefabObject(GameObject prefabRoot, string prefabObjectPath)
        {
            if (prefabRoot == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(prefabObjectPath))
            {
                return prefabRoot;
            }

            string trimmed = prefabObjectPath;
            if (trimmed.StartsWith("/"))
            {
                trimmed = trimmed.Substring(1);
            }

            if (string.IsNullOrEmpty(trimmed) || trimmed == prefabRoot.name)
            {
                return prefabRoot;
            }

            if (trimmed.StartsWith(prefabRoot.name + "/"))
            {
                trimmed = trimmed.Substring(prefabRoot.name.Length + 1);
            }

            Transform found = prefabRoot.transform.Find(trimmed);
            return found != null ? found.gameObject : null;
        }

        private static string BuildRelativePath(GameObject root, GameObject target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            Transform current = target.transform;
            while (current != null)
            {
                stack.Push(current.name);
                if (current.gameObject == root)
                {
                    break;
                }
                current = current.parent;
            }

            return "/" + string.Join("/", stack);
        }

        private static string NormalizeSerializedPropertyPath(string fieldPath)
        {
            if (string.IsNullOrEmpty(fieldPath))
            {
                return fieldPath;
            }

            var builder = new StringBuilder(fieldPath.Length * 2);
            int index = 0;
            while (index < fieldPath.Length)
            {
                char c = fieldPath[index];
                if (c == '[')
                {
                    index++;
                    var number = new StringBuilder();
                    while (index < fieldPath.Length && fieldPath[index] != ']')
                    {
                        number.Append(fieldPath[index]);
                        index++;
                    }
                    if (index < fieldPath.Length && fieldPath[index] == ']')
                    {
                        index++;
                    }
                    string idx = number.Length > 0 ? number.ToString() : "0";
                    builder.Append(".Array.data[");
                    builder.Append(idx);
                    builder.Append("]");
                }
                else
                {
                    builder.Append(c);
                    index++;
                }
            }

            return builder.ToString();
        }

        private class FieldPathSegment
        {
            public string Name;
            public bool IsArrayIndex;
            public int ArrayIndex;
        }

        private class FieldResolution
        {
            public Type FieldType;
            public FieldInfo LeafField;
            public Type ElementType;
        }

        private static List<FieldPathSegment> ParseFieldPath(string fieldPath)
        {
            var segments = new List<FieldPathSegment>();
            if (string.IsNullOrEmpty(fieldPath))
            {
                return segments;
            }

            int index = 0;
            while (index < fieldPath.Length)
            {
                char c = fieldPath[index];
                if (c == '.')
                {
                    index++;
                    continue;
                }

                if (c == '[')
                {
                    index++;
                    var number = new StringBuilder();
                    while (index < fieldPath.Length && fieldPath[index] != ']')
                    {
                        number.Append(fieldPath[index]);
                        index++;
                    }
                    if (index < fieldPath.Length && fieldPath[index] == ']')
                    {
                        index++;
                    }

                    int arrayIndex = 0;
                    int.TryParse(number.ToString(), out arrayIndex);
                    segments.Add(new FieldPathSegment { IsArrayIndex = true, ArrayIndex = arrayIndex });
                }
                else
                {
                    int start = index;
                    while (index < fieldPath.Length && fieldPath[index] != '.' && fieldPath[index] != '[')
                    {
                        index++;
                    }

                    string name = fieldPath.Substring(start, index - start);
                    if (!string.IsNullOrEmpty(name))
                    {
                        segments.Add(new FieldPathSegment { Name = name });
                    }
                }
            }

            return segments;
        }

        private static FieldResolution ResolveFieldType(Type componentType, string fieldPath)
        {
            if (componentType == null || string.IsNullOrEmpty(fieldPath))
            {
                return null;
            }

            var segments = ParseFieldPath(fieldPath);
            if (segments.Count == 0)
            {
                return null;
            }

            var resolution = new FieldResolution();
            Type currentType = componentType;

            foreach (var segment in segments)
            {
                if (segment.IsArrayIndex)
                {
                    if (currentType.IsArray)
                    {
                        currentType = currentType.GetElementType();
                        resolution.ElementType = currentType;
                    }
                    else if (IsListType(currentType))
                    {
                        currentType = currentType.GetGenericArguments()[0];
                        resolution.ElementType = currentType;
                    }
                    else
                    {
                        return null;
                    }

                    resolution.LeafField = null;
                    continue;
                }

                FieldInfo field = currentType.GetField(segment.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    resolution.LeafField = field;
                    currentType = field.FieldType;
                    continue;
                }

                PropertyInfo property = currentType.GetProperty(segment.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    resolution.LeafField = null;
                    currentType = property.PropertyType;
                    continue;
                }

                return null;
            }

            resolution.FieldType = currentType;
            return resolution;
        }

        private static bool IsListType(Type type)
        {
            if (type == null) return false;
            if (!type.IsGenericType) return false;
            return type.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static bool PrepareValue(
            SerializedProperty property,
            Type expectedType,
            JToken valueToken,
            string valueTypeHint,
            string enumValue,
            JObject objectReferenceData,
            out object preparedValue,
            out UnityEngine.Object preparedReference,
            out int? preparedEnumIndex,
            out string error,
            List<string> notes)
        {
            preparedValue = null;
            preparedReference = null;
            preparedEnumIndex = null;
            error = null;

            string hint = string.IsNullOrEmpty(valueTypeHint) ? "auto" : valueTypeHint.ToLowerInvariant();

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (valueToken == null)
                    {
                        error = "Boolean fields require a value";
                        return false;
                    }

                    if (valueToken.Type == JTokenType.Boolean)
                    {
                        preparedValue = valueToken.Value<bool>();
                        return true;
                    }

                    if (bool.TryParse(valueToken.ToString(), out bool boolValue))
                    {
                        preparedValue = boolValue;
                        return true;
                    }

                    error = $"Invalid boolean value: {valueToken}";
                    return false;

                case SerializedPropertyType.Integer:
                    if (expectedType != null && expectedType.IsEnum)
                    {
                        int enumIndex = !string.IsNullOrEmpty(enumValue)
                            ? FindEnumIndex(property, enumValue)
                            : (valueToken != null && valueToken.Type == JTokenType.Integer
                                ? valueToken.Value<int>()
                                : FindEnumIndex(property, valueToken?.ToString()));

                        if (enumIndex < 0)
                        {
                            error = $"Enum value not found: {enumValue ?? valueToken?.ToString()}";
                            return false;
                        }

                        preparedEnumIndex = enumIndex;
                        preparedValue = enumIndex;
                        return true;
                    }

                    if (valueToken == null)
                    {
                        error = "Integer fields require a value";
                        return false;
                    }

                    if (valueToken.Type == JTokenType.Integer)
                    {
                        preparedValue = valueToken.Value<long>();
                        return true;
                    }

                    if (valueToken.Type == JTokenType.Boolean)
                    {
                        preparedValue = valueToken.Value<bool>() ? 1L : 0L;
                        return true;
                    }

                    if (long.TryParse(valueToken.ToString(), out long longValue))
                    {
                        preparedValue = longValue;
                        return true;
                    }

                    error = $"Invalid integer value: {valueToken}";
                    return false;

                case SerializedPropertyType.Float:
                    if (valueToken == null)
                    {
                        error = "Float fields require a value";
                        return false;
                    }

                    if (valueToken.Type == JTokenType.Float || valueToken.Type == JTokenType.Integer)
                    {
                        preparedValue = valueToken.Value<double>();
                        return true;
                    }

                    if (double.TryParse(valueToken.ToString(), out double doubleValue))
                    {
                        preparedValue = doubleValue;
                        return true;
                    }

                    error = $"Invalid float value: {valueToken}";
                    return false;

                case SerializedPropertyType.String:
                    preparedValue = valueToken == null || valueToken.Type == JTokenType.Null ? string.Empty : valueToken.ToString();
                    return true;

                case SerializedPropertyType.Enum:
                    {
                        int enumIndex = !string.IsNullOrEmpty(enumValue)
                            ? FindEnumIndex(property, enumValue)
                            : (valueToken != null && valueToken.Type == JTokenType.Integer
                                ? valueToken.Value<int>()
                                : FindEnumIndex(property, valueToken?.ToString()));

                        if (enumIndex < 0)
                        {
                            error = $"Enum value not found: {enumValue ?? valueToken?.ToString()}";
                            return false;
                        }

                        preparedEnumIndex = enumIndex;
                        preparedValue = enumIndex;
                        return true;
                    }

                case SerializedPropertyType.ObjectReference:
                    {
                        Type targetType = expectedType ?? typeof(UnityEngine.Object);
                        string resolveError;
                        var reference = ResolveObjectReference(valueToken, objectReferenceData, targetType, out resolveError);
                        if (!string.IsNullOrEmpty(resolveError))
                        {
                            error = resolveError;
                            return false;
                        }
                        preparedReference = reference;
                        preparedValue = reference;
                        return true;
                    }

                case SerializedPropertyType.Color:
                    try { preparedValue = ConvertValue(valueToken, typeof(Color)); return true; }
                    catch (Exception convEx) { error = $"Invalid color value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Vector2:
                    try { preparedValue = ConvertValue(valueToken, typeof(Vector2)); return true; }
                    catch (Exception convEx) { error = $"Invalid Vector2 value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Vector3:
                    try { preparedValue = ConvertValue(valueToken, typeof(Vector3)); return true; }
                    catch (Exception convEx) { error = $"Invalid Vector3 value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Vector4:
                    try { preparedValue = ConvertValue(valueToken, typeof(Vector4)); return true; }
                    catch (Exception convEx) { error = $"Invalid Vector4 value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Vector2Int:
                    try { preparedValue = ConvertValue(valueToken, typeof(Vector2Int)); return true; }
                    catch (Exception convEx) { error = $"Invalid Vector2Int value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Vector3Int:
                    try { preparedValue = ConvertValue(valueToken, typeof(Vector3Int)); return true; }
                    catch (Exception convEx) { error = $"Invalid Vector3Int value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Quaternion:
                    try { preparedValue = ConvertValue(valueToken, typeof(Quaternion)); return true; }
                    catch (Exception convEx) { error = $"Invalid Quaternion value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Rect:
                    try { preparedValue = ConvertValue(valueToken, typeof(Rect)); return true; }
                    catch (Exception convEx) { error = $"Invalid Rect value: {convEx.Message}"; return false; }

                case SerializedPropertyType.RectInt:
                    try { preparedValue = ConvertValue(valueToken, typeof(RectInt)); return true; }
                    catch (Exception convEx) { error = $"Invalid RectInt value: {convEx.Message}"; return false; }

                case SerializedPropertyType.Bounds:
                    try { preparedValue = ConvertValue(valueToken, typeof(Bounds)); return true; }
                    catch (Exception convEx) { error = $"Invalid Bounds value: {convEx.Message}"; return false; }

                case SerializedPropertyType.BoundsInt:
                    try { preparedValue = ConvertValue(valueToken, typeof(BoundsInt)); return true; }
                    catch (Exception convEx) { error = $"Invalid BoundsInt value: {convEx.Message}"; return false; }

                case SerializedPropertyType.ManagedReference:
                    if (valueToken == null || valueToken.Type == JTokenType.Null)
                    {
                        preparedValue = null;
                        return true;
                    }
                    error = "Setting non-null managed reference values is not supported";
                    return false;

                case SerializedPropertyType.Generic:
                    if (property.isArray)
                    {
                        error = "Setting array properties directly is not supported. Target a specific element instead.";
                    }
                    else
                    {
                        error = "Setting generic serialized fields directly is not supported. Target a concrete child property.";
                    }
                    return false;

                default:
                    error = $"Unsupported SerializedPropertyType: {property.propertyType}";
                    return false;
            }
        }

        private static object ConvertPreparedValue(SerializedProperty property, object preparedValue, UnityEngine.Object preparedReference, int? preparedEnumIndex)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                    return preparedValue;
                case SerializedPropertyType.Enum:
                    if (preparedEnumIndex.HasValue && property.enumDisplayNames != null && property.enumDisplayNames.Length > 0)
                    {
                        int idx = Mathf.Clamp(preparedEnumIndex.Value, 0, property.enumDisplayNames.Length - 1);
                        return property.enumDisplayNames[idx];
                    }
                    return preparedValue;
                case SerializedPropertyType.Color:
                    if (preparedValue is Color color)
                    {
                        return new { r = color.r, g = color.g, b = color.b, a = color.a };
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    if (preparedValue is Vector2 v2) return new { x = v2.x, y = v2.y };
                    break;
                case SerializedPropertyType.Vector3:
                    if (preparedValue is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
                    break;
                case SerializedPropertyType.Vector4:
                    if (preparedValue is Vector4 v4) return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
                    break;
                case SerializedPropertyType.Vector2Int:
                    if (preparedValue is Vector2Int v2i) return new { x = v2i.x, y = v2i.y };
                    break;
                case SerializedPropertyType.Vector3Int:
                    if (preparedValue is Vector3Int v3i) return new { x = v3i.x, y = v3i.y, z = v3i.z };
                    break;
                case SerializedPropertyType.Quaternion:
                    if (preparedValue is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
                    break;
                case SerializedPropertyType.Rect:
                    if (preparedValue is Rect rect) return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
                    break;
                case SerializedPropertyType.RectInt:
                    if (preparedValue is RectInt rectInt) return new { x = rectInt.x, y = rectInt.y, width = rectInt.width, height = rectInt.height };
                    break;
                case SerializedPropertyType.Bounds:
                    if (preparedValue is Bounds bounds) return new
                    {
                        center = new { x = bounds.center.x, y = bounds.center.y, z = bounds.center.z },
                        size = new { x = bounds.size.x, y = bounds.size.y, z = bounds.size.z }
                    };
                    break;
                case SerializedPropertyType.BoundsInt:
                    if (preparedValue is BoundsInt boundsInt) return new
                    {
                        position = new { x = boundsInt.position.x, y = boundsInt.position.y, z = boundsInt.position.z },
                        size = new { x = boundsInt.size.x, y = boundsInt.size.y, z = boundsInt.size.z }
                    };
                    break;
                case SerializedPropertyType.ObjectReference:
                    return SummarizeObjectReference(preparedReference);
            }

            return preparedValue;
        }

        private static bool ApplyPreparedValue(SerializedProperty property, object preparedValue, UnityEngine.Object preparedReference, int? preparedEnumIndex, out string error)
        {
            error = null;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    property.boolValue = preparedValue is bool boolValue && boolValue;
                    return true;
                case SerializedPropertyType.Integer:
                    if (preparedValue is long longValue)
                    {
                        property.longValue = longValue;
                        property.intValue = (int)longValue;
                        return true;
                    }
                    if (preparedValue is int intValue)
                    {
                        property.intValue = intValue;
                        property.longValue = intValue;
                        return true;
                    }
                    error = "Invalid integer prepared value";
                    return false;
                case SerializedPropertyType.Float:
                    if (preparedValue is double doubleValue)
                    {
                        property.doubleValue = doubleValue;
                        property.floatValue = (float)doubleValue;
                        return true;
                    }
                    if (preparedValue is float floatValue)
                    {
                        property.floatValue = floatValue;
                        property.doubleValue = floatValue;
                        return true;
                    }
                    error = "Invalid float prepared value";
                    return false;
                case SerializedPropertyType.String:
                    property.stringValue = preparedValue as string ?? string.Empty;
                    return true;
                case SerializedPropertyType.Enum:
                    if (!preparedEnumIndex.HasValue)
                    {
                        error = "Enum index was not prepared";
                        return false;
                    }
                    property.enumValueIndex = preparedEnumIndex.Value;
                    property.intValue = preparedEnumIndex.Value;
                    return true;
                case SerializedPropertyType.Color:
                    if (preparedValue is Color color)
                    {
                        property.colorValue = color;
                        return true;
                    }
                    error = "Invalid color prepared value";
                    return false;
                case SerializedPropertyType.Vector2:
                    if (preparedValue is Vector2 v2)
                    {
                        property.vector2Value = v2;
                        return true;
                    }
                    error = "Invalid Vector2 prepared value";
                    return false;
                case SerializedPropertyType.Vector3:
                    if (preparedValue is Vector3 v3)
                    {
                        property.vector3Value = v3;
                        return true;
                    }
                    error = "Invalid Vector3 prepared value";
                    return false;
                case SerializedPropertyType.Vector4:
                    if (preparedValue is Vector4 v4)
                    {
                        property.vector4Value = v4;
                        return true;
                    }
                    error = "Invalid Vector4 prepared value";
                    return false;
                case SerializedPropertyType.Vector2Int:
                    if (preparedValue is Vector2Int v2i)
                    {
                        property.vector2IntValue = v2i;
                        return true;
                    }
                    error = "Invalid Vector2Int prepared value";
                    return false;
                case SerializedPropertyType.Vector3Int:
                    if (preparedValue is Vector3Int v3i)
                    {
                        property.vector3IntValue = v3i;
                        return true;
                    }
                    error = "Invalid Vector3Int prepared value";
                    return false;
                case SerializedPropertyType.Quaternion:
                    if (preparedValue is Quaternion quaternion)
                    {
                        property.quaternionValue = quaternion;
                        return true;
                    }
                    error = "Invalid Quaternion prepared value";
                    return false;
                case SerializedPropertyType.Rect:
                    if (preparedValue is Rect rect)
                    {
                        property.rectValue = rect;
                        return true;
                    }
                    error = "Invalid Rect prepared value";
                    return false;
                case SerializedPropertyType.RectInt:
                    if (preparedValue is RectInt rectInt)
                    {
                        property.rectIntValue = rectInt;
                        return true;
                    }
                    error = "Invalid RectInt prepared value";
                    return false;
                case SerializedPropertyType.Bounds:
                    if (preparedValue is Bounds bounds)
                    {
                        property.boundsValue = bounds;
                        return true;
                    }
                    error = "Invalid Bounds prepared value";
                    return false;
                case SerializedPropertyType.BoundsInt:
                    if (preparedValue is BoundsInt boundsInt)
                    {
                        property.boundsIntValue = boundsInt;
                        return true;
                    }
                    error = "Invalid BoundsInt prepared value";
                    return false;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = preparedReference;
                    return true;
                case SerializedPropertyType.ManagedReference:
                    property.managedReferenceValue = null;
                    return true;
            }

            error = $"Unsupported SerializedPropertyType: {property.propertyType}";
            return false;
        }

        private static object SerializePropertyValue(SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Integer:
                    return property.longValue;
                case SerializedPropertyType.Float:
                    return property.doubleValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Enum:
                    if (property.enumDisplayNames != null && property.enumDisplayNames.Length > 0)
                    {
                        int idx = Mathf.Clamp(property.enumValueIndex, 0, property.enumDisplayNames.Length - 1);
                        return property.enumDisplayNames[idx];
                    }
                    return property.enumValueIndex;
                case SerializedPropertyType.Color:
                    {
                        var color = property.colorValue;
                        return new { r = color.r, g = color.g, b = color.b, a = color.a };
                    }
                case SerializedPropertyType.Vector2:
                    {
                        var v2 = property.vector2Value;
                        return new { x = v2.x, y = v2.y };
                    }
                case SerializedPropertyType.Vector3:
                    {
                        var v3 = property.vector3Value;
                        return new { x = v3.x, y = v3.y, z = v3.z };
                    }
                case SerializedPropertyType.Vector4:
                    {
                        var v4 = property.vector4Value;
                        return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
                    }
                case SerializedPropertyType.Vector2Int:
                    {
                        var v2i = property.vector2IntValue;
                        return new { x = v2i.x, y = v2i.y };
                    }
                case SerializedPropertyType.Vector3Int:
                    {
                        var v3i = property.vector3IntValue;
                        return new { x = v3i.x, y = v3i.y, z = v3i.z };
                    }
                case SerializedPropertyType.Quaternion:
                    {
                        var q = property.quaternionValue;
                        return new { x = q.x, y = q.y, z = q.z, w = q.w };
                    }
                case SerializedPropertyType.Rect:
                    {
                        var rect = property.rectValue;
                        return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
                    }
                case SerializedPropertyType.RectInt:
                    {
                        var rectInt = property.rectIntValue;
                        return new { x = rectInt.x, y = rectInt.y, width = rectInt.width, height = rectInt.height };
                    }
                case SerializedPropertyType.Bounds:
                    {
                        var bounds = property.boundsValue;
                        return new
                        {
                            center = new { x = bounds.center.x, y = bounds.center.y, z = bounds.center.z },
                            size = new { x = bounds.size.x, y = bounds.size.y, z = bounds.size.z }
                        };
                    }
                case SerializedPropertyType.BoundsInt:
                    {
                        var boundsInt = property.boundsIntValue;
                        return new
                        {
                            position = new { x = boundsInt.position.x, y = boundsInt.position.y, z = boundsInt.position.z },
                            size = new { x = boundsInt.size.x, y = boundsInt.size.y, z = boundsInt.size.z }
                        };
                    }
                case SerializedPropertyType.ObjectReference:
                    return SummarizeObjectReference(property.objectReferenceValue);
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceValue != null ? property.managedReferenceFullTypename : null;
            }

            if (property.isArray)
            {
                var items = new List<object>();
                for (int i = 0; i < property.arraySize; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    items.Add(SerializePropertyValue(element));
                }
                return items;
            }

            return null;
        }

        private static object SummarizeObjectReference(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            return new
            {
                name = obj.name,
                type = obj.GetType().Name,
                assetPath = AssetDatabase.GetAssetPath(obj),
                instanceId = obj.GetInstanceID()
            };
        }

        private static UnityEngine.Object ResolveObjectReference(JToken valueToken, JObject objectReferenceData, Type expectedType, out string error)
        {
            error = null;
            string assetPath = objectReferenceData?["assetPath"]?.ToString();
            string guid = objectReferenceData?["guid"]?.ToString();

            if (valueToken != null && valueToken.Type == JTokenType.String)
            {
                assetPath ??= valueToken.ToString();
            }
            else if (valueToken != null && valueToken.Type == JTokenType.Null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(assetPath))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    error = $"Failed to resolve asset GUID: {guid}";
                    return null;
                }
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                if (objectReferenceData == null && (valueToken == null || valueToken.Type == JTokenType.Null))
                {
                    return null;
                }

                error = "Object reference requires assetPath or guid";
                return null;
            }

            UnityEngine.Object reference;
            if (expectedType != null && expectedType != typeof(UnityEngine.Object))
            {
                reference = AssetDatabase.LoadAssetAtPath(assetPath, expectedType);
                if (reference == null)
                {
                    reference = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                }
            }
            else
            {
                reference = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            }

            if (reference == null)
            {
                error = $"Asset not found at path: {assetPath}";
                return null;
            }

            if (expectedType != null && expectedType != typeof(UnityEngine.Object) && !expectedType.IsAssignableFrom(reference.GetType()))
            {
                error = $"Object reference type mismatch. Expected {expectedType.Name}, got {reference.GetType().Name}";
                return null;
            }

            return reference;
        }

        private static int FindEnumIndex(SerializedProperty property, string value)
        {
            if (property == null || string.IsNullOrEmpty(value))
            {
                return -1;
            }

            if (property.enumDisplayNames != null)
            {
                for (int i = 0; i < property.enumDisplayNames.Length; i++)
                {
                    if (string.Equals(property.enumDisplayNames[i], value, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            if (property.enumNames != null)
            {
                for (int i = 0; i < property.enumNames.Length; i++)
                {
                    if (string.Equals(property.enumNames[i], value, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        #endregion
    }
}
