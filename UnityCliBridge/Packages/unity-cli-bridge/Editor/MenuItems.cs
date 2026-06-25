using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.EditorIntegration
{
    internal static class MenuItems
    {
        internal const string ServerWindowPath = "Tools/Unity CLI/Server Window";
        internal const string StartBridgePath = "Tools/Unity CLI/Start Bridge";
        internal const string StopBridgePath = "Tools/Unity CLI/Stop Bridge";
        internal const string RestartBridgePath = "Tools/Unity CLI/Restart Bridge";
        internal const string OpenSettingsPath = "Tools/Unity CLI/Open Settings";
        internal const string RunSceneSamplePath = "Tools/Unity CLI/Run Sample/Scene";
        internal const string RunAddressablesSamplePath = "Tools/Unity CLI/Run Sample/Addressables";
        internal const string CleanupSamplePath = "Tools/Unity CLI/Run Sample/Cleanup";

        [MenuItem(ServerWindowPath, priority = 1000)]
        internal static void OpenServerWindow()
        {
            UnityCliBridgeServerWindow.Open();
        }

        [MenuItem(StartBridgePath, priority = 1010)]
        internal static void StartBridge()
        {
            Core.UnityCliBridge.Start();
        }

        [MenuItem(StartBridgePath, validate = true)]
        internal static bool CanStartBridge() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        [MenuItem(StopBridgePath, priority = 1011)]
        internal static void StopBridge()
        {
            Core.UnityCliBridge.Stop();
        }

        [MenuItem(StopBridgePath, validate = true)]
        internal static bool CanStopBridge() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        [MenuItem(RestartBridgePath, priority = 1012)]
        internal static void RestartBridge()
        {
            Core.UnityCliBridge.Restart();
        }

        [MenuItem(RestartBridgePath, validate = true)]
        internal static bool CanRestartBridge() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        [MenuItem(OpenSettingsPath, priority = 1020)]
        internal static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Unity CLI Bridge");
        }

        [MenuItem(RunSceneSamplePath, priority = 1030)]
        internal static void RunSceneSample()
        {
            SampleWorkflows.RunSceneSample();
        }

        [MenuItem(RunSceneSamplePath, validate = true)]
        internal static bool CanRunSceneSample() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        [MenuItem(RunAddressablesSamplePath, priority = 1031)]
        internal static void RunAddressablesSample()
        {
            SampleWorkflows.RunAddressablesSample();
        }

        [MenuItem(RunAddressablesSamplePath, validate = true)]
        internal static bool CanRunAddressablesSample() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        [MenuItem(CleanupSamplePath, priority = 1032)]
        internal static void CleanupSample()
        {
            SampleWorkflows.Cleanup();
        }

        [MenuItem(CleanupSamplePath, validate = true)]
        internal static bool CanCleanupSample() => CanRunEditorStateCommand(EditorApplication.isPlayingOrWillChangePlaymode);

        internal static bool CanRunEditorStateCommand(bool isPlayingOrWillChangePlaymode)
        {
            return !isPlayingOrWillChangePlaymode;
        }
    }
}
