using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.EditorIntegration
{
    internal class UnityCliBridgeServerWindow : EditorWindow
    {
        private const string WindowTitle = "Unity CLI Bridge";

        internal static void Open()
        {
            var window = GetWindow<UnityCliBridgeServerWindow>(utility: false, title: WindowTitle, focus: true);
            window.minSize = new Vector2(320f, 180f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity CLI Bridge", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", Core.UnityCliBridge.Status.ToString());

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Start")) MenuItems.StartBridge();
                if (GUILayout.Button("Stop")) MenuItems.StopBridge();
                if (GUILayout.Button("Restart")) MenuItems.RestartBridge();
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Run Scene Sample")) MenuItems.RunSceneSample();
                if (GUILayout.Button("Run Addressables Sample")) MenuItems.RunAddressablesSample();
                if (GUILayout.Button("Cleanup Samples")) MenuItems.CleanupSample();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Open Project Settings"))
            {
                MenuItems.OpenSettings();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Bridge control and samples are disabled while Play Mode is active or changing.", MessageType.Warning);
            }
        }
    }
}
