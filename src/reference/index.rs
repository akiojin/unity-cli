use std::collections::BTreeMap;
use std::fs;
use std::path::Path;
use std::sync::OnceLock;
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use walkdir::WalkDir;

pub const INDEX_REL_PATH: &str = ".unity-cli-index/symbols.json";
pub const INDEX_VERSION: u32 = 1;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct ReferenceSymbolEntry {
    pub path: String,
    pub name: String,
    pub kind: String,
    pub line: u32,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub namespace: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub container: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub fqn: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default, PartialEq, Eq)]
pub struct IndexedFile {
    pub signature: String,
    pub symbols: Vec<ReferenceSymbolEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReferenceSymbolIndex {
    pub version: u32,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub unity_version: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub branch: Option<String>,
    pub generated_at_epoch_ms: u64,
    pub files: BTreeMap<String, IndexedFile>,
}

impl Default for ReferenceSymbolIndex {
    fn default() -> Self {
        Self {
            version: INDEX_VERSION,
            unity_version: None,
            branch: None,
            generated_at_epoch_ms: 0,
            files: BTreeMap::new(),
        }
    }
}

pub fn extract_symbols_from_text(content: &str, rel_path: &str) -> Vec<ReferenceSymbolEntry> {
    static NAMESPACE_RE: OnceLock<Regex> = OnceLock::new();
    static TYPE_RE: OnceLock<Regex> = OnceLock::new();
    static METHOD_RE: OnceLock<Regex> = OnceLock::new();
    static PROPERTY_RE: OnceLock<Regex> = OnceLock::new();
    let ns_re = NAMESPACE_RE.get_or_init(|| {
        Regex::new(r"(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)")
            .expect("namespace regex compiles")
    });
    let type_re = TYPE_RE.get_or_init(|| {
        Regex::new(
            r"(?m)^[ \t]*(?:\[[^\]]*\][ \t]*)*(?:(?:public|internal|protected|private|static|abstract|sealed|partial|readonly)[ \t]+)*(class|interface|struct|enum)[ \t]+([A-Za-z_][A-Za-z0-9_]*)",
        )
        .expect("type regex compiles")
    });
    let method_re = METHOD_RE.get_or_init(|| {
        Regex::new(
            r"(?m)^[ \t]*(?:\[[^\]]*\][ \t]*)*(?:public|internal|protected|private)[ \t]+(?:(?:static|virtual|override|abstract|sealed|partial|async|extern|new|readonly|unsafe)[ \t]+)*(?:[A-Za-z_][\w<>,\?\[\]\. \t]*?)[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*\(",
        )
        .expect("method regex compiles")
    });
    let property_re = PROPERTY_RE.get_or_init(|| {
        Regex::new(
            r"(?m)^[ \t]*(?:\[[^\]]*\][ \t]*)*(?:public|internal|protected|private)[ \t]+(?:(?:static|virtual|override|abstract|sealed|partial|readonly)[ \t]+)*(?:[A-Za-z_][\w<>,\?\[\]\. \t]*?)[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*\{[ \t]*(?:get|set)",
        )
        .expect("property regex compiles")
    });

    let namespace = ns_re
        .captures(content)
        .and_then(|c| c.get(1).map(|m| m.as_str().to_string()));

    let mut symbols = Vec::new();
    let mut consumed_spans: Vec<(usize, usize)> = Vec::new();
    for cap in type_re.captures_iter(content) {
        let kind = cap
            .get(1)
            .map(|m| m.as_str().to_string())
            .unwrap_or_default();
        let name = cap
            .get(2)
            .map(|m| m.as_str().to_string())
            .unwrap_or_default();
        if name.is_empty() {
            continue;
        }
        let full = cap.get(0).expect("full match exists");
        consumed_spans.push((full.start(), full.end()));
        let line = (content[..full.start()].matches('\n').count() as u32) + 1;
        let fqn = namespace.as_ref().map(|ns| format!("{ns}.{name}"));
        symbols.push(ReferenceSymbolEntry {
            path: rel_path.to_string(),
            name,
            kind,
            line,
            namespace: namespace.clone(),
            container: None,
            fqn,
        });
    }
    for cap in property_re.captures_iter(content) {
        let name = cap
            .get(1)
            .map(|m| m.as_str().to_string())
            .unwrap_or_default();
        if name.is_empty() {
            continue;
        }
        let full = cap.get(0).expect("full match exists");
        if overlaps_consumed(full.start(), &consumed_spans) {
            continue;
        }
        consumed_spans.push((full.start(), full.end()));
        let line = (content[..full.start()].matches('\n').count() as u32) + 1;
        let fqn = namespace.as_ref().map(|ns| format!("{ns}.{name}"));
        symbols.push(ReferenceSymbolEntry {
            path: rel_path.to_string(),
            name,
            kind: "property".to_string(),
            line,
            namespace: namespace.clone(),
            container: None,
            fqn,
        });
    }
    for cap in method_re.captures_iter(content) {
        let name = cap
            .get(1)
            .map(|m| m.as_str().to_string())
            .unwrap_or_default();
        if name.is_empty() {
            continue;
        }
        let full = cap.get(0).expect("full match exists");
        if overlaps_consumed(full.start(), &consumed_spans) {
            continue;
        }
        let line = (content[..full.start()].matches('\n').count() as u32) + 1;
        let fqn = namespace.as_ref().map(|ns| format!("{ns}.{name}"));
        symbols.push(ReferenceSymbolEntry {
            path: rel_path.to_string(),
            name,
            kind: "method".to_string(),
            line,
            namespace: namespace.clone(),
            container: None,
            fqn,
        });
    }
    symbols
}

fn overlaps_consumed(pos: usize, spans: &[(usize, usize)]) -> bool {
    spans.iter().any(|(start, end)| pos >= *start && pos < *end)
}

pub fn file_signature(metadata: &fs::Metadata) -> String {
    let mtime = metadata
        .modified()
        .ok()
        .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
        .map(|d| d.as_millis())
        .unwrap_or(0);
    format!("{}:{}", metadata.len(), mtime)
}

pub fn build_or_update_index(version_dir: &Path) -> Result<ReferenceSymbolIndex> {
    let index_path = version_dir.join(INDEX_REL_PATH);
    let mut index = load_existing_index(&index_path).unwrap_or_default();
    if index.version != INDEX_VERSION {
        index = ReferenceSymbolIndex::default();
    }
    let mut seen = std::collections::BTreeSet::new();
    for entry in WalkDir::new(version_dir)
        .follow_links(false)
        .into_iter()
        .filter_map(|e| e.ok())
    {
        if !entry.file_type().is_file() {
            continue;
        }
        let path = entry.path();
        if path.extension().and_then(|s| s.to_str()) != Some("cs") {
            continue;
        }
        // Always `/`-separated so `--path Runtime/Export` matches on Windows too.
        let rel = match path.strip_prefix(version_dir) {
            Ok(p) => p
                .components()
                .map(|component| component.as_os_str().to_string_lossy())
                .collect::<Vec<_>>()
                .join("/"),
            Err(_) => continue,
        };
        if rel.starts_with(".unity-cli-index") || rel.starts_with(".git") {
            continue;
        }
        seen.insert(rel.clone());
        let metadata = match fs::metadata(path) {
            Ok(m) => m,
            Err(_) => continue,
        };
        let sig = file_signature(&metadata);
        if let Some(existing) = index.files.get(&rel) {
            if existing.signature == sig {
                continue;
            }
        }
        let contents = match fs::read_to_string(path) {
            Ok(s) => s,
            Err(_) => continue,
        };
        let symbols = extract_symbols_from_text(&contents, &rel);
        index.files.insert(
            rel,
            IndexedFile {
                signature: sig,
                symbols,
            },
        );
    }
    index.files.retain(|k, _| seen.contains(k));
    index.generated_at_epoch_ms = now_epoch_ms();
    save_index(&index_path, &index)?;
    Ok(index)
}

pub fn find_symbol(
    index: &ReferenceSymbolIndex,
    name: &str,
    kind: Option<&str>,
    namespace: Option<&str>,
) -> Vec<ReferenceSymbolEntry> {
    let mut hits = Vec::new();
    for file in index.files.values() {
        for sym in &file.symbols {
            if sym.name != name {
                continue;
            }
            if let Some(k) = kind {
                if sym.kind != k {
                    continue;
                }
            }
            if let Some(ns) = namespace {
                if sym.namespace.as_deref() != Some(ns) {
                    continue;
                }
            }
            hits.push(sym.clone());
        }
    }
    hits.sort_by(|a, b| a.path.cmp(&b.path).then(a.line.cmp(&b.line)));
    hits
}

fn load_existing_index(path: &Path) -> Option<ReferenceSymbolIndex> {
    let contents = fs::read_to_string(path).ok()?;
    serde_json::from_str(&contents).ok()
}

fn save_index(path: &Path, index: &ReferenceSymbolIndex) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create {}", parent.display()))?;
    }
    let contents = serde_json::to_string_pretty(index)
        .with_context(|| format!("failed to serialize symbol index for {}", path.display()))?;
    fs::write(path, contents).with_context(|| format!("failed to write {}", path.display()))?;
    Ok(())
}

fn now_epoch_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .ok()
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

pub fn validate_kind(kind: &str) -> Result<()> {
    const ALLOWED: &[&str] = &[
        "class",
        "interface",
        "struct",
        "enum",
        "method",
        "property",
        "field",
    ];
    if ALLOWED.contains(&kind) {
        Ok(())
    } else {
        Err(anyhow!(
            "kind '{}' is not allowed (expected one of: {})",
            kind,
            ALLOWED.join(", ")
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;
    use tempfile::TempDir;

    #[test]
    fn extract_symbols_from_text_finds_class() {
        let text = "namespace UnityEngine {\n    public class Animator : Behaviour {\n    }\n}\n";
        let symbols = extract_symbols_from_text(text, "Runtime/Animator.cs");
        assert!(
            symbols
                .iter()
                .any(|s| s.name == "Animator" && s.kind == "class"),
            "expected to find class Animator: {symbols:?}"
        );
    }

    #[test]
    fn extract_symbols_from_text_captures_namespace() {
        let text = "namespace UnityEditor {\n    public class AnimatorInspector { }\n}\n";
        let symbols = extract_symbols_from_text(text, "Editor/AnimatorInspector.cs");
        let hit = symbols
            .iter()
            .find(|s| s.name == "AnimatorInspector")
            .expect("AnimatorInspector should be indexed");
        assert_eq!(hit.namespace.as_deref(), Some("UnityEditor"));
        assert_eq!(hit.kind, "class");
        assert!(hit.line >= 1);
    }

    #[test]
    fn extract_symbols_from_text_finds_struct_and_interface() {
        let text = "public interface IBar { }\npublic struct Vec3 { }\n";
        let symbols = extract_symbols_from_text(text, "Runtime/Types.cs");
        assert!(symbols
            .iter()
            .any(|s| s.name == "IBar" && s.kind == "interface"));
        assert!(symbols
            .iter()
            .any(|s| s.name == "Vec3" && s.kind == "struct"));
    }

    #[test]
    fn extract_symbols_from_text_finds_methods() {
        let text = "namespace UnityEngine {\n    public class Animator {\n        public void Play(string stateName) {}\n        public static int GetLayerCount() { return 0; }\n    }\n}\n";
        let symbols = extract_symbols_from_text(text, "Runtime/Animator.cs");
        let play = symbols
            .iter()
            .find(|s| s.name == "Play")
            .expect("Play should be extracted");
        assert_eq!(play.kind, "method");
        assert_eq!(play.namespace.as_deref(), Some("UnityEngine"));
        assert!(symbols
            .iter()
            .any(|s| s.name == "GetLayerCount" && s.kind == "method"));
        assert!(symbols
            .iter()
            .any(|s| s.name == "Animator" && s.kind == "class"));
    }

    #[test]
    fn extract_symbols_from_text_finds_property() {
        let text =
            "public class Foo {\n    public int Count { get; set; }\n    public string Name { get { return _name; } }\n}\n";
        let symbols = extract_symbols_from_text(text, "Runtime/Foo.cs");
        assert!(symbols
            .iter()
            .any(|s| s.name == "Count" && s.kind == "property"));
        assert!(symbols
            .iter()
            .any(|s| s.name == "Name" && s.kind == "property"));
        assert!(!symbols
            .iter()
            .any(|s| s.name == "Count" && s.kind == "method"));
    }

    #[test]
    fn extract_symbols_from_text_does_not_double_capture_class_name() {
        let text = "public class Bar {}\n";
        let symbols = extract_symbols_from_text(text, "Runtime/Bar.cs");
        let bar_entries: Vec<_> = symbols.iter().filter(|s| s.name == "Bar").collect();
        assert_eq!(bar_entries.len(), 1);
        assert_eq!(bar_entries[0].kind, "class");
    }

    #[test]
    fn build_or_update_index_persists_signature() {
        let tmp = TempDir::new().unwrap();
        let version_dir = tmp.path();
        let src_dir = version_dir.join("Runtime");
        fs::create_dir_all(&src_dir).unwrap();
        fs::write(
            src_dir.join("Foo.cs"),
            "namespace UnityEngine {\n    public class Foo { }\n}\n",
        )
        .unwrap();
        let index = build_or_update_index(version_dir).unwrap();
        assert_eq!(index.version, INDEX_VERSION);
        assert_eq!(index.files.len(), 1);
        let entry = index
            .files
            .iter()
            .find(|(p, _)| p.contains("Foo.cs"))
            .unwrap();
        assert!(!entry.1.signature.is_empty());
        assert!(entry.1.symbols.iter().any(|s| s.name == "Foo"));
        assert!(version_dir.join(INDEX_REL_PATH).exists());
    }

    #[test]
    fn find_symbol_filters_by_kind_and_namespace() {
        let mut index = ReferenceSymbolIndex::default();
        index.files.insert(
            "Runtime/A.cs".to_string(),
            IndexedFile {
                signature: "1:1".to_string(),
                symbols: vec![
                    ReferenceSymbolEntry {
                        path: "Runtime/A.cs".to_string(),
                        name: "Foo".to_string(),
                        kind: "class".to_string(),
                        line: 2,
                        namespace: Some("UnityEngine".to_string()),
                        container: None,
                        fqn: Some("UnityEngine.Foo".to_string()),
                    },
                    ReferenceSymbolEntry {
                        path: "Runtime/A.cs".to_string(),
                        name: "Foo".to_string(),
                        kind: "method".to_string(),
                        line: 10,
                        namespace: Some("UnityEngine".to_string()),
                        container: Some("Animator".to_string()),
                        fqn: Some("UnityEngine.Animator.Foo".to_string()),
                    },
                ],
            },
        );
        let hits = find_symbol(&index, "Foo", Some("class"), None);
        assert_eq!(hits.len(), 1);
        assert_eq!(hits[0].kind, "class");
        let ns_hits = find_symbol(&index, "Foo", None, Some("UnityEngine"));
        assert_eq!(ns_hits.len(), 2);
        let nothing = find_symbol(&index, "Bar", None, None);
        assert!(nothing.is_empty());
    }

    #[test]
    fn validate_kind_accepts_known_and_rejects_unknown() {
        validate_kind("class").unwrap();
        validate_kind("method").unwrap();
        let err = validate_kind("alien").unwrap_err();
        assert!(format!("{err:#}").contains("not allowed"));
    }

    #[test]
    fn file_signature_changes_when_content_changes() {
        let tmp = TempDir::new().unwrap();
        let path = tmp.path().join("a.cs");
        fs::write(&path, b"short").unwrap();
        let sig1 = file_signature(&fs::metadata(&path).unwrap());
        std::thread::sleep(std::time::Duration::from_millis(15));
        fs::write(&path, b"longer content").unwrap();
        let sig2 = file_signature(&fs::metadata(&path).unwrap());
        assert_ne!(sig1, sig2);
    }

    #[test]
    fn build_or_update_index_skips_already_indexed_files_until_change() {
        let tmp = TempDir::new().unwrap();
        let version_dir = tmp.path();
        fs::create_dir_all(version_dir.join("Runtime")).unwrap();
        fs::write(version_dir.join("Runtime/X.cs"), "public class X { }\n").unwrap();
        let first = build_or_update_index(version_dir).unwrap();
        let first_sig = first
            .files
            .values()
            .next()
            .map(|f| f.signature.clone())
            .unwrap();
        let second = build_or_update_index(version_dir).unwrap();
        let second_sig = second
            .files
            .values()
            .next()
            .map(|f| f.signature.clone())
            .unwrap();
        assert_eq!(first_sig, second_sig);
    }

    fn fixture_root() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/reference-cache")
    }

    #[test]
    fn build_or_update_index_handles_existing_fixture() {
        let tmp = TempDir::new().unwrap();
        let version_dir = tmp.path();
        // Copy fixture into version_dir
        for entry in WalkDir::new(fixture_root())
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let src = entry.path();
            let rel = src.strip_prefix(fixture_root()).unwrap();
            let dst = version_dir.join(rel);
            if entry.file_type().is_dir() {
                fs::create_dir_all(&dst).unwrap();
            } else if entry.file_type().is_file() {
                if let Some(parent) = dst.parent() {
                    fs::create_dir_all(parent).unwrap();
                }
                fs::copy(src, &dst).unwrap();
            }
        }
        let index = build_or_update_index(version_dir).unwrap();
        assert!(index.files.len() >= 2);
        assert!(index.files.keys().all(|rel| !rel.contains('\\')));
        assert!(index.files.keys().any(|rel| rel.contains('/')));
        let hits = find_symbol(&index, "Animator", Some("class"), None);
        assert!(!hits.is_empty(), "Animator class should be discovered");
    }
}
