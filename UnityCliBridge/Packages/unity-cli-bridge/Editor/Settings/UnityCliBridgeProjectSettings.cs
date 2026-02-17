using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Settings
{
    [FilePath("ProjectSettings/UnityCliBridgeSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class UnityCliBridgeProjectSettings : ScriptableSingleton<UnityCliBridgeProjectSettings>
    {
        [SerializeField] private string unityHost = "localhost";
        [SerializeField] private int port = 6400;

        public string ResolvedUnityHost => string.IsNullOrWhiteSpace(unityHost) ? "localhost" : unityHost.Trim();
        public int ResolvedPort => (port > 0 && port < 65536) ? port : 6400;

        public void SetUnityHost(string value)
        {
            unityHost = string.IsNullOrWhiteSpace(value) ? "localhost" : value.Trim();
        }

        public void SetPort(int value)
        {
            port = Mathf.Clamp(value, 1, 65535);
        }

        public void SaveProjectSettings(bool saveAsText)
        {
            Save(saveAsText);
        }
    }
}
