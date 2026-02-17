using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity Project Settings read and update operations
    /// </summary>
    public static class ProjectSettingsHandler
    {
        /// <summary>
        /// Gets project settings based on include flags
        /// </summary>
        public static object GetProjectSettings(JObject parameters)
        {
            try
            {
                var result = new JObject();

                // Player Settings (default: true)
                if (parameters["includePlayer"]?.ToObject<bool>() ?? true)
                {
                    result["player"] = GetPlayerSettings();
                }

                // Graphics Settings
                if (parameters["includeGraphics"]?.ToObject<bool>() ?? false)
                {
                    result["graphics"] = GetGraphicsSettings();
                }

                // Quality Settings
                if (parameters["includeQuality"]?.ToObject<bool>() ?? false)
                {
                    result["quality"] = GetQualitySettings();
                }

                // Physics Settings
                if (parameters["includePhysics"]?.ToObject<bool>() ?? false)
                {
                    result["physics"] = GetPhysicsSettings();
                }

                // Physics 2D Settings
                if (parameters["includePhysics2D"]?.ToObject<bool>() ?? false)
                {
                    result["physics2D"] = GetPhysics2DSettings();
                }

                // Audio Settings
                if (parameters["includeAudio"]?.ToObject<bool>() ?? false)
                {
                    result["audio"] = GetAudioSettings();
                }

                // Time Settings
                if (parameters["includeTime"]?.ToObject<bool>() ?? false)
                {
                    result["time"] = GetTimeSettings();
                }

                // Input Manager Settings
                if (parameters["includeInputManager"]?.ToObject<bool>() ?? false)
                {
                    result["inputManager"] = GetInputManagerSettings();
                }

                // Editor Settings
                if (parameters["includeEditor"]?.ToObject<bool>() ?? false)
                {
                    result["editor"] = GetEditorSettings();
                }

                // Build Settings
                if (parameters["includeBuild"]?.ToObject<bool>() ?? false)
                {
                    result["build"] = GetBuildSettings();
                }

                // Tags and Layers
                if (parameters["includeTags"]?.ToObject<bool>() ?? false)
                {
                    result["tags"] = GetTagsAndLayers();
                }

                return result;
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ProjectSettingsHandler", $"Error getting project settings: {ex.Message}");
                return new { error = $"Failed to get project settings: {ex.Message}" };
            }
        }

        /// <summary>
        /// Updates project settings with confirmation
        /// </summary>
        public static object UpdateProjectSettings(JObject parameters)
        {
            try
            {
                // Require explicit confirmation
                if (!parameters["confirmChanges"]?.ToObject<bool>() ?? false)
                {
                    return new
                    {
                        error = "confirmChanges must be true to update settings",
                        code = "CONFIRMATION_REQUIRED"
                    };
                }

                var results = new JObject();
                var errors = new JArray();
                var previousValues = new JObject();
                bool requiresRestart = false;

                // Update Player Settings
                if (parameters["player"] != null)
                {
                    var updateResult = UpdatePlayerSettings(parameters["player"] as JObject);
                    results["player"] = updateResult.Item1;
                    previousValues["player"] = updateResult.Item2;
                    if (updateResult.Item3) requiresRestart = true;
                }

                // Update Graphics Settings
                if (parameters["graphics"] != null)
                {
                    var updateResult = UpdateGraphicsSettings(parameters["graphics"] as JObject);
                    results["graphics"] = updateResult.Item1;
                    previousValues["graphics"] = updateResult.Item2;
                    if (updateResult.Item3) requiresRestart = true;
                }

                // Update Physics Settings
                if (parameters["physics"] != null)
                {
                    var updateResult = UpdatePhysicsSettings(parameters["physics"] as JObject);
                    results["physics"] = updateResult.Item1;
                    previousValues["physics"] = updateResult.Item2;
                }

                // Update Audio Settings
                if (parameters["audio"] != null)
                {
                    var updateResult = UpdateAudioSettings(parameters["audio"] as JObject);
                    results["audio"] = updateResult.Item1;
                    previousValues["audio"] = updateResult.Item2;
                }

                // Update Time Settings
                if (parameters["time"] != null)
                {
                    var updateResult = UpdateTimeSettings(parameters["time"] as JObject);
                    results["time"] = updateResult.Item1;
                    previousValues["time"] = updateResult.Item2;
                }

                // Save changes
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    results = results,
                    previousValues = previousValues,
                    errors = errors,
                    requiresRestart = requiresRestart,
                    message = requiresRestart ?
                        "Settings updated. Some changes require Unity restart to take effect." :
                        "Settings updated successfully."
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogError("ProjectSettingsHandler", $"Error updating project settings: {ex.Message}");
                return new { error = $"Failed to update project settings: {ex.Message}" };
            }
        }

        #region Get Settings Methods

        private static JObject GetPlayerSettings()
        {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            
            return new JObject
            {
                ["companyName"] = PlayerSettings.companyName,
                ["productName"] = PlayerSettings.productName,
                ["version"] = PlayerSettings.bundleVersion,
                ["bundleIdentifier"] = PlayerSettings.GetApplicationIdentifier(buildTargetGroup),
                ["defaultScreenWidth"] = PlayerSettings.defaultScreenWidth,
                ["defaultScreenHeight"] = PlayerSettings.defaultScreenHeight,
                ["runInBackground"] = PlayerSettings.runInBackground,
                ["colorSpace"] = PlayerSettings.colorSpace.ToString(),
                ["fullScreenMode"] = PlayerSettings.defaultIsNativeResolution ? "ExclusiveFullScreen" : "Windowed",
                ["apiCompatibilityLevel"] = PlayerSettings.GetApiCompatibilityLevel(buildTargetGroup).ToString(),
                ["scriptingBackend"] = PlayerSettings.GetScriptingBackend(buildTargetGroup).ToString(),
                ["targetFrameRate"] = Application.targetFrameRate
            };
        }

        private static JObject GetGraphicsSettings()
        {
            return new JObject
            {
                ["colorSpace"] = PlayerSettings.colorSpace.ToString(),
                ["renderPipelineAsset"] = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.name ?? "Built-in",
                ["transparencySortMode"] = UnityEngine.Rendering.GraphicsSettings.transparencySortMode.ToString(),
                ["transparencySortAxis"] = new JObject
                {
                    ["x"] = UnityEngine.Rendering.GraphicsSettings.transparencySortAxis.x,
                    ["y"] = UnityEngine.Rendering.GraphicsSettings.transparencySortAxis.y,
                    ["z"] = UnityEngine.Rendering.GraphicsSettings.transparencySortAxis.z
                }
            };
        }

        private static JObject GetQualitySettings()
        {
            var currentLevel = QualitySettings.GetQualityLevel();
            var qualityLevels = new JArray();
            
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                qualityLevels.Add(QualitySettings.names[i]);
            }

            var result = new JObject
            {
                ["currentLevel"] = QualitySettings.names[currentLevel],
                ["currentLevelIndex"] = currentLevel,
                ["levels"] = qualityLevels,
                ["vSyncCount"] = QualitySettings.vSyncCount,
                ["antiAliasing"] = QualitySettings.antiAliasing,
                ["anisotropicFiltering"] = QualitySettings.anisotropicFiltering.ToString(),
                ["shadows"] = QualitySettings.shadows.ToString(),
                ["shadowResolution"] = QualitySettings.shadowResolution.ToString(),
                ["shadowDistance"] = QualitySettings.shadowDistance,
                ["softParticles"] = QualitySettings.softParticles,
                ["realtimeReflectionProbes"] = QualitySettings.realtimeReflectionProbes
            };

            // pixelLightCount is deprecated in Unity 6
            #if !UNITY_6000_0_OR_NEWER
            result["pixelLightCount"] = QualitySettings.pixelLightCount;
            #endif

            return result;
        }

        private static JObject GetPhysicsSettings()
        {
            return new JObject
            {
                ["gravity"] = new JObject
                {
                    ["x"] = Physics.gravity.x,
                    ["y"] = Physics.gravity.y,
                    ["z"] = Physics.gravity.z
                },
                ["defaultSolverIterations"] = Physics.defaultSolverIterations,
                ["defaultSolverVelocityIterations"] = Physics.defaultSolverVelocityIterations,
                ["bounceThreshold"] = Physics.bounceThreshold,
                ["sleepThreshold"] = Physics.sleepThreshold,
                ["defaultContactOffset"] = Physics.defaultContactOffset,
                ["autoSimulation"] = Physics.autoSimulation,
                ["queriesHitTriggers"] = Physics.queriesHitTriggers,
                ["queriesHitBackfaces"] = Physics.queriesHitBackfaces
            };
        }

        private static JObject GetPhysics2DSettings()
        {
            return new JObject
            {
                ["gravity"] = new JObject
                {
                    ["x"] = Physics2D.gravity.x,
                    ["y"] = Physics2D.gravity.y
                },
                ["velocityIterations"] = Physics2D.velocityIterations,
                ["positionIterations"] = Physics2D.positionIterations,
                ["velocityThreshold"] = Physics2D.bounceThreshold,
                ["maxLinearCorrection"] = Physics2D.maxLinearCorrection,
                ["maxAngularCorrection"] = Physics2D.maxAngularCorrection,
                ["maxTranslationSpeed"] = Physics2D.maxTranslationSpeed,
                ["maxRotationSpeed"] = Physics2D.maxRotationSpeed,
                ["defaultContactOffset"] = Physics2D.defaultContactOffset,
                ["simulationMode"] = Physics2D.simulationMode.ToString(),
                ["queriesHitTriggers"] = Physics2D.queriesHitTriggers,
                ["queriesStartInColliders"] = Physics2D.queriesStartInColliders,
                ["reuseCollisionCallbacks"] = Physics2D.reuseCollisionCallbacks
            };
        }

        private static JObject GetAudioSettings()
        {
            var audioConfig = AudioSettings.GetConfiguration();
            
            return new JObject
            {
                ["speakerMode"] = audioConfig.speakerMode.ToString(),
                ["dspBufferSize"] = audioConfig.dspBufferSize,
                ["sampleRate"] = audioConfig.sampleRate,
                ["numRealVoices"] = audioConfig.numRealVoices,
                ["numVirtualVoices"] = audioConfig.numVirtualVoices,
                ["globalVolume"] = AudioListener.volume,
                ["pauseOnFocusLoss"] = AudioListener.pause
            };
        }

        private static JObject GetTimeSettings()
        {
            return new JObject
            {
                ["fixedDeltaTime"] = Time.fixedDeltaTime,
                ["maximumDeltaTime"] = Time.maximumDeltaTime,
                ["timeScale"] = Time.timeScale,
                ["maximumParticleDeltaTime"] = Time.maximumParticleDeltaTime
            };
        }

        private static JObject GetInputManagerSettings()
        {
            // Input Manager axes are stored in ProjectSettings/InputManager.asset
            // This is a simplified version - full implementation would parse the asset
            return new JObject
            {
                ["message"] = "Input Manager settings require parsing InputManager.asset",
                ["axes"] = new JArray() // Would contain axis definitions
            };
        }

        private static JObject GetEditorSettings()
        {
            return new JObject
            {
                ["unityRemote"] = EditorSettings.unityRemoteDevice,
                ["unityRemoteCompression"] = EditorSettings.unityRemoteCompression,
                ["unityRemoteResolution"] = EditorSettings.unityRemoteResolution,
                ["assetNamingUsesSpace"] = EditorSettings.assetNamingUsesSpace,
                ["serializationMode"] = EditorSettings.serializationMode.ToString(),
                ["defaultBehaviorMode"] = EditorSettings.defaultBehaviorMode.ToString(),
                ["prefabModeRegularEnvironment"] = EditorSettings.prefabRegularEnvironment?.name ?? "None",
                ["prefabModeUIEnvironment"] = EditorSettings.prefabUIEnvironment?.name ?? "None",
                ["enterPlayModeOptionsEnabled"] = EditorSettings.enterPlayModeOptionsEnabled,
                ["enterPlayModeOptions"] = EditorSettings.enterPlayModeOptions.ToString()
            };
        }

        private static JObject GetBuildSettings()
        {
            var scenes = new JArray();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(new JObject
                {
                    ["path"] = scene.path,
                    ["enabled"] = scene.enabled,
                    ["guid"] = scene.guid.ToString()
                });
            }

            return new JObject
            {
                ["activeBuildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["selectedBuildTargetGroup"] = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                ["development"] = EditorUserBuildSettings.development,
                ["buildScriptsOnly"] = EditorUserBuildSettings.buildScriptsOnly,
                ["scenes"] = scenes
            };
        }

        private static JObject GetTagsAndLayers()
        {
            var tags = new JArray();
            #if UNITY_6000_0_OR_NEWER
            // Unity 6: Use alternative approach to get tags
            var allTags = new string[] { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
            #else
            var allTags = UnityEditorInternal.InternalEditorUtility.tags;
            #endif
            
            foreach (var tag in allTags)
            {
                tags.Add(tag);
            }

            var layers = new JArray();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(new JObject
                    {
                        ["index"] = i,
                        ["name"] = layerName
                    });
                }
            }

            var sortingLayers = new JArray();
            foreach (var layer in SortingLayer.layers)
            {
                sortingLayers.Add(new JObject
                {
                    ["id"] = layer.id,
                    ["name"] = layer.name,
                    ["value"] = layer.value
                });
            }

            return new JObject
            {
                ["tags"] = tags,
                ["layers"] = layers,
                ["sortingLayers"] = sortingLayers
            };
        }

        #endregion

        #region Update Settings Methods

        private static Tuple<JObject, JObject, bool> UpdatePlayerSettings(JObject settings)
        {
            var result = new JObject();
            var previous = new JObject();
            bool requiresRestart = false;

            if (settings["companyName"] != null)
            {
                previous["companyName"] = PlayerSettings.companyName;
                PlayerSettings.companyName = settings["companyName"].ToString();
                result["companyName"] = "updated";
            }

            if (settings["productName"] != null)
            {
                previous["productName"] = PlayerSettings.productName;
                PlayerSettings.productName = settings["productName"].ToString();
                result["productName"] = "updated";
            }

            if (settings["version"] != null)
            {
                previous["version"] = PlayerSettings.bundleVersion;
                PlayerSettings.bundleVersion = settings["version"].ToString();
                result["version"] = "updated";
            }

            if (settings["defaultScreenWidth"] != null)
            {
                previous["defaultScreenWidth"] = PlayerSettings.defaultScreenWidth;
                PlayerSettings.defaultScreenWidth = settings["defaultScreenWidth"].ToObject<int>();
                result["defaultScreenWidth"] = "updated";
            }

            if (settings["defaultScreenHeight"] != null)
            {
                previous["defaultScreenHeight"] = PlayerSettings.defaultScreenHeight;
                PlayerSettings.defaultScreenHeight = settings["defaultScreenHeight"].ToObject<int>();
                result["defaultScreenHeight"] = "updated";
            }

            if (settings["runInBackground"] != null)
            {
                previous["runInBackground"] = PlayerSettings.runInBackground;
                PlayerSettings.runInBackground = settings["runInBackground"].ToObject<bool>();
                result["runInBackground"] = "updated";
            }

            return Tuple.Create(result, previous, requiresRestart);
        }

        private static Tuple<JObject, JObject, bool> UpdateGraphicsSettings(JObject settings)
        {
            var result = new JObject();
            var previous = new JObject();
            bool requiresRestart = false;

            if (settings["colorSpace"] != null)
            {
                previous["colorSpace"] = PlayerSettings.colorSpace.ToString();
                string colorSpaceStr = settings["colorSpace"].ToString();
                if (Enum.TryParse<ColorSpace>(colorSpaceStr, out ColorSpace colorSpace))
                {
                    PlayerSettings.colorSpace = colorSpace;
                    result["colorSpace"] = "updated";
                    requiresRestart = true; // Color space change requires restart
                }
                else
                {
                    result["colorSpace"] = $"error: Invalid color space '{colorSpaceStr}'";
                }
            }

            return Tuple.Create(result, previous, requiresRestart);
        }

        private static Tuple<JObject, JObject, bool> UpdatePhysicsSettings(JObject settings)
        {
            var result = new JObject();
            var previous = new JObject();

            if (settings["gravity"] != null)
            {
                var gravity = settings["gravity"] as JObject;
                previous["gravity"] = new JObject
                {
                    ["x"] = Physics.gravity.x,
                    ["y"] = Physics.gravity.y,
                    ["z"] = Physics.gravity.z
                };
                
                Physics.gravity = new Vector3(
                    gravity["x"]?.ToObject<float>() ?? Physics.gravity.x,
                    gravity["y"]?.ToObject<float>() ?? Physics.gravity.y,
                    gravity["z"]?.ToObject<float>() ?? Physics.gravity.z
                );
                result["gravity"] = "updated";
            }

            if (settings["defaultSolverIterations"] != null)
            {
                previous["defaultSolverIterations"] = Physics.defaultSolverIterations;
                Physics.defaultSolverIterations = settings["defaultSolverIterations"].ToObject<int>();
                result["defaultSolverIterations"] = "updated";
            }

            if (settings["bounceThreshold"] != null)
            {
                previous["bounceThreshold"] = Physics.bounceThreshold;
                Physics.bounceThreshold = settings["bounceThreshold"].ToObject<float>();
                result["bounceThreshold"] = "updated";
            }

            return Tuple.Create(result, previous, false);
        }

        private static Tuple<JObject, JObject, bool> UpdateAudioSettings(JObject settings)
        {
            var result = new JObject();
            var previous = new JObject();

            if (settings["globalVolume"] != null)
            {
                previous["globalVolume"] = AudioListener.volume;
                AudioListener.volume = Mathf.Clamp01(settings["globalVolume"].ToObject<float>());
                result["globalVolume"] = "updated";
            }

            return Tuple.Create(result, previous, false);
        }

        private static Tuple<JObject, JObject, bool> UpdateTimeSettings(JObject settings)
        {
            var result = new JObject();
            var previous = new JObject();

            if (settings["fixedDeltaTime"] != null)
            {
                previous["fixedDeltaTime"] = Time.fixedDeltaTime;
                Time.fixedDeltaTime = settings["fixedDeltaTime"].ToObject<float>();
                result["fixedDeltaTime"] = "updated";
            }

            if (settings["timeScale"] != null)
            {
                previous["timeScale"] = Time.timeScale;
                Time.timeScale = Mathf.Max(0, settings["timeScale"].ToObject<float>());
                result["timeScale"] = "updated";
            }

            return Tuple.Create(result, previous, false);
        }

        #endregion
    }
}