using UnityEditor;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Centralizes AssetDatabase refresh behavior for script-editing tools.
    /// - Default: debounced (coalesce calls)
    /// - Immediate: throttled to avoid back-to-back refresh storms
    /// - None: skip refresh (caller handles)
    /// </summary>
    public static class RefreshController
    {
        private static double _lastImmediateAt = -100.0;
        private const double ImmediateCooldownSec = 1.2; // throttle window

        public static void Debounced()
        {
            DebouncedAssetRefresh.Request();
        }

        public static void ImmediateThrottled()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastImmediateAt >= ImmediateCooldownSec)
            {
                _lastImmediateAt = now;
                AssetDatabase.Refresh();
            }
            else
            {
                Debounced();
            }
        }
    }
}

