use std::collections::{BTreeMap, BTreeSet};
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use walkdir::{DirEntry, WalkDir};

const INDEX_REL_PATH: &str = ".unity/cache/unity-cli/symbol-index.json";
const INDEX_VERSION: u32 = 1;
const MAX_SNIPPET_CHARS: usize = 400;

#[derive(Debug, Clone, Serialize, Deserialize)]
struct SymbolEntry {
    path: String,
    name: String,
    kind: String,
    line: usize,
    column: usize,
    #[serde(skip_serializing_if = "Option::is_none")]
    container: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    namespace: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
struct IndexedFile {
    signature: String,
    symbols: Vec<SymbolEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct SymbolIndex {
    version: u32,
    generated_at_epoch_ms: u64,
    files: BTreeMap<String, IndexedFile>,
}

impl Default for SymbolIndex {
    fn default() -> Self {
        Self {
            version: INDEX_VERSION,
            generated_at_epoch_ms: 0,
            files: BTreeMap::new(),
        }
    }
}

#[derive(Debug, Clone)]
struct ScopedType {
    name: String,
    depth: i32,
}

pub fn maybe_execute_local_tool(tool_name: &str, params: &Value) -> Option<Result<Value>> {
    match tool_name {
        "read" => Some(local_read(params)),
        "search" => Some(local_search(params)),
        "list_packages" => Some(local_list_packages()),
        "get_symbols" => Some(local_get_symbols(params)),
        "build_index" => Some(local_build_index(params)),
        "update_index" => Some(local_update_index(params)),
        "find_symbol" => Some(local_find_symbol(params)),
        "find_refs" => Some(local_find_refs(params)),
        _ => None,
    }
}

fn local_read(params: &Value) -> Result<Value> {
    let path = params
        .get("path")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("read requires `path`"))?;

    let start_line = params
        .get("startLine")
        .and_then(Value::as_u64)
        .unwrap_or(1)
        .max(1);
    let max_lines = params
        .get("maxLines")
        .and_then(Value::as_u64)
        .unwrap_or(200)
        .clamp(1, 1000);

    let root = project_root()?;
    let rel = normalize_rel_path(path)
        .ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))?;
    let abs = resolve_existing_project_path(&root, &rel)?;
    let content = fs::read_to_string(&abs)
        .with_context(|| format!("Failed to read file: {}", abs.display()))?;

    let lines: Vec<&str> = content.lines().collect();
    let start_idx = (start_line as usize).saturating_sub(1).min(lines.len());
    let end_idx = (start_idx + max_lines as usize).min(lines.len());
    let selected = &lines[start_idx..end_idx];
    let body = selected.join("\n");

    Ok(json!({
        "success": true,
        "path": rel,
        "startLine": start_idx + 1,
        "endLine": end_idx,
        "lineCount": selected.len(),
        "content": body,
        "text": selected.join("\n")
    }))
}

fn local_search(params: &Value) -> Result<Value> {
    let pattern = params
        .get("pattern")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("search requires `pattern`"))?;
    let regex = Regex::new(pattern).with_context(|| format!("Invalid regex pattern: {pattern}"))?;
    let limit = params
        .get("limit")
        .and_then(Value::as_u64)
        .unwrap_or(50)
        .clamp(1, 500) as usize;

    let root = project_root()?;
    let search_root = params.get("path").and_then(Value::as_str);

    let roots = if let Some(raw_rel) = search_root {
        let rel = normalize_rel_path(raw_rel)
            .ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))?;
        vec![resolve_candidate_project_path(&root, &rel)?]
    } else {
        vec![root.join("Assets"), root.join("Packages")]
    };

    let mut matches = Vec::new();

    for dir in roots {
        if !dir.exists() {
            continue;
        }

        for entry in WalkDir::new(&dir)
            .follow_links(false)
            .into_iter()
            .filter_entry(|entry| is_included_entry(entry))
            .filter_map(|entry| entry.ok())
        {
            let p = entry.path();
            if !entry.file_type().is_file() || !is_csharp_file(p) {
                continue;
            }

            let text = match fs::read_to_string(p) {
                Ok(text) => text,
                Err(_) => continue,
            };

            let rel = to_rel_project_path(&root, p);
            for (idx, line) in text.lines().enumerate() {
                if regex.is_match(line) {
                    matches.push(json!({
                        "path": rel,
                        "line": idx + 1,
                        "text": line
                    }));
                    if matches.len() >= limit {
                        return Ok(json!({
                            "success": true,
                            "pattern": pattern,
                            "matches": matches,
                            "count": matches.len(),
                            "truncated": true
                        }));
                    }
                }
            }
        }
    }

    Ok(json!({
        "success": true,
        "pattern": pattern,
        "matches": matches,
        "count": matches.len(),
        "truncated": false
    }))
}

fn local_list_packages() -> Result<Value> {
    let root = project_root()?;
    let packages_dir = root.join("Packages");
    if !packages_dir.exists() {
        return Ok(json!({
            "success": true,
            "packages": [],
            "count": 0
        }));
    }

    let mut packages = Vec::new();
    for entry in fs::read_dir(&packages_dir)
        .with_context(|| format!("Failed to read packages dir: {}", packages_dir.display()))?
    {
        let entry = entry?;
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }

        let name = entry.file_name().to_string_lossy().to_string();
        let rel = to_rel_project_path(&root, &path);
        packages.push(json!({
            "name": name,
            "path": rel
        }));
    }

    packages.sort_by(|a, b| a["name"].as_str().cmp(&b["name"].as_str()));

    Ok(json!({
        "success": true,
        "packages": packages,
        "count": packages.len()
    }))
}

fn local_get_symbols(params: &Value) -> Result<Value> {
    let path = params
        .get("path")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("get_symbols requires `path`"))?;
    let root = project_root()?;
    let rel = normalize_rel_path(path)
        .ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))?;

    if !rel.to_ascii_lowercase().ends_with(".cs") {
        return Err(anyhow!("Only .cs files are supported"));
    }

    let abs = resolve_existing_project_path(&root, &rel)?;
    let symbols = extract_symbols_from_file(&abs, &rel)?;
    let list = symbols.iter().map(symbol_to_value).collect::<Vec<_>>();

    Ok(json!({
        "success": true,
        "path": rel,
        "symbols": list
    }))
}

fn local_build_index(params: &Value) -> Result<Value> {
    let root = project_root()?;
    let exclude_package_cache = params
        .get("excludePackageCache")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let scope = params
        .get("scope")
        .and_then(Value::as_str)
        .unwrap_or("all")
        .to_ascii_lowercase();

    let mut dirs = match scope.as_str() {
        "assets" => vec![root.join("Assets")],
        "packages" => vec![root.join("Packages")],
        "embedded" => vec![root.join("Packages")],
        "library" => vec![root.join("Library/PackageCache")],
        _ => vec![root.join("Assets"), root.join("Packages")],
    };
    if !exclude_package_cache && scope != "assets" && scope != "embedded" {
        dirs.push(root.join("Library/PackageCache"));
    }

    let files = collect_cs_files(&root, &dirs);
    let mut index = SymbolIndex::default();
    let mut indexed_symbols = 0usize;
    let mut skipped = Vec::new();

    for (rel, abs) in files {
        match extract_symbols_from_file(&abs, &rel) {
            Ok(symbols) => {
                indexed_symbols += symbols.len();
                index.files.insert(
                    rel,
                    IndexedFile {
                        signature: file_signature(&abs),
                        symbols,
                    },
                );
            }
            Err(error) => {
                skipped.push(json!({
                    "path": rel,
                    "reason": error.to_string()
                }));
            }
        }
    }

    index.generated_at_epoch_ms = now_epoch_ms();
    let index_path = save_index(&root, &index)?;

    Ok(json!({
        "success": skipped.is_empty(),
        "indexedFiles": index.files.len(),
        "indexedSymbols": indexed_symbols,
        "indexPath": to_rel_project_path(&root, &index_path),
        "generatedAtEpochMs": index.generated_at_epoch_ms,
        "skipped": skipped
    }))
}

fn local_update_index(params: &Value) -> Result<Value> {
    let paths = params
        .get("paths")
        .and_then(Value::as_array)
        .ok_or_else(|| anyhow!("update_index requires `paths` array"))?;
    if paths.is_empty() {
        return Ok(json!({
            "success": false,
            "error": "invalid_arguments",
            "message": "paths must be a non-empty array."
        }));
    }

    let root = project_root()?;
    let mut index = match load_index_if_exists(&root) {
        Ok(Some(index)) => index,
        Ok(None) => SymbolIndex::default(),
        Err(_) => SymbolIndex::default(),
    };

    let mut dedup = BTreeSet::new();
    let mut requested = Vec::new();
    for raw in paths {
        let Some(path_raw) = raw.as_str() else {
            continue;
        };
        let rel = match normalize_rel_or_abs_path(&root, path_raw) {
            Ok(rel) => rel,
            Err(error) => {
                requested.push((path_raw.to_string(), None, Some(error.to_string())));
                continue;
            }
        };
        if dedup.insert(rel.clone()) {
            requested.push((path_raw.to_string(), Some(rel), None));
        }
    }

    let mut updated = 0usize;
    let mut skipped = Vec::new();
    let mut failures = Vec::new();

    for (requested_path, rel_path, rel_error) in requested {
        if let Some(reason) = rel_error {
            failures.push(json!({ "path": requested_path, "reason": reason }));
            continue;
        }
        let rel = rel_path.expect("relative path should exist");
        let abs = resolve_candidate_project_path(&root, &rel)?;

        if !abs.exists() {
            skipped.push(json!({ "path": requested_path, "reason": "missing" }));
            failures.push(json!({ "path": requested_path, "reason": "missing" }));
            continue;
        }
        if !is_csharp_file(&abs) {
            skipped.push(json!({ "path": requested_path, "reason": "unsupported_extension" }));
            failures.push(json!({ "path": requested_path, "reason": "unsupported_extension" }));
            continue;
        }

        match extract_symbols_from_file(&abs, &rel) {
            Ok(symbols) => {
                updated += 1;
                index.files.insert(
                    rel,
                    IndexedFile {
                        signature: file_signature(&abs),
                        symbols,
                    },
                );
            }
            Err(error) => {
                failures.push(json!({
                    "path": requested_path,
                    "reason": error.to_string()
                }));
            }
        }
    }

    index.generated_at_epoch_ms = now_epoch_ms();
    save_index(&root, &index)?;

    let mut result = json!({
        "success": failures.is_empty(),
        "updated": updated,
        "skipped": skipped
    });
    if !failures.is_empty() {
        result["partialSuccess"] = Value::Bool(updated > 0);
        result["failures"] = Value::Array(failures);
    }

    Ok(result)
}

fn local_find_symbol(params: &Value) -> Result<Value> {
    let name = params
        .get("name")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("find_symbol requires `name`"))?;
    let kind = params.get("kind").and_then(Value::as_str);
    let scope = params
        .get("scope")
        .and_then(Value::as_str)
        .unwrap_or("all")
        .to_ascii_lowercase();
    let exact = params
        .get("exact")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    let root = project_root()?;
    let index = match load_index_if_exists(&root) {
        Ok(Some(index)) if index_is_ready(&index) => index,
        Ok(_) => {
            return Ok(index_not_ready_response());
        }
        Err(error) => {
            return Ok(json!({
                "success": false,
                "error": "index_corrupted",
                "message": format!("Failed to read local symbol index: {error}")
            }));
        }
    };

    let mut grouped: BTreeMap<String, Vec<Value>> = BTreeMap::new();
    let mut total = 0usize;

    for (path, file) in &index.files {
        if !path_matches_scope(path, &scope) {
            continue;
        }

        for symbol in &file.symbols {
            if !symbol_name_matches(&symbol.name, name, exact) {
                continue;
            }
            if let Some(expected_kind) = kind {
                if !symbol.kind.eq_ignore_ascii_case(expected_kind) {
                    continue;
                }
            }

            grouped
                .entry(path.clone())
                .or_default()
                .push(symbol_to_value(symbol));
            total += 1;
        }
    }

    let results = grouped
        .into_iter()
        .map(|(path, symbols)| json!({ "path": path, "symbols": symbols }))
        .collect::<Vec<_>>();

    Ok(json!({
        "success": true,
        "results": results,
        "total": total
    }))
}

fn local_find_refs(params: &Value) -> Result<Value> {
    let name = params
        .get("name")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("find_refs requires `name`"))?;
    let scope = params
        .get("scope")
        .and_then(Value::as_str)
        .unwrap_or("all")
        .to_ascii_lowercase();
    let start_after = params.get("startAfter").and_then(Value::as_str);
    let path_filter = params.get("path").and_then(Value::as_str);
    let page_size = params
        .get("pageSize")
        .and_then(Value::as_u64)
        .unwrap_or(50)
        .clamp(1, 1000) as usize;
    let max_bytes = params
        .get("maxBytes")
        .and_then(Value::as_u64)
        .unwrap_or((1024 * 64) as u64)
        .clamp(128, (1024 * 1024) as u64) as usize;
    let max_matches_per_file = params
        .get("maxMatchesPerFile")
        .and_then(Value::as_u64)
        .unwrap_or(5)
        .clamp(1, 100) as usize;
    let snippet_context = params
        .get("snippetContext")
        .and_then(Value::as_u64)
        .unwrap_or(1)
        .clamp(0, 20) as usize;

    let root = project_root()?;
    let index = match load_index_if_exists(&root) {
        Ok(Some(index)) if index_is_ready(&index) => index,
        Ok(_) => {
            return Ok(index_not_ready_response());
        }
        Err(error) => {
            return Ok(json!({
                "success": false,
                "error": "index_corrupted",
                "message": format!("Failed to read local symbol index: {error}")
            }));
        }
    };
    drop(index);

    let name_regex = Regex::new(&format!(r"\b{}\b", regex::escape(name)))
        .with_context(|| format!("Invalid identifier for references search: {name}"))?;

    let dirs = match scope.as_str() {
        "assets" => vec![root.join("Assets")],
        "packages" => vec![root.join("Packages"), root.join("Library/PackageCache")],
        "embedded" => vec![root.join("Packages")],
        "library" => vec![root.join("Library/PackageCache")],
        _ => vec![
            root.join("Assets"),
            root.join("Packages"),
            root.join("Library/PackageCache"),
        ],
    };

    let files = collect_cs_files(&root, &dirs);
    let mut results = Vec::new();
    let mut total = 0usize;
    let mut bytes = 0usize;
    let mut truncated = false;
    let mut last_path: Option<String> = None;

    for (path, abs) in files {
        if let Some(cursor) = start_after {
            if path.as_str() <= cursor {
                continue;
            }
        }
        if let Some(filter) = path_filter {
            if !path.contains(filter) {
                continue;
            }
        }

        let text = match fs::read_to_string(&abs) {
            Ok(text) => text,
            Err(_) => continue,
        };
        let lines = text.lines().collect::<Vec<_>>();
        let mut refs = Vec::new();

        for (idx, line) in lines.iter().enumerate() {
            if refs.len() >= max_matches_per_file {
                break;
            }
            for found in name_regex.find_iter(line) {
                if refs.len() >= max_matches_per_file {
                    break;
                }
                if total >= page_size {
                    truncated = true;
                    break;
                }

                let (snippet, snippet_truncated) =
                    build_snippet(&lines, idx, snippet_context, MAX_SNIPPET_CHARS);
                let mut item = json!({
                    "line": idx + 1,
                    "column": found.start() + 1,
                    "snippet": snippet
                });
                if snippet_truncated {
                    item["snippetTruncated"] = Value::Bool(true);
                }

                let item_bytes = serde_json::to_vec(&item)
                    .context("Failed to encode find_refs result item")?
                    .len();
                if bytes + item_bytes > max_bytes {
                    truncated = true;
                    break;
                }

                bytes += item_bytes;
                total += 1;
                refs.push(item);
            }
            if truncated {
                break;
            }
        }

        if !refs.is_empty() {
            last_path = Some(path.clone());
            results.push(json!({ "path": path, "references": refs }));
        }

        if truncated {
            break;
        }
    }

    let mut response = json!({
        "success": true,
        "results": results,
        "total": total,
        "truncated": truncated
    });
    if truncated {
        if let Some(cursor) = last_path {
            response["cursor"] = Value::String(cursor);
        }
    }

    Ok(response)
}

fn project_root() -> Result<PathBuf> {
    if let Ok(raw) = env::var("UNITY_PROJECT_ROOT") {
        let trimmed = raw.trim();
        if !trimmed.is_empty() {
            return Ok(PathBuf::from(trimmed));
        }
    }
    env::current_dir().context("Failed to resolve current directory")
}

fn resolve_existing_project_path(root: &Path, rel: &str) -> Result<PathBuf> {
    let candidate = resolve_candidate_project_path(root, rel)?;
    let normalized = candidate
        .canonicalize()
        .with_context(|| format!("Failed to resolve path: {}", candidate.display()))?;
    let root_canon = root
        .canonicalize()
        .with_context(|| format!("Failed to resolve project root: {}", root.display()))?;

    if !normalized.starts_with(&root_canon) {
        return Err(anyhow!("path escapes project root"));
    }
    Ok(normalized)
}

fn resolve_candidate_project_path(root: &Path, rel: &str) -> Result<PathBuf> {
    let rel_path = Path::new(rel);
    if rel_path.is_absolute() {
        return Err(anyhow!("path must be project-relative"));
    }
    if rel_path
        .components()
        .any(|component| matches!(component, std::path::Component::ParentDir))
    {
        return Err(anyhow!("path must not include `..`"));
    }
    Ok(root.join(rel_path))
}

fn normalize_rel_or_abs_path(root: &Path, raw: &str) -> Result<String> {
    let path = Path::new(raw);
    if path.is_absolute() {
        let root_canon = root
            .canonicalize()
            .with_context(|| format!("Failed to resolve project root: {}", root.display()))?;

        let rel = if let Ok(canon_path) = path.canonicalize() {
            canon_path.strip_prefix(&root_canon).ok().map(PathBuf::from)
        } else {
            path.strip_prefix(&root_canon).ok().map(PathBuf::from)
        }
        .ok_or_else(|| anyhow!("path must be under project root"))?;

        let rel_text = rel.to_string_lossy().replace('\\', "/");
        return normalize_rel_path(&rel_text)
            .ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"));
    }

    normalize_rel_path(raw).ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))
}

fn normalize_rel_path(raw: &str) -> Option<String> {
    let mut normalized = raw.trim().replace('\\', "/");
    while normalized.starts_with("./") {
        normalized = normalized[2..].to_string();
    }
    normalized = normalized.trim_start_matches('/').to_string();

    let prefixes = ["Assets/", "Packages/", "Library/PackageCache/"];
    if let Some(start) = prefixes
        .iter()
        .filter_map(|prefix| normalized.find(prefix))
        .min()
    {
        normalized = normalized[start..].to_string();
    }

    if !prefixes.iter().any(|prefix| normalized.starts_with(prefix)) {
        return None;
    }

    let parts = normalized
        .split('/')
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>();
    if parts.iter().any(|part| *part == "..") {
        return None;
    }

    Some(parts.join("/"))
}

fn collect_cs_files(root: &Path, dirs: &[PathBuf]) -> Vec<(String, PathBuf)> {
    let mut by_path = BTreeMap::new();

    for dir in dirs {
        if !dir.exists() {
            continue;
        }
        for entry in WalkDir::new(dir)
            .follow_links(false)
            .into_iter()
            .filter_entry(|entry| is_included_entry(entry))
            .filter_map(|entry| entry.ok())
        {
            if !entry.file_type().is_file() {
                continue;
            }
            let abs = entry.path();
            if !is_csharp_file(abs) {
                continue;
            }
            let rel = to_rel_project_path(root, abs);
            by_path.insert(rel, abs.to_path_buf());
        }
    }

    by_path.into_iter().collect()
}

fn is_included_entry(entry: &DirEntry) -> bool {
    if entry.depth() == 0 {
        return true;
    }
    if !entry.file_type().is_dir() {
        return true;
    }

    let name = entry.file_name().to_string_lossy();
    !matches!(name.as_ref(), ".git" | "obj" | "bin")
}

fn is_csharp_file(path: &Path) -> bool {
    path.extension()
        .and_then(|value| value.to_str())
        .map(|ext| ext.eq_ignore_ascii_case("cs"))
        .unwrap_or(false)
}

fn extract_symbols_from_file(abs: &Path, rel_path: &str) -> Result<Vec<SymbolEntry>> {
    let text = fs::read_to_string(abs)
        .with_context(|| format!("Failed to read C# file: {}", abs.display()))?;
    Ok(extract_symbols_from_text(rel_path, &text))
}

fn extract_symbols_from_text(path: &str, text: &str) -> Vec<SymbolEntry> {
    let mut symbols = Vec::new();
    let mut namespace: Option<String> = None;
    let mut type_stack: Vec<ScopedType> = Vec::new();
    let mut pending_type: Option<String> = None;
    let mut in_block_comment = false;
    let mut brace_depth = 0i32;

    for (line_idx, line) in text.lines().enumerate() {
        while let Some(last) = type_stack.last() {
            if last.depth > brace_depth {
                type_stack.pop();
            } else {
                break;
            }
        }

        let cleaned = strip_csharp_comments(line, &mut in_block_comment);
        let trimmed = cleaned.trim();
        let open_count = cleaned.chars().filter(|c| *c == '{').count() as i32;
        let close_count = cleaned.chars().filter(|c| *c == '}').count() as i32;

        if let Some(caps) = namespace_regex().captures(trimmed) {
            namespace = Some(caps[1].to_string());
        }

        let mut push_type_now: Option<String> = None;
        if let Some(caps) = type_regex().captures(trimmed) {
            let kind = caps[1].to_string();
            let symbol_name = caps[2].to_string();
            let container = type_stack.last().map(|scope| scope.name.clone());
            symbols.push(SymbolEntry {
                path: path.to_string(),
                name: symbol_name.clone(),
                kind,
                line: line_idx + 1,
                column: symbol_column(line, &symbol_name),
                container,
                namespace: namespace.clone(),
            });

            if open_count > close_count {
                push_type_now = Some(symbol_name);
            } else if open_count == 0 {
                pending_type = Some(symbol_name);
            }
        } else if let Some(container_name) = type_stack.last().map(|scope| scope.name.clone()) {
            if trimmed.starts_with('[') {
                // Attribute lines are intentionally ignored.
            } else if let Some(caps) = property_regex().captures(trimmed) {
                let symbol_name = caps[1].to_string();
                symbols.push(SymbolEntry {
                    path: path.to_string(),
                    name: symbol_name.clone(),
                    kind: "property".to_string(),
                    line: line_idx + 1,
                    column: symbol_column(line, &symbol_name),
                    container: Some(container_name),
                    namespace: namespace.clone(),
                });
            } else if let Some(caps) = method_regex().captures(trimmed) {
                let symbol_name = caps[1].to_string();
                if !is_control_keyword(&symbol_name) {
                    symbols.push(SymbolEntry {
                        path: path.to_string(),
                        name: symbol_name.clone(),
                        kind: "method".to_string(),
                        line: line_idx + 1,
                        column: symbol_column(line, &symbol_name),
                        container: Some(container_name),
                        namespace: namespace.clone(),
                    });
                }
            } else if let Some(caps) = constructor_regex().captures(trimmed) {
                let symbol_name = caps[1].to_string();
                if symbol_name == container_name {
                    symbols.push(SymbolEntry {
                        path: path.to_string(),
                        name: symbol_name.clone(),
                        kind: "method".to_string(),
                        line: line_idx + 1,
                        column: symbol_column(line, &symbol_name),
                        container: Some(container_name),
                        namespace: namespace.clone(),
                    });
                }
            } else if let Some(caps) = field_regex().captures(trimmed) {
                let symbol_name = caps[1].to_string();
                symbols.push(SymbolEntry {
                    path: path.to_string(),
                    name: symbol_name.clone(),
                    kind: "field".to_string(),
                    line: line_idx + 1,
                    column: symbol_column(line, &symbol_name),
                    container: Some(container_name),
                    namespace: namespace.clone(),
                });
            }
        }

        brace_depth += open_count - close_count;
        if brace_depth < 0 {
            brace_depth = 0;
        }

        if let Some(type_name) = push_type_now {
            type_stack.push(ScopedType {
                name: type_name,
                depth: brace_depth.max(1),
            });
        } else if pending_type.is_some() && open_count > close_count {
            type_stack.push(ScopedType {
                name: pending_type.take().expect("pending type should exist"),
                depth: brace_depth.max(1),
            });
        }

        while let Some(last) = type_stack.last() {
            if last.depth > brace_depth {
                type_stack.pop();
            } else {
                break;
            }
        }
    }

    symbols
}

fn strip_csharp_comments(line: &str, in_block_comment: &mut bool) -> String {
    let mut out = String::new();
    let chars = line.chars().collect::<Vec<_>>();
    let mut idx = 0usize;
    let mut in_string = false;
    let mut escape = false;

    while idx < chars.len() {
        let ch = chars[idx];
        let next = chars.get(idx + 1).copied();

        if *in_block_comment {
            if ch == '*' && next == Some('/') {
                *in_block_comment = false;
                idx += 2;
            } else {
                idx += 1;
            }
            continue;
        }

        if !in_string && ch == '/' && next == Some('*') {
            *in_block_comment = true;
            idx += 2;
            continue;
        }
        if !in_string && ch == '/' && next == Some('/') {
            break;
        }

        if ch == '"' && !escape {
            in_string = !in_string;
        }
        escape = ch == '\\' && !escape;
        out.push(ch);
        idx += 1;
    }

    out
}

fn symbol_column(line: &str, name: &str) -> usize {
    line.find(name).map(|idx| idx + 1).unwrap_or(1)
}

fn is_control_keyword(name: &str) -> bool {
    matches!(
        name,
        "if" | "for" | "foreach" | "while" | "switch" | "catch" | "using" | "lock"
    )
}

fn namespace_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(r"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)")
            .expect("namespace regex should compile")
    })
}

fn type_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(
            r"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial|new|\s)*(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        )
        .expect("type regex should compile")
    })
}

fn method_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(
            r"^\s*(?:public|private|protected|internal|static|virtual|override|sealed|abstract|async|partial|new|extern|\s)+[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*(?:\{|=>)",
        )
        .expect("method regex should compile")
    })
}

fn constructor_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(
            r"^\s*(?:public|private|protected|internal|static|extern|\s)*([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\)\s*(?:\{|=>)",
        )
        .expect("constructor regex should compile")
    })
}

fn property_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(
            r"^\s*(?:public|private|protected|internal|static|virtual|override|sealed|abstract|new|\s)+[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(?:get|set)\b",
        )
        .expect("property regex should compile")
    })
}

fn field_regex() -> &'static Regex {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    REGEX.get_or_init(|| {
        Regex::new(
            r"^\s*(?:public|private|protected|internal|static|readonly|const|volatile|new|\s)+[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|;)",
        )
        .expect("field regex should compile")
    })
}

fn index_file_path(root: &Path) -> PathBuf {
    root.join(INDEX_REL_PATH)
}

fn load_index_if_exists(root: &Path) -> Result<Option<SymbolIndex>> {
    let path = index_file_path(root);
    if !path.exists() {
        return Ok(None);
    }

    let raw = fs::read_to_string(&path)
        .with_context(|| format!("Failed to read local index: {}", path.display()))?;
    let mut index: SymbolIndex = serde_json::from_str(&raw)
        .with_context(|| format!("Failed to parse local index JSON: {}", path.display()))?;
    if index.version == 0 {
        index.version = INDEX_VERSION;
    }
    Ok(Some(index))
}

fn save_index(root: &Path, index: &SymbolIndex) -> Result<PathBuf> {
    let path = index_file_path(root);
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create index directory: {}", parent.display()))?;
    }
    let serialized =
        serde_json::to_string_pretty(index).context("Failed to serialize local index JSON")?;
    fs::write(&path, serialized)
        .with_context(|| format!("Failed to write local index file: {}", path.display()))?;
    Ok(path)
}

fn index_is_ready(index: &SymbolIndex) -> bool {
    index
        .files
        .values()
        .any(|file| !file.symbols.is_empty() || !file.signature.is_empty())
}

fn index_not_ready_response() -> Value {
    json!({
        "success": false,
        "error": "index_not_ready",
        "message": "Code index is not built. Run build_index first.",
        "hint": "Use build_index to create local symbol index, then retry."
    })
}

fn symbol_name_matches(candidate: &str, expected: &str, exact: bool) -> bool {
    if exact {
        candidate == expected
    } else {
        candidate.contains(expected)
    }
}

fn path_matches_scope(path: &str, scope: &str) -> bool {
    match scope {
        "assets" => path.starts_with("Assets/"),
        "packages" => path.starts_with("Packages/") || path.starts_with("Library/PackageCache/"),
        "embedded" => path.starts_with("Packages/"),
        "library" => path.starts_with("Library/PackageCache/"),
        _ => true,
    }
}

fn symbol_to_value(symbol: &SymbolEntry) -> Value {
    let mut object = serde_json::Map::new();
    object.insert("name".to_string(), Value::String(symbol.name.clone()));
    object.insert("kind".to_string(), Value::String(symbol.kind.clone()));
    object.insert("line".to_string(), Value::Number(symbol.line.into()));
    object.insert("column".to_string(), Value::Number(symbol.column.into()));
    if let Some(container) = &symbol.container {
        object.insert("container".to_string(), Value::String(container.clone()));
    }
    if let Some(namespace) = &symbol.namespace {
        object.insert("namespace".to_string(), Value::String(namespace.clone()));
    }
    Value::Object(object)
}

fn file_signature(path: &Path) -> String {
    match fs::metadata(path) {
        Ok(metadata) => {
            let mtime = metadata
                .modified()
                .ok()
                .and_then(|time| time.duration_since(UNIX_EPOCH).ok())
                .map(|duration| duration.as_millis())
                .unwrap_or(0);
            format!("{}-{}", metadata.len(), mtime)
        }
        Err(_) => "0-0".to_string(),
    }
}

fn now_epoch_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_millis() as u64)
        .unwrap_or(0)
}

fn build_snippet(lines: &[&str], index: usize, context: usize, max_chars: usize) -> (String, bool) {
    let start = index.saturating_sub(context);
    let end = (index + context + 1).min(lines.len());
    let joined = lines[start..end].join("\n");
    if joined.chars().count() <= max_chars {
        return (joined, false);
    }

    let mut shortened = joined.chars().take(max_chars).collect::<String>();
    shortened.push_str("...");
    (shortened, true)
}

fn to_rel_project_path(root: &Path, path: &Path) -> String {
    match path.strip_prefix(root) {
        Ok(rel) => rel.to_string_lossy().replace('\\', "/"),
        Err(_) => path.to_string_lossy().replace('\\', "/"),
    }
}

#[cfg(test)]
mod tests {
    use super::maybe_execute_local_tool;
    use serde_json::json;
    use std::path::Path;
    use std::sync::{Mutex, OnceLock};

    fn env_lock() -> &'static Mutex<()> {
        static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
        LOCK.get_or_init(|| Mutex::new(()))
    }

    fn write_file(path: &Path, content: &str) {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).expect("parent directory should be created");
        }
        std::fs::write(path, content).expect("file should be written");
    }

    #[test]
    fn read_returns_file_slice() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Test.cs"),
            "line1\nline2\nline3\nline4\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let value = maybe_execute_local_tool(
            "read",
            &json!({"path":"Assets/Scripts/Test.cs","startLine":2,"maxLines":2}),
        )
        .expect("tool should be handled")
        .expect("read should succeed");

        assert_eq!(value["startLine"], 2);
        assert_eq!(value["endLine"], 3);
        assert_eq!(value["lineCount"], 2);

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn search_finds_matching_lines() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Test.cs"),
            "public class Foo {}\npublic class Bar {}\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let value = maybe_execute_local_tool("search", &json!({"pattern":"class","limit":10}))
            .expect("tool should be handled")
            .expect("search should succeed");

        assert_eq!(value["count"], 2);
        assert_eq!(value["truncated"], false);

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn list_packages_lists_package_dirs() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        std::fs::create_dir_all(tmp.path().join("Packages/com.test.a"))
            .expect("package dir should be created");
        std::fs::create_dir_all(tmp.path().join("Packages/com.test.b"))
            .expect("package dir should be created");
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let value = maybe_execute_local_tool("list_packages", &json!({}))
            .expect("tool should be handled")
            .expect("list_packages should succeed");

        assert_eq!(value["count"], 2);

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn get_symbols_extracts_class_and_method() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Player.cs"),
            "namespace Demo {\n  public class Player {\n    public int Health;\n    public void Jump() {}\n  }\n}\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let value =
            maybe_execute_local_tool("get_symbols", &json!({"path":"Assets/Scripts/Player.cs"}))
                .expect("tool should be handled")
                .expect("get_symbols should succeed");

        let symbols = value["symbols"]
            .as_array()
            .expect("symbols should be an array");
        assert!(symbols
            .iter()
            .any(|symbol| symbol["name"] == "Player" && symbol["kind"] == "class"));
        assert!(symbols
            .iter()
            .any(|symbol| symbol["name"] == "Jump" && symbol["kind"] == "method"));

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn find_symbol_returns_index_not_ready_before_build() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Player.cs"),
            "public class Player {}\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let value = maybe_execute_local_tool("find_symbol", &json!({"name":"Player"}))
            .expect("tool should be handled")
            .expect("find_symbol should return structured response");

        assert_eq!(value["success"], false);
        assert_eq!(value["error"], "index_not_ready");

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn build_index_and_find_symbol_roundtrip() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Player.cs"),
            "namespace Demo {\n  public class Player { public void Jump() {} }\n}\n",
        );
        write_file(
            &tmp.path().join("Packages/com.demo/Runtime/Enemy.cs"),
            "namespace Demo {\n  public class Enemy { }\n}\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        let build = maybe_execute_local_tool("build_index", &json!({}))
            .expect("tool should be handled")
            .expect("build_index should succeed");
        assert_eq!(build["indexedFiles"], 2);

        let found = maybe_execute_local_tool(
            "find_symbol",
            &json!({"name":"Player","kind":"class","scope":"assets","exact":true}),
        )
        .expect("tool should be handled")
        .expect("find_symbol should succeed");
        assert_eq!(found["success"], true);
        assert!(found["total"].as_u64().expect("total should be number") >= 1);
        assert!(found["results"]
            .as_array()
            .expect("results should be array")
            .iter()
            .any(|result| result["path"].as_str().unwrap_or("").starts_with("Assets/")));

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn update_index_refreshes_symbol_names() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        let target = tmp.path().join("Assets/Scripts/Player.cs");
        write_file(&target, "public class BeforeName {}\n");
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        maybe_execute_local_tool("build_index", &json!({}))
            .expect("tool should be handled")
            .expect("build_index should succeed");
        write_file(&target, "public class AfterName {}\n");

        let updated = maybe_execute_local_tool(
            "update_index",
            &json!({"paths":["Assets/Scripts/Player.cs"]}),
        )
        .expect("tool should be handled")
        .expect("update_index should succeed");
        assert_eq!(updated["updated"], 1);

        let before =
            maybe_execute_local_tool("find_symbol", &json!({"name":"BeforeName","exact":true}))
                .expect("tool should be handled")
                .expect("find_symbol should succeed");
        assert_eq!(before["total"], 0);

        let after =
            maybe_execute_local_tool("find_symbol", &json!({"name":"AfterName","exact":true}))
                .expect("tool should be handled")
                .expect("find_symbol should succeed");
        assert_eq!(after["success"], true);
        assert!(after["total"].as_u64().expect("total should be number") >= 1);

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }

    #[test]
    fn find_refs_supports_cursor_paging() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let tmp = tempfile::tempdir().expect("temp dir should be created");
        write_file(
            &tmp.path().join("Assets/Scripts/Player.cs"),
            "public class Player {}\n",
        );
        write_file(
            &tmp.path().join("Assets/Scripts/UserA.cs"),
            "public class UserA { private Player _p; }\n",
        );
        write_file(
            &tmp.path().join("Assets/Scripts/UserB.cs"),
            "public class UserB { private Player _p; }\n",
        );
        std::env::set_var("UNITY_PROJECT_ROOT", tmp.path());

        maybe_execute_local_tool("build_index", &json!({}))
            .expect("tool should be handled")
            .expect("build_index should succeed");

        let first_page = maybe_execute_local_tool(
            "find_refs",
            &json!({"name":"Player","pageSize":1,"maxBytes":65536,"maxMatchesPerFile":5}),
        )
        .expect("tool should be handled")
        .expect("find_refs should succeed");

        assert_eq!(first_page["success"], true);
        assert_eq!(first_page["truncated"], true);
        let cursor = first_page["cursor"]
            .as_str()
            .expect("cursor should be present when truncated")
            .to_string();

        let second_page = maybe_execute_local_tool(
            "find_refs",
            &json!({"name":"Player","pageSize":10,"startAfter":cursor}),
        )
        .expect("tool should be handled")
        .expect("find_refs should succeed");
        assert_eq!(second_page["success"], true);
        assert!(
            second_page["total"]
                .as_u64()
                .expect("total should be number")
                >= 1,
            "expected remaining reference results after cursor"
        );

        std::env::remove_var("UNITY_PROJECT_ROOT");
    }
}
