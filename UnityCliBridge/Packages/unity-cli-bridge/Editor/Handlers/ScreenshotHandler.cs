using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles screenshot capture operations in Unity Editor
    /// </summary>
    public static class ScreenshotHandler
    {
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
                    return new { error = "Invalid capture mode. Must be 'game', 'scene', or 'window'" };
                }
                
                // 保存先は固定: <workspace>/.unity/captures/image_<mode>_<timestamp>.png
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    var wsParam = parameters["workspaceRoot"]?.ToString();
                    string workspaceRoot = !string.IsNullOrEmpty(wsParam) ? wsParam : ResolveWorkspaceRoot(projectRoot);
                    var captureDir = Path.Combine(workspaceRoot, ".unity", "captures");
                    outputPath = Path.Combine(captureDir, $"image_{captureMode}_{timestamp}.png");
                }
                
                // Ensure directory exists (support outside Assets)
                string directory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                }
                
                // Capture based on mode
                object result = null;
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
                
                return result;
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ScreenshotHandler", $"Error capturing screenshot: {ex.Message}");
                return new { error = $"Failed to capture screenshot: {ex.Message}" };
            }
        }

        private static string ResolveWorkspaceRoot(string projectRoot)
        {
            try
            {
                // 現在のプロジェクト直下から親方向に最大3階層まで探索
                string dir = projectRoot;
                for (int i = 0; i < 3; i++)
                {
                    var unityDir = Path.Combine(dir, ".unity");
                    if (Directory.Exists(unityDir)) return dir;
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return projectRoot;
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                var na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
        
        /// <summary>
        /// Captures the Game View
        /// </summary>
        private static object CaptureGameView(string outputPath, int width, int height, bool includeUI, bool encodeAsBase64)
        {
            try
            {
                BridgeLogger.Log("ScreenshotHandler", $"CaptureGameView called: path={outputPath}, width={width}, height={height}");
                
                // Try to capture from main camera if in Play Mode
                if (EditorApplication.isPlaying && Camera.main != null)
                {
                    BridgeLogger.Log("ScreenshotHandler", "Using main camera in Play Mode");
                    return CaptureFromCamera(Camera.main, outputPath, width, height, "game", encodeAsBase64);
                }
                
                // Otherwise try to get Game View and capture it
                BridgeLogger.Log("ScreenshotHandler", "Getting Game View window");
                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType, false);
                
                if (gameView == null)
                {
                    BridgeLogger.LogError("ScreenshotHandler", "Game View not found");
                    return new { error = "Game View not found. Please open the Game View window." };
                }
                
                // Focus the Game View
                gameView.Focus();
                BridgeLogger.Log("ScreenshotHandler", "Game View focused");
                
                // Use reflection to get the Game View's render size
                var renderSize = GetGameViewRenderSize(gameView);
                int captureWidth = width > 0 ? width : renderSize.x;
                int captureHeight = height > 0 ? height : renderSize.y;
                BridgeLogger.Log("ScreenshotHandler", $"Capture size: {captureWidth}x{captureHeight}");
                
                // Create RenderTexture and capture
                BridgeLogger.Log("ScreenshotHandler", "Calling CaptureWindowImmediate");
                byte[] imageBytes = CaptureWindowImmediate(captureWidth, captureHeight);
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    BridgeLogger.LogError("ScreenshotHandler", "CaptureWindowImmediate returned null or empty data");
                    return new { error = "Failed to capture Game View - no image data" };
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
                File.WriteAllBytes(outputPath, imageBytes);
                
                if (!File.Exists(outputPath))
                {
                    BridgeLogger.LogError("ScreenshotHandler", $"File was not created at: {outputPath}");
                    return new { error = "Failed to capture screenshot - file not created" };
                }
                
                BridgeLogger.Log("ScreenshotHandler", $"File created successfully: {outputPath}");
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                var result = new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = "game",
                    includeUI = includeUI,
                    fileSize = imageBytes.Length,
                    message = "Game View screenshot captured successfully"
                };
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    return new
                    {
                        result.path,
                        result.width,
                        result.height,
                        result.captureMode,
                        result.includeUI,
                        result.fileSize,
                        result.message,
                        base64Data = Convert.ToBase64String(imageBytes)
                    };
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to capture Game View: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Captures the Scene View
        /// </summary>
        private static object CaptureSceneView(string outputPath, int width, int height, bool encodeAsBase64)
        {
            try
            {
                // Get the Scene View
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new { error = "Scene View not found. Please open a Scene View window." };
                }
                
                // Focus the Scene View
                sceneView.Focus();
                
                // Get Scene View camera
                Camera sceneCamera = sceneView.camera;
                if (sceneCamera == null)
                {
                    return new { error = "Scene View camera not available" };
                }
                
                // Determine capture resolution
                int captureWidth = width > 0 ? width : (int)sceneView.position.width;
                int captureHeight = height > 0 ? height : (int)sceneView.position.height;
                
                // Create render texture
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
                
                // Encode to PNG
                byte[] imageBytes = screenshot.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                // Save to file
                File.WriteAllBytes(outputPath, imageBytes);
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                var result = new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = "scene",
                    fileSize = imageBytes.Length,
                    cameraPosition = new { x = sceneCamera.transform.position.x, y = sceneCamera.transform.position.y, z = sceneCamera.transform.position.z },
                    cameraRotation = new { x = sceneCamera.transform.eulerAngles.x, y = sceneCamera.transform.eulerAngles.y, z = sceneCamera.transform.eulerAngles.z },
                    message = "Scene View screenshot captured successfully"
                };
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    return new
                    {
                        result.path,
                        result.width,
                        result.height,
                        result.captureMode,
                        result.fileSize,
                        result.cameraPosition,
                        result.cameraRotation,
                        result.message,
                        base64Data = Convert.ToBase64String(imageBytes)
                    };
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to capture Scene View: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Captures a specific Editor Window
        /// </summary>
        private static object CaptureEditorWindow(string outputPath, string windowName, bool encodeAsBase64)
        {
            try
            {
                if (string.IsNullOrEmpty(windowName))
                {
                    return new { error = "windowName is required for window capture mode" };
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
                    return new { error = $"Window '{windowName}' not found" };
                }
                
                // Focus the window
                targetWindow.Focus();
                
                // Get window dimensions
                int width = (int)targetWindow.position.width;
                int height = (int)targetWindow.position.height;
                
                // Note: Direct window capture is limited in Unity Editor
                // This is a placeholder for the approach
                return new { error = "Direct window capture is not fully supported. Use 'game' or 'scene' mode instead." };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to capture window: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Captures using LLM Explorer mode - allows AI to explore the scene freely
        /// </summary>
        private static object CaptureExplorerView(string outputPath, JObject explorerSettings, bool encodeAsBase64)
        {
            try
            {
                BridgeLogger.Log("ScreenshotHandler", "CaptureExplorerView called");
                
                // Parse explorer settings
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
                    
                    // Create render texture and capture
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
                    
                    // Encode to PNG
                    byte[] imageBytes = screenshot.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(screenshot);
                    
                    // Save to file
                    File.WriteAllBytes(outputPath, imageBytes);
                    UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                    
                    var result = new
                    {
                        path = outputPath,
                        width = width,
                        height = height,
                        captureMode = "explorer",
                        fileSize = imageBytes.Length,
                        cameraPosition = new { x = explorerCamera.transform.position.x, y = explorerCamera.transform.position.y, z = explorerCamera.transform.position.z },
                        cameraRotation = new { x = explorerCamera.transform.eulerAngles.x, y = explorerCamera.transform.eulerAngles.y, z = explorerCamera.transform.eulerAngles.z },
                        message = "Explorer view screenshot captured successfully"
                    };
                    
                    // Add base64 if requested
                    if (encodeAsBase64)
                    {
                        return new
                        {
                            result.path,
                            result.width,
                            result.height,
                            result.captureMode,
                            result.fileSize,
                            result.cameraPosition,
                            result.cameraRotation,
                            result.message,
                            base64Data = Convert.ToBase64String(imageBytes)
                        };
                    }
                    
                    return result;
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
                return new { error = $"Failed to capture explorer view: {ex.Message}" };
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
                string analysisType = parameters["analysisType"]?.ToString() ?? "basic"; // basic, ui, content
                
                if (string.IsNullOrEmpty(imagePath))
                {
                    return new { error = "imagePath is required" };
                }
                
                if (!File.Exists(imagePath))
                {
                    return new { error = $"Image file not found: {imagePath}" };
                }
                
                // Load the image
                byte[] imageBytes = File.ReadAllBytes(imagePath);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageBytes);
                
                var analysis = new
                {
                    success = true,
                    imagePath = imagePath,
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
        private static object CaptureFromCamera(Camera camera, string outputPath, int width, int height, string captureMode, bool encodeAsBase64)
        {
            try
            {
                // Determine capture resolution
                int captureWidth = width > 0 ? width : camera.pixelWidth;
                int captureHeight = height > 0 ? height : camera.pixelHeight;
                
                // Create RenderTexture
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
                
                // Encode to PNG
                byte[] imageBytes = screenshot.EncodeToPNG();
                
                // Cleanup
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    return new { error = "Failed to encode screenshot" };
                }
                
                // Save to file
                File.WriteAllBytes(outputPath, imageBytes);
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                
                var result = new
                {
                    path = outputPath,
                    width = captureWidth,
                    height = captureHeight,
                    captureMode = captureMode,
                    fileSize = imageBytes.Length,
                    message = $"Screenshot captured successfully from {camera.name}"
                };
                
                // Add base64 if requested
                if (encodeAsBase64)
                {
                    return new
                    {
                        result.path,
                        result.width,
                        result.height,
                        result.captureMode,
                        result.fileSize,
                        result.message,
                        base64Data = Convert.ToBase64String(imageBytes)
                    };
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to capture from camera: {ex.Message}" };
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
        private static byte[] CaptureWindowImmediate(int width, int height)
        {
            try
            {
                BridgeLogger.Log("ScreenshotHandler", $"CaptureWindowImmediate called: {width}x{height}");
                
                // Create a render texture for immediate capture
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
                    return null;
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
                
                // Encode to PNG
                byte[] imageBytes = screenshot.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(screenshot);
                
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    BridgeLogger.Log("ScreenshotHandler", $"PNG encoded successfully: {imageBytes.Length} bytes");
                }
                else
                {
                    BridgeLogger.LogError("ScreenshotHandler", "PNG encoding failed or returned empty data");
                }
                
                return imageBytes;
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("ScreenshotHandler", $"Failed to capture window immediately: {ex.Message}");
                BridgeLogger.LogError("ScreenshotHandler", $"Stack trace: {ex.StackTrace}");
                return null;
            }
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
