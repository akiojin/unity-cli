using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles compilation monitoring and error detection for Unity CLI Bridge
    /// </summary>
    public static class CompilationHandler
    {
        /// <summary>
        /// Compilation message structure
        /// </summary>
        public class CompilationMessage
        {
            public string type;
            public string message;
            public string file;
            public int line;
            public int column;
            public string timestamp;
        }

        // Mode bits for script compilation entries (Unity internal LogEntry.mode)
        private const int ModeBitScriptCompileError = 1 << 12;   // 0x00001000
        private const int ModeBitScriptCompileWarning = 1 << 13; // 0x00002000

        /// <summary>
        /// Get current compilation state and recent errors
        /// </summary>
        public static object GetCompilationState(JObject parameters)
        {
            try
            {
                // Parse parameters
                bool includeMessages = parameters["includeMessages"]?.ToObject<bool>() ?? false;
                int maxMessages = parameters["maxMessages"]?.ToObject<int>() ?? 50;

                // Get current compilation state
                bool isCompiling = EditorApplication.isCompiling;
                bool isUpdating = EditorApplication.isUpdating;

                // Separate compilation errors from general console errors
                var (compErrors, compWarnings, consErrors, consWarnings) = GetErrorCounts();

                // Snapshot console for error details (optional)
                var uniqueMessages = SnapshotConsoleMessages(maxMessages)
                    .GroupBy(m => $"{m.file}:{m.line}:{m.message}")
                    .Select(g => g.First())
                    .OrderByDescending(m => DateTime.Parse(m.timestamp))
                    .Take(maxMessages)
                    .ToList();

                var result = new
                {
                    success = true,
                    isCompiling = isCompiling,
                    isUpdating = isUpdating,
                    isMonitoring = false,
                    lastCompilationTime = GetLastAssemblyWriteTime(),
                    messageCount = uniqueMessages.Count,
                    errorCount = compErrors,
                    warningCount = compWarnings,
                    consoleErrorCount = consErrors,
                    consoleWarningCount = consWarnings
                };

                if (includeMessages)
                {
                    return new
                    {
                        success = result.success,
                        isCompiling = result.isCompiling,
                        isUpdating = result.isUpdating,
                        isMonitoring = result.isMonitoring,
                        lastCompilationTime = result.lastCompilationTime,
                        messageCount = result.messageCount,
                        errorCount = result.errorCount,
                        warningCount = result.warningCount,
                        consoleErrorCount = result.consoleErrorCount,
                        consoleWarningCount = result.consoleWarningCount,
                        messages = uniqueMessages
                    };
                }

                return result;
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("CompilationHandler", $"Error getting compilation state: {e.Message}");
                return new { error = $"Failed to get compilation state: {e.Message}" };
            }
        }

        /// <summary>
        /// Take a snapshot of current Unity console for Error/Warning logs and convert to CompilationMessage list.
        /// Uses existing ConsoleHandler.ReadConsole to avoid duplicated reflection logic.
        /// </summary>
        private static List<CompilationMessage> SnapshotConsoleMessages(int maxMessages)
        {
            var list = new List<CompilationMessage>();
            try
            {
                var p = new JObject
                {
                    ["count"] = Math.Max(maxMessages, 50), // capture reasonably large window
                    ["logTypes"] = new JArray("Error", "Warning"),
                    ["includeStackTrace"] = false,
                    ["format"] = "detailed",
                    ["sortOrder"] = "newest",
                    ["groupBy"] = "none"
                };

                var resultObj = ConsoleHandler.ReadConsole(p);
                var result = JObject.FromObject(resultObj);
                var logs = result["logs"] as JArray;
                if (logs != null)
                {
                    foreach (var l in logs)
                    {
                        var type = l["logType"]?.ToString();
                        if (type != "Error" && type != "Warning") continue;

                        list.Add(new CompilationMessage
                        {
                            type = type,
                            message = l["message"]?.ToString() ?? string.Empty,
                            file = l["file"]?.ToString(),
                            line = l["line"]?.ToObject<int?>() ?? 0,
                            column = 0,
                            timestamp = DateTime.Now.ToString("o")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("CompilationHandler", $"SnapshotConsoleMessages failed: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Helper method to capitalize first letter
        /// </summary>
        private static string CapitalizeFirst(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            
            return char.ToUpper(input[0]) + input.Substring(1).ToLower();
        }

        private static (int compilationErrors, int compilationWarnings,
                    int consoleErrors, int consoleWarnings) GetErrorCounts()
        {
            int compErr = 0, compWarn = 0;
            int consErr = 0, consWarn = 0;
            try
            {
                // Get total console counts via GetCountsByType for consoleErrorCount / consoleWarningCount
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                var getCountsByType = logEntriesType?.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (getCountsByType != null)
                {
                    int e = 0, w = 0, l = 0;
                    object[] args = { e, w, l };
                    getCountsByType.Invoke(null, args);
                    consErr = (int)args[0];
                    consWarn = (int)args[1];
                }

                // Walk console entries to count only script-compilation errors/warnings
                var startMethod = logEntriesType?.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var endMethod = logEntriesType?.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var getCount = logEntriesType?.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var getEntry = logEntriesType?.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                var logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                var modeField = logEntryType?.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (startMethod != null && endMethod != null && getCount != null && getEntry != null && modeField != null)
                {
                    startMethod.Invoke(null, null);
                    try
                    {
                        int total = (int)getCount.Invoke(null, null);
                        var entry = Activator.CreateInstance(logEntryType);
                        for (int i = 0; i < total; i++)
                        {
                            getEntry.Invoke(null, new object[] { i, entry });
                            int mode = (int)modeField.GetValue(entry);
                            if ((mode & ModeBitScriptCompileError) != 0)
                                compErr++;
                            if ((mode & ModeBitScriptCompileWarning) != 0)
                                compWarn++;
                        }
                    }
                    finally
                    {
                        endMethod.Invoke(null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("CompilationHandler", $"GetErrorCounts failed: {ex.Message}");
            }
            return (compErr, compWarn, consErr, consWarn);
        }

        private static string GetLastAssemblyWriteTime()
        {
            try
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ScriptAssemblies"));
                if (!Directory.Exists(dir)) return null;
                var latest = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
                    .Select(f => File.GetLastWriteTimeUtc(f))
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                return latest == DateTime.MinValue ? null : latest.ToString("o");
            }
            catch (Exception ex)
            {
                BridgeLogger.LogWarning("CompilationHandler", $"GetLastAssemblyWriteTime failed: {ex.Message}");
                return null;
            }
        }
    }
}
