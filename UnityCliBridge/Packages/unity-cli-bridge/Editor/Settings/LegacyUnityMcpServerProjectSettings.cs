using UnityEditor;

namespace UnityMCPServer.Settings
{
    // Compatibility wrapper so legacy serialized ProjectSettings asset can still resolve.
    [FilePath("ProjectSettings/UnityMcpServerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class UnityMcpServerProjectSettings : UnityCliBridge.Settings.UnityCliBridgeProjectSettings
    {
    }
}
