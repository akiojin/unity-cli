namespace UnityMCPServer.Helpers
{
    // Compatibility bridge for legacy test scripts that still reference UnityMCPServer.* namespaces.
    public static class DebouncedAssetRefresh
    {
        public static void Request()
        {
            UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
        }
    }
}
