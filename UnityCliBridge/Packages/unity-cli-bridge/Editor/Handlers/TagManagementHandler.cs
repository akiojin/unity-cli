using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
#if !UNITY_6000_0_OR_NEWER
using UnityEditorInternal;
#endif
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity tag management operations
    /// </summary>
    public static class TagManagementHandler
    {
        // Reserved Unity tags that cannot be removed
        private static readonly HashSet<string> RESERVED_TAGS = new HashSet<string>
        {
            "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"
        };

        /// <summary>
        /// Handle tag management operations (add, remove, get)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "get":
                        return GetTags();
                    case "add":
                        var tagNameToAdd = parameters["tagName"]?.ToString();
                        return AddTag(tagNameToAdd);
                    case "remove":
                        var tagNameToRemove = parameters["tagName"]?.ToString();
                        return RemoveTag(tagNameToRemove);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("TagManagementHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get all available tags in the project
        /// </summary>
        public static object GetTags()
        {
            try
            {
                var tags = GetAllTags();
                
                return new
                {
                    success = true,
                    action = "get",
                    tags = tags,
                    count = tags.Count
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("TagManagementHandler", $"Error getting tags: {e.Message}");
                return new { error = $"Failed to get tags: {e.Message}" };
            }
        }

        /// <summary>
        /// Add a new tag to the project
        /// </summary>
        public static object AddTag(string tagName)
        {
            try
            {
                if (string.IsNullOrEmpty(tagName))
                {
                    return new { error = "Tag name cannot be null or empty" };
                }

                // Validate tag name
                if (!IsValidTagName(tagName))
                {
                    return new { error = "Tag name contains invalid characters. Only letters, numbers, and underscores are allowed" };
                }

                // Check if tag already exists
                var currentTags = GetAllTags();
                if (currentTags.Contains(tagName))
                {
                    return new { error = $"Tag \"{tagName}\" already exists" };
                }

                // Check for reserved tag names
                if (RESERVED_TAGS.Contains(tagName))
                {
                    return new { error = $"Tag \"{tagName}\" is reserved and cannot be added" };
                }

                // Add the tag using SerializedObject approach
                var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var tagsProp = tagManager.FindProperty("tags");
                
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
                tagManager.ApplyModifiedProperties();

                // Force refresh of the tags
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();

                int tagsCount = GetAllTags().Count;

                return new
                {
                    success = true,
                    action = "add",
                    tagName = tagName,
                    message = $"Tag \"{tagName}\" added successfully",
                    tagsCount = tagsCount
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("TagManagementHandler", $"Error adding tag '{tagName}': {e.Message}");
                return new { error = $"Failed to add tag: {e.Message}" };
            }
        }

        /// <summary>
        /// Remove an existing tag from the project
        /// </summary>
        public static object RemoveTag(string tagName)
        {
            try
            {
                if (string.IsNullOrEmpty(tagName))
                {
                    return new { error = "Tag name cannot be null or empty" };
                }

                // Check for reserved tag names
                if (RESERVED_TAGS.Contains(tagName))
                {
                    return new { error = $"Cannot remove reserved tag \"{tagName}\"" };
                }

                // Check if tag exists
                var currentTags = GetAllTags();
                if (!currentTags.Contains(tagName))
                {
                    return new { error = $"Tag \"{tagName}\" does not exist" };
                }

                // Check if any GameObjects are using this tag
                var gameObjectsWithTag = GameObject.FindGameObjectsWithTag(tagName);
                if (gameObjectsWithTag.Length > 0)
                {
                    McpLogger.LogWarning("TagManagementHandler", $"Removing tag '{tagName}' while {gameObjectsWithTag.Length} GameObjects are still using it");
                }

                // Remove the tag using SerializedObject approach
                var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var tagsProp = tagManager.FindProperty("tags");
                
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
                    {
                        tagsProp.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
                tagManager.ApplyModifiedProperties();

                // Force refresh of the tags
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();

                int tagsCount = GetAllTags().Count;

                return new
                {
                    success = true,
                    action = "remove",
                    tagName = tagName,
                    message = $"Tag \"{tagName}\" removed successfully",
                    tagsCount = tagsCount,
                    gameObjectsAffected = gameObjectsWithTag.Length
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("TagManagementHandler", $"Error removing tag '{tagName}': {e.Message}");
                return new { error = $"Failed to remove tag: {e.Message}" };
            }
        }

        /// <summary>
        /// Validate if a tag name contains only valid characters
        /// </summary>
        private static bool IsValidTagName(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return false;

            // Unity tag names should only contain letters, numbers, and underscores
            foreach (char c in tagName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get tag usage statistics
        /// </summary>
        public static object GetTagUsage()
        {
            try
            {
                var tags = GetAllTags();
                
                var tagUsage = new Dictionary<string, int>();

                foreach (var tag in tags)
                {
                    var gameObjectsWithTag = GameObject.FindGameObjectsWithTag(tag);
                    tagUsage[tag] = gameObjectsWithTag.Length;
                }

                return new
                {
                    success = true,
                    tagUsage = tagUsage,
                    totalTags = tags.Count(),
                    totalUsages = tagUsage.Values.Sum()
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("TagManagementHandler", $"Error getting tag usage: {e.Message}");
                return new { error = $"Failed to get tag usage: {e.Message}" };
            }
        }

        /// <summary>
        /// Returns all defined tags in the project using TagManager asset.
        /// Works across Unity versions without relying on InternalEditorUtility.
        /// </summary>
        private static List<string> GetAllTags()
        {
            try
            {
                var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (tagManagerAsset != null && tagManagerAsset.Length > 0)
                {
                    var so = new SerializedObject(tagManagerAsset[0]);
                    var tagsProp = so.FindProperty("tags");
                    var list = new List<string>();
                    for (int i = 0; i < tagsProp.arraySize; i++)
                    {
                        var element = tagsProp.GetArrayElementAtIndex(i);
                        list.Add(element.stringValue);
                    }
                    return list;
                }

                #if !UNITY_6000_0_OR_NEWER
                // Fallback for older versions
                return InternalEditorUtility.tags.ToList();
                #else
                return new List<string>();
                #endif
            }
            catch
            {
                #if !UNITY_6000_0_OR_NEWER
                return InternalEditorUtility.tags.ToList();
                #else
                return new List<string>();
                #endif
            }
        }
    }
}
