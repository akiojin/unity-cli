using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Asset Database operations
    /// </summary>
    public static class AssetDatabaseHandler
    {
        /// <summary>
        /// Handle asset database operations (find_assets, get_asset_info, create_folder, etc.)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "find_assets":
                        var filter = parameters["filter"]?.ToString();
                        var searchInFolders = parameters["searchInFolders"]?.ToObject<string[]>();
                        return FindAssets(filter, searchInFolders);
                    case "get_asset_info":
                        var assetPath = parameters["assetPath"]?.ToString();
                        return GetAssetInfo(assetPath);
                    case "create_folder":
                        var folderPath = parameters["folderPath"]?.ToString();
                        return CreateFolder(folderPath);
                    case "delete_asset":
                        var deleteAssetPath = parameters["assetPath"]?.ToString();
                        return DeleteAsset(deleteAssetPath);
                    case "move_asset":
                        var fromPath = parameters["fromPath"]?.ToString();
                        var toPath = parameters["toPath"]?.ToString();
                        return MoveAsset(fromPath, toPath);
                    case "copy_asset":
                        var copyFromPath = parameters["fromPath"]?.ToString();
                        var copyToPath = parameters["toPath"]?.ToString();
                        return CopyAsset(copyFromPath, copyToPath);
                    case "refresh":
                        return RefreshAssetDatabase();
                    case "save":
                        return SaveAssetDatabase();
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Find assets using AssetDatabase search filters
        /// </summary>
        private static object FindAssets(string filter, string[] searchInFolders)
        {
            try
            {
                if (string.IsNullOrEmpty(filter))
                {
                    return new { error = "Filter not specified" };
                }

                var guids = AssetDatabase.FindAssets(filter, searchInFolders);
                var assets = new List<object>();

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    
                    if (asset != null)
                    {
                        var fileInfo = new FileInfo(Path.Combine(Application.dataPath, "..", path));
                        
                        assets.Add(new
                        {
                            path = path,
                            name = asset.name,
                            type = asset.GetType().Name,
                            guid = guid,
                            size = fileInfo.Exists ? (int)(fileInfo.Length / 1024) : 0 // Size in KB
                        });
                    }
                }

                return new
                {
                    success = true,
                    action = "find_assets",
                    filter = filter,
                    searchInFolders = searchInFolders ?? new string[0],
                    assets = assets,
                    count = assets.Count
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error finding assets with filter '{filter}': {e.Message}");
                return new { error = $"Failed to find assets: {e.Message}" };
            }
        }

        /// <summary>
        /// Get detailed information about an asset
        /// </summary>
        private static object GetAssetInfo(string assetPath)
        {
            try
            {
                if (string.IsNullOrEmpty(assetPath))
                {
                    return new { error = "Asset path not specified" };
                }

                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null)
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                var fileInfo = new FileInfo(Path.Combine(Application.dataPath, "..", assetPath));
                var importer = AssetImporter.GetAtPath(assetPath);
                
                // Get dependencies
                var dependencies = AssetDatabase.GetDependencies(assetPath, false).Where(dep => dep != assetPath).ToArray();
                
                // Get import settings based on asset type
                var importSettings = new Dictionary<string, object>();
                if (importer is TextureImporter textureImporter)
                {
                    importSettings["textureType"] = textureImporter.textureType.ToString();
                    importSettings["maxTextureSize"] = textureImporter.maxTextureSize;
                    importSettings["filterMode"] = textureImporter.filterMode.ToString();
                }
                else if (importer is ModelImporter modelImporter)
                {
                    importSettings["scaleFactor"] = modelImporter.globalScale;
                    importSettings["animationType"] = modelImporter.animationType.ToString();
                }
                else if (importer is AudioImporter audioImporter)
                {
                    var settings = audioImporter.defaultSampleSettings;
                    importSettings["loadType"] = settings.loadType.ToString();
                    importSettings["compressionFormat"] = settings.compressionFormat.ToString();
                }

                var info = new Dictionary<string, object>
                {
                    ["name"] = asset.name,
                    ["type"] = asset.GetType().Name,
                    ["guid"] = guid,
                    ["size"] = fileInfo.Exists ? (int)(fileInfo.Length / 1024) : 0, // Size in KB
                    ["lastModified"] = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
                    ["importSettings"] = importSettings,
                    ["dependencies"] = dependencies,
                    ["isValid"] = asset != null
                };

                return new
                {
                    success = true,
                    action = "get_asset_info",
                    assetPath = assetPath,
                    info = info
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error getting asset info for '{assetPath}': {e.Message}");
                return new { error = $"Failed to get asset info: {e.Message}" };
            }
        }

        /// <summary>
        /// Create a new folder in the Asset Database
        /// </summary>
        private static object CreateFolder(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    return new { error = "Folder path not specified" };
                }

                if (AssetDatabase.IsValidFolder(folderPath))
                {
                    return new { error = $"Folder already exists: {folderPath}" };
                }

                // Extract parent folder and folder name
                var parentPath = Path.GetDirectoryName(folderPath).Replace('\\', '/');
                var folderName = Path.GetFileName(folderPath);

                if (!AssetDatabase.IsValidFolder(parentPath))
                {
                    return new { error = $"Parent folder does not exist: {parentPath}" };
                }

                var guid = AssetDatabase.CreateFolder(parentPath, folderName);

                return new
                {
                    success = true,
                    action = "create_folder",
                    folderPath = folderPath,
                    guid = guid,
                    message = $"Folder created: {folderPath}"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error creating folder '{folderPath}': {e.Message}");
                return new { error = $"Failed to create folder: {e.Message}" };
            }
        }

        /// <summary>
        /// Delete an asset from the Asset Database
        /// </summary>
        private static object DeleteAsset(string assetPath)
        {
            try
            {
                if (string.IsNullOrEmpty(assetPath))
                {
                    return new { error = "Asset path not specified" };
                }

                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath))
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    return new { error = $"Failed to delete asset: {assetPath}" };
                }

                return new
                {
                    success = true,
                    action = "delete_asset",
                    assetPath = assetPath,
                    message = $"Asset deleted: {assetPath}"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error deleting asset '{assetPath}': {e.Message}");
                return new { error = $"Failed to delete asset: {e.Message}" };
            }
        }

        /// <summary>
        /// Move an asset to a new location
        /// </summary>
        private static object MoveAsset(string fromPath, string toPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toPath))
                {
                    return new { error = "Source and destination paths must be specified" };
                }

                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fromPath))
                {
                    return new { error = $"Source asset not found: {fromPath}" };
                }

                var errorMessage = AssetDatabase.MoveAsset(fromPath, toPath);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    return new { error = $"Failed to move asset: {errorMessage}" };
                }

                return new
                {
                    success = true,
                    action = "move_asset",
                    fromPath = fromPath,
                    toPath = toPath,
                    message = $"Asset moved from {fromPath} to {toPath}"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error moving asset from '{fromPath}' to '{toPath}': {e.Message}");
                return new { error = $"Failed to move asset: {e.Message}" };
            }
        }

        /// <summary>
        /// Copy an asset to a new location
        /// </summary>
        private static object CopyAsset(string fromPath, string toPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toPath))
                {
                    return new { error = "Source and destination paths must be specified" };
                }

                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fromPath))
                {
                    return new { error = $"Source asset not found: {fromPath}" };
                }

                if (!AssetDatabase.CopyAsset(fromPath, toPath))
                {
                    return new { error = $"Failed to copy asset from {fromPath} to {toPath}" };
                }

                var newGuid = AssetDatabase.AssetPathToGUID(toPath);

                return new
                {
                    success = true,
                    action = "copy_asset",
                    fromPath = fromPath,
                    toPath = toPath,
                    newGuid = newGuid,
                    message = $"Asset copied from {fromPath} to {toPath}"
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error copying asset from '{fromPath}' to '{toPath}': {e.Message}");
                return new { error = $"Failed to copy asset: {e.Message}" };
            }
        }

        /// <summary>
        /// Refresh the Asset Database
        /// </summary>
        private static object RefreshAssetDatabase()
        {
            try
            {
                var startTime = EditorApplication.timeSinceStartup;
                AssetDatabase.Refresh();
                var duration = EditorApplication.timeSinceStartup - startTime;

                return new
                {
                    success = true,
                    action = "refresh",
                    message = "Asset database refreshed",
                    duration = Math.Round(duration, 2)
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error refreshing asset database: {e.Message}");
                return new { error = $"Failed to refresh asset database: {e.Message}" };
            }
        }

        /// <summary>
        /// Save pending changes to the Asset Database
        /// </summary>
        private static object SaveAssetDatabase()
        {
            try
            {
                // Get count of modified assets (this is an approximation)
                var allAssetGuids = AssetDatabase.FindAssets("");
                var modifiedCount = 0;
                
                // Count recently modified assets (modified in last hour as an example)
                var oneHourAgo = DateTime.UtcNow.AddHours(-1);
                foreach (var guid in allAssetGuids.Take(100)) // Limit to avoid performance issues
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var fileInfo = new FileInfo(Path.Combine(Application.dataPath, "..", path));
                    if (fileInfo.Exists && fileInfo.LastWriteTimeUtc > oneHourAgo)
                    {
                        modifiedCount++;
                    }
                }

                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    action = "save",
                    message = "Asset database saved",
                    assetsModified = modifiedCount
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AssetDatabaseHandler", $" Error saving asset database: {e.Message}");
                return new { error = $"Failed to save asset database: {e.Message}" };
            }
        }
    }
}
