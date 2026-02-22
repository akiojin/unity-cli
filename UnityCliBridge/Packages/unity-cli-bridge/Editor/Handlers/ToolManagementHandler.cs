using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Editor tool and plugin management operations
    /// </summary>
    public static class ToolManagementHandler
    {
        private static ListRequest listRequest;
        private static Dictionary<string, ToolInfo> toolCache = new Dictionary<string, ToolInfo>();
        private static DateTime lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan cacheExpiry = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Handle tool management operations (get, activate, deactivate, refresh)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "get":
                        string category = parameters["category"]?.ToString();
                        return GetTools(category);
                    case "activate":
                        var toolNameToActivate = parameters["toolName"]?.ToString();
                        return ActivateTool(toolNameToActivate);
                    case "deactivate":
                        var toolNameToDeactivate = parameters["toolName"]?.ToString();
                        return DeactivateTool(toolNameToDeactivate);
                    case "refresh":
                        return RefreshToolCache();
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("ToolManagementHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get all available tools, optionally filtered by category
        /// </summary>
        private static object GetTools(string category)
        {
            try
            {
                // Ensure cache is up to date
                if (DateTime.Now - lastCacheUpdate > cacheExpiry || toolCache.Count == 0)
                {
                    UpdateToolCache();
                }

                var tools = new List<object>();
                int installedCount = 0;
                int activeCount = 0;

                // Get built-in Unity tools
                AddBuiltInTools(tools, ref installedCount, ref activeCount, category);

                // Get package manager packages
                AddPackageManagerTools(tools, ref installedCount, ref activeCount, category);

                // Sort tools by name
                tools = tools.OrderBy(t => (t as dynamic).name).ToList();

                return new
                {
                    success = true,
                    action = "get",
                    tools = tools,
                    installedCount = installedCount,
                    activeCount = activeCount
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("ToolManagementHandler", $"Error getting tools: {e.Message}");
                return new { error = $"Failed to get tools: {e.Message}" };
            }
        }

        /// <summary>
        /// Add built-in Unity tools to the list
        /// </summary>
        private static void AddBuiltInTools(List<object> tools, ref int installedCount, ref int activeCount, string category)
        {
            // ProBuilder
            bool hasProBuilder = System.Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder") != null;
            if (string.IsNullOrEmpty(category) || category == "Modeling")
            {
                tools.Add(new
                {
                    name = "ProBuilder",
                    displayName = "ProBuilder",
                    version = hasProBuilder ? "5.1.0" : "Not installed",
                    category = "Modeling",
                    isInstalled = hasProBuilder,
                    isActive = hasProBuilder && IsToolWindowOpen("ProBuilder")
                });
                if (hasProBuilder)
                {
                    installedCount++;
                    if (IsToolWindowOpen("ProBuilder")) activeCount++;
                }
            }

            // Cinemachine
            bool hasCinemachine = System.Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine") != null;
            if (string.IsNullOrEmpty(category) || category == "Camera")
            {
                tools.Add(new
                {
                    name = "Cinemachine",
                    displayName = "Cinemachine",
                    version = hasCinemachine ? "2.9.0" : "Not installed",
                    category = "Camera",
                    isInstalled = hasCinemachine,
                    isActive = hasCinemachine && IsToolWindowOpen("Cinemachine")
                });
                if (hasCinemachine)
                {
                    installedCount++;
                    if (IsToolWindowOpen("Cinemachine")) activeCount++;
                }
            }

            // TextMeshPro
            bool hasTextMeshPro = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro") != null;
            if (string.IsNullOrEmpty(category) || category == "UI")
            {
                tools.Add(new
                {
                    name = "TextMeshPro",
                    displayName = "TextMesh Pro",
                    version = hasTextMeshPro ? "3.0.6" : "Not installed",
                    category = "UI",
                    isInstalled = hasTextMeshPro,
                    isActive = hasTextMeshPro && IsToolWindowOpen("TextMeshPro")
                });
                if (hasTextMeshPro)
                {
                    installedCount++;
                    if (IsToolWindowOpen("TextMeshPro")) activeCount++;
                }
            }
        }

        /// <summary>
        /// Add Package Manager tools to the list
        /// </summary>
        private static void AddPackageManagerTools(List<object> tools, ref int installedCount, ref int activeCount, string category)
        {
            // This would normally query the Package Manager API
            // For now, we'll just use cached data
            foreach (var tool in toolCache.Values)
            {
                if (!string.IsNullOrEmpty(category) && tool.Category != category)
                    continue;

                tools.Add(new
                {
                    name = tool.Name,
                    displayName = tool.DisplayName,
                    version = tool.Version,
                    category = tool.Category,
                    isInstalled = tool.IsInstalled,
                    isActive = tool.IsActive
                });

                if (tool.IsInstalled)
                {
                    installedCount++;
                    if (tool.IsActive) activeCount++;
                }
            }
        }

        /// <summary>
        /// Activate a tool
        /// </summary>
        private static object ActivateTool(string toolName)
        {
            try
            {
                if (string.IsNullOrEmpty(toolName))
                {
                    return new { error = "Tool name not specified" };
                }

                // Check if tool exists
                var toolInfo = GetToolInfo(toolName);
                if (toolInfo == null)
                {
                    return new { error = $"Tool not found: {toolName}" };
                }

                // Check if already active
                if (toolInfo.IsActive)
                {
                    return new
                    {
                        success = true,
                        action = "activate",
                        toolName = toolName,
                        alreadyActive = true,
                        message = $"Tool is already active: {toolName}"
                    };
                }

                // Try to open the tool window
                bool activated = OpenToolWindow(toolName);

                if (activated)
                {
                    toolInfo.IsActive = true;
                    return new
                    {
                        success = true,
                        action = "activate",
                        toolName = toolName,
                        previousState = new { isActive = false },
                        currentState = new { isActive = true },
                        message = $"Tool activated: {toolName}"
                    };
                }
                else
                {
                    return new { error = $"Failed to activate tool: {toolName}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("ToolManagementHandler", $"Error activating tool '{toolName}': {e.Message}");
                return new { error = $"Failed to activate tool: {e.Message}" };
            }
        }

        /// <summary>
        /// Deactivate a tool
        /// </summary>
        private static object DeactivateTool(string toolName)
        {
            try
            {
                if (string.IsNullOrEmpty(toolName))
                {
                    return new { error = "Tool name not specified" };
                }

                // Check if tool exists
                var toolInfo = GetToolInfo(toolName);
                if (toolInfo == null)
                {
                    return new { error = $"Tool not found: {toolName}" };
                }

                // Check if already inactive
                if (!toolInfo.IsActive)
                {
                    return new
                    {
                        success = true,
                        action = "deactivate",
                        toolName = toolName,
                        alreadyInactive = true,
                        message = $"Tool is already inactive: {toolName}"
                    };
                }

                // Try to close the tool window
                bool deactivated = CloseToolWindow(toolName);

                if (deactivated)
                {
                    toolInfo.IsActive = false;
                    return new
                    {
                        success = true,
                        action = "deactivate",
                        toolName = toolName,
                        previousState = new { isActive = true },
                        currentState = new { isActive = false },
                        message = $"Tool deactivated: {toolName}"
                    };
                }
                else
                {
                    return new { error = $"Failed to deactivate tool: {toolName}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("ToolManagementHandler", $"Error deactivating tool '{toolName}': {e.Message}");
                return new { error = $"Failed to deactivate tool: {e.Message}" };
            }
        }

        /// <summary>
        /// Refresh the tool cache
        /// </summary>
        private static object RefreshToolCache()
        {
            try
            {
                UpdateToolCache();

                return new
                {
                    success = true,
                    action = "refresh",
                    message = "Tool cache refreshed",
                    toolsCount = toolCache.Count + 3, // +3 for built-in tools
                    timestamp = DateTime.UtcNow.ToString("o")
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("ToolManagementHandler", $"Error refreshing tool cache: {e.Message}");
                return new { error = $"Failed to refresh tool cache: {e.Message}" };
            }
        }

        /// <summary>
        /// Update the tool cache
        /// </summary>
        private static void UpdateToolCache()
        {
            toolCache.Clear();
            
            // In a real implementation, this would query Package Manager
            // For now, we'll add some sample tools
            toolCache["2D Sprite"] = new ToolInfo
            {
                Name = "2D Sprite",
                DisplayName = "2D Sprite Editor",
                Version = "1.0.0",
                Category = "2D",
                IsInstalled = true,
                IsActive = false
            };

            toolCache["Animation"] = new ToolInfo
            {
                Name = "Animation",
                DisplayName = "Animation Window",
                Version = "1.0.0",
                Category = "Animation",
                IsInstalled = true,
                IsActive = EditorWindow.HasOpenInstances<AnimationWindow>()
            };

            lastCacheUpdate = DateTime.Now;
        }

        /// <summary>
        /// Get tool info by name
        /// </summary>
        private static ToolInfo GetToolInfo(string toolName)
        {
            // Check built-in tools first
            switch (toolName)
            {
                case "ProBuilder":
                    return new ToolInfo
                    {
                        Name = "ProBuilder",
                        DisplayName = "ProBuilder",
                        Version = "5.1.0",
                        Category = "Modeling",
                        IsInstalled = System.Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder") != null,
                        IsActive = IsToolWindowOpen("ProBuilder")
                    };
                case "Cinemachine":
                    return new ToolInfo
                    {
                        Name = "Cinemachine",
                        DisplayName = "Cinemachine",
                        Version = "2.9.0",
                        Category = "Camera",
                        IsInstalled = System.Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine") != null,
                        IsActive = IsToolWindowOpen("Cinemachine")
                    };
                case "TextMeshPro":
                    return new ToolInfo
                    {
                        Name = "TextMeshPro",
                        DisplayName = "TextMesh Pro",
                        Version = "3.0.6",
                        Category = "UI",
                        IsInstalled = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro") != null,
                        IsActive = IsToolWindowOpen("TextMeshPro")
                    };
            }

            // Check cache
            return toolCache.ContainsKey(toolName) ? toolCache[toolName] : null;
        }

        /// <summary>
        /// Check if a tool window is open
        /// </summary>
        private static bool IsToolWindowOpen(string toolName)
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            return windows.Any(w => w.GetType().Name.Contains(toolName));
        }

        /// <summary>
        /// Open a tool window
        /// </summary>
        private static bool OpenToolWindow(string toolName)
        {
            try
            {
                switch (toolName)
                {
                    case "ProBuilder":
                        EditorApplication.ExecuteMenuItem("Tools/ProBuilder/ProBuilder Window");
                        return true;
                    case "Cinemachine":
                        // Cinemachine doesn't have a dedicated window, it uses components
                        return true;
                    case "Animation":
                        EditorWindow.GetWindow<AnimationWindow>().Show();
                        return true;
                    default:
                        // Try generic menu item
                        return EditorApplication.ExecuteMenuItem($"Window/{toolName}");
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Close a tool window
        /// </summary>
        private static bool CloseToolWindow(string toolName)
        {
            try
            {
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                var toolWindow = windows.FirstOrDefault(w => w.GetType().Name.Contains(toolName));
                
                if (toolWindow != null)
                {
                    toolWindow.Close();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tool information class
        /// </summary>
        private class ToolInfo
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string Version { get; set; }
            public string Category { get; set; }
            public bool IsInstalled { get; set; }
            public bool IsActive { get; set; }
        }
    }
}