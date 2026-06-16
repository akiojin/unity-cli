using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Handlers;
using UnityCliBridge.Helpers;
using UnityCliBridge.Logging;
using UnityCliBridge.Models;

namespace UnityCliBridge.Core
{
    internal static class BridgeCommandRouter
    {
        private const int MinEditorStateIntervalMs = 250;
        private static DateTime lastEditorStateQueryTime = DateTime.MinValue;
        private static object lastEditorStateData = null;

        private delegate Task<string> CommandHandler(Command command);

        private static readonly IReadOnlyDictionary<string, CommandHandler> Handlers =
            new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase)
            {
                ["ping"] = command => Success(command, new
                {
                    message = "pong",
                    echo = command.Parameters?["message"]?.ToString(),
                    timestamp = DateTime.UtcNow.ToString("o")
                }),
                ["clear_logs"] = command =>
                {
                    LogCapture.ClearLogs();
                    return Success(command, new
                    {
                        message = "Logs cleared successfully",
                        timestamp = DateTime.UtcNow.ToString("o")
                    });
                },
                ["refresh_assets"] = command =>
                {
                    AssetDatabase.Refresh();
                    return Success(command, new
                    {
                        message = "Asset refresh triggered",
                        isCompiling = EditorApplication.isCompiling,
                        timestamp = DateTime.UtcNow.ToString("o")
                    });
                },
                ["create_gameobject"] = command => Success(command, GameObjectHandler.CreateGameObject(command.Parameters)),
                ["find_gameobject"] = command => Success(command, GameObjectHandler.FindGameObjects(command.Parameters)),
                ["modify_gameobject"] = command => Success(command, GameObjectHandler.ModifyGameObject(command.Parameters)),
                ["delete_gameobject"] = command => Success(command, GameObjectHandler.DeleteGameObject(command.Parameters)),
                ["get_hierarchy"] = command => Success(command, GameObjectHandler.GetHierarchy(command.Parameters)),
                ["create_scene"] = command => Success(command, SceneHandler.CreateScene(command.Parameters)),
                ["load_scene"] = command => Success(command, SceneHandler.LoadScene(command.Parameters)),
                ["save_scene"] = command => Success(command, SceneHandler.SaveScene(command.Parameters)),
                ["list_scenes"] = command => Success(command, SceneHandler.ListScenes(command.Parameters)),
                ["get_scene_info"] = command => Success(command, SceneHandler.GetSceneInfo(command.Parameters)),
                ["get_gameobject_details"] = command => Success(command, SceneAnalysisHandler.GetGameObjectDetails(command.Parameters)),
                ["analyze_scene_contents"] = command => Success(command, SceneAnalysisHandler.AnalyzeSceneContents(command.Parameters)),
                ["get_component_values"] = command => Success(command, SceneAnalysisHandler.GetComponentValues(command.Parameters)),
                ["find_by_component"] = command => Success(command, SceneAnalysisHandler.FindByComponent(command.Parameters)),
                ["get_object_references"] = command => Success(command, SceneAnalysisHandler.GetObjectReferences(command.Parameters)),
                ["get_animator_state"] = command => Success(command, AnimatorStateHandler.GetAnimatorState(command.Parameters)),
                ["get_animator_runtime_info"] = command => Success(command, AnimatorStateHandler.GetAnimatorRuntimeInfo(command.Parameters)),
                ["get_input_actions_state"] = command => Success(command, InputActionsHandler.GetInputActionsState(command.Parameters)),
                ["analyze_input_actions_asset"] = command => Success(command, InputActionsHandler.AnalyzeInputActionsAsset(command.Parameters)),
                ["create_action_map"] = command => Success(command, InputActionsHandler.CreateActionMap(command.Parameters)),
                ["remove_action_map"] = command => Success(command, InputActionsHandler.RemoveActionMap(command.Parameters)),
                ["add_input_action"] = command => Success(command, InputActionsHandler.AddInputAction(command.Parameters)),
                ["remove_input_action"] = command => Success(command, InputActionsHandler.RemoveInputAction(command.Parameters)),
                ["add_input_binding"] = command => Success(command, InputActionsHandler.AddInputBinding(command.Parameters)),
                ["remove_input_binding"] = command => Success(command, InputActionsHandler.RemoveInputBinding(command.Parameters)),
                ["remove_all_bindings"] = command => Success(command, InputActionsHandler.RemoveAllBindings(command.Parameters)),
                ["create_composite_binding"] = command => Success(command, InputActionsHandler.CreateCompositeBinding(command.Parameters)),
                ["manage_control_schemes"] = command => Success(command, InputActionsHandler.ManageControlSchemes(command.Parameters)),
                ["play_game"] = command => Success(command, PlayModeHandler.HandleCommand("play_game", command.Parameters)),
                ["pause_game"] = command => Success(command, PlayModeHandler.HandleCommand("pause_game", command.Parameters)),
                ["stop_game"] = command => Success(command, PlayModeHandler.HandleCommand("stop_game", command.Parameters)),
                ["get_editor_state"] = HandleGetEditorState,
                ["find_ui_elements"] = command => Success(command, UIInteractionHandler.FindUIElements(command.Parameters)),
                ["click_ui_element"] = async command => Response.SuccessResult(command.Id, await UIInteractionHandler.ClickUIElement(command.Parameters)),
                ["get_ui_element_state"] = command => Success(command, UIInteractionHandler.GetUIElementState(command.Parameters)),
                ["set_ui_element_value"] = command => Success(command, UIInteractionHandler.SetUIElementValue(command.Parameters)),
                ["simulate_ui_input"] = async command => Response.SuccessResult(command.Id, await UIInteractionHandler.SimulateUIInput(command.Parameters)),
#if ENABLE_INPUT_SYSTEM
                ["input_keyboard"] = command => Success(command, InputSystemHandler.SimulateKeyboardInput(command.Parameters)),
                ["input_mouse"] = command => Success(command, InputSystemHandler.SimulateMouseInput(command.Parameters)),
                ["input_gamepad"] = command => Success(command, InputSystemHandler.SimulateGamepadInput(command.Parameters)),
                ["input_touch"] = command => Success(command, InputSystemHandler.SimulateTouchInput(command.Parameters)),
                ["create_input_sequence"] = command => Success(command, InputSystemHandler.CreateInputSequence(command.Parameters)),
                ["get_current_input_state"] = command => Success(command, InputSystemHandler.GetCurrentInputState(command.Parameters)),
#endif
                ["create_animator_controller"] = command => Success(command, AssetManagementHandler.CreateAnimatorController(command.Parameters)),
                ["create_animation_clip"] = command => Success(command, AssetManagementHandler.CreateAnimationClip(command.Parameters)),
                ["create_sprite_atlas"] = command => Success(command, AssetManagementHandler.CreateSpriteAtlas(command.Parameters)),
                ["create_prefab"] = command => Success(command, AssetManagementHandler.CreatePrefab(command.Parameters)),
                ["modify_prefab"] = command => Success(command, AssetManagementHandler.ModifyPrefab(command.Parameters)),
                ["instantiate_prefab"] = command => Success(command, AssetManagementHandler.InstantiatePrefab(command.Parameters)),
                ["create_material"] = command => Success(command, AssetManagementHandler.CreateMaterial(command.Parameters)),
                ["modify_material"] = command => Success(command, AssetManagementHandler.ModifyMaterial(command.Parameters)),
                ["open_prefab"] = command => Success(command, AssetManagementHandler.OpenPrefab(command.Parameters)),
                ["exit_prefab_mode"] = command => Success(command, AssetManagementHandler.ExitPrefabMode(command.Parameters)),
                ["save_prefab"] = command => Success(command, AssetManagementHandler.SavePrefab(command.Parameters)),
                ["execute_menu_item"] = command => Success(command, MenuHandler.ExecuteMenuItem(command.Parameters)),
                ["package_manager"] = command => Success(command, PackageManagerHandler.HandleCommand(command.Parameters?["action"]?.ToString() ?? "list", command.Parameters)),
                ["registry_config"] = command => Success(command, RegistryConfigHandler.HandleCommand(command.Parameters?["action"]?.ToString() ?? "list", command.Parameters)),
                ["clear_console"] = command => Success(command, ConsoleHandler.ClearConsole(command.Parameters)),
                ["read_console"] = command => Success(command, ConsoleHandler.ReadConsole(command.Parameters)),
                ["capture_screenshot"] = command => Success(command, ScreenshotHandler.CaptureScreenshot(command.Parameters)),
                ["analyze_screenshot"] = command => Success(command, ScreenshotHandler.AnalyzeScreenshot(command.Parameters)),
                ["capture_video_start"] = command => Success(command, VideoCaptureHandler.Start(command.Parameters)),
                ["capture_video_stop"] = command => Success(command, VideoCaptureHandler.Stop(command.Parameters)),
                ["capture_video_status"] = command => Success(command, VideoCaptureHandler.Status(command.Parameters)),
                ["profiler_start"] = command => Success(command, ProfilerHandler.Start(command.Parameters)),
                ["profiler_stop"] = command => Success(command, ProfilerHandler.Stop(command.Parameters)),
                ["profiler_status"] = command => Success(command, ProfilerHandler.GetStatus(command.Parameters)),
                ["profiler_get_metrics"] = command => Success(command, ProfilerHandler.GetAvailableMetrics(command.Parameters)),
                ["add_component"] = command => Success(command, ComponentHandler.AddComponent(command.Parameters)),
                ["remove_component"] = command => Success(command, ComponentHandler.RemoveComponent(command.Parameters)),
                ["modify_component"] = command => Success(command, ComponentHandler.ModifyComponent(command.Parameters)),
                ["set_component_field"] = command => Success(command, ComponentHandler.SetComponentField(command.Parameters)),
                ["list_components"] = command => Success(command, ComponentHandler.ListComponents(command.Parameters)),
                ["get_component_types"] = command => Success(command, ComponentHandler.GetComponentTypes(command.Parameters)),
                ["get_compilation_state"] = command => Success(command, CompilationHandler.GetCompilationState(command.Parameters)),
                ["run_tests"] = command => Success(command, TestExecutionHandler.RunTests(command.Parameters)),
                ["get_test_status"] = command => Success(command, TestExecutionHandler.GetTestStatus(command.Parameters)),
                ["quit_editor"] = command =>
                {
                    var response = Response.SuccessResult(command.Id, new { message = "Unity Editor quitting" });
                    EditorApplication.delayCall += () => EditorApplication.Exit(0);
                    return Task.FromResult(response);
                },
                ["manage_tags"] = command => Success(command, TagManagementHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_layers"] = command => Success(command, LayerManagementHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_selection"] = command => Success(command, SelectionHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_windows"] = command => Success(command, WindowManagementHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_tools"] = command => Success(command, ToolManagementHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_asset_import_settings"] = command => Success(command, AssetImportSettingsHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["manage_asset_database"] = command => Success(command, AssetDatabaseHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["analyze_asset_dependencies"] = command => Success(command, AssetDependencyHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["addressables_manage"] = command => Success(command, AddressablesHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["addressables_build"] = command => Success(command, AddressablesHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["addressables_analyze"] = command => Success(command, AddressablesHandler.HandleCommand(command.Parameters["action"]?.ToString(), command.Parameters)),
                ["get_project_setting"] = command => Success(command, ProjectSettingsHandler.GetProjectSetting(command.Parameters)),
                ["set_project_setting"] = command => Success(command, ProjectSettingsHandler.SetProjectSetting(command.Parameters)),
                ["get_project_settings"] = command => Success(command, ProjectSettingsHandler.GetProjectSettings(command.Parameters)),
                ["get_package_setting"] = command => Success(command, PackageSettingsHandler.GetPackageSetting(command.Parameters)),
                ["set_package_setting"] = command => Success(command, PackageSettingsHandler.SetPackageSetting(command.Parameters)),
                ["get_editor_info"] = HandleGetEditorInfo,
                ["update_project_settings"] = command => Success(command, ProjectSettingsHandler.UpdateProjectSettings(command.Parameters)),
                ["get_command_stats"] = command => Success(command, BridgeCommandStats.CaptureSnapshot())
            };

        internal static IReadOnlyCollection<string> RegisteredCommandTypes => Handlers.Keys.ToArray();

        internal static Task<string> Handle(Command command)
        {
            if (command?.Type != null && Handlers.TryGetValue(command.Type, out var handler))
            {
                return handler(command);
            }

            return Task.FromResult(Response.ErrorResult(
                command?.Id,
                $"Unknown command type: {command?.Type}",
                "UNKNOWN_COMMAND",
                new { commandType = command?.Type }
            ));
        }

        private static Task<string> Success(Command command, object result) =>
            Task.FromResult(Response.SuccessResult(command.Id, result));

        private static Task<string> HandleGetEditorState(Command command)
        {
            var now = DateTime.UtcNow;
            if ((now - lastEditorStateQueryTime).TotalMilliseconds < MinEditorStateIntervalMs && lastEditorStateData != null)
            {
                return Success(command, lastEditorStateData);
            }

            var stateResult = PlayModeHandler.HandleCommand("get_editor_state", command.Parameters);
            lastEditorStateQueryTime = now;
            lastEditorStateData = stateResult;
            return Success(command, stateResult);
        }

        private static Task<string> HandleGetEditorInfo(Command command)
        {
            try
            {
                var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length).Replace('\\', '/');
                var assetsPath = Path.Combine(projectRoot, "Assets").Replace('\\', '/');
                var packagesPath = Path.Combine(projectRoot, "Packages").Replace('\\', '/');
                var workspaceRoot = ResolveWorkspaceRoot(projectRoot);
                var codeIndexRoot = Path.Combine(workspaceRoot, ".unity", "cache", "code-index").Replace('\\', '/');
                var info = new
                {
                    projectRoot,
                    assetsPath,
                    packagesPath,
                    codeIndexRoot,
                    unity = new
                    {
                        productName = Application.productName,
                        unityVersion = Application.unityVersion,
                        platform = Application.platform.ToString()
                    }
                };
                return Success(command, info);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Response.ErrorResult(command.Id, $"Failed to get editor info: {ex.Message}", "GET_EDITOR_INFO_ERROR", null));
            }
        }

        private static string ResolveWorkspaceRoot(string projectRoot)
        {
            try
            {
                var dir = projectRoot;
                for (var i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
                {
                    var unityDir = Path.Combine(dir, ".unity");
                    if (Directory.Exists(unityDir))
                    {
                        return dir.Replace('\\', '/');
                    }

                    var parent = Directory.GetParent(dir);
                    if (parent == null)
                    {
                        break;
                    }

                    dir = parent.FullName;
                }
            }
            catch
            {
            }

            return projectRoot.Replace('\\', '/');
        }
    }
}
