using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles Scene-related operations
    /// </summary>
    public static class SceneHandler
    {
        /// <summary>
        /// Creates a new scene based on parameters
        /// </summary>
        public static object CreateScene(JObject parameters)
        {
            try
            {
                // Get scene name (required)
                var sceneName = parameters["sceneName"]?.ToString();
                if (string.IsNullOrEmpty(sceneName))
                {
                    return new { error = "Scene name cannot be empty" };
                }

                // Validate scene name
                if (sceneName.Contains("/") || sceneName.Contains("\\") || 
                    sceneName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return new { error = "Scene name contains invalid characters" };
                }

                // Get optional parameters
                var path = parameters["path"]?.ToString() ?? "Assets/Scenes/";
                var loadScene = parameters["loadScene"]?.ToObject<bool>() ?? true;
                var addToBuildSettings = parameters["addToBuildSettings"]?.ToObject<bool>() ?? false;

                // Ensure path ends with /
                if (!path.EndsWith("/"))
                {
                    path += "/";
                }

                // Validate path
                if (!path.StartsWith("Assets/"))
                {
                    return new { error = "Invalid path: Path must be within Assets folder" };
                }

                // Create full scene path
                var scenePath = path + sceneName + ".unity";

                // Check if scene already exists
                if (File.Exists(scenePath))
                {
                    return new { error = $"Scene with name \"{sceneName}\" already exists at path \"{scenePath}\"" };
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(scenePath);
                if (!AssetDatabase.IsValidFolder(directory))
                {
                    // Create directory structure
                    var folders = directory.Split('/');
                    var currentPath = folders[0]; // "Assets"
                    
                    for (int i = 1; i < folders.Length; i++)
                    {
                        var nextPath = currentPath + "/" + folders[i];
                        if (!AssetDatabase.IsValidFolder(nextPath))
                        {
                            AssetDatabase.CreateFolder(currentPath, folders[i]);
                        }
                        currentPath = nextPath;
                    }
                }

                // Create the scene
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, 
                    loadScene ? NewSceneMode.Single : NewSceneMode.Additive);
                
                // Save the scene
                bool saved = EditorSceneManager.SaveScene(newScene, scenePath);
                if (!saved)
                {
                    return new { error = "Failed to save scene" };
                }

                // Add to build settings if requested
                int sceneIndex = -1;
                if (addToBuildSettings)
                {
                    var buildScenes = EditorBuildSettings.scenes.ToList();
                    buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
                    EditorBuildSettings.scenes = buildScenes.ToArray();
                    sceneIndex = buildScenes.Count - 1;
                }

                // If we didn't load the scene, we need to unload it
                if (!loadScene && newScene.IsValid())
                {
                    EditorSceneManager.CloseScene(newScene, true);
                }

                // Prepare result
                var result = new
                {
                    sceneName = sceneName,
                    path = scenePath,
                    sceneIndex = sceneIndex,
                    isLoaded = loadScene,
                    summary = GenerateSummary(sceneName, scenePath, loadScene, sceneIndex)
                };

                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to create scene: {ex.Message}" };
            }
        }

        private static string GenerateSummary(string sceneName, string path, bool isLoaded, int sceneIndex)
        {
            var summary = isLoaded ? "Created and loaded" : "Created";
            summary += $" scene \"{sceneName}\" at \"{path}\"";
            
            if (sceneIndex >= 0)
            {
                summary += $" (build index: {sceneIndex})";
            }
            else if (!isLoaded)
            {
                summary += " (not loaded)";
            }

            return summary;
        }

        /// <summary>
        /// Loads an existing scene based on parameters
        /// </summary>
        public static object LoadScene(JObject parameters)
        {
            try
            {
                // Get scene identification parameters
                var scenePath = parameters["scenePath"]?.ToString();
                var sceneName = parameters["sceneName"]?.ToString();
                
                // Validate that either scenePath or sceneName is provided
                if (string.IsNullOrEmpty(scenePath) && string.IsNullOrEmpty(sceneName))
                {
                    return new { error = "Either scenePath or sceneName must be provided" };
                }
                
                // Validate that only one is provided
                if (!string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(sceneName))
                {
                    return new { error = "Provide either scenePath or sceneName, not both" };
                }
                
                // Get load mode
                var loadModeStr = parameters["loadMode"]?.ToString() ?? "Single";
                
                // Validate load mode
                if (loadModeStr != "Single" && loadModeStr != "Additive")
                {
                    return new { error = "Invalid load mode. Must be 'Single' or 'Additive'" };
                }
                
                LoadSceneMode loadMode = loadModeStr == "Single" ? LoadSceneMode.Single : LoadSceneMode.Additive;
                
                // Store previous scene info (for Single mode)
                string previousSceneName = SceneManager.GetActiveScene().name;
                
                Scene loadedScene;
                string actualScenePath = scenePath;
                
                // Load by path
                if (!string.IsNullOrEmpty(scenePath))
                {
                    // Check if file exists
                    if (!File.Exists(scenePath))
                    {
                        return new { error = $"Scene file not found: {scenePath}" };
                    }
                    
                    // Load the scene
                    loadedScene = EditorSceneManager.OpenScene(scenePath, loadMode == LoadSceneMode.Single ? 
                        OpenSceneMode.Single : OpenSceneMode.Additive);
                }
                else // Load by name
                {
                    // Check if scene is in build settings
                    var buildScenes = EditorBuildSettings.scenes;
                    var sceneInBuild = buildScenes.FirstOrDefault(s => 
                        Path.GetFileNameWithoutExtension(s.path) == sceneName && s.enabled);
                    
                    if (sceneInBuild == null)
                    {
                        return new { error = $"Scene \"{sceneName}\" is not in build settings. Add it to build settings or load by path." };
                    }
                    
                    actualScenePath = sceneInBuild.path;
                    
                    // Load the scene
                    loadedScene = EditorSceneManager.OpenScene(actualScenePath, loadMode == LoadSceneMode.Single ? 
                        OpenSceneMode.Single : OpenSceneMode.Additive);
                }
                
                // Verify scene was loaded
                if (!loadedScene.IsValid())
                {
                    return new { error = "Failed to load scene" };
                }
                
                // Get active scene count (for additive mode)
                int activeSceneCount = SceneManager.sceneCount;
                
                // Prepare result
                var result = new
                {
                    sceneName = loadedScene.name,
                    scenePath = actualScenePath,
                    loadMode = loadModeStr,
                    isLoaded = loadedScene.isLoaded,
                    previousScene = loadMode == LoadSceneMode.Single ? previousSceneName : null,
                    activeSceneCount = loadMode == LoadSceneMode.Additive ? activeSceneCount : (int?)null,
                    summary = GenerateLoadSummary(loadedScene.name, loadModeStr, activeSceneCount)
                };
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to load scene: {ex.Message}" };
            }
        }
        
        private static string GenerateLoadSummary(string sceneName, string loadMode, int activeSceneCount)
        {
            var summary = $"Loaded scene \"{sceneName}\" in {loadMode} mode";
            
            if (loadMode == "Additive" && activeSceneCount > 1)
            {
                summary += $" ({activeSceneCount} scenes active)";
            }
            
            return summary;
        }

        /// <summary>
        /// Saves the current scene
        /// </summary>
        public static object SaveScene(JObject parameters)
        {
            try
            {
                // Get current scene
                var currentScene = SceneManager.GetActiveScene();
                
                // Check if a scene is loaded
                if (!currentScene.IsValid())
                {
                    return new { error = "No scene is currently loaded" };
                }
                
                // Get parameters
                var scenePath = parameters["scenePath"]?.ToString();
                var saveAs = parameters["saveAs"]?.ToObject<bool>() ?? false;
                
                // Validate saveAs requires scenePath
                if (saveAs && string.IsNullOrEmpty(scenePath))
                {
                    return new { error = "scenePath is required when saveAs is true" };
                }
                
                // Determine the save path
                string savePath = scenePath;
                string originalPath = currentScene.path;
                
                if (string.IsNullOrEmpty(savePath))
                {
                    // Save to current scene path
                    savePath = currentScene.path;
                    
                    // If scene has never been saved, require a path
                    if (string.IsNullOrEmpty(savePath))
                    {
                        return new { error = "Scene has never been saved. Please provide a scenePath." };
                    }
                }
                else
                {
                    // Validate the provided path
                    if (!savePath.StartsWith("Assets/"))
                    {
                        return new { error = "Invalid path: Path must be within Assets folder" };
                    }
                    
                    if (!savePath.EndsWith(".unity"))
                    {
                        return new { error = "Invalid path: Path must end with .unity" };
                    }
                    
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(savePath);
                    if (!AssetDatabase.IsValidFolder(directory))
                    {
                        // Create directory structure
                        var folders = directory.Split('/');
                        var currentPath = folders[0]; // "Assets"
                        
                        for (int i = 1; i < folders.Length; i++)
                        {
                            var nextPath = currentPath + "/" + folders[i];
                            if (!AssetDatabase.IsValidFolder(nextPath))
                            {
                                AssetDatabase.CreateFolder(currentPath, folders[i]);
                            }
                            currentPath = nextPath;
                        }
                    }
                }
                
                // Check if scene is dirty (has unsaved changes)
                bool wasDirty = currentScene.isDirty;
                
                // Save the scene
                bool saved = false;
                if (saveAs && !string.IsNullOrEmpty(scenePath))
                {
                    // Save as new scene
                    saved = EditorSceneManager.SaveScene(currentScene, savePath, true);
                }
                else
                {
                    // Regular save
                    saved = EditorSceneManager.SaveScene(currentScene, savePath);
                }
                
                if (!saved)
                {
                    return new { error = "Failed to save scene" };
                }
                
                // Prepare result
                var result = new
                {
                    sceneName = currentScene.name,
                    scenePath = savePath,
                    originalPath = saveAs ? originalPath : null,
                    saved = saved,
                    isDirty = false, // After saving, scene is no longer dirty
                    summary = GenerateSaveSummary(currentScene.name, savePath, saveAs, wasDirty)
                };
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to save scene: {ex.Message}" };
            }
        }
        
        private static string GenerateSaveSummary(string sceneName, string savePath, bool saveAs, bool wasDirty)
        {
            if (!wasDirty && !saveAs)
            {
                return $"Scene \"{sceneName}\" has no unsaved changes";
            }
            
            var summary = saveAs ? "Saved scene" : "Saved scene";
            summary += $" \"{sceneName}\"";
            
            if (saveAs)
            {
                summary += $" as \"{savePath}\"";
            }
            else
            {
                summary += $" to \"{savePath}\"";
            }
            
            return summary;
        }

        /// <summary>
        /// Lists all scenes in the project
        /// </summary>
        public static object ListScenes(JObject parameters)
        {
            try
            {
                // Get filter parameters
                var includeLoadedOnly = parameters["includeLoadedOnly"]?.ToObject<bool>() ?? false;
                var includeBuildScenesOnly = parameters["includeBuildScenesOnly"]?.ToObject<bool>() ?? false;
                var includePath = parameters["includePath"]?.ToString();
                
                var sceneList = new System.Collections.Generic.List<object>();
                
                // Get all scene files in the project
                var allSceneGuids = AssetDatabase.FindAssets("t:Scene");
                var allScenePaths = new System.Collections.Generic.List<string>();
                
                foreach (var guid in allSceneGuids)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(scenePath) && scenePath.EndsWith(".unity"))
                    {
                        allScenePaths.Add(scenePath);
                    }
                }
                
                // Get build scenes
                var buildScenes = EditorBuildSettings.scenes;
                var buildScenePaths = new System.Collections.Generic.HashSet<string>();
                foreach (var buildScene in buildScenes)
                {
                    if (buildScene.enabled)
                    {
                        buildScenePaths.Add(buildScene.path);
                    }
                }
                
                // Get loaded scenes
                var loadedScenes = new System.Collections.Generic.HashSet<string>();
                var activeScenePath = SceneManager.GetActiveScene().path;
                
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                    {
                        loadedScenes.Add(scene.path);
                    }
                }
                
                // Process each scene
                foreach (var scenePath in allScenePaths)
                {
                    // Apply filters
                    if (includeLoadedOnly && !loadedScenes.Contains(scenePath))
                        continue;
                        
                    if (includeBuildScenesOnly && !buildScenePaths.Contains(scenePath))
                        continue;
                        
                    if (!string.IsNullOrEmpty(includePath) && !scenePath.Contains(includePath))
                        continue;
                    
                    // Get scene info
                    var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                    var isLoaded = loadedScenes.Contains(scenePath);
                    var isActive = scenePath == activeScenePath;
                    
                    // Find build index
                    int buildIndex = -1;
                    for (int i = 0; i < buildScenes.Length; i++)
                    {
                        if (buildScenes[i].path == scenePath && buildScenes[i].enabled)
                        {
                            buildIndex = i;
                            break;
                        }
                    }
                    
                    sceneList.Add(new
                    {
                        name = sceneName,
                        path = scenePath,
                        buildIndex = buildIndex,
                        isLoaded = isLoaded,
                        isActive = isActive
                    });
                }
                
                // Count statistics
                int loadedCount = sceneList.Count(s => ((dynamic)s).isLoaded);
                int inBuildCount = sceneList.Count(s => ((dynamic)s).buildIndex >= 0);
                
                // Prepare result
                var result = new
                {
                    scenes = sceneList,
                    totalCount = sceneList.Count,
                    loadedCount = loadedCount,
                    inBuildCount = inBuildCount,
                    summary = GenerateListSummary(sceneList.Count, loadedCount, inBuildCount, 
                        includeLoadedOnly, includeBuildScenesOnly, includePath)
                };
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to list scenes: {ex.Message}" };
            }
        }
        
        private static string GenerateListSummary(int totalCount, int loadedCount, int inBuildCount,
            bool includeLoadedOnly, bool includeBuildScenesOnly, string includePath)
        {
            if (totalCount == 0)
            {
                return "No scenes found";
            }
            
            var summary = $"Found {totalCount} scene{(totalCount != 1 ? "s" : "")}";
            
            if (includeLoadedOnly)
            {
                summary = $"Found {totalCount} loaded scene{(totalCount != 1 ? "s" : "")}";
            }
            else if (includeBuildScenesOnly)
            {
                summary = $"Found {totalCount} scene{(totalCount != 1 ? "s" : "")} in build settings";
            }
            else if (!string.IsNullOrEmpty(includePath))
            {
                summary = $"Found {totalCount} scene{(totalCount != 1 ? "s" : "")} matching path \"{includePath}\"";
            }
            else
            {
                // Add counts for unfiltered results
                summary += $" ({loadedCount} loaded, {inBuildCount} in build settings)";
            }
            
            return summary;
        }

        /// <summary>
        /// Gets detailed information about a scene
        /// </summary>
        public static object GetSceneInfo(JObject parameters)
        {
            try
            {
                // Get scene identification parameters
                var scenePath = parameters["scenePath"]?.ToString();
                var sceneName = parameters["sceneName"]?.ToString();
                var includeGameObjects = parameters["includeGameObjects"]?.ToObject<bool>() ?? false;
                
                // Validate that only one identifier is provided if any
                if (!string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(sceneName))
                {
                    return new { error = "Provide either scenePath or sceneName, not both" };
                }
                
                Scene targetScene;
                string targetScenePath = scenePath;
                
                // Determine which scene to get info about
                if (string.IsNullOrEmpty(scenePath) && string.IsNullOrEmpty(sceneName))
                {
                    // Get info about current scene
                    targetScene = SceneManager.GetActiveScene();
                    targetScenePath = targetScene.path;
                }
                else if (!string.IsNullOrEmpty(scenePath))
                {
                    // Find scene by path
                    targetScene = default(Scene);
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.path == scenePath)
                        {
                            targetScene = scene;
                            break;
                        }
                    }
                }
                else // Find by name
                {
                    // First try loaded scenes
                    targetScene = SceneManager.GetSceneByName(sceneName);
                    
                    // If not loaded, find in project
                    if (!targetScene.IsValid())
                    {
                        var allSceneGuids = AssetDatabase.FindAssets("t:Scene");
                        foreach (var guid in allSceneGuids)
                        {
                            var path = AssetDatabase.GUIDToAssetPath(guid);
                            if (Path.GetFileNameWithoutExtension(path) == sceneName)
                            {
                                targetScenePath = path;
                                break;
                            }
                        }
                        
                        if (string.IsNullOrEmpty(targetScenePath))
                        {
                            return new { error = $"Scene not found: {sceneName}" };
                        }
                    }
                    else
                    {
                        targetScenePath = targetScene.path;
                    }
                }
                
                // Validate scene path exists
                if (!targetScene.IsValid() && !string.IsNullOrEmpty(targetScenePath))
                {
                    if (!File.Exists(targetScenePath))
                    {
                        return new { error = $"Scene not found: {targetScenePath}" };
                    }
                }
                
                // Gather scene information
                var sceneInfo = new System.Collections.Generic.Dictionary<string, object>();
                
                sceneInfo["sceneName"] = targetScene.IsValid() ? targetScene.name : Path.GetFileNameWithoutExtension(targetScenePath);
                sceneInfo["scenePath"] = targetScenePath;
                sceneInfo["isLoaded"] = targetScene.IsValid() && targetScene.isLoaded;
                sceneInfo["isActive"] = targetScene.IsValid() && SceneManager.GetActiveScene() == targetScene;
                sceneInfo["isDirty"] = targetScene.IsValid() && targetScene.isDirty;
                
                // Get build index
                int buildIndex = -1;
                var buildScenes = EditorBuildSettings.scenes;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    if (buildScenes[i].path == targetScenePath && buildScenes[i].enabled)
                    {
                        buildIndex = i;
                        break;
                    }
                }
                sceneInfo["buildIndex"] = buildIndex;
                
                // Get file info if scene exists on disk
                if (!string.IsNullOrEmpty(targetScenePath) && File.Exists(targetScenePath))
                {
                    var fileInfo = new FileInfo(targetScenePath);
                    sceneInfo["fileSize"] = fileInfo.Length;
                    sceneInfo["lastModified"] = fileInfo.LastWriteTimeUtc.ToString("o");
                }
                
                // Get GameObject info if scene is loaded and requested
                if (targetScene.IsValid() && targetScene.isLoaded && includeGameObjects)
                {
                    var rootObjects = targetScene.GetRootGameObjects();
                    var rootObjectList = new System.Collections.Generic.List<object>();
                    int totalObjectCount = 0;
                    
                    foreach (var obj in rootObjects)
                    {
                        int childCount = CountChildren(obj.transform);
                        totalObjectCount += childCount + 1; // Include the root object itself
                        
                        rootObjectList.Add(new
                        {
                            name = obj.name,
                            childCount = childCount
                        });
                    }
                    
                    sceneInfo["rootGameObjects"] = rootObjectList;
                    sceneInfo["rootObjectCount"] = rootObjects.Length;
                    sceneInfo["totalObjectCount"] = totalObjectCount;
                }
                else if (includeGameObjects && (!targetScene.IsValid() || !targetScene.isLoaded))
                {
                    // Can't get GameObject info for unloaded scenes
                    sceneInfo["rootGameObjects"] = new object[0];
                    sceneInfo["rootObjectCount"] = 0;
                    sceneInfo["totalObjectCount"] = 0;
                }
                
                // Generate summary
                sceneInfo["summary"] = GenerateSceneInfoSummary(sceneInfo);
                
                return sceneInfo;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to get scene info: {ex.Message}" };
            }
        }
        
        private static int CountChildren(Transform transform)
        {
            int count = transform.childCount;
            foreach (Transform child in transform)
            {
                count += CountChildren(child);
            }
            return count;
        }
        
        private static string GenerateSceneInfoSummary(System.Collections.Generic.Dictionary<string, object> info)
        {
            var sceneName = info["sceneName"].ToString();
            var isLoaded = (bool)info["isLoaded"];
            var isActive = (bool)info["isActive"];
            var isDirty = (bool)info["isDirty"];
            var buildIndex = (int)info["buildIndex"];
            
            var summary = $"Scene \"{sceneName}\" - ";
            
            if (isLoaded)
            {
                summary += isActive ? "Loaded and active" : "Loaded";
                if (isDirty)
                {
                    summary += " (unsaved changes)";
                }
            }
            else
            {
                summary += "Not loaded";
            }
            
            if (buildIndex >= 0)
            {
                summary += $", in build settings (index: {buildIndex})";
            }
            else
            {
                summary += ", not in build settings";
            }
            
            // Add file size if available
            if (info.ContainsKey("fileSize"))
            {
                var fileSize = (long)info["fileSize"];
                var sizeStr = FormatFileSize(fileSize);
                summary += $", {sizeStr}";
            }
            
            // Add GameObject count if available
            if (info.ContainsKey("totalObjectCount"))
            {
                var totalCount = (int)info["totalObjectCount"];
                summary += $", {totalCount} total GameObjects";
            }
            
            return summary;
        }
        
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.#} {sizes[order]}";
        }
    }
}
