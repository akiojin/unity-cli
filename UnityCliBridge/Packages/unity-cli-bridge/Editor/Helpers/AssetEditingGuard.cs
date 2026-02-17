using UnityEditor;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Safe guard around AssetDatabase.StartAssetEditing/StopAssetEditing.
    /// - Prevents nested calls
    /// - Ensures Stop is called on assembly reload/quitting
    /// - Persists a sentinel in EditorPrefs to recover across reload if needed
    /// </summary>
    [InitializeOnLoad]
    public static class AssetEditingGuard
    {
        private const string SentinelKey = "UnityMCP.AssetEditing.Active";
        private static int _depth;

        static AssetEditingGuard()
        {
            // Recover if previous session left sentinel active (after reload/startup)
            if (EditorPrefs.GetBool(SentinelKey, false))
            {
                TryStop();
            }

            // IMPORTANT: Do not call AssetDatabase.StopAssetEditing() inside beforeAssemblyReload callback
            // to avoid stalls. Just clear the sentinel; actual Stop will run on next domain init.
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                EditorPrefs.SetBool(SentinelKey, false);
                _depth = 0;
            };
            EditorApplication.quitting += TryStop;
        }

        public static bool IsActive => _depth > 0;

        public static void Begin()
        {
            if (_depth == 0)
            {
                AssetDatabase.StartAssetEditing();
                EditorPrefs.SetBool(SentinelKey, true);
            }
            _depth++;
        }

        public static void End()
        {
            if (_depth == 0) return;
            _depth--;
            if (_depth == 0)
            {
                TryStop();
            }
        }

        private static void TryStop()
        {
            // Best-effort: Stop only if sentinel was set or depth suggests active
            try { AssetDatabase.StopAssetEditing(); } catch { }
            EditorPrefs.SetBool(SentinelKey, false);
            _depth = 0;
        }
    }
}
