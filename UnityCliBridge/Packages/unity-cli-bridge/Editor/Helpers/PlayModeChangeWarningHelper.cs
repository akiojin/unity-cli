using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliBridge.Helpers
{
    /// <summary>
    /// Provides standardized warnings for commands that mutate scene objects during Play Mode.
    /// </summary>
    public static class PlayModeChangeWarningHelper
    {
        private const string WarningCode = "PLAY_MODE_RUNTIME_CHANGES";
        private const string WarningMessage = "Changes made while Play Mode is running are temporary and will be lost when Play Mode stops. Re-apply the change in Edit Mode to persist it.";

        private static readonly Dictionary<string, Func<JObject, bool>> WarningPredicates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["add_component"] = RequiresSceneTarget,
            ["remove_component"] = RequiresSceneTarget,
            ["modify_component"] = RequiresSceneTarget,
            ["set_component_field"] = ShouldWarnSetComponentField,
            ["instantiate_prefab"] = _ => true,
            // Future-proofing for potential scene mutation commands becoming playable again
            ["create_gameobject"] = _ => true,
            ["modify_gameobject"] = _ => true,
            ["delete_gameobject"] = _ => true
        };

        /// <summary>
        /// Allows tests to override Play Mode detection.
        /// </summary>
        public static Func<bool> PlayModeDetector = () => Application.isPlaying;

        /// <summary>
        /// Returns warning payloads when the specified command mutates scene data during Play Mode.
        /// </summary>
        public static IReadOnlyList<Dictionary<string, object>> GetWarnings(string commandType, JObject parameters)
        {
            if (string.IsNullOrEmpty(commandType))
            {
                return null;
            }

            if (!(PlayModeDetector?.Invoke() ?? Application.isPlaying))
            {
                return null;
            }

            if (!WarningPredicates.TryGetValue(commandType, out var predicate))
            {
                return null;
            }

            var targetParameters = parameters ?? new JObject();
            if (predicate != null && !predicate(targetParameters))
            {
                return null;
            }

            return new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["code"] = WarningCode,
                    ["message"] = WarningMessage,
                    ["severity"] = "warning",
                    ["tool"] = commandType
                }
            };
        }

        private static bool RequiresSceneTarget(JObject parameters)
        {
            var gameObjectPath = parameters?["gameObjectPath"]?.ToString();
            return !string.IsNullOrEmpty(gameObjectPath);
        }

        private static bool ShouldWarnSetComponentField(JObject parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            var prefabAssetPath = parameters["prefabAssetPath"]?.ToString();
            if (!string.IsNullOrEmpty(prefabAssetPath))
            {
                return false;
            }

            var scope = parameters["scope"]?.ToString();
            if (!string.IsNullOrEmpty(scope) &&
                (scope.Equals("prefabAsset", StringComparison.OrdinalIgnoreCase) ||
                 scope.Equals("prefabStage", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var hasSceneTarget = !string.IsNullOrEmpty(parameters["gameObjectPath"]?.ToString());
            if (!hasSceneTarget)
            {
                return false;
            }

            var runtime = parameters["runtime"]?.ToObject<bool>() ?? false;
            var dryRun = parameters["dryRun"]?.ToObject<bool>() ?? false;

            return runtime && !dryRun;
        }
    }
}
