using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Unity asset import settings operations
    /// </summary>
    public static class AssetImportSettingsHandler
    {
        /// <summary>
        /// Handle asset import settings operations (get, modify, apply_preset, reimport)
        /// </summary>
        public static object HandleCommand(string action, JObject parameters)
        {
            try
            {
                var assetPath = parameters["assetPath"]?.ToString();
                
                if (string.IsNullOrEmpty(assetPath))
                {
                    return new { error = "Asset path not specified" };
                }

                switch (action.ToLower())
                {
                    case "get":
                        return GetImportSettings(assetPath);
                    case "modify":
                        var settings = parameters["settings"] as JObject;
                        return ModifyImportSettings(assetPath, settings);
                    case "apply_preset":
                        var preset = parameters["preset"]?.ToString();
                        return ApplyPreset(assetPath, preset);
                    case "reimport":
                        return ReimportAsset(assetPath);
                    default:
                        return new { error = $"Unknown action: {action}" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetImportSettingsHandler", $"Error handling {action}: {e.Message}");
                return new { error = e.Message };
            }
        }

        /// <summary>
        /// Get import settings for an asset
        /// </summary>
        private static object GetImportSettings(string assetPath)
        {
            try
            {
                var assetImporter = AssetImporter.GetAtPath(assetPath);
                if (assetImporter == null)
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                var settings = new Dictionary<string, object>();
                
                // Get asset type
                settings["assetType"] = assetImporter.GetType().Name.Replace("Importer", "");

                // Handle different importer types
                if (assetImporter is TextureImporter textureImporter)
                {
                    settings["textureType"] = textureImporter.textureType.ToString();
                    settings["filterMode"] = textureImporter.filterMode.ToString();
                    settings["wrapMode"] = textureImporter.wrapMode.ToString();
                    settings["maxTextureSize"] = textureImporter.maxTextureSize;
                    settings["compressionQuality"] = textureImporter.textureCompression == TextureImporterCompression.Compressed ? 50 : 100;
                    settings["generateMipMaps"] = textureImporter.mipmapEnabled;
                    settings["readable"] = textureImporter.isReadable;
                    settings["crunchedCompression"] = textureImporter.crunchedCompression;
                    settings["sRGBTexture"] = textureImporter.sRGBTexture;
                }
                else if (assetImporter is ModelImporter modelImporter)
                {
                    settings["assetType"] = "Model";
                    settings["scaleFactor"] = modelImporter.globalScale;
                    settings["useFileScale"] = modelImporter.useFileScale;
                    settings["importBlendShapes"] = modelImporter.importBlendShapes;
                    settings["importVisibility"] = modelImporter.importVisibility;
                    settings["importCameras"] = modelImporter.importCameras;
                    settings["importLights"] = modelImporter.importLights;
                    settings["generateColliders"] = modelImporter.addCollider;
                    settings["animationType"] = modelImporter.animationType.ToString();
                    settings["optimizeMesh"] = modelImporter.optimizeMeshPolygons;
                }
                else if (assetImporter is AudioImporter audioImporter)
                {
                    settings["assetType"] = "Audio";
                    settings["forceToMono"] = audioImporter.forceToMono;
                    settings["loadInBackground"] = audioImporter.loadInBackground;
                    settings["ambisonic"] = audioImporter.ambisonic;
                    
                    var sampleSettings = audioImporter.defaultSampleSettings;
                    settings["loadType"] = sampleSettings.loadType.ToString();
                    settings["compressionFormat"] = sampleSettings.compressionFormat.ToString();
                    settings["quality"] = sampleSettings.quality;
                    settings["sampleRateSetting"] = sampleSettings.sampleRateSetting.ToString();
                }

                return new
                {
                    success = true,
                    action = "get",
                    assetPath = assetPath,
                    settings = settings
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetImportSettingsHandler", $"Error getting import settings for '{assetPath}': {e.Message}");
                return new { error = $"Failed to get import settings: {e.Message}" };
            }
        }

        /// <summary>
        /// Modify import settings for an asset
        /// </summary>
        private static object ModifyImportSettings(string assetPath, JObject newSettings)
        {
            try
            {
                if (newSettings == null)
                {
                    return new { error = "Settings not specified" };
                }

                var assetImporter = AssetImporter.GetAtPath(assetPath);
                if (assetImporter == null)
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                var previousSettings = new Dictionary<string, object>();
                var appliedSettings = new Dictionary<string, object>();

                // Apply settings based on importer type
                if (assetImporter is TextureImporter textureImporter)
                {
                    foreach (var setting in newSettings)
                    {
                        var key = setting.Key;
                        var value = setting.Value;

                        switch (key)
                        {
                            case "maxTextureSize":
                                previousSettings[key] = textureImporter.maxTextureSize;
                                textureImporter.maxTextureSize = value.Value<int>();
                                appliedSettings[key] = value.Value<int>();
                                break;
                            case "compressionQuality":
                                previousSettings[key] = textureImporter.textureCompression == TextureImporterCompression.Compressed ? 50 : 100;
                                var quality = value.Value<int>();
                                textureImporter.textureCompression = quality < 100 ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
                                appliedSettings[key] = quality;
                                break;
                            case "textureType":
                                previousSettings[key] = textureImporter.textureType.ToString();
                                textureImporter.textureType = (TextureImporterType)Enum.Parse(typeof(TextureImporterType), value.ToString());
                                appliedSettings[key] = value.ToString();
                                break;
                            case "filterMode":
                                previousSettings[key] = textureImporter.filterMode.ToString();
                                textureImporter.filterMode = (FilterMode)Enum.Parse(typeof(FilterMode), value.ToString());
                                appliedSettings[key] = value.ToString();
                                break;
                            case "generateMipMaps":
                                previousSettings[key] = textureImporter.mipmapEnabled;
                                textureImporter.mipmapEnabled = value.Value<bool>();
                                appliedSettings[key] = value.Value<bool>();
                                break;
                        }
                    }
                }
                else if (assetImporter is ModelImporter modelImporter)
                {
                    foreach (var setting in newSettings)
                    {
                        var key = setting.Key;
                        var value = setting.Value;

                        switch (key)
                        {
                            case "scaleFactor":
                                previousSettings[key] = modelImporter.globalScale;
                                modelImporter.globalScale = value.Value<float>();
                                appliedSettings[key] = value.Value<float>();
                                break;
                            case "animationType":
                                previousSettings[key] = modelImporter.animationType.ToString();
                                modelImporter.animationType = (ModelImporterAnimationType)Enum.Parse(typeof(ModelImporterAnimationType), value.ToString());
                                appliedSettings[key] = value.ToString();
                                break;
                            case "optimizeMesh":
                                previousSettings[key] = modelImporter.optimizeMeshPolygons;
                                modelImporter.optimizeMeshPolygons = value.Value<bool>();
                                appliedSettings[key] = value.Value<bool>();
                                break;
                        }
                    }
                }

                // Apply the changes
                assetImporter.SaveAndReimport();

                return new
                {
                    success = true,
                    action = "modify",
                    assetPath = assetPath,
                    previousSettings = previousSettings,
                    newSettings = appliedSettings,
                    message = $"Import settings modified for: {assetPath}"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetImportSettingsHandler", $"Error modifying import settings for '{assetPath}': {e.Message}");
                return new { error = $"Failed to modify import settings: {e.Message}" };
            }
        }

        /// <summary>
        /// Apply a preset to an asset
        /// </summary>
        private static object ApplyPreset(string assetPath, string preset)
        {
            try
            {
                if (string.IsNullOrEmpty(preset))
                {
                    return new { error = "Preset not specified" };
                }

                var assetImporter = AssetImporter.GetAtPath(assetPath);
                if (assetImporter == null)
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                var appliedSettings = new Dictionary<string, object>();

                // Apply preset based on type
                if (assetImporter is TextureImporter textureImporter)
                {
                    switch (preset)
                    {
                        case "UI_Sprite":
                            textureImporter.textureType = TextureImporterType.Sprite;
                            textureImporter.filterMode = FilterMode.Bilinear;
                            textureImporter.maxTextureSize = 2048;
                            textureImporter.mipmapEnabled = false;
                            
                            appliedSettings["textureType"] = "Sprite";
                            appliedSettings["filterMode"] = "Bilinear";
                            appliedSettings["maxTextureSize"] = 2048;
                            appliedSettings["generateMipMaps"] = false;
                            break;
                            
                        case "3D_Texture":
                            textureImporter.textureType = TextureImporterType.Default;
                            textureImporter.filterMode = FilterMode.Trilinear;
                            textureImporter.mipmapEnabled = true;
                            textureImporter.anisoLevel = 4;
                            
                            appliedSettings["textureType"] = "Default";
                            appliedSettings["filterMode"] = "Trilinear";
                            appliedSettings["generateMipMaps"] = true;
                            appliedSettings["anisoLevel"] = 4;
                            break;
                            
                        case "Icon":
                            textureImporter.textureType = TextureImporterType.Sprite;
                            textureImporter.filterMode = FilterMode.Point;
                            textureImporter.maxTextureSize = 256;
                            textureImporter.mipmapEnabled = false;
                            textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                            
                            appliedSettings["textureType"] = "Sprite";
                            appliedSettings["filterMode"] = "Point";
                            appliedSettings["maxTextureSize"] = 256;
                            appliedSettings["generateMipMaps"] = false;
                            appliedSettings["compression"] = "None";
                            break;
                            
                        default:
                            return new { error = $"Unknown preset: {preset}" };
                    }
                }
                else if (assetImporter is ModelImporter modelImporter)
                {
                    switch (preset)
                    {
                        case "Character":
                            modelImporter.animationType = ModelImporterAnimationType.Human;
                            modelImporter.optimizeMeshPolygons = true;
                            modelImporter.importBlendShapes = true;
                            
                            appliedSettings["animationType"] = "Human";
                            appliedSettings["optimizeMesh"] = true;
                            appliedSettings["importBlendShapes"] = true;
                            break;
                            
                        case "Static_Prop":
                            modelImporter.animationType = ModelImporterAnimationType.None;
                            modelImporter.optimizeMeshPolygons = true;
                            modelImporter.addCollider = false;
                            modelImporter.generateSecondaryUV = true;
                            
                            appliedSettings["animationType"] = "None";
                            appliedSettings["optimizeMesh"] = true;
                            appliedSettings["generateColliders"] = false;
                            appliedSettings["generateLightmapUVs"] = true;
                            break;
                            
                        default:
                            return new { error = $"Unknown preset: {preset}" };
                    }
                }
                else
                {
                    return new { error = $"No presets available for asset type: {assetImporter.GetType().Name}" };
                }

                // Apply the changes
                assetImporter.SaveAndReimport();

                return new
                {
                    success = true,
                    action = "apply_preset",
                    assetPath = assetPath,
                    preset = preset,
                    appliedSettings = appliedSettings,
                    message = $"Preset \"{preset}\" applied to: {assetPath}"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetImportSettingsHandler", $"Error applying preset '{preset}' to '{assetPath}': {e.Message}");
                return new { error = $"Failed to apply preset: {e.Message}" };
            }
        }

        /// <summary>
        /// Reimport an asset
        /// </summary>
        private static object ReimportAsset(string assetPath)
        {
            try
            {
                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath))
                {
                    return new { error = $"Asset not found: {assetPath}" };
                }

                var startTime = EditorApplication.timeSinceStartup;
                
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                
                var duration = EditorApplication.timeSinceStartup - startTime;

                return new
                {
                    success = true,
                    action = "reimport",
                    assetPath = assetPath,
                    message = $"Asset reimported: {assetPath}",
                    duration = Math.Round(duration, 3)
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetImportSettingsHandler", $"Error reimporting '{assetPath}': {e.Message}");
                return new { error = $"Failed to reimport asset: {e.Message}" };
            }
        }
    }
}