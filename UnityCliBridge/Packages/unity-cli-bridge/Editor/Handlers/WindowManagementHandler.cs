using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Editor window management operations
    /// </summary>
    public static class WindowManagementHandler
    {
        /// <summary>
        /// Handle window management operations (get, focus, get_state)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                switch (action.ToLower())
                {
                    case "get":
                        bool includeHidden = parameters["includeHidden"]?.ToObject<bool>() ?? false;
                        return GetWindows(includeHidden);
                    case "focus":
                        var windowTypeToFocus = parameters["windowType"]?.ToString();
                        return FocusWindow(windowTypeToFocus);
                    case "get_state":
                        var windowTypeToGetState = parameters["windowType"]?.ToString();
                        return GetWindowState(windowTypeToGetState);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("WindowManagementHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get all open editor windows
        /// </summary>
        private static object GetWindows(bool includeHidden)
        {
            try
            {
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                var windowList = new List<object>();
                EditorWindow focusedWindow = null;
                int visibleCount = 0;
                int hiddenCount = 0;

                foreach (var window in windows)
                {
                    if (window == null) continue;
                    
                    // Skip certain internal windows
                    var typeName = window.GetType().Name;
                    if (typeName.Contains("HostView") || typeName.Contains("DockArea")) continue;

                    bool isVisible = IsWindowVisible(window);
                    
                    if (!includeHidden && !isVisible) continue;

                    if (isVisible) visibleCount++;
                    else hiddenCount++;

                    if (window == EditorWindow.focusedWindow)
                    {
                        focusedWindow = window;
                    }

                    var windowInfo = new Dictionary<string, object>
                    {
                        { "type", window.GetType().Name },
                        { "title", window.titleContent.text },
                        { "hasFocus", window == EditorWindow.focusedWindow },
                        { "docked", IsWindowDocked(window) },
                        { "position", new {
                            x = window.position.x,
                            y = window.position.y,
                            width = window.position.width,
                            height = window.position.height
                        }}
                    };

                    if (includeHidden)
                    {
                        windowInfo["visible"] = isVisible;
                    }

                    windowList.Add(windowInfo);
                }

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "action", "get" },
                    { "windows", windowList },
                    { "count", windowList.Count }
                };

                if (focusedWindow != null)
                {
                    result["focusedWindow"] = focusedWindow.GetType().Name;
                }

                if (includeHidden)
                {
                    result["visibleCount"] = visibleCount;
                    result["hiddenCount"] = hiddenCount;
                }

                return result;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("WindowManagementHandler", $"Error getting windows: {e.Message}");
                return new { error = $"Failed to get windows: {e.Message}" };
            }
        }

        /// <summary>
        /// Focus a specific window type
        /// </summary>
        private static object FocusWindow(string windowType)
        {
            try
            {
                if (string.IsNullOrEmpty(windowType))
                {
                    return new { error = "Window type not specified" };
                }

                // Get current focused window before changing
                var previousFocus = EditorWindow.focusedWindow?.GetType().Name;

                // Find window of the specified type
                EditorWindow targetWindow = null;
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                
                foreach (var window in windows)
                {
                    if (window != null && window.GetType().Name == windowType)
                    {
                        targetWindow = window;
                        break;
                    }
                }

                if (targetWindow == null)
                {
                    return new { error = $"Window type \"{windowType}\" not found" };
                }

                // Check if already focused
                if (targetWindow == EditorWindow.focusedWindow)
                {
                    return new
                    {
                        success = true,
                        action = "focus",
                        windowType = windowType,
                        alreadyFocused = true,
                        message = $"Window \"{windowType}\" is already focused"
                    };
                }

                // Focus the window
                targetWindow.Focus();
                targetWindow.Show();

                return new
                {
                    success = true,
                    action = "focus",
                    windowType = windowType,
                    previousFocus = previousFocus,
                    message = $"Focused window: {windowType}"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("WindowManagementHandler", $"Error focusing window '{windowType}': {e.Message}");
                return new { error = $"Failed to focus window: {e.Message}" };
            }
        }

        /// <summary>
        /// Get detailed state of a specific window
        /// </summary>
        private static object GetWindowState(string windowType)
        {
            try
            {
                if (string.IsNullOrEmpty(windowType))
                {
                    return new { error = "Window type not specified" };
                }

                // Find window of the specified type
                EditorWindow targetWindow = null;
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                
                foreach (var window in windows)
                {
                    if (window != null && window.GetType().Name == windowType)
                    {
                        targetWindow = window;
                        break;
                    }
                }

                if (targetWindow == null)
                {
                    return new
                    {
                        success = true,
                        action = "get_state",
                        windowType = windowType,
                        state = (object)null,
                        message = $"Window \"{windowType}\" is not open"
                    };
                }

                // Get detailed window state
                var state = new Dictionary<string, object>
                {
                    { "type", targetWindow.GetType().Name },
                    { "title", targetWindow.titleContent.text },
                    { "hasFocus", targetWindow == EditorWindow.focusedWindow },
                    { "docked", IsWindowDocked(targetWindow) },
                    { "floating", !IsWindowDocked(targetWindow) },
                    { "position", new {
                        x = targetWindow.position.x,
                        y = targetWindow.position.y,
                        width = targetWindow.position.width,
                        height = targetWindow.position.height
                    }},
                    { "minSize", new {
                        width = targetWindow.minSize.x,
                        height = targetWindow.minSize.y
                    }},
                    { "maxSize", new {
                        width = targetWindow.maxSize.x,
                        height = targetWindow.maxSize.y
                    }},
                    { "maximized", targetWindow.maximized }
                };

                // Check for specific window types
                if (targetWindow is SceneView sceneView)
                {
                    state["isPlayModeView"] = false;
                    state["hasUnsavedChanges"] = EditorSceneManager.GetActiveScene().isDirty;
                }
                else if (windowType == "GameView")
                {
                    state["isPlayModeView"] = EditorApplication.isPlaying;
                    state["hasUnsavedChanges"] = false;
                }
                else
                {
                    state["isPlayModeView"] = false;
                    state["hasUnsavedChanges"] = false;
                }

                return new
                {
                    success = true,
                    action = "get_state",
                    windowType = windowType,
                    state = state
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("WindowManagementHandler", $"Error getting window state for '{windowType}': {e.Message}");
                return new { error = $"Failed to get window state: {e.Message}" };
            }
        }

        /// <summary>
        /// Check if a window is docked
        /// </summary>
        private static bool IsWindowDocked(EditorWindow window)
        {
            // Use reflection to check if window is docked
            try
            {
                var type = window.GetType();
                var property = type.GetProperty("docked", BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                {
                    return (bool)property.GetValue(window);
                }

                // Alternative: check parent type
                var parent = window.GetType().GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
                if (parent != null)
                {
                    var parentValue = parent.GetValue(window);
                    if (parentValue != null)
                    {
                        return parentValue.GetType().Name.Contains("DockArea");
                    }
                }
            }
            catch
            {
                // If reflection fails, assume docked
            }

            return true; // Default to docked
        }

        /// <summary>
        /// Check if a window is visible
        /// </summary>
        private static bool IsWindowVisible(EditorWindow window)
        {
            // A window is considered visible if it has non-zero dimensions
            return window.position.width > 0 && window.position.height > 0;
        }
    }
}