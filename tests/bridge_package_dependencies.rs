use serde_json::Value;
use std::fs;
use std::path::{Path, PathBuf};

const BRIDGE: &str = "UnityCliBridge/Packages/unity-cli-bridge";

fn collect_cs_files(dir: &Path, out: &mut Vec<PathBuf>) {
    for entry in fs::read_dir(dir).unwrap() {
        let path = entry.unwrap().path();
        if path.is_dir() {
            collect_cs_files(&path, out);
        } else if path.extension().is_some_and(|ext| ext == "cs") {
            out.push(path);
        }
    }
}

/// Unity does not add uGUI to projects that omit it, so the bridge must declare
/// it or a fresh project fails with CS0234 and enters Safe Mode (#373).
#[test]
fn bridge_declares_ugui_when_sources_use_it() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join(BRIDGE);
    let mut files = Vec::new();
    collect_cs_files(&root, &mut files);
    let users: Vec<_> = files
        .iter()
        .filter(|path| {
            let source = fs::read_to_string(path).unwrap();
            source.contains("UnityEngine.UI;")
                || source.contains("UnityEngine.EventSystems")
                || source.contains("UnityEngine.UI.")
        })
        .collect();
    assert!(!users.is_empty(), "expected bridge sources to use uGUI");

    let package: Value =
        serde_json::from_str(&fs::read_to_string(root.join("package.json")).unwrap()).unwrap();
    assert!(
        package["dependencies"]["com.unity.ugui"].is_string(),
        "package.json must declare com.unity.ugui; used by {users:?}"
    );
}
