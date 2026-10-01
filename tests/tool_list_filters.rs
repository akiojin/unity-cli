use serde_json::Value;
use std::process::Command;

fn run_json(args: &[&str]) -> (Value, usize) {
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port().to_string();
    drop(listener);
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args(args)
        .args(["--output", "json"])
        .args([
            "--host",
            "127.0.0.1",
            "--port",
            &port,
            "--timeout-ms",
            "100",
        ])
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env_remove("UNITY_CLI_AUTH_TOKEN_FILE")
        .env(
            "UNITY_CLI_TOOLS_ROOT",
            std::env::temp_dir().join("unity-cli-tool-list-test"),
        )
        .output()
        .expect("unity-cli should run");
    assert!(
        output.status.success(),
        "unity-cli {args:?} failed: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    let value: Value = serde_json::from_slice(&output.stdout).expect("stdout should be JSON");
    assert_eq!(value["success"], true);
    (value["data"].clone(), output.stdout.len())
}

#[test]
fn offline_default_list_has_schemas_and_names_only_is_compatible() {
    let (tools, _) = run_json(&["tool", "list", "--limit", "2"]);
    for tool in tools.as_array().unwrap() {
        assert_eq!(tool["source"], "builtin");
        assert!(tool["params_schema"].is_object());
        assert!(tool["description"].is_string());
    }
    let (only_names, _) = run_json(&["tool", "list", "--limit", "2", "--names-only"]);
    assert!(only_names.as_array().unwrap().iter().all(Value::is_string));
    assert_eq!(names(&tools), names(&only_names));
}

fn names(value: &Value) -> Vec<String> {
    value
        .as_array()
        .expect("tool list JSON should be an array")
        .iter()
        .map(|item| match item {
            Value::String(name) => name.clone(),
            Value::Object(map) => map["name"].as_str().unwrap().to_string(),
            other => panic!("unexpected item: {other}"),
        })
        .collect()
}

#[test]
fn query_returns_only_matching_tools() {
    let (value, _) = run_json(&["tool", "list", "--query", "screenshot"]);
    let mut found = names(&value);
    found.sort();
    assert_eq!(found, vec!["analyze_screenshot", "capture_screenshot"]);
}

#[test]
fn compact_is_name_and_description_only_and_at_most_half_of_full_catalog() {
    let (compact, compact_bytes) = run_json(&["tool", "list", "--compact"]);
    let items = compact.as_array().unwrap();
    assert_eq!(items.len(), 154);
    for item in items {
        let map = item.as_object().expect("compact item should be an object");
        let mut keys: Vec<&str> = map.keys().map(String::as_str).collect();
        keys.sort();
        assert_eq!(keys, vec!["description", "name"], "item: {item}");
        let description = map["description"].as_str().unwrap();
        assert!(!description.is_empty() && !description.contains('\n'));
    }

    let (_, full_bytes) = run_json(&["tool", "schema"]);
    assert!(
        compact_bytes * 2 <= full_bytes,
        "compact={compact_bytes} full={full_bytes}"
    );
}

#[test]
fn category_returns_only_that_docs_section() {
    let (value, _) = run_json(&["tool", "list", "--category", "scenes"]);
    let mut found = names(&value);
    found.sort();
    assert_eq!(
        found,
        vec![
            "create_scene",
            "get_scene_bake_status",
            "get_scene_info",
            "list_scenes",
            "load_scene",
            "save_scene",
            "start_scene_bake",
        ]
    );
}

#[test]
fn limit_and_offset_page_the_filtered_list() {
    let (all, _) = run_json(&["tool", "list", "--category", "scenes"]);
    let all = names(&all);
    let (page, _) = run_json(&[
        "tool",
        "list",
        "--category",
        "scenes",
        "--offset",
        "2",
        "--limit",
        "3",
    ]);
    assert_eq!(names(&page), all[2..5].to_vec());
}

#[test]
fn unknown_category_is_rejected() {
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args(["tool", "list", "--category", "nope", "--output", "json"])
        .output()
        .expect("unity-cli should run");
    assert!(!output.status.success());
    let stderr = String::from_utf8_lossy(&output.stderr);
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stderr.contains("scenes") || stdout.contains("scenes"),
        "error should list valid categories: {stderr}{stdout}"
    );
}
