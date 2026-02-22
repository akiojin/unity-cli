using UnityEditor;

namespace UnityMCPServer.Settings
{
    /// <summary>
    /// Compatibility shim that keeps legacy <c>UnityMCPServer.Settings</c> settings deserialization working.
    /// This type maps legacy <c>ProjectSettings/UnityMcpServerSettings.asset</c> data to
    /// <see cref="UnityCliBridge.Settings.UnityCliBridgeProjectSettings"/> for projects migrated from
    /// the old unity-mcp-server package.
    /// Remove only after all supported projects no longer contain
    /// <c>ProjectSettings/UnityMcpServerSettings.asset</c> and have fully migrated to
    /// <c>ProjectSettings/UnityCliBridgeSettings.asset</c>.
    /// </summary>
    [FilePath("ProjectSettings/UnityMcpServerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class UnityMcpServerProjectSettings : UnityCliBridge.Settings.UnityCliBridgeProjectSettings
    {
    }
}
