#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#pragma warning disable CS0234
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
#pragma warning restore CS0234

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Input Actions asset analysis and editing operations
    /// </summary>
    public static class InputActionsHandler
    {
        #region Analysis Functions

        /// <summary>
        /// Get the state and structure of an Input Actions asset
        /// </summary>
        public static object GetInputActionsState(JObject parameters)
        {
            try
            {
                var assetName = parameters["assetName"]?.ToString();
                var assetPath = parameters["assetPath"]?.ToString();
                var includeBindings = parameters["includeBindings"]?.ToObject<bool>() ?? true;
                var includeControlSchemes = parameters["includeControlSchemes"]?.ToObject<bool>() ?? true;
                var includeJsonStructure = parameters["includeJsonStructure"]?.ToObject<bool>() ?? false;

                // Find the Input Actions asset
                InputActionAsset asset = null;
                
                if (!string.IsNullOrEmpty(assetPath))
                {
                    asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                }
                else if (!string.IsNullOrEmpty(assetName))
                {
                    // Search for asset by name
                    var guids = AssetDatabase.FindAssets($"t:InputActionAsset {assetName}");
                    if (guids.Length > 0)
                    {
                        assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                        asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                    }
                }

                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found: {assetName ?? assetPath}" };
                }

                var result = new Dictionary<string, object>
                {
                    ["assetName"] = asset.name,
                    ["assetPath"] = assetPath,
                    ["actionMaps"] = GetActionMapsInfo(asset, includeBindings)
                };

                if (includeControlSchemes)
                {
                    result["controlSchemes"] = GetControlSchemesInfo(asset);
                }

                if (includeJsonStructure)
                {
                    // Load and parse the JSON directly
                    var jsonPath = assetPath;
                    if (File.Exists(jsonPath))
                    {
                        var jsonContent = File.ReadAllText(jsonPath);
                        result["jsonStructure"] = JObject.Parse(jsonContent);
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Analyze Input Actions asset in detail
        /// </summary>
        public static object AnalyzeInputActionsAsset(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var includeJsonStructure = parameters["includeJsonStructure"]?.ToObject<bool>() ?? true;
                var includeStatistics = parameters["includeStatistics"]?.ToObject<bool>() ?? true;

                if (string.IsNullOrEmpty(assetPath))
                {
                    return new { error = "assetPath is required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var result = new Dictionary<string, object>
                {
                    ["assetName"] = asset.name,
                    ["assetPath"] = assetPath,
                    ["actionMapCount"] = asset.actionMaps.Count,
                    ["actionMaps"] = GetDetailedActionMapsInfo(asset)
                };

                if (includeStatistics)
                {
                    var stats = new Dictionary<string, object>
                    {
                        ["totalActions"] = asset.actionMaps.Sum(m => m.actions.Count),
                        ["totalBindings"] = asset.actionMaps.Sum(m => m.bindings.Count),
                        ["totalControlSchemes"] = asset.controlSchemes.Count,
                        ["devicesUsed"] = GetUsedDevices(asset)
                    };
                    result["statistics"] = stats;
                }

                if (includeJsonStructure)
                {
                    if (File.Exists(assetPath))
                    {
                        var jsonContent = File.ReadAllText(assetPath);
                        result["jsonStructure"] = JObject.Parse(jsonContent);
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        #endregion

        #region Action Map Management

        /// <summary>
        /// Create a new Action Map
        /// </summary>
        public static object CreateActionMap(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actions = parameters["actions"]?.ToObject<List<JObject>>();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName))
                {
                    return new { error = "assetPath and mapName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                // Check if map already exists
                if (asset.FindActionMap(mapName) != null)
                {
                    return new { error = $"Action Map '{mapName}' already exists" };
                }

                // Create new action map
                var newMap = asset.AddActionMap(mapName);

                // Add actions if provided
                if (actions != null)
                {
                    foreach (var actionData in actions)
                    {
                        var actionName = actionData["name"]?.ToString();
                        var actionType = actionData["type"]?.ToString() ?? "Button";
                        
                        if (!string.IsNullOrEmpty(actionName))
                        {
                            var action = newMap.AddAction(actionName);
                            
                            // Set action type
                            if (Enum.TryParse<InputActionType>(actionType, out var type))
                            {
                                action.expectedControlType = GetExpectedControlType(type);
                            }
                        }
                    }
                }

                // Save the asset
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Created Action Map '{mapName}'",
                    actionCount = newMap.actions.Count
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Remove an Action Map
        /// </summary>
        public static object RemoveActionMap(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName))
                {
                    return new { error = "assetPath and mapName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                asset.RemoveActionMap(map);

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Removed Action Map '{mapName}'"
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        #endregion

        #region Action Management

        /// <summary>
        /// Add a new Action to an Action Map
        /// </summary>
        public static object AddInputAction(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();
                var actionType = parameters["actionType"]?.ToString() ?? "Button";

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                {
                    return new { error = "assetPath, mapName, and actionName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                // Check if action already exists
                if (map.FindAction(actionName) != null)
                {
                    return new { error = $"Action '{actionName}' already exists in map '{mapName}'" };
                }

                var action = map.AddAction(actionName);
                
                // Set action type
                if (Enum.TryParse<InputActionType>(actionType, out var type))
                {
                    action.expectedControlType = GetExpectedControlType(type);
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Added Action '{actionName}' to map '{mapName}'",
                    actionId = action.id.ToString()
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Remove an Action from an Action Map
        /// </summary>
        public static object RemoveInputAction(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                {
                    return new { error = "assetPath, mapName, and actionName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                var action = map.FindAction(actionName);
                if (action == null)
                {
                    return new { error = $"Action '{actionName}' not found in map '{mapName}'" };
                }

                // Remove action and its bindings
                // First remove all bindings for this action
                while (action.bindings.Count > 0)
                {
                    action.ChangeBinding(0).Erase();
                }
                
                // Note: There's no direct API to remove an action from a map in Unity Input System
                // The action still exists but has no bindings

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Removed Action '{actionName}' from map '{mapName}'"
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        #endregion

        #region Binding Management

        /// <summary>
        /// Add a new Binding to an Action
        /// </summary>
        public static object AddInputBinding(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();
                var path = parameters["path"]?.ToString();
                var groups = parameters["groups"]?.ToString();
                var interactions = parameters["interactions"]?.ToString();
                var processors = parameters["processors"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || 
                    string.IsNullOrEmpty(actionName) || string.IsNullOrEmpty(path))
                {
                    return new { error = "assetPath, mapName, actionName, and path are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                var action = map.FindAction(actionName);
                if (action == null)
                {
                    return new { error = $"Action '{actionName}' not found in map '{mapName}'" };
                }

                // Add binding
                action.AddBinding(path, interactions: interactions, processors: processors, groups: groups);

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Added binding '{path}' to action '{actionName}'",
                    bindingCount = action.bindings.Count
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Remove a Binding from an Action
        /// </summary>
        public static object RemoveInputBinding(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();
                var bindingIndex = parameters["bindingIndex"]?.ToObject<int>();
                var bindingPath = parameters["bindingPath"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                {
                    return new { error = "assetPath, mapName, and actionName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                var action = map.FindAction(actionName);
                if (action == null)
                {
                    return new { error = $"Action '{actionName}' not found in map '{mapName}'" };
                }

                // Find binding to remove
                InputBinding? bindingToRemove = null;
                int removeIndex = -1;

                if (bindingIndex.HasValue)
                {
                    if (bindingIndex.Value >= 0 && bindingIndex.Value < action.bindings.Count)
                    {
                        bindingToRemove = action.bindings[bindingIndex.Value];
                        removeIndex = bindingIndex.Value;
                    }
                }
                else if (!string.IsNullOrEmpty(bindingPath))
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (action.bindings[i].path == bindingPath)
                        {
                            bindingToRemove = action.bindings[i];
                            removeIndex = i;
                            break;
                        }
                    }
                }

                if (!bindingToRemove.HasValue || removeIndex == -1)
                {
                    return new { error = "Binding not found" };
                }

                // Use ChangeBinding to remove binding
                action.ChangeBinding(removeIndex).Erase();

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Removed binding from action '{actionName}'",
                    remainingBindings = action.bindings.Count
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Remove all bindings from an Action
        /// </summary>
        public static object RemoveAllBindings(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                {
                    return new { error = "assetPath, mapName, and actionName are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                var action = map.FindAction(actionName);
                if (action == null)
                {
                    return new { error = $"Action '{actionName}' not found in map '{mapName}'" };
                }

                var bindingCount = action.bindings.Count;
                
                // Remove all bindings using ChangeBinding
                while (action.bindings.Count > 0)
                {
                    action.ChangeBinding(0).Erase();
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Removed all {bindingCount} bindings from action '{actionName}'"
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        /// <summary>
        /// Create a composite binding (e.g., 2D Vector for WASD movement)
        /// </summary>
        public static object CreateCompositeBinding(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var mapName = parameters["mapName"]?.ToString();
                var actionName = parameters["actionName"]?.ToString();
                var compositeType = parameters["compositeType"]?.ToString() ?? "2DVector";
                var compositeName = parameters["name"]?.ToString() ?? compositeType;
                var bindings = parameters["bindings"]?.ToObject<JObject>();
                var groups = parameters["groups"]?.ToString();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(mapName) || 
                    string.IsNullOrEmpty(actionName) || bindings == null)
                {
                    return new { error = "assetPath, mapName, actionName, and bindings are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                var map = asset.FindActionMap(mapName);
                if (map == null)
                {
                    return new { error = $"Action Map '{mapName}' not found" };
                }

                var action = map.FindAction(actionName);
                if (action == null)
                {
                    return new { error = $"Action '{actionName}' not found in map '{mapName}'" };
                }

                // Add composite binding with parts using the With() method
                // CompositeSyntax.With() is the correct API for adding composite parts
                if (compositeType == "2DVector")
                {
                    var composite = action.AddCompositeBinding("2DVector");
                    
                    // Add each part using With(name, binding, groups, processors)
                    if (bindings["up"] != null)
                        composite = composite.With("up", bindings["up"].ToString(), groups);
                    if (bindings["down"] != null)
                        composite = composite.With("down", bindings["down"].ToString(), groups);
                    if (bindings["left"] != null)
                        composite = composite.With("left", bindings["left"].ToString(), groups);
                    if (bindings["right"] != null)
                        composite = composite.With("right", bindings["right"].ToString(), groups);
                }
                else if (compositeType == "1DAxis")
                {
                    var composite = action.AddCompositeBinding("1DAxis");
                    
                    // Add each part using With(name, binding, groups, processors)
                    if (bindings["negative"] != null)
                        composite = composite.With("negative", bindings["negative"].ToString(), groups);
                    if (bindings["positive"] != null)
                        composite = composite.With("positive", bindings["positive"].ToString(), groups);
                }
                else
                {
                    // For other composite types, just create the composite without parts
                    action.AddCompositeBinding(compositeType);
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new { 
                    success = true, 
                    message = $"Created composite binding '{compositeName}' for action '{actionName}'"
                };
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        #endregion

        #region Control Scheme Management

        /// <summary>
        /// Manage Control Schemes (add/remove/modify)
        /// </summary>
        public static object ManageControlSchemes(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var operation = parameters["operation"]?.ToString(); // "add", "remove", "modify"
                var schemeName = parameters["schemeName"]?.ToString();
                var devices = parameters["devices"]?.ToObject<List<string>>();

                if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(operation))
                {
                    return new { error = "assetPath and operation are required" };
                }

                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);
                if (asset == null)
                {
                    return new { error = $"Input Actions asset not found at path: {assetPath}" };
                }

                switch (operation.ToLower())
                {
                    case "add":
                        if (string.IsNullOrEmpty(schemeName) || devices == null || devices.Count == 0)
                        {
                            return new { error = "schemeName and devices are required for add operation" };
                        }

                        // Check if scheme already exists
                        if (asset.controlSchemes.Any(s => s.name == schemeName))
                        {
                            return new { error = $"Control Scheme '{schemeName}' already exists" };
                        }

                        // Create device requirements
                        var deviceRequirements = new List<InputControlScheme.DeviceRequirement>();
                        foreach (var device in devices)
                        {
                            deviceRequirements.Add(new InputControlScheme.DeviceRequirement
                            {
                                controlPath = $"<{device}>",
                                isOptional = false
                            });
                        }

                        // Add control scheme
                        asset.AddControlScheme(new InputControlScheme(schemeName, deviceRequirements.ToArray()));

                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();

                        return new { 
                            success = true, 
                            message = $"Added Control Scheme '{schemeName}'" 
                        };

                    case "remove":
                        if (string.IsNullOrEmpty(schemeName))
                        {
                            return new { error = "schemeName is required for remove operation" };
                        }

                        var schemeToRemove = asset.controlSchemes.FirstOrDefault(s => s.name == schemeName);
                        if (schemeToRemove == null)
                        {
                            return new { error = $"Control Scheme '{schemeName}' not found" };
                        }

                        asset.RemoveControlScheme(schemeName);

                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();

                        return new { 
                            success = true, 
                            message = $"Removed Control Scheme '{schemeName}'" 
                        };

                    default:
                        return new { error = $"Unknown operation: {operation}" };
                }
            }
            catch (Exception e)
            {
                return new { error = e.Message, stackTrace = e.StackTrace };
            }
        }

        #endregion

        #region Helper Methods

        private static List<Dictionary<string, object>> GetActionMapsInfo(InputActionAsset asset, bool includeBindings)
        {
            var maps = new List<Dictionary<string, object>>();

            foreach (var map in asset.actionMaps)
            {
                var mapInfo = new Dictionary<string, object>
                {
                    ["name"] = map.name,
                    ["id"] = map.id.ToString(),
                    ["actions"] = GetActionsInfo(map, includeBindings)
                };

                maps.Add(mapInfo);
            }

            return maps;
        }

        private static List<Dictionary<string, object>> GetDetailedActionMapsInfo(InputActionAsset asset)
        {
            var maps = new List<Dictionary<string, object>>();

            foreach (var map in asset.actionMaps)
            {
                var mapInfo = new Dictionary<string, object>
                {
                    ["name"] = map.name,
                    ["id"] = map.id.ToString(),
                    ["actionCount"] = map.actions.Count,
                    ["bindingCount"] = map.bindings.Count,
                    ["actions"] = GetDetailedActionsInfo(map)
                };

                maps.Add(mapInfo);
            }

            return maps;
        }

        private static List<Dictionary<string, object>> GetActionsInfo(InputActionMap map, bool includeBindings)
        {
            var actions = new List<Dictionary<string, object>>();

            foreach (var action in map.actions)
            {
                var actionInfo = new Dictionary<string, object>
                {
                    ["name"] = action.name,
                    ["id"] = action.id.ToString(),
                    ["type"] = action.type.ToString(),
                    ["expectedControlType"] = action.expectedControlType
                };

                if (includeBindings)
                {
                    actionInfo["bindings"] = GetBindingsInfo(action);
                }

                actions.Add(actionInfo);
            }

            return actions;
        }

        private static List<Dictionary<string, object>> GetDetailedActionsInfo(InputActionMap map)
        {
            var actions = new List<Dictionary<string, object>>();

            foreach (var action in map.actions)
            {
                var actionInfo = new Dictionary<string, object>
                {
                    ["name"] = action.name,
                    ["id"] = action.id.ToString(),
                    ["type"] = action.type.ToString(),
                    ["expectedControlType"] = action.expectedControlType,
                    ["bindingCount"] = action.bindings.Count,
                    ["bindings"] = GetDetailedBindingsInfo(action)
                };

                actions.Add(actionInfo);
            }

            return actions;
        }

        private static List<Dictionary<string, object>> GetBindingsInfo(InputAction action)
        {
            var bindings = new List<Dictionary<string, object>>();

            foreach (var binding in action.bindings)
            {
                var bindingInfo = new Dictionary<string, object>
                {
                    ["path"] = binding.path,
                    ["groups"] = binding.groups,
                    ["isComposite"] = binding.isComposite,
                    ["isPartOfComposite"] = binding.isPartOfComposite
                };

                if (binding.isComposite)
                {
                    bindingInfo["name"] = binding.name;
                }

                bindings.Add(bindingInfo);
            }

            return bindings;
        }

        private static List<Dictionary<string, object>> GetDetailedBindingsInfo(InputAction action)
        {
            var bindings = new List<Dictionary<string, object>>();

            foreach (var binding in action.bindings)
            {
                var bindingInfo = new Dictionary<string, object>
                {
                    ["id"] = binding.id.ToString(),
                    ["path"] = binding.path,
                    ["groups"] = binding.groups,
                    ["interactions"] = binding.interactions,
                    ["processors"] = binding.processors,
                    ["isComposite"] = binding.isComposite,
                    ["isPartOfComposite"] = binding.isPartOfComposite
                };

                if (binding.isComposite || binding.isPartOfComposite)
                {
                    bindingInfo["name"] = binding.name;
                }

                bindings.Add(bindingInfo);
            }

            return bindings;
        }

        private static List<Dictionary<string, object>> GetControlSchemesInfo(InputActionAsset asset)
        {
            var schemes = new List<Dictionary<string, object>>();

            foreach (var scheme in asset.controlSchemes)
            {
                var schemeInfo = new Dictionary<string, object>
                {
                    ["name"] = scheme.name,
                    ["bindingGroup"] = scheme.bindingGroup,
                    ["devices"] = GetDeviceRequirementsInfo(scheme.deviceRequirements)
                };

                schemes.Add(schemeInfo);
            }

            return schemes;
        }

        private static List<Dictionary<string, object>> GetDeviceRequirementsInfo(ReadOnlyArray<InputControlScheme.DeviceRequirement> requirements)
        {
            var devices = new List<Dictionary<string, object>>();

            foreach (var requirement in requirements)
            {
                devices.Add(new Dictionary<string, object>
                {
                    ["controlPath"] = requirement.controlPath,
                    ["isOptional"] = requirement.isOptional
                });
            }

            return devices;
        }

        private static List<string> GetUsedDevices(InputActionAsset asset)
        {
            var devices = new HashSet<string>();

            foreach (var map in asset.actionMaps)
            {
                foreach (var binding in map.bindings)
                {
                    if (!string.IsNullOrEmpty(binding.path))
                    {
                        // Extract device from path (e.g., "<Keyboard>/a" -> "Keyboard")
                        var match = System.Text.RegularExpressions.Regex.Match(binding.path, @"<(\w+)>/");
                        if (match.Success)
                        {
                            devices.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            return devices.ToList();
        }

        private static string GetExpectedControlType(InputActionType type)
        {
            switch (type)
            {
                case InputActionType.Value:
                    return "Vector2";
                case InputActionType.Button:
                    return "Button";
                case InputActionType.PassThrough:
                    return "";
                default:
                    return "";
            }
        }

        #endregion
    }
}
#else
namespace UnityCliBridge.Handlers
{
    public static class InputActionsHandler
    {
        public static object GetInputActionsState(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object AnalyzeInputActionsAsset(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object CreateActionMap(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object RemoveActionMap(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object AddInputAction(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object RemoveInputAction(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object AddInputBinding(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object RemoveInputBinding(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object RemoveAllBindings(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object CreateCompositeBinding(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }

        public static object ManageControlSchemes(Newtonsoft.Json.Linq.JObject parameters)
        {
            return new { error = "Input System package is not installed or enabled" };
        }
    }
}
#endif