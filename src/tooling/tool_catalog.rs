use serde::Serialize;
use serde_json::{json, Value};

pub const TOOL_NAMES: &[&str] = &[
    "addressables_analyze",
    "addressables_build",
    "addressables_manage",
    "get_animator_runtime_info",
    "get_animator_state",
    "create_animator_controller",
    "create_animation_clip",
    "create_sprite_atlas",
    "find_by_component",
    "get_component_values",
    "get_gameobject_details",
    "get_object_references",
    "analyze_scene_contents",
    "manage_asset_database",
    "analyze_asset_dependencies",
    "manage_asset_import_settings",
    "create_material",
    "modify_material",
    "create_prefab",
    "exit_prefab_mode",
    "instantiate_prefab",
    "modify_prefab",
    "open_prefab",
    "save_prefab",
    "build_index",
    "update_index",
    "get_compilation_state",
    "add_component",
    "set_component_field",
    "get_component_types",
    "list_components",
    "modify_component",
    "remove_component",
    "clear_console",
    "clear_logs",
    "read_console",
    "manage_layers",
    "quit_editor",
    "manage_selection",
    "manage_tags",
    "manage_tools",
    "manage_windows",
    "create_gameobject",
    "delete_gameobject",
    "find_gameobject",
    "get_hierarchy",
    "modify_gameobject",
    "add_input_action",
    "create_action_map",
    "remove_action_map",
    "remove_input_action",
    "analyze_input_actions_asset",
    "get_input_actions_state",
    "add_input_binding",
    "create_composite_binding",
    "remove_input_binding",
    "remove_all_bindings",
    "manage_control_schemes",
    "input_gamepad",
    "input_keyboard",
    "input_mouse",
    "input_touch",
    "create_input_sequence",
    "get_current_input_state",
    "execute_menu_item",
    "package_manager",
    "registry_config",
    "get_editor_info",
    "get_editor_state",
    "pause_game",
    "play_game",
    "stop_game",
    "profiler_get_metrics",
    "profiler_start",
    "profiler_status",
    "profiler_stop",
    "create_scene",
    "get_scene_info",
    "list_scenes",
    "load_scene",
    "save_scene",
    "analyze_screenshot",
    "capture_screenshot",
    "list_packages",
    "read",
    "find_refs",
    "search",
    "find_symbol",
    "get_symbols",
    "rename_symbol",
    "replace_symbol_body",
    "insert_before_symbol",
    "insert_after_symbol",
    "remove_symbol",
    "validate_text_edits",
    "write_csharp_file",
    "create_csharp_file",
    "apply_csharp_edits",
    "create_class",
    "get_project_setting",
    "set_project_setting",
    "get_project_settings",
    "get_package_setting",
    "set_package_setting",
    "update_project_settings",
    "get_command_stats",
    "ping",
    "refresh_assets",
    "get_test_status",
    "run_tests",
    "click_ui_element",
    "find_ui_elements",
    "get_ui_element_state",
    "set_ui_element_value",
    "simulate_ui_input",
    "capture_video_start",
    "capture_video_status",
    "capture_video_stop",
    "reference_fetch",
    "reference_status",
    "reference_search",
    "reference_grep",
    "reference_view",
    "reference_clean",
    "reference_find_symbol",
    "reference_diff",
    "reference_resolve_symbol_at",
    "reference_embed_build",
    "reference_embed_search",
];

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum ToolExecutor {
    Local,
    Remote,
}

#[derive(Debug, Clone, Serialize)]
pub struct ToolSpec {
    pub name: &'static str,
    pub description: &'static str,
    pub mutating: bool,
    pub executor: ToolExecutor,
    pub params_schema: Value,
    pub response_schema: Value,
}

pub fn is_known_tool(name: &str) -> bool {
    TOOL_NAMES.contains(&name)
}

pub fn get_tool_spec(name: &str) -> Option<ToolSpec> {
    if !is_known_tool(name) {
        return None;
    }

    Some(ToolSpec {
        name: to_static_name(name),
        description: tool_description(name),
        mutating: !is_read_only_tool(name),
        executor: tool_executor(name),
        params_schema: tool_params_schema(name),
        response_schema: default_response_schema(),
    })
}

pub fn list_tool_specs() -> Vec<ToolSpec> {
    TOOL_NAMES
        .iter()
        .filter_map(|name| get_tool_spec(name))
        .collect()
}

fn to_static_name(name: &str) -> &'static str {
    TOOL_NAMES
        .iter()
        .copied()
        .find(|candidate| *candidate == name)
        .expect("known tool name must exist in catalog")
}

fn tool_description(name: &str) -> &'static str {
    match name {
        "ping" => "Check Unity Editor connectivity",
        "create_scene" => "Create a new scene",
        "list_packages" => "List installed packages",
        "create_animator_controller" => {
            "Create an AnimatorController asset with parameters, states, and transitions"
        }
        "create_animation_clip" => {
            "Create an AnimationClip asset from sprite frames with frame rate and loop settings"
        }
        "create_sprite_atlas" => "Create a SpriteAtlas asset with packables and packing settings",
        "read" => "Read a C# source file",
        "search" => "Search code by pattern",
        "get_symbols" => "Get symbols in a C# source file",
        "build_index" => "Build the local code search index",
        "update_index" => "Update the local code search index",
        "find_symbol" => "Find symbol definitions",
        "find_refs" => "Find symbol references",
        "run_tests" => "Run EditMode/PlayMode tests",
        _ => "Unity CLI tool operation",
    }
}

fn tool_executor(name: &str) -> ToolExecutor {
    match name {
        "read"
        | "search"
        | "list_packages"
        | "get_symbols"
        | "build_index"
        | "update_index"
        | "find_symbol"
        | "find_refs"
        | "rename_symbol"
        | "replace_symbol_body"
        | "insert_before_symbol"
        | "insert_after_symbol"
        | "remove_symbol"
        | "validate_text_edits"
        | "write_csharp_file"
        | "create_csharp_file"
        | "apply_csharp_edits"
        | "create_class"
        | "reference_fetch"
        | "reference_status"
        | "reference_search"
        | "reference_grep"
        | "reference_view"
        | "reference_clean"
        | "reference_find_symbol"
        | "reference_diff"
        | "reference_resolve_symbol_at"
        | "reference_embed_build"
        | "reference_embed_search" => ToolExecutor::Local,
        _ => ToolExecutor::Remote,
    }
}

fn is_read_only_tool(name: &str) -> bool {
    matches!(
        name,
        "addressables_analyze"
            | "get_animator_runtime_info"
            | "get_animator_state"
            | "find_by_component"
            | "get_component_values"
            | "get_gameobject_details"
            | "get_object_references"
            | "analyze_scene_contents"
            | "analyze_asset_dependencies"
            | "get_compilation_state"
            | "get_component_types"
            | "list_components"
            | "read_console"
            | "find_gameobject"
            | "get_hierarchy"
            | "analyze_input_actions_asset"
            | "get_input_actions_state"
            | "get_current_input_state"
            | "get_editor_info"
            | "get_editor_state"
            | "profiler_get_metrics"
            | "profiler_status"
            | "get_scene_info"
            | "list_scenes"
            | "analyze_screenshot"
            | "list_packages"
            | "read"
            | "find_refs"
            | "search"
            | "find_symbol"
            | "get_symbols"
            | "get_project_setting"
            | "get_package_setting"
            | "get_project_settings"
            | "get_command_stats"
            | "ping"
            | "get_test_status"
            | "find_ui_elements"
            | "get_ui_element_state"
            | "capture_video_status"
            | "reference_status"
            | "reference_search"
            | "reference_grep"
            | "reference_view"
            | "reference_find_symbol"
            | "reference_diff"
            | "reference_resolve_symbol_at"
            | "reference_embed_search"
    )
}

fn tool_params_schema(name: &str) -> Value {
    match name {
        "ping" => object_schema(&[("message", string_schema())], &[], false),
        "create_scene" => object_schema(
            &[
                ("sceneName", string_schema()),
                ("path", string_schema()),
                ("loadScene", boolean_schema()),
                ("addToBuildSettings", boolean_schema()),
            ],
            &["sceneName"],
            false,
        ),
        "load_scene" => with_one_of(
            object_schema(
                &[
                    ("scenePath", string_schema()),
                    ("sceneName", string_schema()),
                    ("loadMode", enum_string_schema(&["Single", "Additive"])),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("scenePath", string_schema())], &["scenePath"], true),
                object_schema(&[("sceneName", string_schema())], &["sceneName"], true),
            ],
        ),
        "save_scene" => object_schema(
            &[("scenePath", string_schema()), ("saveAs", boolean_schema())],
            &[],
            false,
        ),
        "list_scenes" => object_schema(
            &[
                ("includeLoadedOnly", boolean_schema()),
                ("includeBuildScenesOnly", boolean_schema()),
                ("includePath", string_schema()),
            ],
            &[],
            false,
        ),
        "get_scene_info" => object_schema(
            &[
                ("scenePath", string_schema()),
                ("sceneName", string_schema()),
                ("includeGameObjects", boolean_schema()),
            ],
            &[],
            false,
        ),
        "create_gameobject" => object_schema(
            &[
                ("name", string_schema()),
                (
                    "primitiveType",
                    enum_string_schema(&["cube", "sphere", "cylinder", "capsule", "plane", "quad"]),
                ),
                ("position", vector3_schema()),
                ("rotation", vector3_schema()),
                ("scale", vector3_schema()),
                ("parentPath", string_schema()),
                ("tag", string_schema()),
                ("layer", integer_schema()),
            ],
            &[],
            false,
        ),
        "find_gameobject" => object_schema(
            &[
                ("name", string_schema()),
                ("tag", string_schema()),
                ("layer", integer_schema()),
                ("exactMatch", boolean_schema()),
            ],
            &[],
            false,
        ),
        "modify_gameobject" => object_schema(
            &[
                ("path", string_schema()),
                ("name", string_schema()),
                ("position", vector3_schema()),
                ("rotation", vector3_schema()),
                ("scale", vector3_schema()),
                ("active", boolean_schema()),
                ("tag", string_schema()),
                ("layer", integer_schema()),
                ("parentPath", string_schema()),
                ("runtime", boolean_schema()),
            ],
            &["path"],
            false,
        ),
        "delete_gameobject" => with_any_of(
            object_schema(
                &[
                    ("path", string_schema()),
                    ("paths", array_of(string_schema())),
                    ("includeChildren", boolean_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("path", string_schema())], &["path"], true),
                object_schema(&[("paths", array_of(string_schema()))], &["paths"], true),
            ],
        ),
        "get_hierarchy" => object_schema(
            &[
                ("rootPath", string_schema()),
                ("includeInactive", boolean_schema()),
                ("maxDepth", integer_schema()),
                ("includeComponents", boolean_schema()),
                ("includeTransform", boolean_schema()),
                ("includeTags", boolean_schema()),
                ("includeLayers", boolean_schema()),
                ("nameOnly", boolean_schema()),
                ("maxObjects", integer_schema()),
            ],
            &[],
            false,
        ),
        "read" => object_schema(
            &[
                ("path", string_schema()),
                ("startLine", integer_schema()),
                ("maxLines", integer_schema()),
            ],
            &["path"],
            false,
        ),
        "search" => object_schema(
            &[
                ("pattern", string_schema()),
                ("path", string_schema()),
                ("limit", integer_schema()),
            ],
            &["pattern"],
            false,
        ),
        "list_packages" => object_schema(&[], &[], false),
        "get_symbols" => object_schema(&[("path", string_schema())], &["path"], false),
        "build_index" => object_schema(
            &[
                ("excludePackageCache", boolean_schema()),
                ("outputPath", string_schema()),
                (
                    "scope",
                    enum_string_schema(&["all", "assets", "packages", "embedded", "library"]),
                ),
            ],
            &[],
            false,
        ),
        "update_index" => object_schema(&[("paths", array_of(string_schema()))], &["paths"], false),
        "find_symbol" => object_schema(
            &[
                ("name", string_schema()),
                ("kind", string_schema()),
                ("scope", string_schema()),
                ("exact", boolean_schema()),
            ],
            &["name"],
            false,
        ),
        "find_refs" => object_schema(
            &[
                ("name", string_schema()),
                ("scope", string_schema()),
                ("startAfter", string_schema()),
                ("path", string_schema()),
                ("pageSize", integer_schema()),
                ("maxBytes", integer_schema()),
                ("maxMatchesPerFile", integer_schema()),
                ("snippetContext", integer_schema()),
            ],
            &["name"],
            false,
        ),
        "rename_symbol" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("namePath", string_schema()),
                ("newName", string_schema()),
                ("apply", boolean_schema()),
            ],
            &["namePath", "newName"],
            false,
        ),
        "replace_symbol_body" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("namePath", string_schema()),
                ("body", string_schema()),
                ("apply", boolean_schema()),
            ],
            &["namePath", "body"],
            false,
        ),
        "insert_before_symbol" | "insert_after_symbol" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("namePath", string_schema()),
                ("text", string_schema()),
                ("apply", boolean_schema()),
            ],
            &["namePath", "text"],
            false,
        ),
        "remove_symbol" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("namePath", string_schema()),
                ("apply", boolean_schema()),
                ("failOnReferences", boolean_schema()),
                ("removeEmptyFile", boolean_schema()),
            ],
            &["namePath"],
            false,
        ),
        "validate_text_edits" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("newText", string_schema()),
            ],
            &["newText"],
            false,
        ),
        "write_csharp_file" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("newText", string_schema()),
                ("validate", boolean_schema()),
                ("apply", boolean_schema()),
                ("format", boolean_schema()),
                ("refresh", boolean_schema()),
                ("waitForCompile", boolean_schema()),
                ("updateIndex", boolean_schema()),
            ],
            &["newText"],
            false,
        ),
        "create_csharp_file" => object_schema(
            &[
                ("relative", string_schema()),
                ("path", string_schema()),
                ("text", string_schema()),
                ("overwrite", boolean_schema()),
                ("validate", boolean_schema()),
                ("apply", boolean_schema()),
                ("format", boolean_schema()),
                ("refresh", boolean_schema()),
                ("waitForCompile", boolean_schema()),
                ("updateIndex", boolean_schema()),
            ],
            &["text"],
            false,
        ),
        "apply_csharp_edits" => object_schema(
            &[
                (
                    "files",
                    array_of(object_schema(
                        &[
                            ("relative", string_schema()),
                            ("path", string_schema()),
                            ("newText", string_schema()),
                        ],
                        &["newText"],
                        false,
                    )),
                ),
                ("validate", boolean_schema()),
                ("apply", boolean_schema()),
                ("format", boolean_schema()),
                ("refresh", boolean_schema()),
                ("waitForCompile", boolean_schema()),
                ("updateIndex", boolean_schema()),
            ],
            &["files"],
            false,
        ),
        "create_class" => object_schema(
            &[
                ("name", string_schema()),
                ("namespace", string_schema()),
                ("inherits", string_schema()),
                ("folder", string_schema()),
                ("path", string_schema()),
            ],
            &["name"],
            false,
        ),
        "add_component" => object_schema(
            &[
                ("gameObjectPath", string_schema()),
                ("componentType", string_schema()),
                ("properties", any_object_schema()),
            ],
            &["gameObjectPath", "componentType"],
            false,
        ),
        "remove_component" => object_schema(
            &[
                ("gameObjectPath", string_schema()),
                ("componentType", string_schema()),
                ("componentIndex", integer_schema()),
            ],
            &["gameObjectPath", "componentType"],
            false,
        ),
        "modify_component" => object_schema(
            &[
                ("gameObjectPath", string_schema()),
                ("componentType", string_schema()),
                ("componentIndex", integer_schema()),
                ("properties", any_object_schema()),
            ],
            &["gameObjectPath", "componentType"],
            false,
        ),
        "set_component_field" => object_schema(
            &[
                ("componentType", string_schema()),
                ("fieldPath", string_schema()),
                (
                    "scope",
                    enum_string_schema(&["scene", "prefab_asset", "prefab_mode", "auto"]),
                ),
                ("gameObjectPath", string_schema()),
                ("prefabAssetPath", string_schema()),
                ("prefabObjectPath", string_schema()),
                ("serializedPropertyPath", string_schema()),
                ("valueType", string_schema()),
                ("enumValue", string_schema()),
                ("componentIndex", integer_schema()),
                ("dryRun", boolean_schema()),
                ("applyPrefabChanges", boolean_schema()),
                ("createUndo", boolean_schema()),
                ("markSceneDirty", boolean_schema()),
                ("runtime", boolean_schema()),
                ("objectReference", any_object_schema()),
                ("value", any_schema()),
            ],
            &["componentType", "fieldPath"],
            false,
        ),
        "list_components" => object_schema(
            &[
                ("gameObjectPath", string_schema()),
                ("includeProperties", boolean_schema()),
            ],
            &["gameObjectPath"],
            false,
        ),
        "get_component_types" => object_schema(
            &[
                ("category", string_schema()),
                ("search", string_schema()),
                ("onlyAddable", boolean_schema()),
            ],
            &[],
            false,
        ),
        "get_compilation_state" => object_schema(
            &[
                ("includeMessages", boolean_schema()),
                ("maxMessages", integer_schema()),
            ],
            &[],
            false,
        ),
        "input_keyboard" => with_any_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["press", "release", "type", "combo"]),
                    ),
                    ("actions", array_of(any_object_schema())),
                    ("key", string_schema()),
                    ("text", string_schema()),
                    ("keys", array_of(string_schema())),
                    ("holdSeconds", number_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("action", string_schema())], &["action"], true),
                object_schema(
                    &[("actions", array_of(any_object_schema()))],
                    &["actions"],
                    true,
                ),
            ],
        ),
        "input_mouse" => with_any_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["move", "click", "drag", "scroll", "button"]),
                    ),
                    ("actions", array_of(any_object_schema())),
                    ("x", number_schema()),
                    ("y", number_schema()),
                    ("absolute", boolean_schema()),
                    ("button", string_schema()),
                    ("clickCount", integer_schema()),
                    ("buttonAction", string_schema()),
                    ("startX", number_schema()),
                    ("startY", number_schema()),
                    ("endX", number_schema()),
                    ("endY", number_schema()),
                    ("deltaX", number_schema()),
                    ("deltaY", number_schema()),
                    ("holdSeconds", number_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("action", string_schema())], &["action"], true),
                object_schema(
                    &[("actions", array_of(any_object_schema()))],
                    &["actions"],
                    true,
                ),
            ],
        ),
        "input_gamepad" => with_any_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["button", "stick", "trigger", "dpad"]),
                    ),
                    ("actions", array_of(any_object_schema())),
                    ("button", string_schema()),
                    ("buttonAction", string_schema()),
                    ("stick", string_schema()),
                    ("x", number_schema()),
                    ("y", number_schema()),
                    ("trigger", string_schema()),
                    ("value", number_schema()),
                    ("direction", string_schema()),
                    ("holdSeconds", number_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("action", string_schema())], &["action"], true),
                object_schema(
                    &[("actions", array_of(any_object_schema()))],
                    &["actions"],
                    true,
                ),
            ],
        ),
        "input_touch" => with_any_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["tap", "swipe", "pinch", "multi"]),
                    ),
                    ("actions", array_of(any_object_schema())),
                    ("x", number_schema()),
                    ("y", number_schema()),
                    ("touchId", integer_schema()),
                    ("startX", number_schema()),
                    ("startY", number_schema()),
                    ("endX", number_schema()),
                    ("endY", number_schema()),
                    ("duration", integer_schema()),
                    ("centerX", number_schema()),
                    ("centerY", number_schema()),
                    ("startDistance", number_schema()),
                    ("endDistance", number_schema()),
                    ("touches", array_of(any_object_schema())),
                    ("holdSeconds", number_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("action", string_schema())], &["action"], true),
                object_schema(
                    &[("actions", array_of(any_object_schema()))],
                    &["actions"],
                    true,
                ),
            ],
        ),
        "create_input_sequence" => object_schema(
            &[
                (
                    "sequence",
                    array_of(object_schema(
                        &[("type", string_schema()), ("params", any_object_schema())],
                        &["type", "params"],
                        false,
                    )),
                ),
                ("delayBetween", integer_schema()),
            ],
            &["sequence"],
            false,
        ),
        "get_current_input_state" => object_schema(&[], &[], false),
        "find_ui_elements" => object_schema(
            &[
                ("elementType", string_schema()),
                ("tagFilter", string_schema()),
                ("namePattern", string_schema()),
                ("includeInactive", boolean_schema()),
                ("canvasFilter", string_schema()),
                ("uiDocumentFilter", string_schema()),
                ("uiSystem", string_schema()),
            ],
            &[],
            false,
        ),
        "click_ui_element" => object_schema(
            &[
                ("elementPath", string_schema()),
                ("clickType", string_schema()),
                ("holdDuration", integer_schema()),
                ("position", vector2_schema()),
            ],
            &["elementPath"],
            false,
        ),
        "get_ui_element_state" => object_schema(
            &[
                ("elementPath", string_schema()),
                ("includeChildren", boolean_schema()),
                ("includeInteractableInfo", boolean_schema()),
            ],
            &["elementPath"],
            false,
        ),
        "set_ui_element_value" => object_schema(
            &[
                ("elementPath", string_schema()),
                ("value", any_schema()),
                ("triggerEvents", boolean_schema()),
            ],
            &["elementPath", "value"],
            false,
        ),
        "simulate_ui_input" => object_schema(
            &[
                (
                    "inputSequence",
                    array_of(object_schema(
                        &[("type", string_schema()), ("params", any_object_schema())],
                        &["type", "params"],
                        false,
                    )),
                ),
                ("waitBetween", integer_schema()),
                ("validateState", boolean_schema()),
            ],
            &["inputSequence"],
            false,
        ),
        "capture_screenshot" => object_schema(
            &[
                (
                    "captureMode",
                    enum_string_schema(&["game", "scene", "window", "explorer"]),
                ),
                ("width", integer_schema()),
                ("height", integer_schema()),
                ("includeUI", boolean_schema()),
                ("windowName", string_schema()),
                ("encodeAsBase64", boolean_schema()),
                ("explorerSettings", any_object_schema()),
            ],
            &[],
            false,
        ),
        "analyze_screenshot" => with_any_of(
            object_schema(
                &[
                    ("imagePath", string_schema()),
                    ("base64Data", string_schema()),
                    ("analysisType", string_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("imagePath", string_schema())], &["imagePath"], true),
                object_schema(&[("base64Data", string_schema())], &["base64Data"], true),
            ],
        ),
        "capture_video_start" => object_schema(
            &[
                ("captureMode", enum_string_schema(&["game"])),
                ("width", integer_schema()),
                ("height", integer_schema()),
                ("fps", integer_schema()),
                ("includeUI", boolean_schema()),
                ("maxDurationSec", number_schema()),
                (
                    "format",
                    enum_string_schema(&["mp4", "webm", "png_sequence"]),
                ),
            ],
            &[],
            false,
        ),
        "capture_video_stop" | "capture_video_status" => object_schema(&[], &[], false),
        "profiler_start" => object_schema(
            &[
                ("mode", enum_string_schema(&["normal", "deep"])),
                ("recordToFile", boolean_schema()),
                ("metrics", array_of(string_schema())),
                ("maxDurationSec", number_schema()),
            ],
            &[],
            false,
        ),
        "profiler_stop" => object_schema(&[("sessionId", string_schema())], &[], false),
        "profiler_status" => object_schema(&[], &[], false),
        "profiler_get_metrics" => object_schema(
            &[
                ("listAvailable", boolean_schema()),
                ("metrics", array_of(string_schema())),
            ],
            &[],
            false,
        ),
        "play_game" => object_schema(&[("delayMs", integer_schema())], &[], false),
        "pause_game" | "stop_game" => object_schema(&[], &[], false),
        "clear_console" => object_schema(
            &[
                ("clearOnPlay", boolean_schema()),
                ("clearOnRecompile", boolean_schema()),
                ("clearOnBuild", boolean_schema()),
                ("preserveWarnings", boolean_schema()),
                ("preserveErrors", boolean_schema()),
            ],
            &[],
            false,
        ),
        "read_console" => object_schema(
            &[
                ("count", integer_schema()),
                ("logTypes", array_of(string_schema())),
                ("filterText", string_schema()),
                ("includeStackTrace", boolean_schema()),
                ("format", string_schema()),
                ("sinceTimestamp", string_schema()),
                ("untilTimestamp", string_schema()),
                ("sortOrder", string_schema()),
                ("groupBy", string_schema()),
            ],
            &[],
            false,
        ),
        "clear_logs" | "refresh_assets" | "quit_editor" | "get_editor_info"
        | "get_editor_state" | "get_command_stats" => object_schema(&[], &[], false),
        "get_project_setting" => object_schema(&[("path", string_schema())], &["path"], false),
        "set_project_setting" => object_schema(
            &[
                ("path", string_schema()),
                ("value", any_schema()),
                ("confirmChanges", boolean_schema()),
            ],
            &["path", "value", "confirmChanges"],
            false,
        ),
        "get_project_settings" => object_schema(
            &[
                ("includePlayer", boolean_schema()),
                ("includeGraphics", boolean_schema()),
                ("includeQuality", boolean_schema()),
                ("includePhysics", boolean_schema()),
                ("includePhysics2D", boolean_schema()),
                ("includeAudio", boolean_schema()),
                ("includeTime", boolean_schema()),
                ("includeInputManager", boolean_schema()),
                ("includeEditor", boolean_schema()),
                ("includeBuild", boolean_schema()),
                ("includeTags", boolean_schema()),
            ],
            &[],
            false,
        ),
        "get_package_setting" => object_schema(
            &[
                ("package", string_schema()),
                ("key", string_schema()),
                ("scope", enum_string_schema(&["project", "user"])),
            ],
            &["package", "key"],
            false,
        ),
        "set_package_setting" => object_schema(
            &[
                ("package", string_schema()),
                ("key", string_schema()),
                ("value", any_schema()),
                ("scope", enum_string_schema(&["project", "user"])),
                ("confirmChanges", boolean_schema()),
            ],
            &["package", "key", "value", "confirmChanges"],
            false,
        ),
        "update_project_settings" => object_schema(
            &[
                ("confirmChanges", boolean_schema()),
                ("player", any_object_schema()),
                ("graphics", any_object_schema()),
                ("physics", any_object_schema()),
                ("audio", any_object_schema()),
                ("time", any_object_schema()),
            ],
            &["confirmChanges"],
            false,
        ),
        "get_gameobject_details" => with_any_of(
            object_schema(
                &[
                    ("gameObjectName", string_schema()),
                    ("path", string_schema()),
                    ("includeChildren", boolean_schema()),
                    ("includeComponents", boolean_schema()),
                    ("includeMaterials", boolean_schema()),
                    ("maxDepth", integer_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(
                    &[("gameObjectName", string_schema())],
                    &["gameObjectName"],
                    true,
                ),
                object_schema(&[("path", string_schema())], &["path"], true),
            ],
        ),
        "analyze_scene_contents" => object_schema(
            &[
                ("includeInactive", boolean_schema()),
                ("groupByType", boolean_schema()),
                ("includePrefabInfo", boolean_schema()),
                ("includeMemoryInfo", boolean_schema()),
            ],
            &[],
            false,
        ),
        "get_component_values" => object_schema(
            &[
                ("gameObjectName", string_schema()),
                ("componentType", string_schema()),
                ("componentIndex", integer_schema()),
                ("includePrivateFields", boolean_schema()),
                ("includeInherited", boolean_schema()),
            ],
            &["gameObjectName", "componentType"],
            false,
        ),
        "find_by_component" => object_schema(
            &[
                ("componentType", string_schema()),
                ("includeInactive", boolean_schema()),
                (
                    "searchScope",
                    enum_string_schema(&["scene", "prefabs", "all"]),
                ),
                ("matchExactType", boolean_schema()),
            ],
            &["componentType"],
            false,
        ),
        "get_object_references" => object_schema(
            &[
                ("gameObjectName", string_schema()),
                ("includeAssetReferences", boolean_schema()),
                ("includeHierarchyReferences", boolean_schema()),
                ("searchInPrefabs", boolean_schema()),
            ],
            &["gameObjectName"],
            false,
        ),
        "get_animator_state" => object_schema(
            &[
                ("gameObjectName", string_schema()),
                ("includeParameters", boolean_schema()),
                ("includeStates", boolean_schema()),
                ("includeTransitions", boolean_schema()),
                ("includeClips", boolean_schema()),
                ("layerIndex", integer_schema()),
            ],
            &["gameObjectName"],
            false,
        ),
        "get_animator_runtime_info" => object_schema(
            &[
                ("gameObjectName", string_schema()),
                ("includeIK", boolean_schema()),
                ("includeRootMotion", boolean_schema()),
                ("includeBehaviours", boolean_schema()),
            ],
            &["gameObjectName"],
            false,
        ),
        "create_animator_controller" => object_schema(
            &[
                ("controllerPath", string_schema()),
                ("overwrite", boolean_schema()),
                (
                    "parameters",
                    array_of(object_schema(
                        &[
                            ("name", string_schema()),
                            (
                                "type",
                                enum_string_schema(&["Bool", "Float", "Int", "Trigger"]),
                            ),
                            ("defaultBool", boolean_schema()),
                            ("defaultFloat", number_schema()),
                            ("defaultInt", integer_schema()),
                        ],
                        &["name", "type"],
                        false,
                    )),
                ),
                (
                    "states",
                    array_of(object_schema(
                        &[("name", string_schema()), ("motionPath", string_schema())],
                        &["name"],
                        false,
                    )),
                ),
                ("defaultState", string_schema()),
                (
                    "transitions",
                    array_of(object_schema(
                        &[
                            ("from", string_schema()),
                            ("to", string_schema()),
                            ("hasExitTime", boolean_schema()),
                            ("exitTime", number_schema()),
                            ("duration", number_schema()),
                            (
                                "conditions",
                                array_of(object_schema(
                                    &[
                                        ("parameter", string_schema()),
                                        (
                                            "mode",
                                            enum_string_schema(&[
                                                "If", "IfNot", "Greater", "Less", "Equals",
                                                "NotEqual",
                                            ]),
                                        ),
                                        ("threshold", number_schema()),
                                    ],
                                    &["parameter", "mode"],
                                    false,
                                )),
                            ),
                        ],
                        &["from", "to"],
                        false,
                    )),
                ),
            ],
            &["controllerPath"],
            false,
        ),
        "create_animation_clip" => object_schema(
            &[
                ("clipPath", string_schema()),
                ("spritePaths", array_of(string_schema())),
                ("frameRate", number_schema()),
                ("loopTime", boolean_schema()),
                ("bindingPath", string_schema()),
                ("overwrite", boolean_schema()),
            ],
            &["clipPath", "spritePaths"],
            false,
        ),
        "create_sprite_atlas" => object_schema(
            &[
                ("atlasPath", string_schema()),
                ("overwrite", boolean_schema()),
                ("packables", array_of(string_schema())),
                (
                    "packingSettings",
                    object_schema(
                        &[
                            ("padding", integer_schema()),
                            ("allowRotation", boolean_schema()),
                            ("tightPacking", boolean_schema()),
                        ],
                        &[],
                        false,
                    ),
                ),
                (
                    "textureSettings",
                    object_schema(
                        &[
                            (
                                "filterMode",
                                enum_string_schema(&["Point", "Bilinear", "Trilinear"]),
                            ),
                            ("generateMipMaps", boolean_schema()),
                        ],
                        &[],
                        false,
                    ),
                ),
            ],
            &["atlasPath"],
            false,
        ),
        "get_input_actions_state" => with_any_of(
            object_schema(
                &[
                    ("assetName", string_schema()),
                    ("assetPath", string_schema()),
                    ("includeBindings", boolean_schema()),
                    ("includeControlSchemes", boolean_schema()),
                    ("includeJsonStructure", boolean_schema()),
                ],
                &[],
                false,
            ),
            vec![
                object_schema(&[("assetName", string_schema())], &["assetName"], true),
                object_schema(&[("assetPath", string_schema())], &["assetPath"], true),
            ],
        ),
        "analyze_input_actions_asset" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("includeJsonStructure", boolean_schema()),
                ("includeStatistics", boolean_schema()),
            ],
            &["assetPath"],
            false,
        ),
        "create_action_map" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actions", array_of(any_object_schema())),
            ],
            &["assetPath", "mapName"],
            false,
        ),
        "remove_action_map" => object_schema(
            &[("assetPath", string_schema()), ("mapName", string_schema())],
            &["assetPath", "mapName"],
            false,
        ),
        "add_input_action" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actionName", string_schema()),
                ("actionType", string_schema()),
            ],
            &["assetPath", "mapName", "actionName"],
            false,
        ),
        "remove_input_action" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actionName", string_schema()),
            ],
            &["assetPath", "mapName", "actionName"],
            false,
        ),
        "add_input_binding" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actionName", string_schema()),
                ("path", string_schema()),
                ("groups", string_schema()),
                ("interactions", string_schema()),
                ("processors", string_schema()),
            ],
            &["assetPath", "mapName", "actionName", "path"],
            false,
        ),
        "remove_input_binding" => with_any_of(
            object_schema(
                &[
                    ("assetPath", string_schema()),
                    ("mapName", string_schema()),
                    ("actionName", string_schema()),
                    ("bindingIndex", integer_schema()),
                    ("bindingPath", string_schema()),
                ],
                &["assetPath", "mapName", "actionName"],
                false,
            ),
            vec![
                object_schema(
                    &[("bindingIndex", integer_schema())],
                    &["bindingIndex"],
                    true,
                ),
                object_schema(&[("bindingPath", string_schema())], &["bindingPath"], true),
            ],
        ),
        "remove_all_bindings" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actionName", string_schema()),
            ],
            &["assetPath", "mapName", "actionName"],
            false,
        ),
        "create_composite_binding" => object_schema(
            &[
                ("assetPath", string_schema()),
                ("mapName", string_schema()),
                ("actionName", string_schema()),
                ("compositeType", string_schema()),
                ("name", string_schema()),
                ("bindings", any_object_schema()),
                ("groups", string_schema()),
            ],
            &["assetPath", "mapName", "actionName", "bindings"],
            false,
        ),
        "manage_control_schemes" => object_schema(
            &[
                ("assetPath", string_schema()),
                (
                    "operation",
                    enum_string_schema(&["add", "remove", "modify"]),
                ),
                ("schemeName", string_schema()),
                ("devices", array_of(string_schema())),
            ],
            &["assetPath", "operation"],
            false,
        ),
        "create_prefab" => with_any_of(
            object_schema(
                &[
                    ("gameObjectPath", string_schema()),
                    ("prefabPath", string_schema()),
                    ("createFromTemplate", boolean_schema()),
                    ("overwrite", boolean_schema()),
                ],
                &["prefabPath"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("prefabPath", string_schema()),
                        ("gameObjectPath", string_schema()),
                    ],
                    &["prefabPath", "gameObjectPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("prefabPath", string_schema()),
                        ("createFromTemplate", boolean_schema()),
                    ],
                    &["prefabPath", "createFromTemplate"],
                    true,
                ),
            ],
        ),
        "modify_prefab" => object_schema(
            &[
                ("prefabPath", string_schema()),
                ("modifications", any_object_schema()),
                ("applyToInstances", boolean_schema()),
            ],
            &["prefabPath", "modifications"],
            false,
        ),
        "instantiate_prefab" => object_schema(
            &[
                ("prefabPath", string_schema()),
                ("position", vector3_schema()),
                ("rotation", vector3_schema()),
                ("parent", string_schema()),
                ("name", string_schema()),
            ],
            &["prefabPath"],
            false,
        ),
        "create_material" => object_schema(
            &[
                ("materialPath", string_schema()),
                ("shader", string_schema()),
                ("properties", any_object_schema()),
                ("copyFrom", string_schema()),
                ("overwrite", boolean_schema()),
            ],
            &["materialPath"],
            false,
        ),
        "modify_material" => object_schema(
            &[
                ("materialPath", string_schema()),
                ("properties", any_object_schema()),
                ("shader", string_schema()),
            ],
            &["materialPath"],
            false,
        ),
        "open_prefab" => object_schema(
            &[
                ("prefabPath", string_schema()),
                ("focusObject", string_schema()),
                ("isolateObject", boolean_schema()),
            ],
            &["prefabPath"],
            false,
        ),
        "exit_prefab_mode" => object_schema(&[("saveChanges", boolean_schema())], &[], false),
        "save_prefab" => object_schema(
            &[
                ("gameObjectPath", string_schema()),
                ("includeChildren", boolean_schema()),
            ],
            &[],
            false,
        ),
        "analyze_asset_dependencies" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "get_dependencies",
                            "get_dependents",
                            "analyze_circular",
                            "find_unused",
                            "analyze_size_impact",
                            "validate_references",
                        ]),
                    ),
                    ("assetPath", string_schema()),
                    ("recursive", boolean_schema()),
                    ("includeBuiltIn", boolean_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_dependencies"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_dependents"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["analyze_circular"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["find_unused"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["analyze_size_impact"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["validate_references"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "manage_asset_database" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "find_assets",
                            "get_asset_info",
                            "create_folder",
                            "delete_asset",
                            "move_asset",
                            "copy_asset",
                            "refresh",
                            "save",
                        ]),
                    ),
                    ("filter", string_schema()),
                    ("searchInFolders", array_of(string_schema())),
                    ("assetPath", string_schema()),
                    ("folderPath", string_schema()),
                    ("fromPath", string_schema()),
                    ("toPath", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("action", enum_string_schema(&["find_assets"])),
                        ("filter", string_schema()),
                    ],
                    &["action", "filter"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_asset_info"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["create_folder"])),
                        ("folderPath", string_schema()),
                    ],
                    &["action", "folderPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["delete_asset"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["move_asset"])),
                        ("fromPath", string_schema()),
                        ("toPath", string_schema()),
                    ],
                    &["action", "fromPath", "toPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["copy_asset"])),
                        ("fromPath", string_schema()),
                        ("toPath", string_schema()),
                    ],
                    &["action", "fromPath", "toPath"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["refresh"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["save"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "manage_asset_import_settings" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["get", "modify", "apply_preset", "reimport"]),
                    ),
                    ("assetPath", string_schema()),
                    ("settings", any_object_schema()),
                    ("preset", string_schema()),
                ],
                &["action", "assetPath"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["modify"])),
                        ("assetPath", string_schema()),
                        ("settings", any_object_schema()),
                    ],
                    &["action", "assetPath", "settings"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["apply_preset"])),
                        ("assetPath", string_schema()),
                        ("preset", string_schema()),
                    ],
                    &["action", "assetPath", "preset"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["reimport"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
            ],
        ),
        "manage_layers" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "get",
                            "add",
                            "remove",
                            "get_by_name",
                            "get_by_index",
                        ]),
                    ),
                    ("layerName", string_schema()),
                    ("layerIndex", integer_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["get"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add"])),
                        ("layerName", string_schema()),
                    ],
                    &["action", "layerName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove"])),
                        ("layerName", string_schema()),
                    ],
                    &["action", "layerName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_by_name"])),
                        ("layerName", string_schema()),
                    ],
                    &["action", "layerName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_by_index"])),
                        ("layerIndex", integer_schema()),
                    ],
                    &["action", "layerIndex"],
                    true,
                ),
            ],
        ),
        "manage_selection" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["get", "set", "clear", "get_details"]),
                    ),
                    ("includeDetails", boolean_schema()),
                    ("objectPaths", array_of(string_schema())),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["get"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["set"])),
                        ("objectPaths", array_of(string_schema())),
                    ],
                    &["action", "objectPaths"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["clear"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["get_details"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "manage_tags" => with_one_of(
            object_schema(
                &[
                    ("action", enum_string_schema(&["get", "add", "remove"])),
                    ("tagName", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["get"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add"])),
                        ("tagName", string_schema()),
                    ],
                    &["action", "tagName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove"])),
                        ("tagName", string_schema()),
                    ],
                    &["action", "tagName"],
                    true,
                ),
            ],
        ),
        "manage_tools" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["get", "activate", "deactivate", "refresh"]),
                    ),
                    ("category", string_schema()),
                    ("toolName", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["get"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["activate"])),
                        ("toolName", string_schema()),
                    ],
                    &["action", "toolName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["deactivate"])),
                        ("toolName", string_schema()),
                    ],
                    &["action", "toolName"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["refresh"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "manage_windows" => with_one_of(
            object_schema(
                &[
                    ("action", enum_string_schema(&["get", "focus", "get_state"])),
                    ("includeHidden", boolean_schema()),
                    ("windowType", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["get"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["focus"])),
                        ("windowType", string_schema()),
                    ],
                    &["action", "windowType"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["get_state"])),
                        ("windowType", string_schema()),
                    ],
                    &["action", "windowType"],
                    true,
                ),
            ],
        ),
        "execute_menu_item" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&["execute", "get_available_menus"]),
                    ),
                    ("menuPath", string_schema()),
                    ("alias", string_schema()),
                    ("safetyCheck", boolean_schema()),
                    ("parameters", any_object_schema()),
                ],
                &[],
                false,
            ),
            vec![
                // Backward-compatible default behavior: action omitted => execute
                object_schema(
                    &[
                        ("menuPath", string_schema()),
                        ("alias", string_schema()),
                        ("safetyCheck", boolean_schema()),
                        ("parameters", any_object_schema()),
                    ],
                    &["menuPath"],
                    false,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["execute"])),
                        ("menuPath", string_schema()),
                    ],
                    &["action", "menuPath"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["get_available_menus"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "package_manager" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "search",
                            "list",
                            "add",
                            "install",
                            "remove",
                            "uninstall",
                            "info",
                            "recommend",
                        ]),
                    ),
                    ("keyword", string_schema()),
                    ("limit", integer_schema()),
                    ("includeBuiltIn", boolean_schema()),
                    ("packageId", string_schema()),
                    ("version", string_schema()),
                    ("packageName", string_schema()),
                    ("category", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("action", enum_string_schema(&["search"])),
                        ("keyword", string_schema()),
                    ],
                    &["action", "keyword"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["list"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add"])),
                        ("packageId", string_schema()),
                    ],
                    &["action", "packageId"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["install"])),
                        ("packageId", string_schema()),
                    ],
                    &["action", "packageId"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove"])),
                        ("packageName", string_schema()),
                    ],
                    &["action", "packageName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["uninstall"])),
                        ("packageName", string_schema()),
                    ],
                    &["action", "packageName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["info"])),
                        ("packageName", string_schema()),
                    ],
                    &["action", "packageName"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["recommend"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "registry_config" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "list",
                            "add_openupm",
                            "add_nuget",
                            "remove",
                            "add_scope",
                            "recommend",
                        ]),
                    ),
                    ("scopes", array_of(string_schema())),
                    ("autoAddPopular", boolean_schema()),
                    ("registryName", string_schema()),
                    ("scope", string_schema()),
                    ("registry", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["list"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["add_openupm"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["add_nuget"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove"])),
                        ("registryName", string_schema()),
                    ],
                    &["action", "registryName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add_scope"])),
                        ("registryName", string_schema()),
                        ("scope", string_schema()),
                    ],
                    &["action", "registryName", "scope"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["recommend"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "addressables_manage" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "add_entry",
                            "remove_entry",
                            "set_address",
                            "add_label",
                            "remove_label",
                            "list_entries",
                            "list_groups",
                            "create_group",
                            "remove_group",
                            "move_entry",
                        ]),
                    ),
                    ("assetPath", string_schema()),
                    ("address", string_schema()),
                    ("groupName", string_schema()),
                    ("labels", array_of(string_schema())),
                    ("newAddress", string_schema()),
                    ("label", string_schema()),
                    ("pageSize", integer_schema()),
                    ("offset", integer_schema()),
                    ("targetGroupName", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add_entry"])),
                        ("assetPath", string_schema()),
                        ("address", string_schema()),
                        ("groupName", string_schema()),
                    ],
                    &["action", "assetPath", "address", "groupName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove_entry"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["set_address"])),
                        ("assetPath", string_schema()),
                        ("newAddress", string_schema()),
                    ],
                    &["action", "assetPath", "newAddress"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["add_label"])),
                        ("assetPath", string_schema()),
                        ("label", string_schema()),
                    ],
                    &["action", "assetPath", "label"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove_label"])),
                        ("assetPath", string_schema()),
                        ("label", string_schema()),
                    ],
                    &["action", "assetPath", "label"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["list_entries"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["list_groups"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["create_group"])),
                        ("groupName", string_schema()),
                    ],
                    &["action", "groupName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["remove_group"])),
                        ("groupName", string_schema()),
                    ],
                    &["action", "groupName"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["move_entry"])),
                        ("assetPath", string_schema()),
                        ("targetGroupName", string_schema()),
                    ],
                    &["action", "assetPath", "targetGroupName"],
                    true,
                ),
            ],
        ),
        "addressables_build" => with_one_of(
            object_schema(
                &[
                    ("action", enum_string_schema(&["build", "clean_build"])),
                    ("buildTarget", string_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["build"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["clean_build"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "addressables_analyze" => with_one_of(
            object_schema(
                &[
                    (
                        "action",
                        enum_string_schema(&[
                            "analyze_duplicates",
                            "analyze_dependencies",
                            "analyze_unused",
                        ]),
                    ),
                    ("assetPath", string_schema()),
                    ("pageSize", integer_schema()),
                    ("offset", integer_schema()),
                ],
                &["action"],
                false,
            ),
            vec![
                object_schema(
                    &[("action", enum_string_schema(&["analyze_duplicates"]))],
                    &["action"],
                    true,
                ),
                object_schema(
                    &[
                        ("action", enum_string_schema(&["analyze_dependencies"])),
                        ("assetPath", string_schema()),
                    ],
                    &["action", "assetPath"],
                    true,
                ),
                object_schema(
                    &[("action", enum_string_schema(&["analyze_unused"]))],
                    &["action"],
                    true,
                ),
            ],
        ),
        "run_tests" => object_schema(
            &[
                (
                    "testMode",
                    enum_string_schema(&["EditMode", "PlayMode", "All"]),
                ),
                ("mode", string_schema()),
                ("filter", string_schema()),
                ("category", string_schema()),
                ("namespace", string_schema()),
                ("includeDetails", boolean_schema()),
                ("exportPath", string_schema()),
            ],
            &[],
            false,
        ),
        "get_test_status" => object_schema(
            &[
                ("includeTestResults", boolean_schema()),
                ("includeFileContent", boolean_schema()),
            ],
            &[],
            false,
        ),
        "reference_fetch" => object_schema(
            &[
                ("version", string_schema()),
                ("branch", string_schema()),
                ("force", boolean_schema()),
                ("acceptLicense", boolean_schema()),
                ("projectRoot", string_schema()),
            ],
            &[],
            false,
        ),
        "reference_status" => object_schema(&[("version", string_schema())], &[], false),
        "reference_search" => object_schema(
            &[
                ("pattern", string_schema()),
                ("version", string_schema()),
                ("path", string_schema()),
                ("maxResults", integer_schema()),
                ("regex", boolean_schema()),
                ("context", integer_schema()),
                ("projectRoot", string_schema()),
            ],
            &["pattern"],
            false,
        ),
        "reference_grep" => object_schema(
            &[
                ("pattern", string_schema()),
                ("version", string_schema()),
                ("fileGlob", string_schema()),
                ("context", integer_schema()),
                ("projectRoot", string_schema()),
            ],
            &["pattern"],
            false,
        ),
        "reference_view" => object_schema(
            &[
                ("path", string_schema()),
                ("version", string_schema()),
                ("startLine", integer_schema()),
                ("maxLines", integer_schema()),
                ("projectRoot", string_schema()),
            ],
            &["path"],
            false,
        ),
        "reference_clean" => object_schema(
            &[
                ("keep", integer_schema()),
                ("version", string_schema()),
                ("dryRun", boolean_schema()),
            ],
            &[],
            false,
        ),
        "reference_find_symbol" => object_schema(
            &[
                ("name", string_schema()),
                ("kind", string_schema()),
                ("namespace", string_schema()),
                ("version", string_schema()),
                ("projectRoot", string_schema()),
            ],
            &["name"],
            false,
        ),
        "reference_diff" => object_schema(
            &[
                ("from", string_schema()),
                ("to", string_schema()),
                ("symbol", string_schema()),
                ("path", string_schema()),
                ("maxSymbols", integer_schema()),
            ],
            &["from", "to"],
            false,
        ),
        "reference_resolve_symbol_at" => object_schema(
            &[
                ("path", string_schema()),
                ("line", integer_schema()),
                ("column", integer_schema()),
                ("version", string_schema()),
                ("projectRoot", string_schema()),
            ],
            &["path", "line", "column"],
            false,
        ),
        "reference_embed_build" => object_schema(
            &[
                ("version", string_schema()),
                ("projectRoot", string_schema()),
            ],
            &[],
            false,
        ),
        "reference_embed_search" => object_schema(
            &[
                ("query", string_schema()),
                ("version", string_schema()),
                ("topK", integer_schema()),
                ("projectRoot", string_schema()),
            ],
            &["query"],
            false,
        ),
        _ => default_params_schema(),
    }
}

fn default_params_schema() -> Value {
    json!({
        "type": "object",
        "additionalProperties": true
    })
}

fn default_response_schema() -> Value {
    json!({
        "type": "object",
        "additionalProperties": true
    })
}

fn object_schema(
    properties: &[(&str, Value)],
    required: &[&str],
    additional_allowed: bool,
) -> Value {
    let mut map = serde_json::Map::new();
    for (name, schema) in properties {
        map.insert((*name).to_string(), schema.clone());
    }

    let mut schema = json!({
        "type": "object",
        "properties": map,
        "additionalProperties": additional_allowed
    });
    if !required.is_empty() {
        schema["required"] = json!(required);
    }
    schema
}

fn string_schema() -> Value {
    json!({ "type": "string" })
}

fn boolean_schema() -> Value {
    json!({ "type": "boolean" })
}

fn integer_schema() -> Value {
    json!({ "type": "integer" })
}

fn number_schema() -> Value {
    json!({ "type": "number" })
}

fn any_schema() -> Value {
    json!({})
}

fn any_object_schema() -> Value {
    object_schema(&[], &[], true)
}

fn array_of(item_schema: Value) -> Value {
    json!({
        "type": "array",
        "items": item_schema
    })
}

fn vector2_schema() -> Value {
    object_schema(
        &[("x", number_schema()), ("y", number_schema())],
        &["x", "y"],
        false,
    )
}

fn vector3_schema() -> Value {
    object_schema(
        &[
            ("x", number_schema()),
            ("y", number_schema()),
            ("z", number_schema()),
        ],
        &["x", "y", "z"],
        false,
    )
}

fn with_one_of(mut schema: Value, variants: Vec<Value>) -> Value {
    schema["oneOf"] = Value::Array(variants);
    schema
}

fn with_any_of(mut schema: Value, variants: Vec<Value>) -> Value {
    schema["anyOf"] = Value::Array(variants);
    schema
}

fn enum_string_schema(values: &[&str]) -> Value {
    json!({
        "type": "string",
        "enum": values
    })
}

#[cfg(test)]
mod tests {
    use super::{get_tool_spec, is_known_tool, list_tool_specs, ToolExecutor, TOOL_NAMES};
    use serde_json::{json, Value};

    #[test]
    fn tool_catalog_keeps_manifest_parity_count() {
        assert_eq!(TOOL_NAMES.len(), 129);
    }

    #[test]
    fn known_tool_lookup_works() {
        assert!(is_known_tool("ping"));
        assert!(!is_known_tool("not_existing_tool"));
    }

    #[test]
    fn tool_specs_cover_catalog() {
        assert_eq!(list_tool_specs().len(), TOOL_NAMES.len());
    }

    #[test]
    fn tools_doc_matches_registered_catalog() {
        let docs = include_str!("../../docs/tools.md");
        let runtime_tool_count = TOOL_NAMES
            .iter()
            .filter(|name| !name.starts_with("reference_"))
            .count();
        let reference_tool_count = TOOL_NAMES.len() - runtime_tool_count;

        assert!(docs.contains(&format!("Registered tool total: {}", TOOL_NAMES.len())));
        assert!(docs.contains(&format!(
            "## Runtime Tool APIs ({runtime_tool_count} tools)"
        )));
        assert!(docs.contains(&format!(
            "## Reference Cache ({reference_tool_count} tools)"
        )));

        for name in TOOL_NAMES {
            assert!(
                docs.contains(&format!("`{name}`")),
                "docs/tools.md is missing `{name}`"
            );
        }
    }

    #[test]
    fn remote_tools_have_unity_bridge_dispatch_cases() {
        let bridge_router = include_str!(
            "../../UnityCliBridge/Packages/unity-cli-bridge/Editor/Core/BridgeCommandRouter.cs"
        );

        for name in TOOL_NAMES {
            let spec = get_tool_spec(name).expect("tool must exist");
            if spec.executor == ToolExecutor::Remote {
                assert!(
                    bridge_router.contains(&format!("[\"{name}\"]")),
                    "remote tool `{name}` is missing from BridgeCommandRouter registry"
                );
            }
        }
    }

    #[test]
    fn tool_catalog_avoids_default_params_schema_for_known_tools() {
        let fallback = json!({
            "type": "object",
            "additionalProperties": true
        });
        for name in TOOL_NAMES {
            let spec = get_tool_spec(name).expect("tool must exist");
            assert_ne!(
                spec.params_schema, fallback,
                "tool `{name}` unexpectedly uses fallback params schema"
            );
        }
    }

    #[test]
    fn ping_schema_is_strict_object() {
        let spec = get_tool_spec("ping").expect("ping must exist");
        assert!(!spec.mutating);
        assert_eq!(spec.executor, ToolExecutor::Remote);
        assert_eq!(spec.params_schema["type"], "object");
        assert_eq!(spec.params_schema["additionalProperties"], false);
    }

    #[test]
    fn local_executor_tools_are_marked() {
        let spec = get_tool_spec("search").expect("search must exist");
        assert_eq!(spec.executor, ToolExecutor::Local);
    }

    #[test]
    fn rename_symbol_schema_is_strict_and_requires_name_path() {
        let spec = get_tool_spec("rename_symbol").expect("rename_symbol must exist");
        assert_eq!(spec.executor, ToolExecutor::Local);
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(
            spec.params_schema["required"],
            json!(["namePath", "newName"])
        );
    }

    #[test]
    fn csharp_file_write_schemas_are_local_and_strict() {
        for name in [
            "write_csharp_file",
            "create_csharp_file",
            "apply_csharp_edits",
        ] {
            let spec = get_tool_spec(name).expect("csharp edit tool must exist");
            assert_eq!(spec.executor, ToolExecutor::Local);
            assert_eq!(spec.params_schema["additionalProperties"], false);
        }
    }

    #[test]
    fn build_index_schema_allows_output_path() {
        let spec = get_tool_spec("build_index").expect("build_index must exist");
        assert!(spec.params_schema["properties"]["outputPath"].is_object());
    }

    #[test]
    fn modify_gameobject_schema_requires_path() {
        let spec = get_tool_spec("modify_gameobject").expect("modify_gameobject must exist");
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(spec.params_schema["required"], json!(["path"]));
        assert_eq!(
            spec.params_schema["properties"]["position"]["type"],
            "object"
        );
    }

    #[test]
    fn run_tests_schema_allows_test_mode_enum() {
        let spec = get_tool_spec("run_tests").expect("run_tests must exist");
        assert_eq!(
            spec.params_schema["properties"]["testMode"]["enum"],
            json!(["EditMode", "PlayMode", "All"])
        );
    }

    #[test]
    fn load_scene_schema_uses_one_of_identifier_variants() {
        let spec = get_tool_spec("load_scene").expect("load_scene must exist");
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(2)
        );
    }

    #[test]
    fn delete_gameobject_schema_uses_any_of_targets() {
        let spec = get_tool_spec("delete_gameobject").expect("delete_gameobject must exist");
        assert_eq!(
            spec.params_schema["anyOf"].as_array().map(|v| v.len()),
            Some(2)
        );
    }

    #[test]
    fn set_component_field_schema_requires_component_and_field() {
        let spec = get_tool_spec("set_component_field").expect("set_component_field must exist");
        assert_eq!(
            spec.params_schema["required"],
            json!(["componentType", "fieldPath"])
        );
        assert_eq!(spec.params_schema["additionalProperties"], false);
    }

    #[test]
    fn input_keyboard_schema_uses_any_of_for_action_or_actions() {
        let spec = get_tool_spec("input_keyboard").expect("input_keyboard must exist");
        assert_eq!(
            spec.params_schema["anyOf"].as_array().map(|v| v.len()),
            Some(2)
        );
    }

    #[test]
    fn create_prefab_schema_allows_template_mode_without_source_object() {
        let spec = get_tool_spec("create_prefab").expect("create_prefab must exist");
        assert_eq!(spec.params_schema["required"], json!(["prefabPath"]));
        assert_eq!(
            spec.params_schema["anyOf"].as_array().map(|v| v.len()),
            Some(2)
        );
    }

    #[test]
    fn capture_video_start_schema_restricts_mode_and_format() {
        let spec = get_tool_spec("capture_video_start").expect("capture_video_start must exist");
        assert_eq!(
            spec.params_schema["properties"]["captureMode"]["enum"],
            json!(["game"])
        );
        assert_eq!(
            spec.params_schema["properties"]["format"]["enum"],
            json!(["mp4", "webm", "png_sequence"])
        );
    }

    #[test]
    fn clear_logs_schema_is_strict_empty_object() {
        let spec = get_tool_spec("clear_logs").expect("clear_logs must exist");
        assert_eq!(spec.params_schema["type"], "object");
        assert_eq!(spec.params_schema["additionalProperties"], false);
    }

    #[test]
    fn create_animator_controller_schema_is_strict_and_nested() {
        let spec =
            get_tool_spec("create_animator_controller").expect("create_animator_controller exists");
        assert_eq!(spec.params_schema["required"], json!(["controllerPath"]));
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(
            spec.params_schema["properties"]["parameters"]["items"]["properties"]["type"]["enum"],
            json!(["Bool", "Float", "Int", "Trigger"])
        );
        assert_eq!(
            spec.params_schema["properties"]["transitions"]["items"]["properties"]["conditions"]
                ["items"]["properties"]["mode"]["enum"],
            json!(["If", "IfNot", "Greater", "Less", "Equals", "NotEqual"])
        );
    }

    #[test]
    fn create_animation_clip_schema_requires_clip_and_sprite_paths() {
        let spec = get_tool_spec("create_animation_clip").expect("create_animation_clip exists");
        assert_eq!(
            spec.params_schema["required"],
            json!(["clipPath", "spritePaths"])
        );
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(
            spec.params_schema["properties"]["spritePaths"]["items"]["type"],
            "string"
        );
    }

    #[test]
    fn create_sprite_atlas_schema_is_strict_and_nested() {
        let spec = get_tool_spec("create_sprite_atlas").expect("create_sprite_atlas exists");
        assert_eq!(spec.params_schema["required"], json!(["atlasPath"]));
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(
            spec.params_schema["properties"]["textureSettings"]["properties"]["filterMode"]["enum"],
            json!(["Point", "Bilinear", "Trilinear"])
        );
    }

    #[test]
    fn get_gameobject_details_schema_uses_any_of_identifiers() {
        let spec = get_tool_spec("get_gameobject_details").expect("get_gameobject_details exists");
        assert_eq!(
            spec.params_schema["anyOf"].as_array().map(|v| v.len()),
            Some(2)
        );
    }

    #[test]
    fn manage_layers_schema_requires_action_enum() {
        let spec = get_tool_spec("manage_layers").expect("manage_layers exists");
        assert_eq!(spec.params_schema["required"], json!(["action"]));
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(5)
        );
        assert_eq!(
            spec.params_schema["properties"]["action"]["enum"],
            json!(["get", "add", "remove", "get_by_name", "get_by_index"])
        );
    }

    #[test]
    fn addressables_manage_schema_has_manage_actions() {
        let spec = get_tool_spec("addressables_manage").expect("addressables_manage exists");
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(10)
        );
        assert_eq!(
            spec.params_schema["properties"]["action"]["enum"],
            json!([
                "add_entry",
                "remove_entry",
                "set_address",
                "add_label",
                "remove_label",
                "list_entries",
                "list_groups",
                "create_group",
                "remove_group",
                "move_entry"
            ])
        );
    }

    #[test]
    fn package_manager_schema_has_required_action() {
        let spec = get_tool_spec("package_manager").expect("package_manager exists");
        assert_eq!(spec.params_schema["required"], json!(["action"]));
        assert_eq!(spec.params_schema["additionalProperties"], false);
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(8)
        );
    }

    #[test]
    fn analyze_asset_dependencies_schema_has_expected_actions() {
        let spec =
            get_tool_spec("analyze_asset_dependencies").expect("analyze_asset_dependencies exists");
        assert_eq!(spec.params_schema["required"], json!(["action"]));
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(6)
        );
        assert_eq!(
            spec.params_schema["properties"]["action"]["enum"],
            json!([
                "get_dependencies",
                "get_dependents",
                "analyze_circular",
                "find_unused",
                "analyze_size_impact",
                "validate_references"
            ])
        );
    }

    #[test]
    fn addressables_analyze_schema_uses_action_specific_variants() {
        let spec = get_tool_spec("addressables_analyze").expect("addressables_analyze exists");
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(3)
        );
        assert_eq!(
            spec.params_schema["properties"]["action"]["enum"],
            json!([
                "analyze_duplicates",
                "analyze_dependencies",
                "analyze_unused"
            ])
        );
    }

    #[test]
    fn manage_asset_database_schema_uses_action_specific_variants() {
        let spec = get_tool_spec("manage_asset_database").expect("manage_asset_database exists");
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(8)
        );
    }

    #[test]
    fn execute_menu_item_schema_supports_default_and_action_variants() {
        let spec = get_tool_spec("execute_menu_item").expect("execute_menu_item exists");
        assert!(spec.params_schema.get("required").is_none());
        assert_eq!(
            spec.params_schema["oneOf"].as_array().map(|v| v.len()),
            Some(3)
        );
        assert_eq!(
            spec.params_schema["properties"]["action"]["enum"],
            json!(["execute", "get_available_menus"])
        );
    }

    #[test]
    fn action_enum_schemas_use_composite_variants() {
        for name in TOOL_NAMES {
            let spec = get_tool_spec(name).expect("tool must exist");
            let has_action_enum = spec
                .params_schema
                .get("properties")
                .and_then(Value::as_object)
                .and_then(|props| props.get("action"))
                .and_then(|action_schema| action_schema.get("enum"))
                .and_then(Value::as_array)
                .is_some();
            if has_action_enum {
                let has_one_of = spec.params_schema.get("oneOf").is_some();
                let has_any_of = spec.params_schema.get("anyOf").is_some();
                assert!(
                    has_one_of || has_any_of,
                    "tool `{name}` has action enum but missing oneOf/anyOf variants"
                );
            }
        }
    }
}
