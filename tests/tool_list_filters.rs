use serde_json::Value;
use std::process::Command;

fn run_json(args: &[&str]) -> (Value, usize) {
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args(args)
        .args(["--output", "json"])
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
    let value = serde_json::from_slice(&output.stdout).expect("stdout should be JSON");
    (value, output.stdout.len())
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
    assert_eq!(items.len(), 152);
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
