using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityCliBridge.Settings
{
    internal class UnityCliBridgeSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/Unity CLI Bridge";
        private const string ServerScriptPath = "Packages/unity-cli-bridge/Editor/Core/UnityCliBridgeHost.cs";

        private SerializedObject _serializedSettings;

        public UnityCliBridgeSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var settings = UnityCliBridgeProjectSettings.instance;
            if (settings != null)
            {
                _serializedSettings = new SerializedObject(settings);
            }
        }

        public override void OnGUI(string searchContext)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                EditorGUILayout.HelpBox("Failed to load Unity CLI Bridge settings.", MessageType.Error);
                return;
            }

            if (!_serializedSettings.hasModifiedProperties)
            {
                _serializedSettings.Update();
            }

            EditorGUILayout.LabelField("TCP Listener", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("unityHost"), new GUIContent("Host"));
            EditorGUILayout.LabelField("", "CLI env: UNITY_CLI_HOST", EditorStyles.miniLabel);
            
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("port"), new GUIContent("Port"));
            EditorGUILayout.LabelField("", "CLI env: UNITY_CLI_PORT", EditorStyles.miniLabel);

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "These settings control where Unity listens for CLI TCP connections.\n" +
                "unity-cli connects using UNITY_CLI_HOST and UNITY_CLI_PORT.\n" +
                "Only UNITY_CLI_* environment variables are supported.",
                MessageType.Info);

            EditorGUILayout.Space();
            DrawCliEnvironmentVariables();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!_serializedSettings.hasModifiedProperties))
            {
                if (GUILayout.Button("Apply & Restart"))
                {
                    _serializedSettings.ApplyModifiedProperties();

                    var settings = (UnityCliBridgeProjectSettings)_serializedSettings.targetObject;
                    settings.SaveProjectSettings(true);

                    UnityCliBridge.Core.UnityCliBridge.Restart();
                    TriggerReimport();
                }
            }
        }

        private static void DrawCliEnvironmentVariables()
        {
            EditorGUILayout.LabelField("CLI Environment Variables (Reference)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These environment variables configure unity-cli and the Unity TCP bridge.\n" +
                "Set them in your shell or .env file before starting unity-cli.",
                MessageType.None);
            
            EditorGUI.indentLevel++;
            
            // Connection
            EditorGUILayout.LabelField("Connection", EditorStyles.miniBoldLabel);
            DrawEnvVarRow("UNITY_CLI_HOST", "Unity TCP host (default: localhost)");
            DrawEnvVarRow("UNITY_CLI_PORT", "Unity TCP port (default: 6400)");
            DrawEnvVarRow("UNITY_CLI_TIMEOUT_MS", "Command timeout milliseconds");
            
            EditorGUILayout.Space(4);
            
            // Logging & Diagnostics
            EditorGUILayout.LabelField("Logging", EditorStyles.miniBoldLabel);
            DrawEnvVarRow("UNITY_CLI_LOG_LEVEL", "debug|info|warn|error (optional)");
            
            EditorGUILayout.Space(4);
            
            // HTTP Transport
            EditorGUILayout.LabelField("HTTP Transport", EditorStyles.miniBoldLabel);
            DrawEnvVarRow("UNITY_CLI_HTTP_ENABLED", "true|false (optional)");
            DrawEnvVarRow("UNITY_CLI_HTTP_PORT", "HTTP port (optional)");
            
            EditorGUILayout.Space(4);
            
            // Advanced
            EditorGUILayout.LabelField("Advanced", EditorStyles.miniBoldLabel);
            DrawEnvVarRow("UNITY_CLI_LSP_REQUEST_TIMEOUT_MS", "LSP timeout ms (default: 60000)");
            DrawEnvVarRow("UNITY_CLI_TELEMETRY_ENABLED", "true|false (default: false)");
            DrawEnvVarRow("UNITY_PROJECT_ROOT", "Unity project path (auto-detected)");

            EditorGUI.indentLevel--;
        }

        private static void DrawEnvVarRow(string varName, string description)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(varName, GUILayout.Width(280), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField(description, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new UnityCliBridgeSettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Unity CLI Bridge",
                keywords = new HashSet<string>(new[] { "Unity", "CLI", "Bridge", "TCP", "Host", "Port" })
            };
        }

        private static void TriggerReimport()
        {
            try
            {
                AssetDatabase.ImportAsset(ServerScriptPath, ImportAssetOptions.ForceUpdate);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
