using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Core;
using UnityCliBridge.Helpers;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles screenshot capture operations in Unity Editor
    /// </summary>
    public static class ScreenshotHandler
    {
        private sealed class TimedCaptureOutcome
        {
            public JObject Payload { get; }
            public Dictionary<string, double> Timings { get; }

            public TimedCaptureOutcome(JObject payload, Dictionary<string, double> timings)
            {
                Payload = payload;
                Timings = timings ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private sealed class TimedImageBytes
        {
            public byte[] ImageBytes { get; }
            public Dictionary<string, double> Timings { get; }

            public TimedImageBytes(byte[] imageBytes, Dictionary<string, double> timings)
            {
                ImageBytes = imageBytes;
                Timings = timings ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Captures a screenshot from the Unity Editor
        /// </summary>
        public static object CaptureScreenshot(JObject parameters)
        {
            try
            {
                // Parse parameters (保存先は固定のため outputPath は無視)
                string outputPath = null;
                string captureMode = parameters["captureMode"]?.ToString() ?? "game"; // game, scene, or window
                int width = parameters["width"]?.ToObject<int>() ?? 0;
                int height = parameters["height"]?.ToObject<int>() ?? 0;
                bool includeUI = parameters["includeUI"]?.ToObject<bool>() ?? true;
                string windowName = parameters["windowName"]?.ToString();
                bool encodeAsBase64 = parameters["encodeAsBase64"]?.ToObject<bool>() ?? false;
                
                // Validate capture mode
                if (!IsValidCaptureMode(captureMode))
                {
                    return CreateError("Invalid capture mode. Must be 'game', 'scene', or 'window'");
                }
                
                // 保存先は固定: <unityProjectRoot>/.unity/capture/image_<mode>_<timestamp>.png
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var projectRoot = CapturePathResolver.GetProjectRootFromAssetsPath(Application.dataPath);
                    outputPath = CapturePathResolver.BuildCaptureFilePath(projectRoot, "image", captureMode, timestamp, ".png");
                }
                
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                // Ensure directory exists (support outside Assets)
                string directory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(directory))
                {
                    var ioStopwatch = Stopwatch.StartNew();
                    Directory.CreateDirectory(directory);
                    ioStopwatch.Stop();
                    timings["ioMs"] = ioStopwatch.Elapsed.TotalMilliseconds;
                }
                
                // Capture based on mode
                TimedCaptureOutcome result = null;
                switch (captureMode)
                {
                    case "game":
                        result = CaptureGameView(outputPath, width, height, includeUI, encodeAsBase64);
                        break;
                    case "scene":
                        result = CaptureSceneView(outputPath, width, height, encodeAsBase64);
                        break;
                    case "window":
                        result = CaptureEditorWindow(outputPath, windowName, encodeAsBase64);
                        break;
                    case "explorer":
                        JObject explorerSettings = parameters["explorerSettings"] as JObject;
                        result = CaptureExplorerView(outputPath, explorerSettings, encodeAsBase64);
                        break;
                }

                if (result == null)
                {
                    return CreateError("Failed to capture screenshot: unsupported capture mode");
                }

                MergeTimings(result.Timings, timings);
                return AttachTimings(result.Payload, result.Timings);
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ScreenshotHandler", $"Error capturing screenshot: {ex.Message}");
                return CreateError($"Failed to capture screenshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Captures the Game View
        /// </summary>
        private static TimedCaptureOutcome CaptureGameView(string outputPath, int width, int height, bool includeUI, bool encodeAsBase64)
        {
            try
            {
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                BridgeLogger.Log("ScreenshotHandler", $"CaptureGameView called: path={outputPath}, width={width}, height={height}");
                
                // Try to capture from main camera if in Play Mode
                if (EditorApplication.isPlaying && Camera.main != null)
                {
                    BridgeLogger.Log("ScreenshotHandler", "Using main camera in Play Mode");
                    return CaptureFromCamera(Camera.main, outputPath, width, height, "game", includeUI, encodeAsBase64);
                }
                
                // Otherwise try to get Game View and capture it
                var prepareStopwatch = Stopwatch.StartNew();
                BridgeLogger.Log("ScreenshotHandler", "Getting Game View window");
                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType, false);
                
                if (gameView == null)
                {
                    BridgeLogger.LogError("ScreenshotHandler", "Game View not found");
                    return new TimedCaptureOutcome(
                        CreateError("Game View not found. Please open the Game View window."),
                        timings);
                }
                
                // Focus the Game View
                gameView.Focus();
                BridgeLogger.Log("ScreenshotHandler", "Game View focused");
                
                // Use reflection to get the Game View's render size
                var renderSize = GetGameViewRenderSize(gameView);
                int captureWidth = width > 0 ? width : renderSize.x;
                int captureHeight = height > 0 ? height : renderSize.y;
                prepareStopwatch.Stop();
                timings["prepareMs"] = prepareStopwatch.Elapsed.TotalMilliseconds;
                BridgeLogger.Log("ScreenshotHandler", $"Capture size: {captureWidth}x{captureHeight}");
                
                // Create RenderTexture and capture
                BridgeLogger.Log("ScreenshotHandler", "Calling CaptureWindowImmediate");
                var captureBytes = CaptureWindowImmediate(captureWidth, captureHeight);
                MergeTimings(timings, captureBytes.Timings);
                byte[] imageBytes = captureBytes.ImageBytes;
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    BridgeLogger.LogError("ScreenshotHandler", "CaptureWindowImmediate returned null or empty data");
                    return new TimedCaptureOutcome(
                        CreateError("Failed to capture Game View - no image data"),
                        timings);
                }
                
                BridgeLogger.Log("ScreenshotHandler", $"Image data captured: {imageBytes.Length} bytes");
                
                // Ensure output directory exists
                string directory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(directory))
                {
                    BridgeLogger.Log("ScreenshotHandler", $"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }
                
                // Save to file
                BridgeLogger.Log("ScreenshotHandler", $"Writing file to: {outputPath}");
                var ioStopwatch = Stopwatch.StartNew();
                File.WriteAllBytes(outputPath, imageBytes);
                ioStopwatch.Stop();
                AddTiming(timings, "ioMs", ioStopwatch.Elapsed.TotalMilliseconds);
                
                if (!File.Exists(outputPath))
                {
                    BridgeLogger.LogError("ScreenshotHandler", $"File was not created at: {outputPath}");
                    return new TimedCaptureOutcome(
                        CreateError("Failed to capture screenshot - file not created"),
                        timings);
                }
                
                BridgeLogger.Log("ScreenshotHandler", $"File created successfully: {outputPath}");
                
                var result = JObject.FromObject(new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = "game",
                    includeUI = includeUI,
                    fileSize = imageBytes.Length,
                    message = "Game View screenshot captured successfully"
                });
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    result["base64Data"] = Convert.ToBase64String(imageBytes);
                }
                
                return new TimedCaptureOutcome(result, timings);
            }
            catch (Exception ex)
            {
                return new TimedCaptureOutcome(CreateError($"Failed to capture Game View: {ex.Message}"), null);
            }
        }
        
        /// <summary>
        /// Captures the Scene View
        /// </summary>
        private static TimedCaptureOutcome CaptureSceneView(string outputPath, int width, int height, bool encodeAsBase64)
        {
            try
            {
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var prepareStopwatch = Stopwatch.StartNew();
                // Get the Scene View
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new TimedCaptureOutcome(
                        CreateError("Scene View not found. Please open a Scene View window."),
                        timings);
                }
                
                // Focus the Scene View
                sceneView.Focus();
                
                // Get Scene View camera
                Camera sceneCamera = sceneView.camera;
                if (sceneCamera == null)
                {
                    return new TimedCaptureOutcome(CreateError("Scene View camera not available"), timings);
                }
                
                // Determine capture resolution
                int captureWidth = width > 0 ? width : (int)sceneView.position.width;
                int captureHeight = height > 0 ? height : (int)sceneView.position.height;
                prepareStopwatch.Stop();
                timings["prepareMs"] = prepareStopwatch.Elapsed.TotalMilliseconds;
                
                // Create render texture
                var captureStopwatch = Stopwatch.StartNew();
                RenderTexture renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
                sceneCamera.targetTexture = renderTexture;
                
                // Render the scene
                sceneCamera.Render();
                
                // Read pixels
                RenderTexture.active = renderTexture;
                Texture2D screenshot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                screenshot.Apply();
                
                // Reset camera and render texture
                sceneCamera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                captureStopwatch.Stop();
                timings["captureMs"] = captureStopwatch.Elapsed.TotalMilliseconds;
                
                // Encode to PNG
                var encodeStopwatch = Stopwatch.StartNew();
                byte[] imageBytes = screenshot.EncodeToPNG();
                encodeStopwatch.Stop();
                timings["encodeMs"] = encodeStopwatch.Elapsed.TotalMilliseconds;
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                // Save to file
                var ioStopwatch = Stopwatch.StartNew();
                File.WriteAllBytes(outputPath, imageBytes);
                ioStopwatch.Stop();
                AddTiming(timings, "ioMs", ioStopwatch.Elapsed.TotalMilliseconds);
                
                var result = JObject.FromObject(new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = "scene",
                    fileSize = imageBytes.Length,
                    cameraPosition = new { x = sceneCamera.transform.position.x, y = sceneCamera.transform.position.y, z = sceneCamera.transform.position.z },
                    cameraRotation = new { x = sceneCamera.transform.eulerAngles.x, y = sceneCamera.transform.eulerAngles.y, z = sceneCamera.transform.eulerAngles.z },
                    message = "Scene View screenshot captured successfully"
                });
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    result["base64Data"] = Convert.ToBase64String(imageBytes);
                }
                
                return new TimedCaptureOutcome(result, timings);
            }
            catch (Exception ex)
            {
                return new TimedCaptureOutcome(CreateError($"Failed to capture Scene View: {ex.Message}"), null);
            }
        }
        
        /// <summary>
        /// Captures a specific Editor Window
        /// </summary>
        private static TimedCaptureOutcome CaptureEditorWindow(string outputPath, string windowName, bool encodeAsBase64)
        {
            try
            {
                if (string.IsNullOrEmpty(windowName))
                {
                    return new TimedCaptureOutcome(CreateError("windowName is required for window capture mode"), null);
                }
                
                // Find the window
                EditorWindow targetWindow = null;
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                
                foreach (var window in windows)
                {
                    if (window.titleContent.text.Contains(windowName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetWindow = window;
                        break;
                    }
                }
                
                if (targetWindow == null)
                {
                    return new TimedCaptureOutcome(CreateError($"Window '{windowName}' not found"), null);
                }
                
                // Focus the window
                targetWindow.Focus();
                
                // Get window dimensions
                int width = (int)targetWindow.position.width;
                int height = (int)targetWindow.position.height;
                
                // Note: Direct window capture is limited in Unity Editor
                // This is a placeholder for the approach
                return new TimedCaptureOutcome(
                    CreateError("Direct window capture is not fully supported. Use 'game' or 'scene' mode instead."),
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                return new TimedCaptureOutcome(CreateError($"Failed to capture window: {ex.Message}"), null);
            }
        }
        
        /// <summary>
        /// Captures using LLM Explorer mode - allows AI to explore the scene freely
        /// </summary>
        private static TimedCaptureOutcome CaptureExplorerView(string outputPath, JObject explorerSettings, bool encodeAsBase64)
        {
            try
            {
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                BridgeLogger.Log("ScreenshotHandler", "CaptureExplorerView called");
                
                // Parse explorer settings
                var prepareStopwatch = Stopwatch.StartNew();
                JObject targetSettings = explorerSettings?["target"] as JObject;
                JObject cameraSettings = explorerSettings?["camera"] as JObject;
                JObject displaySettings = explorerSettings?["display"] as JObject;
                
                // Create temporary explorer camera
                GameObject tempCameraObj = new GameObject("UnityCli_ExplorerCamera");
                Camera explorerCamera = tempCameraObj.AddComponent<Camera>();
                
                try
                {
                    // Configure camera defaults
                    explorerCamera.clearFlags = CameraClearFlags.SolidColor;
                    explorerCamera.backgroundColor = ParseColor(displaySettings?["backgroundColor"], new Color(0.2f, 0.2f, 0.2f));
                    explorerCamera.fieldOfView = cameraSettings?["fieldOfView"]?.ToObject<float>() ?? 60f;
                    explorerCamera.nearClipPlane = cameraSettings?["nearClip"]?.ToObject<float>() ?? 0.3f;
                    explorerCamera.farClipPlane = cameraSettings?["farClip"]?.ToObject<float>() ?? 1000f;
                    
                    // Set camera position and rotation
                    bool positionSet = false;
                    
                    // Check for target-based positioning
                    if (targetSettings != null)
                    {
                        string targetType = targetSettings["type"]?.ToString() ?? "position";
                        
                        if (targetType == "gameObject")
                        {
                            string targetName = targetSettings["name"]?.ToString();
                            if (!string.IsNullOrEmpty(targetName))
                            {
                                GameObject target = GameObject.Find(targetName);
                                if (target != null)
                                {
                                    bool autoFrame = cameraSettings?["autoFrame"]?.ToObject<bool>() ?? true;
                                    if (autoFrame)
                                    {
                                        positionSet = AutoFrameTarget(explorerCamera, target, targetSettings);
                                    }
                                }
                                else
                                {
                                    BridgeLogger.LogWarning("ScreenshotHandler", $"Target GameObject '{targetName}' not found");
                                }
                            }
                        }
                        else if (targetType == "tag")
                        {
                            string tag = targetSettings["tag"]?.ToString();
                            if (!string.IsNullOrEmpty(tag))
                            {
                                GameObject[] targets = GameObject.FindGameObjectsWithTag(tag);
                                if (targets.Length > 0)
                                {
                                    positionSet = AutoFrameTargets(explorerCamera, targets, targetSettings);
                                }
                            }
                        }
                        else if (targetType == "area")
                        {
                            JObject center = targetSettings["center"] as JObject;
                            float radius = targetSettings["radius"]?.ToObject<float>() ?? 10f;
                            if (center != null)
                            {
                                Vector3 centerPos = ParseVector3(center);
                                positionSet = FrameArea(explorerCamera, centerPos, radius);
                            }
                        }
                    }
                    
                    // If not positioned by target, use manual camera settings
                    if (!positionSet && cameraSettings != null)
                    {
                        JObject position = cameraSettings["position"] as JObject;
                        JObject lookAt = cameraSettings["lookAt"] as JObject;
                        JObject rotation = cameraSettings["rotation"] as JObject;
                        
                        if (position != null)
                        {
                            explorerCamera.transform.position = ParseVector3(position);
                            positionSet = true;
                        }
                        
                        if (lookAt != null)
                        {
                            Vector3 lookAtPos = ParseVector3(lookAt);
                            explorerCamera.transform.LookAt(lookAtPos);
                        }
                        else if (rotation != null)
                        {
                            explorerCamera.transform.eulerAngles = ParseVector3(rotation);
                        }
                    }
                    
                    // Default position if nothing was set
                    if (!positionSet)
                    {
                        explorerCamera.transform.position = new Vector3(0, 10, -10);
                        explorerCamera.transform.LookAt(Vector3.zero);
                    }
                    
                    // Configure display options
                    if (displaySettings != null)
                    {
                        // Set culling mask if specified
                        JArray layers = displaySettings["layers"] as JArray;
                        if (layers != null)
                        {
                            int cullingMask = 0;
                            foreach (var layer in layers)
                            {
                                int layerIndex = LayerMask.NameToLayer(layer.ToString());
                                if (layerIndex >= 0)
                                {
                                    cullingMask |= (1 << layerIndex);
                                }
                            }
                            if (cullingMask != 0)
                            {
                                explorerCamera.cullingMask = cullingMask;
                            }
                        }
                        
                        // Highlight target if requested
                        bool highlightTarget = displaySettings["highlightTarget"]?.ToObject<bool>() ?? false;
                        if (highlightTarget && targetSettings != null)
                        {
                            HighlightTargets(targetSettings);
                        }
                    }
                    
                    // Determine capture resolution
                    int width = cameraSettings?["width"]?.ToObject<int>() ?? 1920;
                    int height = cameraSettings?["height"]?.ToObject<int>() ?? 1080;
                    prepareStopwatch.Stop();
                    timings["prepareMs"] = prepareStopwatch.Elapsed.TotalMilliseconds;
                    
                    // Create render texture and capture
                    var captureStopwatch = Stopwatch.StartNew();
                    RenderTexture renderTexture = new RenderTexture(width, height, 24);
                    explorerCamera.targetTexture = renderTexture;
                    explorerCamera.Render();
                    
                    // Read pixels
                    RenderTexture.active = renderTexture;
                    Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                    screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    screenshot.Apply();
                    
                    // Cleanup render texture
                    RenderTexture.active = null;
                    explorerCamera.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                    captureStopwatch.Stop();
                    timings["captureMs"] = captureStopwatch.Elapsed.TotalMilliseconds;
                    
                    // Encode to PNG
                    var encodeStopwatch = Stopwatch.StartNew();
                    byte[] imageBytes = screenshot.EncodeToPNG();
                    encodeStopwatch.Stop();
                    timings["encodeMs"] = encodeStopwatch.Elapsed.TotalMilliseconds;
                    UnityEngine.Object.DestroyImmediate(screenshot);
                    
                    // Save to file
                    var ioStopwatch = Stopwatch.StartNew();
                    File.WriteAllBytes(outputPath, imageBytes);
                    ioStopwatch.Stop();
                    AddTiming(timings, "ioMs", ioStopwatch.Elapsed.TotalMilliseconds);
                    
                    var result = JObject.FromObject(new
                    {
                        path = outputPath,
                        width = width,
                        height = height,
                        captureMode = "explorer",
                        fileSize = imageBytes.Length,
                        cameraPosition = new { x = explorerCamera.transform.position.x, y = explorerCamera.transform.position.y, z = explorerCamera.transform.position.z },
                        cameraRotation = new { x = explorerCamera.transform.eulerAngles.x, y = explorerCamera.transform.eulerAngles.y, z = explorerCamera.transform.eulerAngles.z },
                        message = "Explorer view screenshot captured successfully"
                    });
                    
                    // Add base64 if requested
                    if (encodeAsBase64)
                    {
                        result["base64Data"] = Convert.ToBase64String(imageBytes);
                    }
                    
                    return new TimedCaptureOutcome(result, timings);
                }
                finally
                {
                    // Always cleanup the temporary camera
                    if (tempCameraObj != null)
                    {
                        UnityEngine.Object.DestroyImmediate(tempCameraObj);
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ScreenshotHandler", $"Error in CaptureExplorerView: {ex.Message}");
                return new TimedCaptureOutcome(CreateError($"Failed to capture explorer view: {ex.Message}"), null);
            }
        }
        
        /// <summary>
        /// Analyzes a screenshot for content
        /// </summary>
        public static object AnalyzeScreenshot(JObject parameters)
        {
            try
            {
                string imagePath = parameters["imagePath"]?.ToString();
                string base64Data = parameters["base64Data"]?.ToString();
                string analysisType = parameters["analysisType"]?.ToString() ?? "basic"; // basic, ui, content
                
                if (string.IsNullOrEmpty(imagePath) && string.IsNullOrEmpty(base64Data))
                {
                    return new { error = "Either imagePath or base64Data is required" };
                }
                
                byte[] imageBytes;
                string imageSource;
                
                if (!string.IsNullOrEmpty(imagePath))
                {
                    if (!File.Exists(imagePath))
                    {
                        return new { error = $"Image file not found: {imagePath}" };
                    }
                    
                    imageBytes = File.ReadAllBytes(imagePath);
                    imageSource = imagePath;
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(base64Data) &&
                            base64Data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            int commaIndex = base64Data.IndexOf(',');
                            if (commaIndex >= 0 && commaIndex < base64Data.Length - 1)
                            {
                                base64Data = base64Data.Substring(commaIndex + 1);
                            }
                        }
                        
                        imageBytes = Convert.FromBase64String(base64Data ?? string.Empty);
                        imageSource = "base64Data";
                    }
                    catch (FormatException)
                    {
                        return new { error = "base64Data is not valid Base64" };
                    }
                }
                
                Texture2D texture = new Texture2D(2, 2);
                if (!texture.LoadImage(imageBytes))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return new { error = "Failed to decode image data" };
                }
                
                var analysis = new
                {
                    success = true,
                    imagePath = imagePath,
                    imageSource = imageSource,
                    width = texture.width,
                    height = texture.height,
                    format = texture.format.ToString(),
                    fileSize = imageBytes.Length,
                    analysisType = analysisType
                };
                
                // Basic analysis
                if (analysisType == "basic" || analysisType == "ui")
                {
                    // Analyze dominant colors
                    var dominantColors = AnalyzeDominantColors(texture);
                    
                    // Check for UI elements (simplified)
                    var uiAnalysis = AnalyzeUIElements(texture);
                    
                    UnityEngine.Object.DestroyImmediate(texture);
                    
                    return new
                    {
                        analysis.success,
                        analysis.imagePath,
                        analysis.imageSource,
                        analysis.width,
                        analysis.height,
                        analysis.format,
                        analysis.fileSize,
                        analysis.analysisType,
                        dominantColors = dominantColors,
                        uiElements = uiAnalysis,
                        message = "Screenshot analyzed successfully"
                    };
                }
                
                UnityEngine.Object.DestroyImmediate(texture);
                return analysis;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to analyze screenshot: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Gets the current Game View resolution
        /// </summary>
        private static Vector2Int GetGameViewResolution()
        {
            var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType, false);
            
            if (gameView != null)
            {
                var prop = gameViewType.GetProperty("currentGameViewSize", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var size = prop.GetValue(gameView);
                    var widthProp = size.GetType().GetProperty("width");
                    var heightProp = size.GetType().GetProperty("height");
                    
                    if (widthProp != null && heightProp != null)
                    {
                        int width = (int)widthProp.GetValue(size);
                        int height = (int)heightProp.GetValue(size);
                        return new Vector2Int(width, height);
                    }
                }
            }
            
            // Default resolution
            return new Vector2Int(1920, 1080);
        }
        
        /// <summary>
        /// Analyzes dominant colors in the image
        /// </summary>
        private static object AnalyzeDominantColors(Texture2D texture)
        {
            // Sample pixels at intervals
            int sampleInterval = Mathf.Max(1, texture.width * texture.height / 1000);
            var colorCounts = new System.Collections.Generic.Dictionary<Color32, int>();
            
            for (int y = 0; y < texture.height; y += sampleInterval)
            {
                for (int x = 0; x < texture.width; x += sampleInterval)
                {
                    Color32 pixel = texture.GetPixel(x, y);
                    // Quantize colors
                    pixel.r = (byte)(pixel.r / 32 * 32);
                    pixel.g = (byte)(pixel.g / 32 * 32);
                    pixel.b = (byte)(pixel.b / 32 * 32);
                    
                    if (colorCounts.ContainsKey(pixel))
                        colorCounts[pixel]++;
                    else
                        colorCounts[pixel] = 1;
                }
            }
            
            // Get top colors
            var topColors = new System.Collections.Generic.List<object>();
            var sortedColors = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Color32, int>>(colorCounts);
            sortedColors.Sort((a, b) => b.Value.CompareTo(a.Value));
            
            for (int i = 0; i < Mathf.Min(5, sortedColors.Count); i++)
            {
                var color = sortedColors[i].Key;
                topColors.Add(new
                {
                    r = color.r,
                    g = color.g,
                    b = color.b,
                    hex = ColorUtility.ToHtmlStringRGB(color),
                    percentage = (sortedColors[i].Value * 100.0f) / (texture.width * texture.height / sampleInterval)
                });
            }
            
            return topColors;
        }
        
        /// <summary>
        /// Basic UI element detection
        /// </summary>
        private static object AnalyzeUIElements(Texture2D texture)
        {
            // This is a simplified UI detection
            // In a real implementation, you might use computer vision techniques
            
            var analysis = new
            {
                hasHighContrast = false,
                possibleButtons = 0,
                possibleText = 0,
                edgePixelRatio = 0.0f
            };
            
            // Simple edge detection to identify UI elements
            int edgePixels = 0;
            for (int y = 1; y < texture.height - 1; y++)
            {
                for (int x = 1; x < texture.width - 1; x++)
                {
                    Color current = texture.GetPixel(x, y);
                    Color right = texture.GetPixel(x + 1, y);
                    Color bottom = texture.GetPixel(x, y + 1);
                    
                    float diffRight = Mathf.Abs(current.grayscale - right.grayscale);
                    float diffBottom = Mathf.Abs(current.grayscale - bottom.grayscale);
                    
                    if (diffRight > 0.3f || diffBottom > 0.3f)
                    {
                        edgePixels++;
                    }
                }
            }
            
            return new
            {
                edgePixelRatio = (float)edgePixels / (texture.width * texture.height),
                analysis = "Basic UI analysis complete"
            };
        }
        
        /// <summary>
        /// Validates capture mode
        /// </summary>
        private static bool IsValidCaptureMode(string mode)
        {
            return mode == "game" || mode == "scene" || mode == "window" || mode == "explorer";
        }
        
        /// <summary>
        /// Captures from a specific camera using RenderTexture
        /// </summary>
        private static TimedCaptureOutcome CaptureFromCamera(
            Camera camera,
            string outputPath,
            int width,
            int height,
            string captureMode,
            bool includeUI,
            bool encodeAsBase64)
        {
            try
            {
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var prepareStopwatch = Stopwatch.StartNew();
                // Determine capture resolution
                int captureWidth = width > 0 ? width : camera.pixelWidth;
                int captureHeight = height > 0 ? height : camera.pixelHeight;
                prepareStopwatch.Stop();
                timings["prepareMs"] = prepareStopwatch.Elapsed.TotalMilliseconds;
                
                // Create RenderTexture
                var captureStopwatch = Stopwatch.StartNew();
                RenderTexture renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
                RenderTexture previousTarget = camera.targetTexture;
                
                // Set camera to render to our texture
                camera.targetTexture = renderTexture;
                camera.Render();
                
                // Read pixels from render texture
                RenderTexture.active = renderTexture;
                Texture2D screenshot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                screenshot.Apply();
                
                // Restore camera settings
                camera.targetTexture = previousTarget;
                RenderTexture.active = null;
                captureStopwatch.Stop();
                timings["captureMs"] = captureStopwatch.Elapsed.TotalMilliseconds;
                
                // Encode to PNG
                var encodeStopwatch = Stopwatch.StartNew();
                byte[] imageBytes = screenshot.EncodeToPNG();
                encodeStopwatch.Stop();
                timings["encodeMs"] = encodeStopwatch.Elapsed.TotalMilliseconds;
                
                // Cleanup
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    return new TimedCaptureOutcome(CreateError("Failed to encode screenshot"), timings);
                }
                
                // Save to file
                var ioStopwatch = Stopwatch.StartNew();
                File.WriteAllBytes(outputPath, imageBytes);
                ioStopwatch.Stop();
                AddTiming(timings, "ioMs", ioStopwatch.Elapsed.TotalMilliseconds);
                
                var result = JObject.FromObject(new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = captureMode,
                    includeUI = includeUI,
                    fileSize = imageBytes.Length,
                    message = $"Screenshot captured successfully from {camera.name}"
                });
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    result["base64Data"] = Convert.ToBase64String(imageBytes);
                }
                
                return new TimedCaptureOutcome(result, timings);
            }
            catch (Exception ex)
            {
                return new TimedCaptureOutcome(CreateError($"Failed to capture from camera: {ex.Message}"), null);
            }
        }
        
        /// <summary>
        /// Gets the Game View render size using reflection
        /// </summary>
        private static Vector2Int GetGameViewRenderSize(EditorWindow gameView)
        {
            try
            {
                var gameViewType = gameView.GetType();
                var prop = gameViewType.GetProperty("currentGameViewSize", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                if (prop != null)
                {
                    var size = prop.GetValue(gameView);
                    if (size != null)
                    {
                        var widthProp = size.GetType().GetProperty("width");
                        var heightProp = size.GetType().GetProperty("height");
                        
                        if (widthProp != null && heightProp != null)
                        {
                            int width = (int)widthProp.GetValue(size);
                            int height = (int)heightProp.GetValue(size);
                            return new Vector2Int(width, height);
                        }
                    }
                }
                
                // Fallback to window position size
                return new Vector2Int((int)gameView.position.width, (int)gameView.position.height);
            }
            catch
            {
                // Default resolution if reflection fails
                return new Vector2Int(1920, 1080);
            }
        }
        
        /// <summary>
        /// Captures the current window immediately using screen capture
        /// </summary>
        private static TimedImageBytes CaptureWindowImmediate(int width, int height)
        {
            try
            {
                var timings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                BridgeLogger.Log("ScreenshotHandler", $"CaptureWindowImmediate called: {width}x{height}");
                
                // Create a render texture for immediate capture
                var captureStopwatch = Stopwatch.StartNew();
                RenderTexture renderTexture = new RenderTexture(width, height, 24);
                BridgeLogger.Log("ScreenshotHandler", "RenderTexture created");
                
                // Try to capture from main camera
                Camera camera = Camera.main;
                if (camera == null)
                {
                    BridgeLogger.Log("ScreenshotHandler", "Camera.main is null, looking for alternatives");
                    // Try to find any active camera
                    camera = Camera.current;
                    if (camera == null)
                    {
                        var cameras = Camera.allCameras;
                        if (cameras.Length > 0)
                        {
                            camera = cameras[0];
                            BridgeLogger.Log("ScreenshotHandler", $"Using first available camera: {camera.name}");
                        }
                    }
                    else
                    {
                        BridgeLogger.Log("ScreenshotHandler", $"Using Camera.current: {camera.name}");
                    }
                }
                else
                {
                    BridgeLogger.Log("ScreenshotHandler", $"Using Camera.main: {camera.name}");
                }
                
                if (camera != null)
                {
                    RenderTexture previousTarget = camera.targetTexture;
                    camera.targetTexture = renderTexture;
                    camera.Render();
                    camera.targetTexture = previousTarget;
                    BridgeLogger.Log("ScreenshotHandler", "Camera rendered to texture");
                }
                else
                {
                    BridgeLogger.LogError("ScreenshotHandler", "No camera available for capture");
                    // No camera available, return null
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                    return new TimedImageBytes(null, timings);
                }
                
                // Read pixels
                RenderTexture.active = renderTexture;
                Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();
                BridgeLogger.Log("ScreenshotHandler", "Pixels read and applied");
                
                // Cleanup render texture
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                captureStopwatch.Stop();
                timings["captureMs"] = captureStopwatch.Elapsed.TotalMilliseconds;
                
                // Encode to PNG
                var encodeStopwatch = Stopwatch.StartNew();
                byte[] imageBytes = screenshot.EncodeToPNG();
                encodeStopwatch.Stop();
                timings["encodeMs"] = encodeStopwatch.Elapsed.TotalMilliseconds;
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    BridgeLogger.Log("ScreenshotHandler", $"PNG encoded successfully: {imageBytes.Length} bytes");
                }
                else
                {
                    BridgeLogger.LogError("ScreenshotHandler", "PNG encoding failed or returned empty data");
                }
                
                return new TimedImageBytes(imageBytes, timings);
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ScreenshotHandler", $"Failed to capture window immediately: {ex.Message}");
                BridgeLogger.LogError("ScreenshotHandler", $"Stack trace: {ex.StackTrace}");
                return new TimedImageBytes(null, null);
            }
        }

        private static JObject CreateError(string message)
        {
            return new JObject
            {
                ["error"] = message
            };
        }

        private static JObject AttachTimings(JObject payload, IDictionary<string, double> timings)
        {
            if (payload == null || timings == null || timings.Count == 0)
            {
                return payload;
            }

            foreach (var pair in timings)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value < 0)
                {
                    continue;
                }

                BridgeCommandStats.RecordStageDuration(ToStageKey(pair.Key), pair.Value);
            }

            return payload;
        }

        private static string ToStageKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            var trimmed = key.Trim();
            if (trimmed.EndsWith("Ms", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
            }

            return trimmed.Replace(" ", "_").ToLowerInvariant() + "_ms";
        }

        private static void MergeTimings(IDictionary<string, double> target, IDictionary<string, double> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var pair in source)
            {
                AddTiming(target, pair.Key, pair.Value);
            }
        }

        private static void AddTiming(IDictionary<string, double> timings, string key, double value)
        {
            if (timings == null || string.IsNullOrWhiteSpace(key) || double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return;
            }

            if (timings.TryGetValue(key, out var existing))
            {
                timings[key] = existing + value;
                return;
            }

            timings[key] = value;
        }
        
        /// <summary>
        /// Auto-frames a single target GameObject
        /// </summary>
        private static bool AutoFrameTarget(Camera camera, GameObject target, JObject settings)
        {
            try
            {
                // Get bounds of the target
                Bounds bounds = GetGameObjectBounds(target, settings?["includeChildren"]?.ToObject<bool>() ?? true);
                
                if (bounds.size == Vector3.zero)
                {
                    // No renderer found, use transform position
                    bounds = new Bounds(target.transform.position, Vector3.one * 2f);
                }
                
                // Calculate camera position to frame the target
                float padding = settings?["camera"]?["padding"]?.ToObject<float>() ?? 0.2f;
                float distance = CalculateOptimalDistance(camera, bounds, padding);
                
                // Position camera
                Vector3 offset = settings?["camera"]?["offset"] != null ? 
                    ParseVector3(settings["camera"]["offset"] as JObject) : 
                    new Vector3(0, bounds.size.y * 0.5f, -distance);
                    
                camera.transform.position = bounds.center + offset;
                camera.transform.LookAt(bounds.center);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Auto-frames multiple target GameObjects
        /// </summary>
        private static bool AutoFrameTargets(Camera camera, GameObject[] targets, JObject settings)
        {
            try
            {
                if (targets.Length == 0) return false;
                
                // Calculate combined bounds
                Bounds combinedBounds = GetGameObjectBounds(targets[0], true);
                for (int i = 1; i < targets.Length; i++)
                {
                    Bounds targetBounds = GetGameObjectBounds(targets[i], true);
                    combinedBounds.Encapsulate(targetBounds);
                }
                
                // Frame the combined bounds
                float padding = settings?["camera"]?["padding"]?.ToObject<float>() ?? 0.2f;
                float distance = CalculateOptimalDistance(camera, combinedBounds, padding);
                
                Vector3 offset = new Vector3(0, combinedBounds.size.y * 0.5f, -distance);
                camera.transform.position = combinedBounds.center + offset;
                camera.transform.LookAt(combinedBounds.center);
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Frames a specific area
        /// </summary>
        private static bool FrameArea(Camera camera, Vector3 center, float radius)
        {
            try
            {
                float distance = radius * 2.5f; // Approximate distance for good framing
                camera.transform.position = center + new Vector3(0, radius, -distance);
                camera.transform.LookAt(center);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Gets the bounds of a GameObject including children if specified
        /// </summary>
        private static Bounds GetGameObjectBounds(GameObject obj, bool includeChildren)
        {
            Renderer renderer = includeChildren ? 
                obj.GetComponentInChildren<Renderer>() : 
                obj.GetComponent<Renderer>();
                
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                
                if (includeChildren)
                {
                    Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers)
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }
                
                return bounds;
            }
            
            // No renderer, use collider
            Collider collider = includeChildren ? 
                obj.GetComponentInChildren<Collider>() : 
                obj.GetComponent<Collider>();
                
            if (collider != null)
            {
                return collider.bounds;
            }
            
            // No collider either, return position-based bounds
            return new Bounds(obj.transform.position, Vector3.one);
        }
        
        /// <summary>
        /// Calculates optimal camera distance to frame bounds
        /// </summary>
        private static float CalculateOptimalDistance(Camera camera, Bounds bounds, float padding)
        {
            float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float minDistance = (maxExtent * (1f + padding)) / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            return Mathf.Max(minDistance, 1f); // Minimum 1 unit distance
        }
        
        /// <summary>
        /// Highlights target objects temporarily
        /// </summary>
        private static void HighlightTargets(JObject targetSettings)
        {
            // This is a placeholder for highlight functionality
            // In a full implementation, you might:
            // 1. Add outline shaders
            // 2. Change material colors temporarily
            // 3. Add gizmos or wireframe overlays
            BridgeLogger.Log("ScreenshotHandler", "Target highlighting requested but not yet implemented");
        }
        
        /// <summary>
        /// Parses a Vector3 from JObject
        /// </summary>
        private static Vector3 ParseVector3(JObject obj)
        {
            if (obj == null) return Vector3.zero;
            
            float x = obj["x"]?.ToObject<float>() ?? 0f;
            float y = obj["y"]?.ToObject<float>() ?? 0f;
            float z = obj["z"]?.ToObject<float>() ?? 0f;
            
            return new Vector3(x, y, z);
        }
        
        /// <summary>
        /// Parses a Color from JObject
        /// </summary>
        private static Color ParseColor(JToken colorToken, Color defaultColor)
        {
            if (colorToken == null || colorToken.Type != JTokenType.Object)
                return defaultColor;
                
            JObject colorObj = colorToken as JObject;
            float r = colorObj["r"]?.ToObject<float>() ?? defaultColor.r;
            float g = colorObj["g"]?.ToObject<float>() ?? defaultColor.g;
            float b = colorObj["b"]?.ToObject<float>() ?? defaultColor.b;
            float a = colorObj["a"]?.ToObject<float>() ?? 1f;
            
            return new Color(r, g, b, a);
        }
    }
}
