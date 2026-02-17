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

        // Mode bits for log type detection
        // These values are based on Unity's internal LogEntry mode field
        private const int ModeBitError = 1 << 0;          // 0x00000001
        private const int ModeBitAssert = 1 << 1;         // 0x00000002
        private const int ModeBitWarning = 1 << 2;        // 0x00000004
        private const int ModeBitLog = 1 << 3;            // 0x00000008
        private const int ModeBitFatal = 1 << 4;          // 0x00000010 (Fatal/Exception)
        
        // Additional flags for scripting logs
        private const int ModeBitScriptingError = 1 << 9;      // 0x00000200
        private const int ModeBitScriptingWarning = 1 << 10;   // 0x00000400
        private const int ModeBitScriptingLog = 1 << 11;       // 0x00000800
        private const int ModeBitScriptingException = 1 << 18;  // 0x00040000
        private const int ModeBitScriptingAssertion = 1 << 22;  // 0x00400000
        
        // Alternative Exception bit (sometimes used)
        private const int ModeBitException = ModeBitFatal;
        
        // Debug flag for logging mode bit analysis
        private static bool _debugModeBits = false;

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
                    McpLogger.LogError("ConsoleHandler", "Could not find internal type UnityEditor.LogEntries");
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
                    McpLogger.LogError("ConsoleHandler", "Could not find internal type UnityEditor.LogEntry");
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
                McpLogger.LogError("ConsoleHandler", $"Failed to initialize reflection: {ex}");
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
                    McpLogger.LogWarning("ConsoleHandler", "Selective log preservation is not fully implemented in Unity's console API");
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
                McpLogger.LogError("ConsoleHandler", $"Error clearing console: {ex}");
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
                    for (int i = totalEntries - 1; i >= 0 && logs.Count < count; i--)
                    {
                        _getEntryMethod.Invoke(null, new object[] { i, logEntryInstance });

                        // Extract log data
                        int mode = (int)_modeField.GetValue(logEntryInstance);
                        string message = (string)_messageField.GetValue(logEntryInstance);
                        string file = (string)_fileField.GetValue(logEntryInstance);
                        int line = (int)_lineField.GetValue(logEntryInstance);

                        if (string.IsNullOrEmpty(message))
                            continue;

                        // Extract stack trace early for Assert detection
                        // Note: For some logs, the entire message IS the stack trace
                        string fullStackTrace = ExtractStackTrace(message);
                        
                        // Check if message itself contains stack trace indicators
                        bool messageContainsStackTrace = message.Contains("\n") && 
                            (message.Contains("UnityEngine.Debug:Assert") || 
                             message.Contains("(at ") || 
                             message.Contains(".cs:"));
                        
                        // Determine log type (pass both stack trace and flag)
                        LogType logType = GetLogTypeFromMode(mode, message, fullStackTrace, messageContainsStackTrace);
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

                        // Filter by type
                        if (!logTypes.Contains(logTypeString))
                            continue;

                        // Filter by text
                        if (!string.IsNullOrEmpty(filterText) && 
                            message.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        // Extract stack trace if present (using fullStackTrace from earlier)
                        string stackTrace = null;
                        if (includeStackTrace)
                        {
                            stackTrace = fullStackTrace;
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
                McpLogger.LogError("ConsoleHandler", $"Error reading logs: {ex}");
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
        /// Gets LogType from mode bits, message content, and stack trace
        /// </summary>
        private static LogType GetLogTypeFromMode(int mode, string message = null, string stackTrace = null, bool messageContainsStackTrace = false)
        {
            // Log mode bits for debugging (only for specific messages)
            if (_debugModeBits && !string.IsNullOrEmpty(message))
            {
                // Check if this is one of the problematic assert messages
                if (message.Contains("InputSystemActions") &&
                    (message.Contains(".Disable() has not been called") || message.Contains("This will cause a leak")))
                {
                    McpLogger.Log("ConsoleHandler", $"Debug - Assert message detected: '{message.Split('\n')[0]}', Mode bits: 0x{mode:X8}");
                }
            }
            
            // Special handling for Assert messages that may have different mode values
            // Check stack trace for Assert patterns (more reliable than message)
            if (!string.IsNullOrEmpty(stackTrace))
            {
                // Check for UnityEngine.Debug:Assert in stack trace
                if (stackTrace.Contains("UnityEngine.Debug:Assert"))
                {
                    if (_debugModeBits)
                    {
                        McpLogger.Log("ConsoleHandler", $"Stack trace indicates Assert (UnityEngine.Debug:Assert found), Mode: 0x{mode:X8}");
                    }
                    return LogType.Assert;
                }
            }
            
            // If message contains stack trace info, check it directly
            if (messageContainsStackTrace && !string.IsNullOrEmpty(message))
            {
                // The message itself contains the stack trace
                if (message.Contains("UnityEngine.Debug:Assert"))
                {
                    if (_debugModeBits)
                    {
                        McpLogger.Log("ConsoleHandler", $"Message contains Assert stack trace, Mode: 0x{mode:X8}");
                    }
                    return LogType.Assert;
                }
            }
            
            // Also check message content for Assert patterns
            if (!string.IsNullOrEmpty(message))
            {
                // Check for explicit Assert patterns in the message
                if (message.Contains("UnityEngine.Debug:Assert") ||
                    (message.Contains("Assertion failed") || message.Contains("Assert(")))
                {
                    if (_debugModeBits)
                    {
                        McpLogger.Log("ConsoleHandler", $"Message pattern indicates Assert, Mode: 0x{mode:X8}");
                    }
                    return LogType.Assert;
                }
            }
            
            // Check for Fatal/Exception first (most specific)
            // Fatal bit (1 << 4) is often used for exceptions
            if ((mode & ModeBitFatal) != 0 || (mode & ModeBitScriptingException) != 0)
            {
                // Additional check: some Asserts may have Fatal bit set
                if (!string.IsNullOrEmpty(message) && message.Contains("UnityEngine.Debug:Assert"))
                {
                    return LogType.Assert;
                }
                return LogType.Exception;
            }
            // Check for Assert - expanded check for various Assert patterns
            else if ((mode & (ModeBitAssert | ModeBitScriptingAssertion)) != 0 ||
                     (mode & 0x00000002) != 0 ||  // Direct check for bit 1
                     (mode & 0x00400000) != 0)     // Direct check for bit 22
            {
                return LogType.Assert;
            }
            // Check for Error
            else if ((mode & (ModeBitError | ModeBitScriptingError)) != 0)
            {
                // Double check: some Asserts may be misclassified as Errors
                if (!string.IsNullOrEmpty(message) &&
                    (message.Contains("UnityEngine.Debug:Assert") || message.Contains("Assertion")))
                {
                    if (_debugModeBits)
                    {
                        McpLogger.Log("ConsoleHandler", $"Reclassifying Error as Assert based on message, Mode: 0x{mode:X8}");
                    }
                    return LogType.Assert;
                }
                return LogType.Error;
            }
            // Check for Warning
            else if ((mode & (ModeBitWarning | ModeBitScriptingWarning)) != 0)
            {
                return LogType.Warning;
            }
            // Check for regular Log
            else if ((mode & (ModeBitLog | ModeBitScriptingLog)) != 0)
            {
                return LogType.Log;
            }
            // Default case - try to infer from message if available
            else
            {
                if (!string.IsNullOrEmpty(message))
                {
                    if (message.Contains("UnityEngine.Debug:Assert") || message.Contains("Assertion"))
                        return LogType.Assert;
                    if (message.Contains("Exception") || message.Contains("Error"))
                        return LogType.Error;
                    if (message.Contains("Warning"))
                        return LogType.Warning;
                }
                return LogType.Log;
            }
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