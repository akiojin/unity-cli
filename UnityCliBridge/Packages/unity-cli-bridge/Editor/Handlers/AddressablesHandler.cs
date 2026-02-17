#if UNITY_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEngine;
using UnityCliBridge.Logging;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Addressables operations
    /// </summary>
    public static class AddressablesHandler
    {
        /// <summary>
        /// Handle addressables operations (manage, build, analyze)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    // Manage actions (10)
                    case "add_entry":
                        return AddEntry(parameters);
                    case "remove_entry":
                        return RemoveEntry(parameters);
                    case "set_address":
                        return SetAddress(parameters);
                    case "add_label":
                        return AddLabel(parameters);
                    case "remove_label":
                        return RemoveLabel(parameters);
                    case "list_entries":
                        return ListEntries(parameters);
                    case "list_groups":
                        return ListGroups(parameters);
                    case "create_group":
                        return CreateGroup(parameters);
                    case "remove_group":
                        return RemoveGroup(parameters);
                    case "move_entry":
                        return MoveEntry(parameters);

                    // Build actions (2)
                    case "build":
                        return Build(parameters);
                    case "clean_build":
                        return CleanBuild(parameters);

                    // Analyze actions (3)
                    case "analyze_duplicates":
                        return AnalyzeDuplicates(parameters);
                    case "analyze_dependencies":
                        return AnalyzeDependencies(parameters);
                    case "analyze_unused":
                        return AnalyzeUnused(parameters);

                    default:
                        return CreateErrorResponse($"Unknown action: {action}");
                }
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"Error handling {action}: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        #region Manage Actions

        private static object AddEntry(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var address = parameters["address"]?.ToString();
                var groupName = parameters["groupName"]?.ToString();
                var labels = parameters["labels"]?.ToObject<string[]>() ?? new string[0];

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(address))
                {
                    return CreateErrorResponse("addressが指定されていません", "addressパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(groupName))
                {
                    return CreateErrorResponse("groupNameが指定されていません", "groupNameパラメータを指定してください");
                }

                // Check if asset exists
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();

                // Find or create group
                var group = settings.FindGroup(groupName);
                if (group == null)
                {
                    return CreateErrorResponse(
                        $"グループが見つかりません: {groupName}",
                        "グループを作成するか、既存のグループ名を指定してください",
                        new { groupName, availableGroups = settings.groups.Select(g => g.Name).ToArray() });
                }

                // Create or move entry
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null)
                {
                    return CreateErrorResponse("エントリの作成に失敗しました");
                }

                // Set address
                entry.SetAddress(address, false);

                // Add labels
                foreach (var label in labels)
                {
                    if (!string.IsNullOrEmpty(label))
                    {
                        entry.SetLabel(label, true, false, false);
                    }
                }

                // Save changes
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);

                return CreateSuccessResponse(SerializeEntry(entry));
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"AddEntry error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object RemoveEntry(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    return CreateErrorResponse(
                        $"Addressableエントリが見つかりません: {assetPath}",
                        "指定されたアセットはAddressableとして登録されていません",
                        new { assetPath });
                }

                // Remove entry
                settings.RemoveAssetEntry(guid);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", $"Addressableエントリを削除しました: {assetPath}" }
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"RemoveEntry error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object SetAddress(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var newAddress = parameters["newAddress"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(newAddress))
                {
                    return CreateErrorResponse("newAddressが指定されていません", "newAddressパラメータを指定してください");
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    return CreateErrorResponse(
                        $"Addressableエントリが見つかりません: {assetPath}",
                        "指定されたアセットはAddressableとして登録されていません。まずadd_entryで登録してください",
                        new { assetPath });
                }

                // Set new address
                entry.SetAddress(newAddress, false);
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

                return CreateSuccessResponse(SerializeEntry(entry));
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"SetAddress error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object AddLabel(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var label = parameters["label"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(label))
                {
                    return CreateErrorResponse("labelが指定されていません", "labelパラメータを指定してください");
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    return CreateErrorResponse(
                        $"Addressableエントリが見つかりません: {assetPath}",
                        "指定されたアセットはAddressableとして登録されていません",
                        new { assetPath });
                }

                // Add label
                entry.SetLabel(label, true, false, false);
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

                return CreateSuccessResponse(SerializeEntry(entry));
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"AddLabel error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object RemoveLabel(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var label = parameters["label"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(label))
                {
                    return CreateErrorResponse("labelが指定されていません", "labelパラメータを指定してください");
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    return CreateErrorResponse(
                        $"Addressableエントリが見つかりません: {assetPath}",
                        "指定されたアセットはAddressableとして登録されていません",
                        new { assetPath });
                }

                // Remove label
                entry.SetLabel(label, false, false, false);
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

                return CreateSuccessResponse(SerializeEntry(entry));
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"RemoveLabel error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object ListEntries(JObject parameters)
        {
            try
            {
                var pageSize = parameters["pageSize"]?.ToObject<int>() ?? 20;
                var offset = parameters["offset"]?.ToObject<int>() ?? 0;
                var groupName = parameters["groupName"]?.ToString();

                var settings = GetSettings();
                var allEntries = new List<AddressableAssetEntry>();

                // Collect all entries
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;

                    // Filter by group name if specified
                    if (!string.IsNullOrEmpty(groupName) && group.Name != groupName)
                    {
                        continue;
                    }

                    foreach (var entry in group.entries)
                    {
                        allEntries.Add(entry);
                    }
                }

                var total = allEntries.Count;
                var hasMore = (offset + pageSize) < total;

                // Apply pagination
                var pagedEntries = allEntries
                    .Skip(offset)
                    .Take(pageSize)
                    .Select(e => SerializeEntry(e))
                    .ToList();

                var data = new Dictionary<string, object>
                {
                    { "entries", pagedEntries }
                };

                var pagination = new Dictionary<string, object>
                {
                    { "offset", offset },
                    { "pageSize", pageSize },
                    { "total", total },
                    { "hasMore", hasMore }
                };

                return CreateSuccessResponse(data, pagination);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"ListEntries error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object ListGroups(JObject parameters)
        {
            try
            {
                var settings = GetSettings();
                var groups = new List<object>();

                foreach (var group in settings.groups)
                {
                    if (group == null) continue;

                    groups.Add(new Dictionary<string, object>
                    {
                        { "groupName", group.Name },
                        { "entriesCount", group.entries.Count },
                        { "readOnly", group.ReadOnly }
                    });
                }

                var data = new Dictionary<string, object>
                {
                    { "groups", groups }
                };

                return CreateSuccessResponse(data);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"ListGroups error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object CreateGroup(JObject parameters)
        {
            try
            {
                var groupName = parameters["groupName"]?.ToString();

                if (string.IsNullOrEmpty(groupName))
                {
                    return CreateErrorResponse("groupNameが指定されていません", "groupNameパラメータを指定してください");
                }

                var settings = GetSettings();

                // Check if group already exists
                var existingGroup = settings.FindGroup(groupName);
                if (existingGroup != null)
                {
                    return CreateErrorResponse(
                        $"グループが既に存在します: {groupName}",
                        "別のグループ名を指定するか、既存のグループを使用してください",
                        new { groupName });
                }

                // Create new group based on default template
                var newGroup = settings.CreateGroup(groupName, false, false, true, null);
                if (newGroup == null)
                {
                    return CreateErrorResponse("グループの作成に失敗しました");
                }

                settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupAdded, newGroup, true);

                var data = new Dictionary<string, object>
                {
                    { "groupName", newGroup.Name },
                    { "entriesCount", newGroup.entries.Count },
                    { "readOnly", newGroup.ReadOnly }
                };

                return CreateSuccessResponse(data);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"CreateGroup error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object RemoveGroup(JObject parameters)
        {
            try
            {
                var groupName = parameters["groupName"]?.ToString();

                if (string.IsNullOrEmpty(groupName))
                {
                    return CreateErrorResponse("groupNameが指定されていません", "groupNameパラメータを指定してください");
                }

                var settings = GetSettings();
                var group = settings.FindGroup(groupName);

                if (group == null)
                {
                    return CreateErrorResponse(
                        $"グループが見つかりません: {groupName}",
                        "グループ名が正しいか確認してください",
                        new { groupName });
                }

                // Check if group is empty
                if (group.entries.Count > 0)
                {
                    return CreateErrorResponse(
                        $"グループが空ではありません: {groupName} ({group.entries.Count}個のエントリ)",
                        "グループを削除する前に、すべてのエントリを削除または移動してください",
                        new { groupName, entriesCount = group.entries.Count });
                }

                // Remove group
                settings.RemoveGroup(group);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", $"グループを削除しました: {groupName}" }
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"RemoveGroup error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object MoveEntry(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                var targetGroupName = parameters["targetGroupName"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                if (string.IsNullOrEmpty(targetGroupName))
                {
                    return CreateErrorResponse("targetGroupNameが指定されていません", "targetGroupNameパラメータを指定してください");
                }

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                var settings = GetSettings();
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    return CreateErrorResponse(
                        $"Addressableエントリが見つかりません: {assetPath}",
                        "指定されたアセットはAddressableとして登録されていません",
                        new { assetPath });
                }

                var targetGroup = settings.FindGroup(targetGroupName);
                if (targetGroup == null)
                {
                    return CreateErrorResponse(
                        $"ターゲットグループが見つかりません: {targetGroupName}",
                        "グループを作成するか、既存のグループ名を指定してください",
                        new { targetGroupName, availableGroups = settings.groups.Select(g => g.Name).ToArray() });
                }

                // Move entry to target group
                settings.MoveEntry(entry, targetGroup, false, false);
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);

                return CreateSuccessResponse(SerializeEntry(entry));
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"MoveEntry error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        #endregion

        #region Build Actions

        private static object Build(JObject parameters)
        {
            try
            {
                var buildTargetStr = parameters["buildTarget"]?.ToString();

                var settings = GetSettings();
                var startTime = DateTime.Now;

                // Build Addressables content
                AddressableAssetSettings.BuildPlayerContent(out var result);

                var duration = (DateTime.Now - startTime).TotalSeconds;

                var buildData = new Dictionary<string, object>
                {
                    { "success", string.IsNullOrEmpty(result.Error) },
                    { "duration", duration },
                    { "outputPath", result.OutputPath ?? "" },
                    { "errors", string.IsNullOrEmpty(result.Error) ? new string[0] : new[] { result.Error } }
                };

                if (!string.IsNullOrEmpty(result.Error))
                {
                    McpLogger.LogError("AddressablesHandler", $"Build failed: {result.Error}");
                }
                else
                {
                    McpLogger.Log("AddressablesHandler", $"Build completed successfully in {duration:F2}s. Output: {result.OutputPath}");
                }

                return CreateSuccessResponse(buildData);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"Build error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object CleanBuild(JObject parameters)
        {
            try
            {
                var settings = GetSettings();

                // Clean build cache
                AddressableAssetSettings.CleanPlayerContent();

                McpLogger.Log("AddressablesHandler", "Build cache cleared successfully");

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", "Addressablesビルドキャッシュをクリアしました" }
                };
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"CleanBuild error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        #endregion

        #region Analyze Actions

        private static object AnalyzeDuplicates(JObject parameters)
        {
            try
            {
                var pageSize = parameters["pageSize"]?.ToObject<int>() ?? 20;
                var offset = parameters["offset"]?.ToObject<int>() ?? 0;

                var settings = GetSettings();
                var assetToGroups = new Dictionary<string, List<string>>();

                // Collect all entries and group them by asset path
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;

                    foreach (var entry in group.entries)
                    {
                        if (!assetToGroups.ContainsKey(entry.AssetPath))
                        {
                            assetToGroups[entry.AssetPath] = new List<string>();
                        }
                        assetToGroups[entry.AssetPath].Add(group.Name);
                    }
                }

                // Find duplicates (assets in 2+ groups)
                var duplicates = assetToGroups
                    .Where(kvp => kvp.Value.Count >= 2)
                    .Select(kvp => new Dictionary<string, object>
                    {
                        { "assetPath", kvp.Key },
                        { "groups", kvp.Value.ToArray() }
                    })
                    .ToList();

                var total = duplicates.Count;
                var hasMore = (offset + pageSize) < total;

                // Apply pagination
                var pagedDuplicates = duplicates
                    .Skip(offset)
                    .Take(pageSize)
                    .ToList();

                var data = new Dictionary<string, object>
                {
                    { "duplicates", pagedDuplicates },
                    { "unused", new string[0] },
                    { "dependencies", new Dictionary<string, string[]>() }
                };

                var pagination = new Dictionary<string, object>
                {
                    { "offset", offset },
                    { "pageSize", pageSize },
                    { "total", total },
                    { "hasMore", hasMore }
                };

                return CreateSuccessResponse(data, pagination);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"AnalyzeDuplicates error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object AnalyzeDependencies(JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();

                if (string.IsNullOrEmpty(assetPath))
                {
                    return CreateErrorResponse("assetPathが指定されていません", "assetPathパラメータを指定してください");
                }

                // Check if asset exists
                if (!System.IO.File.Exists(assetPath) && !System.IO.Directory.Exists(assetPath))
                {
                    return CreateErrorResponse(
                        $"アセットが見つかりません: {assetPath}",
                        "アセットパスが正しいか確認してください",
                        new { assetPath });
                }

                // Get dependencies using AssetDatabase
                var dependencies = AssetDatabase.GetDependencies(assetPath, true)
                    .Where(dep => dep != assetPath) // Exclude self
                    .ToArray();

                var dependenciesDict = new Dictionary<string, string[]>
                {
                    { assetPath, dependencies }
                };

                var data = new Dictionary<string, object>
                {
                    { "duplicates", new object[0] },
                    { "unused", new string[0] },
                    { "dependencies", dependenciesDict }
                };

                return CreateSuccessResponse(data);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"AnalyzeDependencies error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        private static object AnalyzeUnused(JObject parameters)
        {
            try
            {
                var pageSize = parameters["pageSize"]?.ToObject<int>() ?? 20;
                var offset = parameters["offset"]?.ToObject<int>() ?? 0;

                var settings = GetSettings();
                var addressableAssets = new HashSet<string>();

                // Collect all Addressable asset paths
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;

                    foreach (var entry in group.entries)
                    {
                        addressableAssets.Add(entry.AssetPath);
                    }
                }

                // Find all assets in the project
                var allAssetGuids = AssetDatabase.FindAssets("t:Object");
                var unusedAssets = new List<string>();

                foreach (var guid in allAssetGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);

                    // Skip packages and special folders
                    if (path.StartsWith("Packages/") || path.StartsWith("ProjectSettings/"))
                    {
                        continue;
                    }

                    // Skip if already Addressable
                    if (addressableAssets.Contains(path))
                    {
                        continue;
                    }

                    // Check if this asset is referenced by any Addressable entry
                    bool isReferenced = false;
                    foreach (var addressablePath in addressableAssets)
                    {
                        var dependencies = AssetDatabase.GetDependencies(addressablePath, true);
                        if (dependencies.Contains(path))
                        {
                            isReferenced = true;
                            break;
                        }
                    }

                    if (!isReferenced)
                    {
                        unusedAssets.Add(path);
                    }
                }

                var total = unusedAssets.Count;
                var hasMore = (offset + pageSize) < total;

                // Apply pagination
                var pagedUnused = unusedAssets
                    .Skip(offset)
                    .Take(pageSize)
                    .ToArray();

                var data = new Dictionary<string, object>
                {
                    { "duplicates", new object[0] },
                    { "unused", pagedUnused },
                    { "dependencies", new Dictionary<string, string[]>() }
                };

                var pagination = new Dictionary<string, object>
                {
                    { "offset", offset },
                    { "pageSize", pageSize },
                    { "total", total },
                    { "hasMore", hasMore }
                };

                return CreateSuccessResponse(data, pagination);
            }
            catch (Exception e)
            {
                McpLogger.LogError("AddressablesHandler", $"AnalyzeUnused error: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse(e.Message);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get AddressableAssetSettings instance
        /// </summary>
        private static AddressableAssetSettings GetSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                throw new InvalidOperationException("Addressables設定が見つかりません。Addressablesパッケージが正しくインストールされているか確認してください。");
            }
            return settings;
        }

        /// <summary>
        /// Create standardized error response
        /// </summary>
        private static object CreateErrorResponse(string message, string solution = null, object context = null)
        {
            var response = new Dictionary<string, object>
            {
                { "success", false },
                { "error", new Dictionary<string, object>
                    {
                        { "message", message }
                    }
                }
            };

            if (!string.IsNullOrEmpty(solution))
            {
                ((Dictionary<string, object>)response["error"])["solution"] = solution;
            }

            if (context != null)
            {
                ((Dictionary<string, object>)response["error"])["context"] = context;
            }

            return response;
        }

        /// <summary>
        /// Create standardized success response
        /// </summary>
        private static object CreateSuccessResponse(object data = null, object pagination = null)
        {
            var response = new Dictionary<string, object>
            {
                { "success", true }
            };

            if (data != null)
            {
                response["data"] = data;
            }

            if (pagination != null)
            {
                response["pagination"] = pagination;
            }

            return response;
        }

        /// <summary>
        /// Serialize AddressableAssetEntry to contract format
        /// </summary>
        private static object SerializeEntry(AddressableAssetEntry entry)
        {
            return new Dictionary<string, object>
            {
                { "guid", entry.guid },
                { "assetPath", entry.AssetPath },
                { "address", entry.address },
                { "labels", entry.labels.ToList() },
                { "groupName", entry.parentGroup?.Name ?? "" }
            };
        }

        #endregion
    }
}

#else
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    public static class AddressablesHandler
    {
        public static object HandleCommand(string action, JObject parameters)
        {
            McpLogger.LogWarning("AddressablesHandler", "Addressables未導入");
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "Addressables未導入" },
                { "solution", "com.unity.addressables を追加してください" }
            };
        }
    }
}
#endif

