using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Editor console operations including clearing and enhanced log reading
    /// </summary>
    public static class ConsoleHandler
    {
        // Reflection members for accessing internal LogEntry data
        private static MethodInfo _startGettingEntriesMethod;
        private static MethodInfo _endGettingEntriesMethod;
        private static MethodInfo _clearMethod;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _getEntryMethod;
        private static FieldInfo _modeField;
        private static FieldInfo _messageField;
        private static FieldInfo _fileField;
        private static FieldInfo _lineField;
        private static FieldInfo _instanceIdField;

        // UnityEditor.ConsoleWindow.Mode / LogMessageFlags (UnityCsReference).
        // Severity is encoded in these bits, never in the message or stacktrace text.
        private const int ModeBitError = 1 << 0;
        private const int ModeBitAssert = 1 << 1;
        private const int ModeBitLog = 1 << 2;
        private const int ModeBitFatal = 1 << 4;
        private const int ModeBitAssetImportError = 1 << 6;
        private const int ModeBitAssetImportWarning = 1 << 7;
        private const int ModeBitScriptingError = 1 << 8;
        private const int ModeBitScriptingWarning = 1 << 9;
        private const int ModeBitScriptingLog = 1 << 10;
        internal const int ModeBitScriptCompileError = 1 << 11;
        internal const int ModeBitScriptCompileWarning = 1 << 12;
        private const int ModeBitScriptingException = 1 << 17;
        private const int ModeBitGraphCompileError = 1 << 20;
        private const int ModeBitScriptingAssertion = 1 << 21;

        static ConsoleHandler()
        {
            InitializeReflection();
        }

        /// <summary>
        /// Initialize reflection members for accessing Unity's internal console APIs
        /// </summary>
        private static void InitializeReflection()
        {
            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null)
                {
                    BridgeLogger.LogError("ConsoleHandler", "Could not find internal type UnityEditor.LogEntries");
                    return;
                }

                BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", staticFlags);
                _endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", staticFlags);
                _clearMethod = logEntriesType.GetMethod("Clear", staticFlags);
                _getCountMethod = logEntriesType.GetMethod("GetCount", staticFlags);
                _getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", staticFlags);

                Type logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntryType == null)
                {
                    BridgeLogger.LogError("ConsoleHandler", "Could not find internal type UnityEditor.LogEntry");
                    return;
                }

                _modeField = logEntryType.GetField("mode", instanceFlags);
                _messageField = logEntryType.GetField("message", instanceFlags);
                _fileField = logEntryType.GetField("file", instanceFlags);
                _lineField = logEntryType.GetField("line", instanceFlags);
                _instanceIdField = logEntryType.GetField("instanceID", instanceFlags);
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ConsoleHandler", $"Failed to initialize reflection: {ex}");
            }
        }

        /// <summary>
        /// Clears the Unity console
        /// </summary>
        /// <param name="parameters">Command parameters</param>
        /// <returns>Clear result</returns>
        public static object ClearConsole(JObject parameters)
        {
            try
            {
                if (_clearMethod == null)
                {
                    return new
                    {
                        success = false,
                        error = "Console reflection not initialized properly"
                    };
                }

                // Extract parameters
                bool clearOnPlay = parameters["clearOnPlay"]?.ToObject<bool>() ?? false;
                bool clearOnRecompile = parameters["clearOnRecompile"]?.ToObject<bool>() ?? false;
                bool clearOnBuild = parameters["clearOnBuild"]?.ToObject<bool>() ?? false;
                bool preserveWarnings = parameters["preserveWarnings"]?.ToObject<bool>() ?? false;
                bool preserveErrors = parameters["preserveErrors"]?.ToObject<bool>() ?? false;

                // Count logs before clearing
                int totalBefore = _getCountMethod != null ? (int)_getCountMethod.Invoke(null, null) : 0;
                int clearedCount = totalBefore;
                int remainingCount = 0;

                // Handle preservation logic (simplified - Unity doesn't natively support selective clearing)
                if (preserveWarnings || preserveErrors)
                {
                    // Note: Unity doesn't provide native selective clearing
                    // This is a placeholder for the response structure
                    BridgeLogger.LogWarning("ConsoleHandler", "Selective log preservation is not fully implemented in Unity's console API");
                }

                // Clear the console
                _clearMethod.Invoke(null, null);

                // Update console preferences if requested
                bool settingsUpdated = false;
                if (clearOnPlay != EditorPrefs.GetBool("ClearOnPlay", true))
                {
                    EditorPrefs.SetBool("ClearOnPlay", clearOnPlay);
                    settingsUpdated = true;
                }

                return new
                {
                    success = true,
                    message = "Console cleared successfully",
                    clearedCount = clearedCount,
                    remainingCount = remainingCount,
                    settingsUpdated = settingsUpdated,
                    clearOnPlay = clearOnPlay,
                    clearOnRecompile = clearOnRecompile,
                    clearOnBuild = clearOnBuild,
                    timestamp = DateTime.UtcNow.ToString("o")
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ConsoleHandler", $"Error clearing console: {ex}");
                return new
                {
                    success = false,
                    error = $"Failed to clear console: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Reads console logs with enhanced filtering
        /// </summary>
        /// <param name="parameters">Command parameters</param>
        /// <returns>Filtered logs</returns>
        public static object ReadConsole(JObject parameters)
        {
            try
            {
                if (!IsReflectionInitialized())
                {
                    return new
                    {
                        success = false,
                        error = "Console reflection not initialized properly"
                    };
                }

                // Extract parameters
                int count = parameters["count"]?.ToObject<int>() ?? 100;
                var logTypes = (parameters["logTypes"] as JArray)?.Select(t => t.ToString()).ToList() ?? new List<string> { "All" };
                string filterText = parameters["filterText"]?.ToString();
                bool includeStackTrace = parameters["includeStackTrace"]?.ToObject<bool>() ?? false;
                string format = parameters["format"]?.ToString() ?? "compact";
                string sinceTimestamp = parameters["sinceTimestamp"]?.ToString();
                string untilTimestamp = parameters["untilTimestamp"]?.ToString();
                string sortOrder = parameters["sortOrder"]?.ToString() ?? "newest";
                string groupBy = parameters["groupBy"]?.ToString() ?? "none";

                // Expand "All" to all types
                if (logTypes.Contains("All"))
                {
                    logTypes = new List<string> { "Log", "Warning", "Error", "Assert", "Exception" };
                }

                // Parse timestamps
                DateTime? sinceTime = null;
                DateTime? untilTime = null;
                if (!string.IsNullOrEmpty(sinceTimestamp))
                {
                    sinceTime = DateTime.Parse(sinceTimestamp);
                }
                if (!string.IsNullOrEmpty(untilTimestamp))
                {
                    untilTime = DateTime.Parse(untilTimestamp);
                }

                // Collect logs
                var logs = new List<object>();
                var statistics = new Dictionary<string, int>
                {
                    { "errors", 0 },
                    { "warnings", 0 },
                    { "logs", 0 },
                    { "asserts", 0 },
                    { "exceptions", 0 }
                };

                _startGettingEntriesMethod.Invoke(null, null);
                try
                {
                    int totalEntries = (int)_getCountMethod.Invoke(null, null);
                    Type logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                    object logEntryInstance = Activator.CreateInstance(logEntryType);

                    // Process entries (newest first by default)
                    for (int i = totalEntries - 1; i >= 0; i--)
                    {
                        _getEntryMethod.Invoke(null, new object[] { i, logEntryInstance });

                        // Extract log data
                        int mode = (int)_modeField.GetValue(logEntryInstance);
                        string message = (string)_messageField.GetValue(logEntryInstance);
                        string file = (string)_fileField.GetValue(logEntryInstance);
                        int line = (int)_lineField.GetValue(logEntryInstance);

                        LogType logType = GetLogTypeFromMode(mode);
                        string logTypeString = logType.ToString();

                        // Update statistics
                        switch (logType)
                        {
                            case LogType.Error: statistics["errors"]++; break;
                            case LogType.Warning: statistics["warnings"]++; break;
                            case LogType.Log: statistics["logs"]++; break;
                            case LogType.Assert: statistics["asserts"]++; break;
                            case LogType.Exception: statistics["exceptions"]++; break;
                        }

                        // Statistics describe the full console, independent of the result limit
                        // and API filters. Empty messages still contribute to Unity's counts.
                        if (logs.Count >= count || string.IsNullOrEmpty(message))
                            continue;

                        // Filter by type
                        if (!logTypes.Contains(logTypeString))
                            continue;

                        // Filter by text
                        if (!string.IsNullOrEmpty(filterText) && 
                            message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        // Extract stack trace only for returned entries
                        string stackTrace = null;
                        if (includeStackTrace)
                        {
                            stackTrace = ExtractStackTrace(message);
                            if (!string.IsNullOrEmpty(stackTrace))
                            {
                                message = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0];
                            }
                        }

                        // Create log entry based on format
                        object logEntry = CreateLogEntry(message, stackTrace, logTypeString, file, line, format);
                        logs.Add(logEntry);
                    }

                    // Reverse if oldest first
                    if (sortOrder == "oldest")
                    {
                        logs.Reverse();
                    }
                }
                finally
                {
                    _endGettingEntriesMethod.Invoke(null, null);
                }

                // Group logs if requested
                object result;
                if (groupBy != "none")
                {
                    var groupedLogs = GroupLogs(logs, groupBy);
                    result = new
                    {
                        success = true,
                        groupedLogs = groupedLogs,
                        count = logs.Count,
                        totalCaptured = (int)_getCountMethod.Invoke(null, null),
                        statistics = statistics,
                        groupBy = groupBy
                    };
                }
                else
                {
                    result = new
                    {
                        success = true,
                        logs = logs,
                        count = logs.Count,
                        totalCaptured = (int)_getCountMethod.Invoke(null, null),
                        statistics = statistics
                    };
                }

                return result;
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ConsoleHandler", $"Error reading logs: {ex}");
                return new
                {
                    success = false,
                    error = $"Failed to read logs: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Creates a log entry object based on the specified format
        /// </summary>
        private static object CreateLogEntry(string message, string stackTrace, string logType, string file, int line, string format)
        {
            switch (format)
            {
                case "compact":
                    return new
                    {
                        message = message,
                        logType = logType,
                        formattedCompact = $"[{logType}] {message}"
                    };

                case "plain":
                    return message;

                case "json":
                case "detailed":
                default:
                    var entry = new Dictionary<string, object>
                    {
                        { "message", message },
                        { "logType", logType },
                        { "file", file },
                        { "line", line },
                        { "timestamp", DateTime.UtcNow.ToString("o") }
                    };
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        entry["stackTrace"] = stackTrace;
                    }
                    return entry;
            }
        }

        /// <summary>
        /// Groups logs by specified criteria
        /// </summary>
        private static Dictionary<string, List<object>> GroupLogs(List<object> logs, string groupBy)
        {
            var grouped = new Dictionary<string, List<object>>();

            foreach (var log in logs)
            {
                string key = "unknown";
                
                if (log is Dictionary<string, object> dict)
                {
                    switch (groupBy)
                    {
                        case "type":
                            key = dict.ContainsKey("logType") ? dict["logType"].ToString() : "unknown";
                            break;
                        case "file":
                            key = dict.ContainsKey("file") ? dict["file"].ToString() : "unknown";
                            break;
                        case "time":
                            // Group by hour for simplicity
                            if (dict.ContainsKey("timestamp") && DateTime.TryParse(dict["timestamp"].ToString(), out DateTime time))
                            {
                                key = time.ToString("yyyy-MM-dd HH:00");
                            }
                            break;
                    }
                }

                if (!grouped.ContainsKey(key))
                {
                    grouped[key] = new List<object>();
                }
                grouped[key].Add(log);
            }

            return grouped;
        }

        /// <summary>
        /// Checks if reflection is properly initialized
        /// </summary>
        private static bool IsReflectionInitialized()
        {
            return _startGettingEntriesMethod != null &&
                   _endGettingEntriesMethod != null &&
                   _clearMethod != null &&
                   _getCountMethod != null &&
                   _getEntryMethod != null &&
                   _modeField != null &&
                   _messageField != null;
        }

        /// <summary>
        /// Gets severity from Unity's native mode bits, preserving Assert/Exception subtypes.
        /// </summary>
        private static LogType GetLogTypeFromMode(int mode)
        {
            if ((mode & ModeBitScriptingException) != 0)
                return LogType.Exception;
            if ((mode & (ModeBitAssert | ModeBitScriptingAssertion)) != 0)
                return LogType.Assert;
            if ((mode & (ModeBitError | ModeBitFatal | ModeBitAssetImportError |
                         ModeBitScriptingError | ModeBitScriptCompileError | ModeBitGraphCompileError)) != 0)
                return LogType.Error;
            if ((mode & (ModeBitAssetImportWarning | ModeBitScriptingWarning | ModeBitScriptCompileWarning)) != 0)
                return LogType.Warning;
            if ((mode & (ModeBitLog | ModeBitScriptingLog)) != 0)
                return LogType.Log;
            return LogType.Log;
        }

        /// <summary>
        /// Extracts stack trace from a log message
        /// </summary>
        private static string ExtractStackTrace(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage))
                return null;

            string[] lines = fullMessage.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
                return null;

            int stackStartIndex = -1;
            for (int i = 1; i < lines.Length; ++i)
            {
                string trimmedLine = lines[i].TrimStart();
                if (trimmedLine.StartsWith("at ") ||
                    trimmedLine.StartsWith("UnityEngine.") ||
                    trimmedLine.StartsWith("UnityEditor.") ||
                    trimmedLine.Contains("(at "))
                {
                    stackStartIndex = i;
                    break;
                }
            }

            if (stackStartIndex > 0)
            {
                return string.Join("\n", lines.Skip(stackStartIndex));
            }

            return null;
        }
    }
}