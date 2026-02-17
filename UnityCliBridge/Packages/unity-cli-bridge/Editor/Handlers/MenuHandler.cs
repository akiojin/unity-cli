using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Information about a menu item
    /// </summary>
    public class MenuItemInfo
    {
        public string MenuPath { get; set; }
        public bool IsCustom { get; set; }
        public string AssemblyName { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        
        public MenuItemInfo(string menuPath, bool isCustom = false)
        {
            MenuPath = menuPath;
            IsCustom = isCustom;
        }
    }
    
    /// <summary>
    /// Handles Unity Editor menu item execution commands
    /// </summary>
    public static class MenuHandler
    {
        // Blacklist of dangerous menu items for safety
        // Includes dialog-opening menus that cause MCP hanging
        private static readonly HashSet<string> BlacklistedMenus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Application control
            "File/Quit",
            
            // Dialog-opening file operations (cause MCP hanging)
            "File/Open Scene",
            "File/New Scene",
            "File/Save Scene As...",
            "File/Build Settings...",
            "File/Build And Run",
            
            // Dialog-opening asset operations (cause MCP hanging)
            "Assets/Import New Asset...",
            "Assets/Import Package/Custom Package...",
            "Assets/Export Package...",
            "Assets/Delete",
            
            // Dialog-opening preferences and settings (cause MCP hanging)
            "Edit/Preferences...",
            "Edit/Project Settings...",
            
            // Dialog-opening window operations (may cause issues)
            "Window/Package Manager",
            "Window/Asset Store",
            
            // Scene view operations that may require focus (potential hanging)
            "GameObject/Align With View",
            "GameObject/Align View to Selected"
        };

        /// <summary>
        /// Executes a Unity Editor menu item
        /// </summary>
        /// <param name="parameters">Command parameters</param>
        /// <returns>Execution result</returns>
        public static object ExecuteMenuItem(JObject parameters)
        {
            try
            {
                // Extract parameters
                string action = parameters["action"]?.ToString()?.ToLower() ?? "execute";
                string menuPath = parameters["menuPath"]?.ToString();
                string alias = parameters["alias"]?.ToString();
                bool safetyCheck = parameters["safetyCheck"]?.ToObject<bool>() ?? true;
                JObject menuParameters = parameters["parameters"] as JObject;

                // Validate menu path
                if (string.IsNullOrWhiteSpace(menuPath))
                {
                    return new
                    {
                        success = false,
                        error = "menuPath is required"
                    };
                }

                // Handle different actions
                switch (action)
                {
                    case "execute":
                        return ExecuteMenuAction(menuPath, alias, safetyCheck, menuParameters);
                    
                    case "get_available_menus":
                        return GetAvailableMenus(menuParameters);
                    
                    default:
                        return new
                        {
                            success = false,
                            error = $"Unknown action: {action}. Valid actions are 'execute', 'get_available_menus'"
                        };
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError("MenuHandler", $"Error executing menu operation: {ex}");
                return new
                {
                    success = false,
                    error = $"Menu operation failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Executes a specific menu item
        /// </summary>
        private static object ExecuteMenuAction(string menuPath, string alias, bool safetyCheck, JObject parameters)
        {
            try
            {
                // Validate menu path format
                if (!menuPath.Contains("/") || menuPath.StartsWith("/") || menuPath.EndsWith("/"))
                {
                    return new
                    {
                        success = false,
                        error = "menuPath must be in format \"Category/MenuItem\" (e.g., \"Assets/Refresh\")"
                    };
                }

                // Check blacklist if safety is enabled
                if (safetyCheck && BlacklistedMenus.Contains(menuPath))
                {
                    return new
                    {
                        success = false,
                        error = $"Menu item is blacklisted for safety: {menuPath}. Use safetyCheck: false to override."
                    };
                }

                // Record execution start time
                var startTime = DateTime.UtcNow;

                // Execute the menu item
                bool executed = false;
                bool menuExists = true;

                try
                {
                    // Try to execute the menu item
                    executed = EditorApplication.ExecuteMenuItem(menuPath);
                    
                    if (!executed)
                    {
                        // Menu item exists but couldn't be executed (might be disabled or context-dependent)
                        McpLogger.LogWarning("MenuHandler", $"Menu item '{menuPath}' could not be executed - it may be disabled or context-dependent");
                    }
                }
                catch (Exception ex)
                {
                    // Menu item might not exist
                    McpLogger.LogWarning("MenuHandler", $"Failed to execute menu item '{menuPath}': {ex.Message}");
                    menuExists = false;
                    executed = false;
                }

                // Calculate execution time
                var executionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                // Build response
                var result = new
                {
                    success = true,
                    menuPath = menuPath,
                    executed = executed,
                    menuExists = menuExists,
                    executionTime = executionTime,
                    message = executed 
                        ? "Menu item executed successfully" 
                        : menuExists 
                            ? "Menu item found but could not be executed (may be disabled or context-dependent)"
                            : "Menu item not found or execution failed"
                };

                // Add alias if provided
                if (!string.IsNullOrEmpty(alias))
                {
                    return new
                    {
                        result.success,
                        result.menuPath,
                        result.executed,
                        result.menuExists,
                        result.executionTime,
                        result.message,
                        alias = alias
                    };
                }

                return result;
            }
            catch (Exception ex)
            {
                McpLogger.LogError("MenuHandler", $"Error executing menu item '{menuPath}': {ex}");
                return new
                {
                    success = false,
                    error = $"Menu item execution failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Gets available Unity Editor menu items with distinction between custom and built-in menus
        /// Returns detailed information about all menu items including their source (custom vs built-in)
        /// Custom menus are detected via reflection from user assemblies
        /// Built-in menus are Unity's standard menu items
        /// </summary>
        private static object GetAvailableMenus(JObject parameters)
        {
            try
            {
                var menuInfoList = new List<MenuItemInfo>();
                var customMenuPaths = new HashSet<string>();
                
                // リフレクションで全MenuItem属性を検出
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var assemblyName = assembly.GetName().Name;
                        // Unity標準アセンブリかどうかを判定
                        bool isUnityAssembly = assemblyName.StartsWith("Unity") || 
                                              assemblyName.StartsWith("UnityEngine") || 
                                              assemblyName.StartsWith("UnityEditor") ||
                                              assemblyName.StartsWith("com.unity");
                        
                        foreach (var type in assembly.GetTypes())
                        {
                            try
                            {
                                var methods = type.GetMethods(
                                    System.Reflection.BindingFlags.Static | 
                                    System.Reflection.BindingFlags.Public | 
                                    System.Reflection.BindingFlags.NonPublic);
                                
                                foreach (var method in methods)
                                {
                                    var menuItemAttrs = method.GetCustomAttributes(typeof(MenuItem), false);
                                    foreach (MenuItem attr in menuItemAttrs)
                                    {
                                        if (!string.IsNullOrEmpty(attr.menuItem))
                                        {
                                            var menuInfo = new MenuItemInfo(attr.menuItem, !isUnityAssembly)
                                            {
                                                AssemblyName = assemblyName,
                                                ClassName = type.FullName,
                                                MethodName = method.Name
                                            };
                                            menuInfoList.Add(menuInfo);
                                            
                                            if (!isUnityAssembly)
                                            {
                                                customMenuPaths.Add(attr.menuItem);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Skip types that can't be loaded
                                continue;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip assemblies that can't be loaded
                        continue;
                    }
                }
                
                // Common Unity menu items (標準メニューとして追加)
                var commonMenus = new List<string>
                {
                    // File menu
                    "File/New Scene",
                    "File/Open Scene",
                    "File/Save",
                    "File/Save As...",
                    "File/Save Project",
                    
                    // Edit menu
                    "Edit/Undo",
                    "Edit/Redo",
                    "Edit/Cut",
                    "Edit/Copy",
                    "Edit/Paste",
                    "Edit/Select All",
                    
                    // Assets menu
                    "Assets/Create/Folder",
                    "Assets/Create/C# Script",
                    "Assets/Create/Material",
                    "Assets/Refresh",
                    "Assets/Reimport All",
                    
                    // GameObject menu
                    "GameObject/Create Empty",
                    "GameObject/3D Object/Cube",
                    "GameObject/3D Object/Sphere",
                    "GameObject/3D Object/Cylinder",
                    "GameObject/3D Object/Plane",
                    "GameObject/Light/Directional Light",
                    "GameObject/Audio/Audio Source",
                    "GameObject/Camera",
                    
                    // Window menu
                    "Window/General/Console",
                    "Window/General/Project",
                    "Window/General/Hierarchy",
                    "Window/General/Inspector",
                    "Window/General/Scene",
                    "Window/General/Game",
                    "Window/Animation/Animation",
                    "Window/Animation/Animator"
                };
                
                // 標準メニューをMenuItemInfoとして追加
                foreach (var menu in commonMenus)
                {
                    if (!customMenuPaths.Contains(menu)) // カスタムメニューと重複しない場合のみ追加
                    {
                        menuInfoList.Add(new MenuItemInfo(menu, false)
                        {
                            AssemblyName = "Unity Built-in",
                            ClassName = "Unity Standard",
                            MethodName = "Built-in"
                        });
                    }
                }
                
                // フィルタリング処理
                var filter = parameters?["filter"]?.ToString();
                var showOnlyCustom = parameters?["onlyCustom"]?.ToObject<bool>() ?? false;
                var showOnlyBuiltIn = parameters?["onlyBuiltIn"]?.ToObject<bool>() ?? false;
                
                var filteredMenuInfo = menuInfoList;
                
                // カスタム/標準のフィルタリング
                if (showOnlyCustom)
                {
                    filteredMenuInfo = filteredMenuInfo.Where(m => m.IsCustom).ToList();
                }
                else if (showOnlyBuiltIn)
                {
                    filteredMenuInfo = filteredMenuInfo.Where(m => !m.IsCustom).ToList();
                }
                
                // キーワードフィルタリング
                if (!string.IsNullOrEmpty(filter))
                {
                    var filterLower = filter.ToLower();
                    filteredMenuInfo = filteredMenuInfo.Where(m =>
                    {
                        var menuLower = m.MenuPath.ToLower();
                        return menuLower.Contains(filterLower) || 
                               (filter.EndsWith("*") && menuLower.StartsWith(filter.TrimEnd('*').ToLower()));
                    }).ToList();
                }
                
                // ソート
                filteredMenuInfo = filteredMenuInfo.OrderBy(m => m.MenuPath).ToList();
                
                // カテゴリ別に整理
                var categorizedMenus = new Dictionary<string, List<object>>();
                foreach (var menuInfo in filteredMenuInfo)
                {
                    var parts = menuInfo.MenuPath.Split('/');
                    if (parts.Length > 0)
                    {
                        var category = parts[0];
                        if (!categorizedMenus.ContainsKey(category))
                        {
                            categorizedMenus[category] = new List<object>();
                        }
                        categorizedMenus[category].Add(new
                        {
                            path = menuInfo.MenuPath,
                            isCustom = menuInfo.IsCustom,
                            assembly = menuInfo.AssemblyName,
                            className = menuInfo.ClassName,
                            method = menuInfo.MethodName
                        });
                    }
                }
                
                // カスタムメニューのみの詳細情報
                var customMenuDetails = filteredMenuInfo
                    .Where(m => m.IsCustom)
                    .Select(m => new
                    {
                        path = m.MenuPath,
                        assembly = m.AssemblyName,
                        className = m.ClassName,
                        method = m.MethodName
                    })
                    .ToList();
                
                // 標準メニューのパスリスト
                var builtInMenuPaths = filteredMenuInfo
                    .Where(m => !m.IsCustom)
                    .Select(m => m.MenuPath)
                    .ToList();
                
                // 全メニューパス（後方互換性のため）
                var allMenuPaths = filteredMenuInfo.Select(m => m.MenuPath).ToList();
                
                return new
                {
                    success = true,
                    // 後方互換性のため従来の形式も残す
                    availableMenus = allMenuPaths,
                    // 詳細情報
                    customMenus = customMenuDetails,
                    builtInMenus = builtInMenuPaths,
                    categorized = categorizedMenus,
                    // 統計情報
                    stats = new
                    {
                        totalMenus = menuInfoList.Count,
                        customCount = menuInfoList.Count(m => m.IsCustom),
                        builtInCount = menuInfoList.Count(m => !m.IsCustom),
                        filteredCount = filteredMenuInfo.Count
                    },
                    message = GetFilterMessage(filter, showOnlyCustom, showOnlyBuiltIn, filteredMenuInfo.Count),
                    note = "Use 'onlyCustom: true' to show only custom menus, 'onlyBuiltIn: true' for Unity standard menus only. Custom menus include detailed source information."
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("MenuHandler", $"Error getting available menus: {ex}");
                return new
                {
                    success = false,
                    error = $"Failed to get available menus: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Generate appropriate filter message based on parameters
        /// </summary>
        private static string GetFilterMessage(string filter, bool onlyCustom, bool onlyBuiltIn, int count)
        {
            var parts = new List<string>();
            
            if (onlyCustom)
                parts.Add("custom menus only");
            else if (onlyBuiltIn)
                parts.Add("built-in menus only");
            
            if (!string.IsNullOrEmpty(filter))
                parts.Add($"filter: '{filter}'");
            
            var filterDesc = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
            return $"Retrieved {count} menu items{filterDesc}";
        }
        
        /// <summary>
        /// Gets the list of blacklisted menu items
        /// </summary>
        /// <returns>Array of blacklisted menu paths</returns>
        public static string[] GetBlacklistedMenus()
        {
            var result = new string[BlacklistedMenus.Count];
            BlacklistedMenus.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Adds a menu item to the blacklist
        /// </summary>
        /// <param name="menuPath">Menu path to blacklist</param>
        public static void AddToBlacklist(string menuPath)
        {
            if (!string.IsNullOrEmpty(menuPath))
            {
                BlacklistedMenus.Add(menuPath);
                McpLogger.Log("MenuHandler", $"Added '{menuPath}' to blacklist");
            }
        }

        /// <summary>
        /// Removes a menu item from the blacklist
        /// </summary>
        /// <param name="menuPath">Menu path to remove from blacklist</param>
        public static bool RemoveFromBlacklist(string menuPath)
        {
            if (!string.IsNullOrEmpty(menuPath))
            {
                bool removed = BlacklistedMenus.Remove(menuPath);
                if (removed)
                {
                    McpLogger.Log("MenuHandler", $"Removed '{menuPath}' from blacklist");
                }
                return removed;
            }
            return false;
        }
    }
}