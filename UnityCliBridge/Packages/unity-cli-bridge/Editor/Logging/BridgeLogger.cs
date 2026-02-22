using UnityEngine;

namespace UnityCliBridge.Logging
{
    /// <summary>
    /// Centralized logging utility for Unity CLI Bridge.
    /// All log messages are prefixed with [unity-cli-bridge] for easy filtering.
    /// </summary>
    public static class BridgeLogger
    {
        private const string Prefix = "[unity-cli-bridge]";

        public static void Log(string message)
        {
            Debug.Log($"{Prefix} {message}");
        }

        public static void Log(string category, string message)
        {
            Debug.Log($"{Prefix}[{category}] {message}");
        }

        public static void LogWarning(string message)
        {
            Debug.LogWarning($"{Prefix} {message}");
        }

        public static void LogWarning(string category, string message)
        {
            Debug.LogWarning($"{Prefix}[{category}] {message}");
        }

        public static void LogError(string message)
        {
            Debug.LogError($"{Prefix} {message}");
        }

        public static void LogError(string category, string message)
        {
            Debug.LogError($"{Prefix}[{category}] {message}");
        }

        public static void LogException(System.Exception ex)
        {
            Debug.LogError($"{Prefix} Exception: {ex.Message}\n{ex.StackTrace}");
        }

        public static void LogException(string category, System.Exception ex)
        {
            Debug.LogError($"{Prefix}[{category}] Exception: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
