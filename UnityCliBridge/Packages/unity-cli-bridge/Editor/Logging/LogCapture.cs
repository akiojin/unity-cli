using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityCliBridge.Logging
{
    /// <summary>
    /// Captures Unity console logs for external access
    /// </summary>
    public static class LogCapture
    {
        private static readonly object lockObj = new object();
        private static readonly Queue<LogEntry> logQueue = new Queue<LogEntry>();
        private static readonly int maxLogs = 1000; // Keep last 1000 logs
        private static bool isCapturing = false;

        public struct LogEntry
        {
            public string message;
            public string stackTrace;
            public LogType logType;
            public DateTime timestamp;
        }

        static LogCapture()
        {
            StartCapture();
        }

        public static void StartCapture()
        {
            if (!isCapturing)
            {
                Application.logMessageReceived += HandleLog;
                isCapturing = true;
                BridgeLogger.Log("Log capture started");
            }
        }

        public static void StopCapture()
        {
            if (isCapturing)
            {
                Application.logMessageReceived -= HandleLog;
                isCapturing = false;
            }
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
            lock (lockObj)
            {
                // Add new log entry
                logQueue.Enqueue(new LogEntry
                {
                    message = logString,
                    stackTrace = stackTrace,
                    logType = type,
                    timestamp = DateTime.Now
                });

                // Remove old logs if we exceed max
                while (logQueue.Count > maxLogs)
                {
                    logQueue.Dequeue();
                }
            }
        }

        public static List<LogEntry> GetLogs(int count = 100, LogType? filterType = null)
        {
            lock (lockObj)
            {
                var result = new List<LogEntry>();
                var logs = logQueue.ToArray();
                
                // Get logs from most recent
                for (int i = logs.Length - 1; i >= 0 && result.Count < count; i--)
                {
                    if (filterType == null || logs[i].logType == filterType)
                    {
                        result.Add(logs[i]);
                    }
                }

                return result;
            }
        }

        public static void ClearLogs()
        {
            lock (lockObj)
            {
                logQueue.Clear();
            }
        }
    }
}