using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Minimal video capture handler skeleton.
    /// Phase 1: manage session state and paths without actual encoding.
    /// Later phases will integrate Unity Recorder / ffmpeg / PNG fallback.
    /// </summary>
    public static class VideoCaptureHandler
    {
        private static bool s_IsRecording;
        private static string s_RecordingId;
        private static string s_OutputPath;
        private static DateTime s_StartedAt;
        private static int s_Frames;
        private static string s_CaptureMode;
        private static int s_Fps;
        private static int s_Width;
        private static int s_Height;
        // Recorder integration (必須依存)
        private static RecorderController s_RecorderController;
        private static RecorderControllerSettings s_RecorderControllerSettings;
        private static MovieRecorderSettings s_MovieRecorderSettings;
        private static bool s_IncludeUI;
        private static double s_MaxDurationSec;
        private static bool s_AutoStopping;
        private static double s_LastCaptureTime;

        public static object Start(JObject parameters)
        {
            try
            {
                if (s_IsRecording)
                {
                    return new { error = "A recording session is already running.", recordingId = s_RecordingId };
                }

                s_CaptureMode = parameters["captureMode"]?.ToString() ?? "game";
                if (!IsValidCaptureMode(s_CaptureMode))
                {
                    return new { error = "Invalid capture mode. Must be 'game'", code = "E_INVALID_MODE" };
                }

                s_Width = parameters["width"]?.ToObject<int>() ?? 0;
                s_Height = parameters["height"]?.ToObject<int>() ?? 0;
                s_Fps = Math.Max(1, parameters["fps"]?.ToObject<int>() ?? 30);
                s_IncludeUI = parameters["includeUI"]?.ToObject<bool>() ?? true;
                s_MaxDurationSec = Math.Max(0, parameters["maxDurationSec"]?.ToObject<double>() ?? 0);

                // 固定保存先: <workspace>/.unity/captures
                string format = parameters["format"]?.ToString() ?? "mp4";
                if (!IsValidFormat(format))
                {
                    return new { error = "Invalid format. Use 'mp4', 'webm' or 'png_sequence'", code = "E_INVALID_FORMAT" };
                }
                // 生成ファイルパスを固定で作成 (<workspace>/.unity/captures)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    var wsParam = parameters["workspaceRoot"]?.ToString();
                    string workspaceRoot = !string.IsNullOrEmpty(wsParam) ? wsParam : ResolveWorkspaceRoot(projectRoot);
                    var captureDir = Path.Combine(workspaceRoot, ".unity", "captures");
                    if (!Directory.Exists(captureDir)) Directory.CreateDirectory(captureDir);
                    string ext = string.Equals(format, "webm", StringComparison.OrdinalIgnoreCase) ? ".webm" : ".mp4";
                    s_OutputPath = Path.Combine(captureDir, $"video_{s_CaptureMode}_{timestamp}{ext}");
                }

                // Guard: dimensions
                if (s_Width < 0 || s_Height < 0)
                {
                    return new { error = "Width/Height must be >= 0", code = "E_INVALID_SIZE" };
                }

                // 保存先ディレクトリを用意（Assets外も許可）
                EnsureDirectory(s_OutputPath);
                // 今回は GameView のみ対応（必須依存のRecorder使用）
                if (!string.Equals(s_CaptureMode, "game", StringComparison.OrdinalIgnoreCase))
                {
                    return new { error = "Unsupported capture mode for Recorder. Use 'game'", code = "E_UNSUPPORTED_MODE" };
                }

                // Recorder 設定
                s_RecorderControllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                s_RecorderController = new RecorderController(s_RecorderControllerSettings);
                s_MovieRecorderSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

                s_MovieRecorderSettings.Enabled = true;
                // 出力先（ワークスペース直下 .unity/captures/<file>）に設定
                var fileNoExt = Path.GetFileNameWithoutExtension(s_OutputPath);
                s_MovieRecorderSettings.FileNameGenerator.Root = OutputPath.Root.Project;
                {
                    string captureDir = Path.GetDirectoryName(s_OutputPath);
                    string leaf = "../.unity/captures";
                    try
                    {
                        if (!string.IsNullOrEmpty(captureDir))
                        {
                            leaf = Path.GetRelativePath(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), captureDir);
                            if (Path.IsPathRooted(leaf)) leaf = "../.unity/captures";
                        }
                    }
                    catch { leaf = "../.unity/captures"; }
                    leaf = leaf.Replace('\\', '/');
                    if (!leaf.StartsWith("/")) leaf = "/" + leaf;
                    s_MovieRecorderSettings.FileNameGenerator.Leaf = leaf;
                }
                s_MovieRecorderSettings.FileNameGenerator.FileName = fileNoExt;
                // フォーマット設定はデフォルト（MP4/H.264）を使用

                int ow = s_Width > 0 ? s_Width : 1280;
                int oh = s_Height > 0 ? s_Height : 720;
                var input = new GameViewInputSettings
                {
                    OutputWidth = ow,
                    OutputHeight = oh
                };
                s_MovieRecorderSettings.ImageInputSettings = input;
                s_MovieRecorderSettings.FrameRate = s_Fps;
                // 音声（最小有効化）
                if (s_MovieRecorderSettings.AudioInputSettings != null)
                {
                    s_MovieRecorderSettings.AudioInputSettings.PreserveAudio = true;
                }

                // 収録動作パラメータ
                s_RecorderControllerSettings.FrameRatePlayback = FrameRatePlayback.Variable;
                s_RecorderControllerSettings.FrameRate = s_Fps;
                s_RecorderControllerSettings.CapFrameRate = false;
                s_RecorderControllerSettings.AddRecorderSettings(s_MovieRecorderSettings);
                s_RecorderControllerSettings.SetRecordModeToManual();
                s_RecorderController.PrepareRecording();
                var startedOk = s_RecorderController.StartRecording();
                RecorderOptions.VerboseMode = true;
                if (!startedOk)
                {
                    BridgeLogger.LogError("VideoCaptureHandler", "Recorder did not start (StartRecording returned false)");
                }
                s_LastCaptureTime = EditorApplication.timeSinceStartup;
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;

                s_RecordingId = Guid.NewGuid().ToString("N");
                s_StartedAt = DateTime.UtcNow;
                s_Frames = 0;
                s_IsRecording = true;

                // Start session
                return new
                {
                    recordingId = s_RecordingId,
                    outputPath = s_OutputPath,
                    captureMode = s_CaptureMode,
                    fps = s_Fps,
                    width = s_Width,
                    height = s_Height,
                    startedAt = s_StartedAt.ToString("o"),
                    note = "Recording started (Recorder mp4/webm).",
                    isRecording = s_RecorderController.IsRecording()
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("VideoCaptureHandler", $"Start error: {ex.Message}");
                return new { error = $"Failed to start recording: {ex.Message}", code = "E_UNKNOWN" };
            }
        }

        public static object Stop(JObject _)
        {
            try
            {
                if (!s_IsRecording)
                {
                    return new { error = "No active recording session." };
                }

                var id = s_RecordingId;
                var path = s_OutputPath;
                var started = s_StartedAt;
                var frames = s_Frames;
                var mode = s_CaptureMode;
                var fps = s_Fps;

                s_IsRecording = false;
                s_RecordingId = null;
                // detach update
                EditorApplication.update -= OnEditorUpdate;
                // stop recorder
                if (s_RecorderController != null)
                {
                    try { s_RecorderController.StopRecording(); } catch (Exception e) { BridgeLogger.LogWarning("VideoCaptureHandler", $"Recorder stop warning: {e.Message}"); }
                }
                s_AutoStopping = false;

                double duration = (DateTime.UtcNow - started).TotalSeconds;

                return new
                {
                    recordingId = id,
                    outputPath = path,
                    captureMode = mode,
                    durationSec = Math.Max(0, duration),
                    frames = frames,
                    fps = fps,
                    note = "Recording stopped (Recorder)."
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("VideoCaptureHandler", $"Stop error: {ex.Message}");
                return new { error = $"Failed to stop recording: {ex.Message}" };
            }
        }

        public static object Status(JObject _)
        {
            try
            {
                if (!s_IsRecording)
                {
                    return new { isRecording = false };
                }

                double elapsed = (DateTime.UtcNow - s_StartedAt).TotalSeconds;
                return new
                {
                    isRecording = true,
                    recordingId = s_RecordingId,
                    outputPath = s_OutputPath,
                    captureMode = s_CaptureMode,
                    elapsedSec = Math.Max(0, elapsed),
                    frames = s_Frames,
                    fps = s_Fps
                };
            }
            catch (Exception ex)
            {
                BridgeLogger.LogError("VideoCaptureHandler", $"Status error: {ex.Message}");
                return new { error = $"Failed to get recording status: {ex.Message}" };
            }
        }

        private static string ResolveWorkspaceRoot(string projectRoot)
        {
            try
            {
                string dir = projectRoot;
                for (int i = 0; i < 10; i++)
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

        private static bool IsValidCaptureMode(string mode)
        {
            // 現段階では GameView のみ対応
            return mode == "game";
        }

        private static bool IsValidFormat(string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return false;
            fmt = fmt.ToLowerInvariant();
            return fmt == "mp4" || fmt == "webm" || fmt == "png_sequence";
        }

        private static void EnsureDirectory(string outputPath)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!s_IsRecording) return;
            double now = EditorApplication.timeSinceStartup;
            double interval = 1.0 / Math.Max(1, s_Fps);
            if (now - s_LastCaptureTime + 1e-6 < interval) return;
            s_LastCaptureTime = now;
            // 期待フレーム数（単純計数）
            s_Frames++;

            // Auto stop by duration
            if (!s_AutoStopping && s_MaxDurationSec > 0)
            {
                double elapsed = (DateTime.UtcNow - s_StartedAt).TotalSeconds;
                if (elapsed >= s_MaxDurationSec)
                {
                    s_AutoStopping = true;
                    EditorApplication.delayCall += () => {
                        try { Stop(null); } catch { /* ignore */ }
                    };
                }
            }
        }

        // 反射ユーティリティは不要になったため削除
    }
}
