using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity layer management operations
    /// </summary>
    public static class LayerManagementHandler
    {
        // Reserved Unity layers that cannot be removed
        private static readonly HashSet<string> RESERVED_LAYERS = new HashSet<string>
        {
            "Default", "TransparentFX", "Ignore Raycast", "Water", "UI"
        };

        /// <summary>
        /// Handle layer management operations (add, remove, get, get_by_name, get_by_index)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "get":
                        return GetLayers();
                    case "add":
                        var layerNameToAdd = parameters["layerName"]?.ToString();
                        return AddLayer(layerNameToAdd);
                    case "remove":
                        var layerNameToRemove = parameters["layerName"]?.ToString();
                        return RemoveLayer(layerNameToRemove);
                    case "get_by_name":
                        var layerNameToGet = parameters["layerName"]?.ToString();
                        return GetLayerByName(layerNameToGet);
                    case "get_by_index":
                        var layerIndexToGet = parameters["layerIndex"]?.ToObject<int>();
                        return GetLayerByIndex(layerIndexToGet ?? -1);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get all available layers with their indices
        /// </summary>
        public static object GetLayers()
        {
            try
            {
                var layers = new List<object>();
                
                // Unity has 32 possible layers (0-31)
                for (int i = 0; i < 32; i++)
                {
                    string layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        layers.Add(new
                        {
                            index = i,
                            name = layerName
                        });
                    }
                }
                
                return new
                {
                    success = true,
                    action = "get",
                    layers = layers,
                    count = layers.Count
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error getting layers: {e.Message}");
                return new { error = $"Failed to get layers: {e.Message}" };
            }
        }

        /// <summary>
        /// Add a new layer to the project
        /// </summary>
        public static object AddLayer(string layerName)
        {
            try
            {
                if (string.IsNullOrEmpty(layerName))
                {
                    return new { error = "Layer name cannot be null or empty" };
                }

                // Validate layer name
                if (!IsValidLayerName(layerName))
                {
                    return new { error = "Layer name contains invalid characters. Only letters, numbers, spaces, and underscores are allowed" };
                }

                // Check if layer already exists
                for (int i = 0; i < 32; i++)
                {
                    if (LayerMask.LayerToName(i) == layerName)
                    {
                        return new { error = $"Layer \"{layerName}\" already exists" };
                    }
                }

                // Check for reserved layer names
                if (RESERVED_LAYERS.Contains(layerName))
                {
                    return new { error = $"Layer \"{layerName}\" is reserved and cannot be added" };
                }

                // Find first available slot
                int availableIndex = -1;
                for (int i = 8; i < 32; i++) // Start from 8 as 0-7 are usually reserved/builtin
                {
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    {
                        availableIndex = i;
                        break;
                    }
                }

                if (availableIndex == -1)
                {
                    return new { error = "No available layer slots. All 32 layers are in use" };
                }

                // Add the layer
                SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
                SerializedProperty layers = tagManager.FindProperty("layers");
                
                if (availableIndex < layers.arraySize)
                {
                    SerializedProperty layerProperty = layers.GetArrayElementAtIndex(availableIndex);
                    layerProperty.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                }

                // Force refresh
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();

                return new
                {
                    success = true,
                    action = "add",
                    layerName = layerName,
                    layerIndex = availableIndex,
                    message = $"Layer \"{layerName}\" added successfully at index {availableIndex}",
                    layersCount = GetLayerCount()
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error adding layer '{layerName}': {e.Message}");
                return new { error = $"Failed to add layer: {e.Message}" };
            }
        }

        /// <summary>
        /// Remove an existing layer from the project
        /// </summary>
        public static object RemoveLayer(string layerName)
        {
            try
            {
                if (string.IsNullOrEmpty(layerName))
                {
                    return new { error = "Layer name cannot be null or empty" };
                }

                // Check for reserved layer names
                if (RESERVED_LAYERS.Contains(layerName))
                {
                    return new { error = $"Cannot remove reserved layer \"{layerName}\"" };
                }

                // Find the layer index
                int layerIndex = -1;
                for (int i = 0; i < 32; i++)
                {
                    if (LayerMask.LayerToName(i) == layerName)
                    {
                        layerIndex = i;
                        break;
                    }
                }

                if (layerIndex == -1)
                {
                    return new { error = $"Layer \"{layerName}\" does not exist" };
                }

                // Check if any GameObjects are using this layer
                var allGameObjects = GameObject.FindObjectsOfType<GameObject>();
                var gameObjectsWithLayer = allGameObjects.Where(go => go.layer == layerIndex).ToArray();
                
                if (gameObjectsWithLayer.Length > 0)
                {
                    McpLogger.LogWarning("LayerManagementHandler", $"Removing layer '{layerName}' while {gameObjectsWithLayer.Length} GameObjects are still using it");
                }

                // Remove the layer
                SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
                SerializedProperty layers = tagManager.FindProperty("layers");
                
                if (layerIndex < layers.arraySize)
                {
                    SerializedProperty layerProperty = layers.GetArrayElementAtIndex(layerIndex);
                    layerProperty.stringValue = "";
                    tagManager.ApplyModifiedProperties();
                }

                // Force refresh
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();

                return new
                {
                    success = true,
                    action = "remove",
                    layerName = layerName,
                    layerIndex = layerIndex,
                    message = $"Layer \"{layerName}\" removed successfully from index {layerIndex}",
                    layersCount = GetLayerCount(),
                    gameObjectsAffected = gameObjectsWithLayer.Length
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error removing layer '{layerName}': {e.Message}");
                return new { error = $"Failed to remove layer: {e.Message}" };
            }
        }

        /// <summary>
        /// Get layer index by name
        /// </summary>
        public static object GetLayerByName(string layerName)
        {
            try
            {
                if (string.IsNullOrEmpty(layerName))
                {
                    return new { error = "Layer name cannot be null or empty" };
                }

                int layerIndex = LayerMask.NameToLayer(layerName);
                
                if (layerIndex == -1)
                {
                    return new { error = $"Layer \"{layerName}\" does not exist" };
                }

                return new
                {
                    success = true,
                    action = "get_by_name",
                    layerName = layerName,
                    layerIndex = layerIndex,
                    message = $"Layer \"{layerName}\" is at index {layerIndex}"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error getting layer by name '{layerName}': {e.Message}");
                return new { error = $"Failed to get layer by name: {e.Message}" };
            }
        }

        /// <summary>
        /// Get layer name by index
        /// </summary>
        public static object GetLayerByIndex(int layerIndex)
        {
            try
            {
                if (layerIndex < 0 || layerIndex > 31)
                {
                    return new { error = "Layer index must be between 0 and 31" };
                }

                string layerName = LayerMask.LayerToName(layerIndex);
                
                if (string.IsNullOrEmpty(layerName))
                {
                    return new
                    {
                        success = true,
                        action = "get_by_index",
                        layerIndex = layerIndex,
                        layerName = (string)null,
                        message = $"Layer at index {layerIndex} is not assigned"
                    };
                }

                return new
                {
                    success = true,
                    action = "get_by_index",
                    layerIndex = layerIndex,
                    layerName = layerName,
                    message = $"Layer at index {layerIndex} is \"{layerName}\""
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("LayerManagementHandler", $"Error getting layer by index {layerIndex}: {e.Message}");
                return new { error = $"Failed to get layer by index: {e.Message}" };
            }
        }

        /// <summary>
        /// Validate if a layer name contains only valid characters
        /// </summary>
        private static bool IsValidLayerName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;

            // Unity layer names should only contain letters, numbers, spaces, and underscores
            foreach (char c in layerName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != ' ')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the count of defined layers
        /// </summary>
        private static int GetLayerCount()
        {
            int count = 0;
            for (int i = 0; i < 32; i++)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
