using UnityEditor;

namespace UnityCliBridge.Tests.Editor
{
    // Editor settings are readable through the bridge but have no generic write API.
    // SessionState keeps the original settings available through the tested reloads.
    internal static class DomainReloadE2ESettings
    {
        private const string Key = "UnityCliBridge.DomainReloadE2E.";
        private const string Menu = "Tools/Unity CLI/Test Runner E2E/";

        [MenuItem(Menu + "Enable Domain Reload")]
        private static void EnableDomainReload()
        {
            RememberSettings();
            EditorSettings.enterPlayModeOptionsEnabled = false;
        }

        [MenuItem(Menu + "Disable Domain Reload")]
        private static void DisableDomainReload()
        {
            RememberSettings();
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSettings.enterPlayModeOptionsEnabled = true;
        }

        private static void RememberSettings()
        {
            if (SessionState.GetBool(Key + "Saved", false)) return;
            SessionState.SetBool(Key + "Enabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Key + "Options", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(Key + "Saved", true);
        }

        [MenuItem(Menu + "Restore Settings")]
        private static void RestoreSettings()
        {
            if (!SessionState.GetBool(Key + "Saved", false)) return;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + "Options", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + "Enabled", false);
            SessionState.EraseBool(Key + "Saved");
        }
    }
}
