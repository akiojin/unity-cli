using UnityEditor;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Coalesces multiple refresh requests into a single AssetDatabase.Refresh call.
    /// Reduces editor stalls from frequent refreshes across handlers.
    /// </summary>
    public static class DebouncedAssetRefresh
    {
        private static bool _scheduled;

        /// <summary>
        /// Request a debounced AssetDatabase.Refresh. Multiple calls within the same update frame
        /// (or before the scheduled callback runs) will be coalesced into a single refresh.
        /// </summary>
        public static void Request()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            _scheduled = false;
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            AssetDatabase.Refresh();
        }
    }
}
