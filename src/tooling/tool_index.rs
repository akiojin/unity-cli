//! Category and one-line summary index for `tool list` filtering.
//!
//! Categories mirror the `docs/tools.md` headings; `tool_index_matches_docs_headings`
//! keeps the two in sync.

use serde::Serialize;

use crate::tool_catalog::TOOL_NAMES;

/// `(tool name, docs/tools.md category heading, one-line summary)`.
const TOOL_INDEX: &[(&str, &str, &str)] = &[
    ("create_scene", "Scenes", "Create a new scene"),
    ("get_scene_info", "Scenes", "Get scene metadata"),
    ("list_scenes", "Scenes", "List all scenes"),
    ("load_scene", "Scenes", "Load a scene"),
    ("save_scene", "Scenes", "Save the current scene"),
    ("start_scene_bake", "Scenes", "Start Lighting, legacy NavMesh, NavMeshSurface, or Occlusion baking"),
    ("get_scene_bake_status", "Scenes", "Poll bake progress, terminal failure, and verified saved artifacts"),
    ("create_gameobject", "GameObjects", "Create a new GameObject"),
    ("delete_gameobject", "GameObjects", "Delete a GameObject"),
    ("find_gameobject", "GameObjects", "Find GameObjects by name or criteria"),
    ("get_hierarchy", "GameObjects", "Get scene hierarchy tree"),
    ("modify_gameobject", "GameObjects", "Modify GameObject properties"),
    ("get_gameobject_details", "GameObjects", "Get detailed GameObject info"),
    ("add_component", "Components", "Add a component to a GameObject"),
    ("set_component_field", "Components", "Set a component field value"),
    ("get_component_types", "Components", "List available component types"),
    ("list_components", "Components", "List components on a GameObject"),
    ("modify_component", "Components", "Modify component properties"),
    ("remove_component", "Components", "Remove a component"),
    ("find_by_component", "Components", "Find GameObjects by component type"),
    ("get_component_values", "Components", "Get component field values"),
    ("get_object_references", "Components", "Get object reference graph"),
    ("create_animator_controller", "Animator", "Create an AnimatorController asset with parameters, states, and transitions"),
    ("create_animation_clip", "Animator", "Create an AnimationClip asset from sprite frames with frame rate and loop settings"),
    ("get_animation_curves", "Animator", "Read numeric bindings, keys and tangents; identify object-reference bindings"),
    ("edit_animation_curve", "Animator", "Set, upsert or remove numeric keys for one validated binding"),
    ("get_animator_runtime_info", "Animator", "Get Animator runtime info"),
    ("get_animator_state", "Animator", "Get current Animator state"),
    ("get_timeline", "Timeline", "Inspect a Timeline asset or PlayableDirector, tracks, clips, and bindings (read-only)"),
    ("manage_timeline", "Timeline", "Create and edit Timeline AnimationTracks, bind a director, or evaluate a time (mutating)"),
    ("create_prefab", "Prefabs", "Create a new Prefab"),
    ("exit_prefab_mode", "Prefabs", "Exit Prefab editing mode"),
    ("instantiate_prefab", "Prefabs", "Instantiate a Prefab in the scene"),
    ("modify_prefab", "Prefabs", "Modify Prefab properties"),
    ("open_prefab", "Prefabs", "Open a Prefab for editing"),
    ("save_prefab", "Prefabs", "Save Prefab changes"),
    ("analyze_scene_contents", "Assets", "Analyze scene asset contents"),
    ("manage_asset_database", "Assets", "Manage AssetDatabase operations"),
    ("analyze_asset_dependencies", "Assets", "Analyze asset dependency graph"),
    ("manage_asset_import_settings", "Assets", "Manage asset import settings"),
    ("create_sprite_atlas", "Assets", "Create a SpriteAtlas asset with packables and packing settings"),
    ("create_material", "Assets", "Create a new Material"),
    ("modify_material", "Assets", "Modify Material properties"),
    ("refresh_assets", "Assets", "Refresh the AssetDatabase"),
    ("vfx_describe_graph", "Visual Effect Graph", "Describe a Visual Effect Graph asset: contexts, blocks, operators, parameters, links, and compile errors"),
    ("vfx_list_library", "Visual Effect Graph", "List available Visual Effect Graph descriptors (`kind`: block, operator, context, parameter, or template)"),
    ("vfx_apply", "Visual Effect Graph", "Apply an authoring mutation to a Visual Effect Graph asset and recompile"),
    ("vfx_runtime", "Visual Effect Graph", "Control a VisualEffect component at runtime via its public API"),
    ("vfx_bake_sdf", "Visual Effect Graph", "Bake a Mesh asset into a Signed Distance Field Texture3D asset"),
    ("vfx_settings", "Visual Effect Graph", "Read or write VFX project settings or per-machine preferences"),
    ("addressables_analyze", "Addressables", "Analyze Addressables configuration"),
    ("addressables_build", "Addressables", "Build Addressables content"),
    ("addressables_manage", "Addressables", "Manage Addressables groups and entries"),
    ("get_compilation_state", "Code / LSP", "Get C# compilation state"),
    ("hot_reload_status", "Code / LSP", "Inspect hot reload preview state"),
    ("hot_reload", "Code / LSP", "Preview methods or recover Play"),
    ("read", "Code / LSP", "Read a C# source file"),
    ("find_refs", "Code / LSP", "Find symbol references"),
    ("search", "Code / LSP", "Search code by pattern"),
    ("find_symbol", "Code / LSP", "Find symbol definitions"),
    ("get_symbols", "Code / LSP", "Get symbols in a file"),
    ("build_index", "Code / LSP", "Build code search index"),
    ("update_index", "Code / LSP", "Update code search index"),
    ("get_index_status", "Code / LSP", "Get code search index status"),
    ("rename_symbol", "Code / LSP", "Rename a C# symbol"),
    ("replace_symbol_body", "Code / LSP", "Replace a C# symbol body"),
    ("insert_before_symbol", "Code / LSP", "Insert C# source before a symbol"),
    ("insert_after_symbol", "Code / LSP", "Insert C# source after a symbol"),
    ("remove_symbol", "Code / LSP", "Remove a C# symbol"),
    ("validate_text_edits", "Code / LSP", "Validate C# text edits"),
    ("write_csharp_file", "Code / LSP", "Write a complete C# source file"),
    ("create_csharp_file", "Code / LSP", "Create a new C# source file"),
    ("apply_csharp_edits", "Code / LSP", "Apply structured C# source edits"),
    ("create_class", "Code / LSP", "Create a C# class"),
    ("add_input_action", "Input System", "Add an Input Action"),
    ("create_action_map", "Input System", "Create an Action Map"),
    ("remove_action_map", "Input System", "Remove an Action Map"),
    ("remove_input_action", "Input System", "Remove an Input Action"),
    ("analyze_input_actions_asset", "Input System", "Analyze Input Actions asset"),
    ("get_input_actions_state", "Input System", "Get Input Actions runtime state"),
    ("add_input_binding", "Input System", "Add an Input Binding"),
    ("create_composite_binding", "Input System", "Create a composite binding"),
    ("remove_input_binding", "Input System", "Remove an Input Binding"),
    ("remove_all_bindings", "Input System", "Remove all bindings from an action"),
    ("manage_control_schemes", "Input System", "Manage control schemes"),
    ("input_gamepad", "Input System", "Simulate gamepad input"),
    ("input_keyboard", "Input System", "Simulate keyboard input"),
    ("input_mouse", "Input System", "Simulate mouse input"),
    ("input_touch", "Input System", "Simulate touch input"),
    ("create_input_sequence", "Input System", "Create an input sequence"),
    ("get_current_input_state", "Input System", "Get current input device state"),
    ("click_ui_element", "UI", "Click a UI element"),
    ("find_ui_elements", "UI", "Find UI elements by criteria"),
    ("get_ui_element_state", "UI", "Get UI element state"),
    ("set_ui_element_value", "UI", "Set UI element value"),
    ("simulate_ui_input", "UI", "Simulate UI input events"),
    ("pause_game", "Playback & Testing", "Pause Play mode"),
    ("play_game", "Playback & Testing", "Enter Play mode"),
    ("stop_game", "Playback & Testing", "Exit Play mode"),
    ("get_test_status", "Playback & Testing", "Get test run status"),
    ("run_tests", "Playback & Testing", "Run EditMode/PlayMode tests"),
    ("build_player", "Player Builds", "Queue a standalone player build on the Unity Editor host"),
    ("get_build_status", "Player Builds", "Read build status, report, and artifact paths by build ID"),
    ("profiler_get_metrics", "Profiler", "Get profiler metrics"),
    ("profiler_start", "Profiler", "Start profiler capture"),
    ("profiler_status", "Profiler", "Get profiler status"),
    ("profiler_stop", "Profiler", "Stop profiler capture"),
    ("clear_console", "Editor", "Clear the Console window"),
    ("clear_logs", "Editor", "Clear editor logs"),
    ("read_console", "Editor", "Read Console output"),
    ("manage_layers", "Editor", "Manage layers"),
    ("quit_editor", "Editor", "Quit Unity Editor"),
    ("manage_selection", "Editor", "Manage editor selection"),
    ("manage_tags", "Editor", "Manage tags"),
    ("manage_tools", "Editor", "Manage editor tools"),
    ("manage_windows", "Editor", "Manage editor windows"),
    ("execute_menu_item", "Editor", "Execute a menu item"),
    ("eval_csharp", "Editor", "Evaluate synchronous C#"),
    ("get_eval_status", "Editor", "Query evaluation result"),
    ("get_eval_stats", "Editor", "Query eval domain counters"),
    ("package_manager", "Editor", "Manage packages"),
    ("registry_config", "Editor", "Configure scoped registries"),
    ("get_editor_info", "Editor", "Get editor version info"),
    ("get_editor_state", "Editor", "Get editor state"),
    ("get_project_setting", "Editor", "Get a project setting"),
    ("set_project_setting", "Editor", "Set a project setting"),
    ("get_project_settings", "Editor", "Get project settings"),
    ("get_package_setting", "Editor", "Get a package setting"),
    ("set_package_setting", "Editor", "Set a package setting"),
    ("update_project_settings", "Editor", "Update project settings"),
    ("analyze_screenshot", "Screenshots & Video", "Analyze a screenshot"),
    ("capture_screenshot", "Screenshots & Video", "Capture a screenshot"),
    ("capture_video_start", "Screenshots & Video", "Start video capture"),
    ("capture_video_status", "Screenshots & Video", "Get video capture status"),
    ("capture_video_stop", "Screenshots & Video", "Stop video capture"),
    ("get_command_stats", "System", "Get bridge command statistics and, via the CLI, merged local transport timing stats"),
    ("ping", "System", "Check Unity Editor connectivity"),
    ("list_packages", "System", "List installed packages"),
    ("reference_fetch", "Reference Cache", "Shallow-clone UnityCsReference for the active Unity version into the local cache."),
    ("reference_status", "Reference Cache", "List cached UnityCsReference versions, branches, fetched-at, and disk usage."),
    ("reference_search", "Reference Cache", "Search the cached reference source for a pattern with optional path and result limits."),
    ("reference_grep", "Reference Cache", "Grep the cached reference source line-by-line with optional file glob and context lines."),
    ("reference_view", "Reference Cache", "Display a slice of a file in the cached reference source by line range."),
    ("reference_clean", "Reference Cache", "Remove old UnityCsReference snapshots, keeping the newest entries."),
    ("reference_find_symbol", "Reference Cache", "Look up type / method / property definitions in the cached reference source via a per-version on-disk index."),
    ("reference_diff", "Reference Cache", "Compare a symbol or path range between two cached Unity versions. Returns symbol-level hunks or `{added, removed, changed}`."),
    ("reference_resolve_symbol_at", "Reference Cache", "Resolve the identifier at a project cursor position (`Assets/...` / `Packages/...`) to candidate reference cache entries with view excerpts."),
    ("reference_embed_build", "Reference Cache", "Build an embedding index (BGE-Small-EN, ONNX) for a cached Unity version. Writes `.unity-cli-index/embeddings.bin`."),
    ("reference_embed_search", "Reference Cache", "Semantic / natural-language lookup over the embedding index. Returns hits sorted by cosine similarity."),
];

#[derive(Debug, Clone, Copy, Serialize, PartialEq, Eq)]
pub struct ToolSummary {
    pub name: &'static str,
    pub description: &'static str,
}

#[derive(Debug, Default)]
pub struct ToolListFilter<'a> {
    pub query: Option<&'a str>,
    pub category: Option<&'a str>,
    pub offset: usize,
    pub limit: Option<usize>,
}

/// Category headings in `docs/tools.md` order.
pub fn tool_categories() -> Vec<&'static str> {
    let mut categories: Vec<&'static str> = Vec::new();
    for (_, category, _) in TOOL_INDEX {
        if !categories.contains(category) {
            categories.push(category);
        }
    }
    categories
}

/// Lowercase slug of a heading: `Playback & Testing` -> `playback-testing`.
pub fn category_slug(category: &str) -> String {
    category
        .split(|c: char| !c.is_ascii_alphanumeric())
        .filter(|part| !part.is_empty())
        .map(str::to_ascii_lowercase)
        .collect::<Vec<_>>()
        .join("-")
}

fn resolve_category(input: &str) -> Option<&'static str> {
    let slug = category_slug(input);
    tool_categories()
        .into_iter()
        .find(|category| category_slug(category) == slug)
}

fn index_entry(name: &str) -> Option<(&'static str, &'static str)> {
    TOOL_INDEX
        .iter()
        .find(|(candidate, _, _)| *candidate == name)
        .map(|(_, category, summary)| (*category, *summary))
}

/// Filters the catalog in `TOOL_NAMES` order. `query` matches the tool name or
/// summary case-insensitively; `category` accepts a heading or its slug.
pub fn filter_tools(filter: &ToolListFilter<'_>) -> Result<Vec<ToolSummary>, String> {
    let category = match filter.category {
        Some(input) => Some(resolve_category(input).ok_or_else(|| {
            let valid: Vec<String> = tool_categories().into_iter().map(category_slug).collect();
            format!(
                "Unknown category `{input}`. Valid categories: {}",
                valid.join(", ")
            )
        })?),
        None => None,
    };
    let query = filter.query.map(str::to_ascii_lowercase);

    let matches = TOOL_NAMES.iter().filter_map(|name| {
        let (tool_category, summary) = index_entry(name).unwrap_or(("", ""));
        if category.is_some_and(|expected| expected != tool_category) {
            return None;
        }
        if let Some(query) = &query {
            if !name.contains(query.as_str())
                && !summary.to_ascii_lowercase().contains(query.as_str())
            {
                return None;
            }
        }
        Some(ToolSummary {
            name,
            description: summary,
        })
    });
    Ok(matches
        .skip(filter.offset)
        .take(filter.limit.unwrap_or(usize::MAX))
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_tool_has_exactly_one_index_entry_with_a_short_summary() {
        for name in TOOL_NAMES {
            let entries: Vec<_> = TOOL_INDEX.iter().filter(|(n, _, _)| n == name).collect();
            assert_eq!(entries.len(), 1, "{name} must have one index entry");
            let summary = entries[0].2;
            assert!(
                !summary.is_empty() && summary.len() <= 160,
                "{name}: {summary}"
            );
        }
        assert_eq!(TOOL_INDEX.len(), TOOL_NAMES.len());
    }

    #[test]
    fn tool_index_matches_docs_headings() {
        let docs = std::fs::read_to_string(concat!(env!("CARGO_MANIFEST_DIR"), "/docs/tools.md"))
            .expect("docs/tools.md should exist");
        let mut category: Option<String> = None;
        let mut documented = Vec::new();
        for line in docs.lines() {
            if let Some(heading) = line.strip_prefix("### ") {
                category = Some(heading.trim().to_string());
            } else if let Some(heading) = line.strip_prefix("## ") {
                category = heading
                    .starts_with("Reference Cache")
                    .then(|| "Reference Cache".to_string());
            } else if let (Some(category), Some(rest)) = (&category, line.strip_prefix("| `")) {
                let name = rest.split('`').next().unwrap_or_default();
                if TOOL_NAMES.contains(&name) {
                    documented.push((name.to_string(), category.clone()));
                }
            }
        }
        assert_eq!(documented.len(), TOOL_NAMES.len());
        for (name, category) in documented {
            assert_eq!(
                index_entry(&name).map(|(c, _)| c),
                Some(category.as_str()),
                "{name} category must match docs/tools.md"
            );
        }
    }

    #[test]
    fn category_accepts_heading_or_slug() {
        assert_eq!(resolve_category("scenes"), Some("Scenes"));
        assert_eq!(
            resolve_category("Playback & Testing"),
            Some("Playback & Testing")
        );
        assert_eq!(
            resolve_category("playback-testing"),
            Some("Playback & Testing")
        );
        assert_eq!(resolve_category("code-lsp"), Some("Code / LSP"));
        assert_eq!(resolve_category("nope"), None);
    }

    #[test]
    fn query_matches_name_or_summary_case_insensitively() {
        let found = filter_tools(&ToolListFilter {
            query: Some("SCREENSHOT"),
            ..Default::default()
        })
        .unwrap();
        let names: Vec<_> = found.iter().map(|tool| tool.name).collect();
        assert_eq!(names, vec!["analyze_screenshot", "capture_screenshot"]);
    }

    #[test]
    fn unknown_category_lists_valid_slugs() {
        let error = filter_tools(&ToolListFilter {
            category: Some("nope"),
            ..Default::default()
        })
        .unwrap_err();
        assert!(
            error.contains("scenes") && error.contains("reference-cache"),
            "{error}"
        );
    }
}
