using System;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Logging;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles play mode control commands (play, pause, stop, get_state)
    /// </summary>
    public static class PlayModeHandler
    {
        private static DateTime _lastPlayRequestTime = DateTime.MinValue;
        private static bool _playRequested;
        public static JObject HandleCommand(string command, JObject parameters)
        {
            try
            {
                switch (command)
                {
                    case "play_game":
                        return HandlePlay(parameters);
                    case "pause_game":
                        return HandlePause();
                    case "stop_game":
                        return HandleStop();
                    case "get_editor_state":
                        return HandleGetState();
                    default:
                        return CreateErrorResponse($"Unknown play mode command: {command}");
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("PlayModeHandler", $"Error handling command {command}: {e.Message}\n{e.StackTrace}");
                return CreateErrorResponse($"Error handling command: {e.Message}");
            }
        }

        private static JObject HandlePlay(JObject parameters)
        {
            try
            {
                // Pre-check: if compiling or there are compile errors, return immediately with reason
                var preCompParams = new JObject { ["includeMessages"] = true, ["maxMessages"] = 100 };
                var preComp = CompilationHandler.GetCompilationState(preCompParams) as JObject;
                bool preCompiling = EditorApplication.isCompiling;
                int preErrors = preComp?["errorCount"]?.ToObject<int?>() ?? 0;
                if (preCompiling || preErrors > 0)
                {
                    var diag = new JObject
                    {
                        ["reason"] = preCompiling ? "compiling" : "compile_errors",
                        ["compilation"] = preComp
                    };
                    return new JObject
                    {
                        ["status"] = "error",
                        ["error"] = preCompiling ? "Cannot enter play mode while compiling" : "Cannot enter play mode due to compile errors",
                        ["state"] = GetEditorState(),
                        ["diagnostics"] = diag
                    };
                }

                string message;
                if (!EditorApplication.isPlaying)
                {
                    _playRequested = true;
                    _lastPlayRequestTime = DateTime.UtcNow;
                    int delayMs = parameters["delayMs"]?.ToObject<int?>() ?? 300;
                    if (delayMs <= 0)
                    {
                        EditorApplication.isPlaying = true;
                        message = "Entered play mode";
                    }
                    else
                    {
                        double target = EditorApplication.timeSinceStartup + (delayMs / 1000.0);
                        EditorApplication.update += DelayedEnter;
                        message = $"Play mode scheduled in {delayMs}ms";
                        void DelayedEnter()
                        {
                            if (EditorApplication.timeSinceStartup >= target)
                            {
                                EditorApplication.update -= DelayedEnter;
                                if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
                            }
                        }
                    }
                }
                else
                {
                    message = "Already in play mode";
                }

                return CreateSuccessResponse(message, GetEditorState());
            }
            catch (Exception e)
            {
                return CreateErrorResponse($"Error entering play mode: {e.Message}");
            }
        }

        private static JObject HandlePause()
        {
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    string message = EditorApplication.isPaused ? "Game paused" : "Game resumed";
                    return CreateSuccessResponse(message, GetEditorState());
                }
                
                return CreateErrorResponse("Cannot pause/resume: Not in play mode");
            }
            catch (Exception e)
            {
                return CreateErrorResponse($"Error pausing/resuming game: {e.Message}");
            }
        }

        private static JObject HandleStop()
        {
            try
            {
                string message;
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    message = "Exited play mode";
                }
                else
                {
                    message = "Already stopped (not in play mode)";
                }

                return CreateSuccessResponse(message, GetEditorState());
            }
            catch (Exception e)
            {
                return CreateErrorResponse($"Error stopping play mode: {e.Message}");
            }
        }

        private static JObject HandleGetState()
        {
            try
            {
                var state = GetEditorState();
                return new JObject
                {
                    ["status"] = "success",
                    ["state"] = state
                };
            }
            catch (Exception e)
            {
                return CreateErrorResponse($"Error getting editor state: {e.Message}");
            }
        }

        private static JObject GetEditorState()
        {
            var state = new JObject
            {
                ["isPlaying"] = EditorApplication.isPlaying,
                ["isPaused"] = EditorApplication.isPaused,
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["applicationPath"] = EditorApplication.applicationPath,
                ["applicationContentsPath"] = EditorApplication.applicationContentsPath,
                ["timeSinceStartup"] = EditorApplication.timeSinceStartup
            };
            // Diagnose playability
            try
            {
                var diag = new JObject();
                bool isCompiling = EditorApplication.isCompiling;
                bool isUpdating = EditorApplication.isUpdating;
                var compParams = new JObject { ["includeMessages"] = true, ["maxMessages"] = 100 };
                var compState = CompilationHandler.GetCompilationState(compParams) as JObject;
                int errorCount = compState?["errorCount"]?.ToObject<int?>() ?? 0;
                string reason = null;
                if (isCompiling) reason = "compiling";
                else if (errorCount > 0) reason = "compile_errors";
                else if (isUpdating) reason = "updating";
                else if (!EditorApplication.isPlaying && _playRequested)
                {
                    var sinceMs = (DateTime.UtcNow - _lastPlayRequestTime).TotalMilliseconds;
                    diag["msSincePlayRequested"] = sinceMs;
                    if (sinceMs > 1500 && errorCount > 0) reason = "compile_errors";
                    else if (sinceMs > 1500 && isCompiling) reason = "compiling";
                    else if (sinceMs > 1500 && isUpdating) reason = "updating";
                    else if (sinceMs > 3000) reason = "unknown_or_blocked";
                }
                diag["reason"] = reason ?? (EditorApplication.isPlaying ? "in_play_mode" : "ready");
                diag["playRequested"] = _playRequested;
                diag["lastPlayRequestTime"] = _lastPlayRequestTime == DateTime.MinValue ? null : _lastPlayRequestTime.ToString("o");
                diag["compilation"] = compState;
                state["playability"] = diag;
            }
            catch { }
            return state;
        }

        private static JObject CreateSuccessResponse(string message, JObject state)
        {
            return new JObject
            {
                ["status"] = "success",
                ["message"] = message,
                ["state"] = state
            };
        }

        private static JObject CreateErrorResponse(string error)
        {
            return new JObject
            {
                ["status"] = "error",
                ["error"] = error
            };
        }
    }
}
