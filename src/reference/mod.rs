#![allow(dead_code)]

pub mod cache;
pub mod diff;
pub mod embed;
pub mod fetcher;
pub mod index;
pub mod search;
pub mod version;

use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, Context, Result};
use serde_json::{json, Value};
use walkdir::WalkDir;

use crate::reference::embed::Embedder;

pub fn maybe_execute_reference_tool(tool_name: &str, params: &Value) -> Option<Result<Value>> {
    match tool_name {
        "reference_fetch" => Some(execute_fetch(params)),
        "reference_status" => Some(execute_status(params)),
        "reference_search" => Some(execute_search(params)),
        "reference_grep" => Some(execute_grep(params)),
        "reference_view" => Some(execute_view(params)),
        "reference_clean" => Some(execute_clean(params)),
        "reference_find_symbol" => Some(execute_find_symbol(params)),
        "reference_diff" => Some(execute_diff(params)),
        "reference_resolve_symbol_at" => Some(execute_resolve_symbol_at(params)),
        "reference_embed_build" => Some(execute_embed_build(params)),
        "reference_embed_search" => Some(execute_embed_search(params)),
        _ => None,
    }
}

fn resolve_version(params: &Value) -> Result<String> {
    if let Some(v) = params.get("version").and_then(Value::as_str) {
        if !v.is_empty() {
            return Ok(v.to_string());
        }
    }
    let project_root = params
        .get("projectRoot")
        .and_then(Value::as_str)
        .map(Path::new)
        .unwrap_or_else(|| Path::new("."));
    version::read_from_project(project_root)
}

fn execute_fetch(params: &Value) -> Result<Value> {
    execute_fetch_from(params, fetcher::UNITY_CS_REFERENCE_URL)
}

fn provenance(meta: Option<&cache::CacheMeta>) -> Value {
    json!({
        "branch": meta.map(|m| &m.branch),
        "sourceRef": meta.and_then(|m| m.source_ref.as_ref()),
        "commitSha": meta.and_then(|m| m.commit_sha.as_ref()),
        "exactMatch": meta.and_then(|m| m.exact_match),
        "selectionReason": meta.and_then(|m| m.selection_reason.as_ref()),
        "sourceUrl": meta.map(|m| &m.source_url),
        "fetchedAt": meta.map(|m| &m.fetched_at),
    })
}

fn execute_fetch_from(params: &Value, source_url: &str) -> Result<Value> {
    let version = resolve_version(params)?;
    let accept_license = params
        .get("acceptLicense")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let force = params
        .get("force")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let dest = cache::version_dir(&version)?;
    if dest.exists() && !force {
        let meta = cache::read_meta(&version).ok();
        let mut result = provenance(meta.as_ref());
        result["ok"] = json!(true);
        result["skipped"] = json!(true);
        result["reason"] = json!("destination already exists; returning stored provenance, pass force=true to refetch a different ref");
        result["version"] = json!(version);
        result["path"] = json!(dest.display().to_string());
        return Ok(result);
    }
    fetcher::require_license_accepted(accept_license)?;
    let refs = fetcher::list_public_refs(source_url)?;
    let explicit = params
        .get("branch")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty());
    let selected = version::select_ref(&version, explicit, &refs)?;
    let branch = selected
        .source_ref
        .strip_prefix("refs/heads/")
        .or_else(|| selected.source_ref.strip_prefix("refs/tags/"))
        .unwrap_or(&selected.source_ref)
        .to_string();
    let meta = cache::CacheMeta {
        version: version.clone(),
        branch,
        commit_sha: Some(selected.commit_sha.clone()),
        source_ref: Some(selected.source_ref.clone()),
        exact_match: Some(selected.exact_match),
        selection_reason: Some(selected.selection_reason.clone()),
        fetched_at: now_unix_seconds_string(),
        source_url: source_url.to_string(),
    };
    // Prepare the entire replacement before touching a previously usable cache.
    let parent = cache::reference_root()?
        .parent()
        .context("cache root has no parent")?
        .to_path_buf();
    std::fs::create_dir_all(&parent)?;
    let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
    let staging = parent.join(format!(".reference-fetch-{}-{nonce}", std::process::id()));
    let backup = parent.join(format!(".reference-backup-{}-{nonce}", std::process::id()));
    let result = (|| -> Result<()> {
        fetcher::fetch_selected(source_url, &selected, &staging, accept_license)?;
        std::fs::write(
            staging.join(".unity-cli-meta.json"),
            serde_json::to_vec_pretty(&meta)?,
        )?;
        std::fs::create_dir_all(dest.parent().context("cache destination has no parent")?)?;
        let replacing = dest.exists();
        if replacing {
            std::fs::rename(&dest, &backup)?;
        }
        if let Err(error) = std::fs::rename(&staging, &dest) {
            if replacing {
                std::fs::rename(&backup, &dest)
                    .context("failed to restore previous reference cache")?;
            }
            return Err(error.into());
        }
        if replacing {
            std::fs::remove_dir_all(&backup)?;
        }
        Ok(())
    })();
    if staging.exists() {
        let _ = std::fs::remove_dir_all(&staging);
    }
    result?;
    let mut result = provenance(Some(&meta));
    result["ok"] = json!(true);
    result["version"] = json!(version);
    result["path"] = json!(dest.display().to_string());
    Ok(result)
}

fn execute_status(_params: &Value) -> Result<Value> {
    let versions = cache::list_versions()?;
    let mut entries = Vec::new();
    for v in versions {
        let dir = cache::version_dir(&v)?;
        let size_bytes = dir_size(&dir).unwrap_or(0);
        let meta = cache::read_meta(&v).ok();
        let mut entry = provenance(meta.as_ref());
        entry["version"] = json!(v);
        entry["sizeBytes"] = json!(size_bytes);
        entry["path"] = json!(dir.display().to_string());
        entries.push(entry);
    }
    Ok(json!({
        "ok": true,
        "versions": entries,
    }))
}

fn execute_search(params: &Value) -> Result<Value> {
    let pattern = params
        .get("pattern")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("search requires `pattern`"))?;
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    let file_glob = params.get("path").and_then(Value::as_str);
    let context = params.get("context").and_then(Value::as_u64).unwrap_or(0) as u32;
    let max_results = params
        .get("maxResults")
        .and_then(Value::as_u64)
        .map(|n| n as usize);
    let mut hits = search::run_grep(&dir, pattern, file_glob, context)?;
    if let Some(n) = max_results {
        hits.truncate(n);
    }
    Ok(json!({
        "ok": true,
        "version": version,
        "hits": hits,
    }))
}

fn execute_grep(params: &Value) -> Result<Value> {
    let pattern = params
        .get("pattern")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("grep requires `pattern`"))?;
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    let file_glob = params.get("fileGlob").and_then(Value::as_str);
    let context = params.get("context").and_then(Value::as_u64).unwrap_or(0) as u32;
    let hits = search::run_grep(&dir, pattern, file_glob, context)?;
    Ok(json!({
        "ok": true,
        "version": version,
        "hits": hits,
    }))
}

fn execute_view(params: &Value) -> Result<Value> {
    let path = params
        .get("path")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("view requires `path`"))?;
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    let start_line = params
        .get("startLine")
        .and_then(Value::as_u64)
        .map(|n| n as u32);
    let max_lines = params
        .get("maxLines")
        .and_then(Value::as_u64)
        .map(|n| n as u32);
    let out = search::run_view(&dir, path, start_line, max_lines)?;
    Ok(json!({
        "ok": true,
        "version": version,
        "view": out,
    }))
}

fn execute_find_symbol(params: &Value) -> Result<Value> {
    let name = params
        .get("name")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("find_symbol requires `name`"))?;
    let kind = params.get("kind").and_then(Value::as_str);
    if let Some(k) = kind {
        index::validate_kind(k)?;
    }
    let namespace = params.get("namespace").and_then(Value::as_str);
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    if !dir.exists() {
        return Err(anyhow!(
            "reference cache for version '{}' does not exist; run `unity-cli reference fetch` first",
            version
        ));
    }
    let symbol_index = index::build_or_update_index(&dir)?;
    let hits = index::find_symbol(&symbol_index, name, kind, namespace);
    Ok(json!({
        "ok": true,
        "version": version,
        "hits": hits,
    }))
}

fn execute_diff(params: &Value) -> Result<Value> {
    let from = params
        .get("from")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .ok_or_else(|| anyhow!("diff requires `from`"))?;
    let to = params
        .get("to")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .ok_or_else(|| anyhow!("diff requires `to`"))?;
    let symbol = params
        .get("symbol")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty());
    let path_filter = params
        .get("path")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty());
    let max_symbols = params
        .get("maxSymbols")
        .and_then(Value::as_u64)
        .map(|n| n as usize);
    if symbol.is_none() && path_filter.is_none() {
        return Err(anyhow!("diff requires either `symbol` or `path`"));
    }
    let from_dir = cache::version_dir(from)?;
    let to_dir = cache::version_dir(to)?;
    diff::ensure_cache_dir(&from_dir, from)?;
    diff::ensure_cache_dir(&to_dir, to)?;

    if let Some(fqn) = symbol {
        let diff_value = diff::compute_symbol_diff(&from_dir, &to_dir, fqn)?;
        return Ok(json!({
            "ok": true,
            "from": from,
            "to": to,
            "diffs": diff_value.map(|d| vec![d]).unwrap_or_default(),
        }));
    }
    let path_diff = diff::compute_path_diff(&from_dir, &to_dir, path_filter, max_symbols)?;
    Ok(json!({
        "ok": true,
        "from": from,
        "to": to,
        "path": path_filter,
        "added": path_diff.added,
        "removed": path_diff.removed,
        "changed": path_diff.changed,
        "truncated": path_diff.truncated,
    }))
}

fn execute_resolve_symbol_at(params: &Value) -> Result<Value> {
    let path = params
        .get("path")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("resolve_symbol_at requires `path`"))?;
    let line = params
        .get("line")
        .and_then(Value::as_u64)
        .ok_or_else(|| anyhow!("resolve_symbol_at requires `line`"))? as u32;
    let column = params
        .get("column")
        .and_then(Value::as_u64)
        .ok_or_else(|| anyhow!("resolve_symbol_at requires `column`"))? as u32;
    let version_hint = params
        .get("version")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty());
    let project_root = params
        .get("projectRoot")
        .and_then(Value::as_str)
        .map(std::path::Path::new);

    if !path.starts_with("Assets/") && !path.starts_with("Packages/") {
        return Err(anyhow!(
            "project path must start with Assets/ or Packages/: {path}"
        ));
    }

    let abs_path = match project_root {
        Some(root) => root.join(path),
        None => std::path::PathBuf::from(path),
    };
    let contents = std::fs::read_to_string(&abs_path)
        .with_context(|| format!("failed to read project file {}", abs_path.display()))?;
    let token = extract_token_at_cursor(&contents, line, column);
    let candidates = match &token {
        Some(name) => collect_resolve_candidates(name, version_hint)?,
        None => Vec::new(),
    };
    Ok(json!({
        "ok": true,
        "cursorPath": path,
        "cursorLine": line,
        "cursorColumn": column,
        "tokenName": token,
        "candidates": candidates,
    }))
}

fn extract_token_at_cursor(content: &str, line: u32, column: u32) -> Option<String> {
    let lines: Vec<&str> = content.lines().collect();
    let line_idx = line.saturating_sub(1) as usize;
    let row = lines.get(line_idx)?;
    let chars: Vec<char> = row.chars().collect();
    let col_idx = column.saturating_sub(1) as usize;
    if col_idx >= chars.len() || !is_ident_char(chars[col_idx]) {
        return None;
    }
    if is_cursor_in_comment_or_string(&chars, col_idx) {
        return None;
    }
    let mut start = col_idx;
    while start > 0 && is_ident_char(chars[start - 1]) {
        start -= 1;
    }
    let mut end = col_idx + 1;
    while end < chars.len() && is_ident_char(chars[end]) {
        end += 1;
    }
    Some(chars[start..end].iter().collect())
}

fn is_cursor_in_comment_or_string(chars: &[char], col_idx: usize) -> bool {
    let mut in_string = false;
    let mut i = 0;
    while i < col_idx {
        let c = chars[i];
        if in_string {
            if c == '\\' && i + 1 < chars.len() {
                i += 2;
                continue;
            }
            if c == '"' {
                in_string = false;
            }
        } else if c == '"' {
            in_string = true;
        } else if c == '/' && i + 1 < chars.len() && chars[i + 1] == '/' {
            return true;
        }
        i += 1;
    }
    in_string
}

fn is_ident_char(c: char) -> bool {
    c.is_alphanumeric() || c == '_'
}

const DEFAULT_RESOLVE_VIEW_WINDOW: u32 = 30;

fn collect_resolve_candidates(name: &str, version_hint: Option<&str>) -> Result<Vec<Value>> {
    let versions: Vec<String> = match version_hint {
        Some(v) => vec![v.to_string()],
        None => cache::list_versions().unwrap_or_default(),
    };
    let mut candidates = Vec::new();
    for ver in &versions {
        let dir = cache::version_dir(ver)?;
        if !dir.exists() {
            continue;
        }
        let symbol_index = index::build_or_update_index(&dir)?;
        let hits = index::find_symbol(&symbol_index, name, None, None);
        for hit in hits {
            let view_excerpt = search::run_view(
                &dir,
                &hit.path,
                Some(hit.line),
                Some(DEFAULT_RESOLVE_VIEW_WINDOW),
            )
            .map(|v| v.lines)
            .unwrap_or_default();
            candidates.push(json!({
                "version": ver,
                "fqn": hit.fqn.clone().unwrap_or_else(|| hit.name.clone()),
                "kind": hit.kind,
                "referencePath": hit.path,
                "referenceLine": hit.line,
                "viewExcerpt": view_excerpt,
            }));
        }
    }
    Ok(candidates)
}

fn execute_embed_build(params: &Value) -> Result<Value> {
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    if !dir.exists() {
        return Err(anyhow!(
            "reference cache for version '{}' does not exist; run `unity-cli reference fetch --version {}` first",
            version,
            version
        ));
    }
    let symbol_index = index::build_or_update_index(&dir)?;
    let embedder = embed::FastEmbedder::new()?;
    let embedding_index = embed::build_embedding_index(&dir, &embedder, &symbol_index)?;
    let path = dir.join(embed::EMBEDDING_INDEX_REL_PATH);
    embed::save_embedding_index(&path, &embedding_index)?;
    Ok(json!({
        "ok": true,
        "version": version,
        "modelId": embedding_index.model_id,
        "dim": embedding_index.dim,
        "count": embedding_index.items.len(),
        "path": path.display().to_string(),
    }))
}

fn execute_embed_search(params: &Value) -> Result<Value> {
    let query = params
        .get("query")
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .ok_or_else(|| anyhow!("embed_search requires `query`"))?;
    let top_k = params
        .get("topK")
        .and_then(Value::as_u64)
        .map(|n| n as usize)
        .unwrap_or(embed::DEFAULT_TOP_K);
    let version = resolve_version(params)?;
    let dir = cache::version_dir(&version)?;
    let index_path = dir.join(embed::EMBEDDING_INDEX_REL_PATH);
    if !index_path.exists() {
        return Err(anyhow!(
            "embedding index missing for version '{}'; run `unity-cli reference embed-build --version {}` first",
            version,
            version
        ));
    }
    let index = embed::load_embedding_index(&index_path)?;
    let embedder = embed::FastEmbedder::new()?;
    let mut vectors = embedder.embed(&[query.to_string()])?;
    let query_vec = vectors
        .pop()
        .ok_or_else(|| anyhow!("embedder returned no vector for query"))?;
    let hits = embed::search(&index, &query_vec, top_k);
    let hits_json: Vec<Value> = hits
        .into_iter()
        .map(|(sym, score)| {
            json!({
                "symbol": sym.symbol,
                "kind": sym.kind,
                "path": sym.path,
                "line": sym.line,
                "score": score,
            })
        })
        .collect();
    Ok(json!({
        "ok": true,
        "version": version,
        "query": query,
        "modelId": index.model_id,
        "hits": hits_json,
    }))
}

fn execute_clean(params: &Value) -> Result<Value> {
    let keep = params.get("keep").and_then(Value::as_u64).unwrap_or(1) as usize;
    let dry_run = params
        .get("dryRun")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let removed = cache::gc(keep, dry_run)?;
    Ok(json!({
        "ok": true,
        "keep": keep,
        "dryRun": dry_run,
        "removed": removed
            .iter()
            .map(|p| p.display().to_string())
            .collect::<Vec<_>>(),
    }))
}

fn now_unix_seconds_string() -> String {
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    format!("{secs}")
}

fn dir_size(path: &Path) -> Result<u64> {
    let mut total = 0u64;
    for entry in WalkDir::new(path)
        .follow_links(false)
        .into_iter()
        .filter_map(|e| e.ok())
    {
        if entry.file_type().is_file() {
            if let Ok(meta) = entry.metadata() {
                total = total.saturating_add(meta.len());
            }
        }
    }
    Ok(total)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::env;
    use std::path::PathBuf;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = env::var(key).ok();
            env::set_var(key, value);
            Self { key, previous }
        }
    }

    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(value) = &self.previous {
                env::set_var(self.key, value);
            } else {
                env::remove_var(self.key);
            }
        }
    }

    fn unique_temp_path(label: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or(Duration::from_secs(0))
            .as_nanos();
        env::temp_dir().join(format!("unity-cli-reference-mod-{label}-{nanos}"))
    }

    #[test]
    fn maybe_execute_returns_none_for_unknown_tool() {
        assert!(maybe_execute_reference_tool("nonexistent", &json!({})).is_none());
    }

    #[test]
    fn execute_clean_dry_run_on_empty_cache_returns_empty_removed() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("clean");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value =
            maybe_execute_reference_tool("reference_clean", &json!({"dryRun": true, "keep": 1}))
                .unwrap()
                .unwrap();
        assert_eq!(value["ok"], true);
        assert_eq!(value["dryRun"], true);
        assert!(value["removed"].is_array());
        assert_eq!(value["removed"].as_array().unwrap().len(), 0);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_status_returns_versions_list() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("status");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        std::fs::create_dir_all(base.join("2023.2.20f1")).unwrap();
        let value = maybe_execute_reference_tool("reference_status", &json!({}))
            .unwrap()
            .unwrap();
        assert_eq!(value["ok"], true);
        let versions = value["versions"].as_array().unwrap();
        assert_eq!(versions.len(), 1);
        assert_eq!(versions[0]["version"], "2023.2.20f1");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn resolve_version_uses_explicit_param() {
        let v = resolve_version(&json!({"version": "2023.2.20f1"})).unwrap();
        assert_eq!(v, "2023.2.20f1");
    }

    #[test]
    fn resolve_version_falls_back_to_project_detection() {
        let tmp = unique_temp_path("resolve-version");
        let settings = tmp.join("ProjectSettings");
        std::fs::create_dir_all(&settings).unwrap();
        std::fs::write(
            settings.join("ProjectVersion.txt"),
            "m_EditorVersion: 2023.2.20f1\n",
        )
        .unwrap();
        let v = resolve_version(&json!({"projectRoot": tmp.to_str().unwrap()})).unwrap();
        assert_eq!(v, "2023.2.20f1");
        let _ = std::fs::remove_dir_all(&tmp);
    }

    #[test]
    fn now_unix_seconds_string_is_decimal() {
        let s = now_unix_seconds_string();
        assert!(!s.is_empty());
        assert!(s.chars().all(|c| c.is_ascii_digit()));
    }

    #[test]
    fn dir_size_empty_returns_zero() {
        let tmp = unique_temp_path("dirsize-empty");
        std::fs::create_dir_all(&tmp).unwrap();
        assert_eq!(dir_size(&tmp).unwrap(), 0);
        let _ = std::fs::remove_dir_all(&tmp);
    }

    #[test]
    fn dir_size_sums_file_lengths() {
        let tmp = unique_temp_path("dirsize-files");
        let sub = tmp.join("a");
        std::fs::create_dir_all(&sub).unwrap();
        std::fs::write(tmp.join("a.txt"), b"hello").unwrap();
        std::fs::write(sub.join("b.txt"), b"world!!").unwrap();
        assert_eq!(dir_size(&tmp).unwrap(), 5 + 7);
        let _ = std::fs::remove_dir_all(&tmp);
    }

    fn copy_dir_recursive(src: &std::path::Path, dst: &std::path::Path) -> std::io::Result<()> {
        std::fs::create_dir_all(dst)?;
        for entry in std::fs::read_dir(src)? {
            let entry = entry?;
            let file_type = entry.file_type()?;
            let src_path = entry.path();
            let dst_path = dst.join(entry.file_name());
            if file_type.is_dir() {
                copy_dir_recursive(&src_path, &dst_path)?;
            } else if file_type.is_file() {
                std::fs::copy(&src_path, &dst_path)?;
            }
        }
        Ok(())
    }

    fn setup_cache_with_fixture(label: &str) -> (PathBuf, &'static str) {
        let root = unique_temp_path(label);
        let version = "fixture-version";
        let dest = root.join("UnityCsReference").join(version);
        let fixture =
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/reference-cache");
        copy_dir_recursive(&fixture, &dest).unwrap();
        (root, version)
    }

    #[test]
    fn execute_grep_via_dispatcher_returns_hits() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("grep-disp");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_grep",
            &json!({"pattern": "class Animator", "version": version}),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        let hits = value["hits"].as_array().unwrap();
        assert!(!hits.is_empty());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_view_via_dispatcher_returns_lines() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("view-disp");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_view",
            &json!({
                "path": "Runtime/Export/Animation/Animator.bindings.cs",
                "version": version,
                "startLine": 1,
                "maxLines": 3,
            }),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        assert_eq!(value["view"]["lines"].as_array().unwrap().len(), 3);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_search_via_dispatcher_truncates_max_results() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("search-disp");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_search",
            &json!({
                "pattern": "class",
                "version": version,
                "maxResults": 1,
            }),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        assert_eq!(value["hits"].as_array().unwrap().len(), 1);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_status_includes_size_bytes() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("status-size");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool("reference_status", &json!({}))
            .unwrap()
            .unwrap();
        let versions = value["versions"].as_array().unwrap();
        assert_eq!(versions.len(), 1);
        assert_eq!(versions[0]["version"], version);
        assert!(versions[0]["sizeBytes"].as_u64().unwrap() > 0);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_fetch_skips_when_destination_exists_without_force() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("fetch-skip");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_fetch",
            &json!({
                "version": version,
                "branch": "fixture/branch",
                "acceptLicense": true,
            }),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        assert_eq!(value["skipped"], true);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn cached_fetch_and_status_report_stored_provenance() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("provenance");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let meta = json!({
            "version": version, "branch": "6000.4", "commit_sha": "a".repeat(40),
            "source_ref": "refs/heads/6000.4", "exact_match": false,
            "selection_reason": "same minor branch; not an exact version match",
            "fetched_at": "123", "source_url": fetcher::UNITY_CS_REFERENCE_URL
        });
        std::fs::write(
            cache::version_dir(version)
                .unwrap()
                .join(".unity-cli-meta.json"),
            meta.to_string(),
        )
        .unwrap();
        let fetched = execute_fetch(&json!({"version":version,"branch":"different"})).unwrap();
        assert_eq!(fetched["branch"], "6000.4");
        assert_eq!(fetched["exactMatch"], false);
        assert_eq!(fetched["commitSha"], "a".repeat(40));
        assert_eq!(fetched["sourceRef"], "refs/heads/6000.4");
        let status = execute_status(&json!({})).unwrap();
        for field in [
            "branch",
            "exactMatch",
            "commitSha",
            "sourceRef",
            "selectionReason",
        ] {
            assert_eq!(status["versions"][0][field], fetched[field]);
        }
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn cached_unknown_version_can_be_used_without_branch_resolution() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("ProjectSettings")).unwrap();
        std::fs::write(
            tmp.path().join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.4.12f1\n",
        )
        .unwrap();
        assert_eq!(
            resolve_version(&json!({"projectRoot":tmp.path()})).unwrap(),
            "6000.4.12f1"
        );
    }

    #[test]
    fn fetch_resolves_project_and_explicit_refs_and_records_actual_commit() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let tmp = tempfile::tempdir().unwrap();
        let _env = EnvVarGuard::set(
            "UNITY_CLI_CACHE_ROOT",
            tmp.path().join("cache").to_str().unwrap(),
        );
        let repo = tmp.path().join("source");
        std::fs::create_dir_all(&repo).unwrap();
        let git = |args: &[&str]| {
            let result = std::process::Command::new("git")
                .arg("-C")
                .arg(&repo)
                .args(args)
                .output()
                .unwrap();
            assert!(
                result.status.success(),
                "{}",
                String::from_utf8_lossy(&result.stderr)
            );
            String::from_utf8(result.stdout).unwrap().trim().to_string()
        };
        git(&["init"]);
        std::fs::write(repo.join("Fixture.cs"), "class Fixture {}\n").unwrap();
        git(&["add", "."]);
        git(&[
            "-c",
            "user.name=Fixture",
            "-c",
            "user.email=fixture@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-m",
            "fixture",
        ]);
        git(&["branch", "6000.4"]);
        git(&["tag", "6000.4.12f1"]);
        let sha = git(&["rev-parse", "HEAD"]);
        let project = tmp.path().join("project");
        std::fs::create_dir_all(project.join("ProjectSettings")).unwrap();
        std::fs::write(
            project.join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.4.12f1\n",
        )
        .unwrap();
        let params = json!({"projectRoot":project,"acceptLicense":true});
        let exact = execute_fetch_from(&params, repo.to_str().unwrap()).unwrap();
        assert_eq!(exact["sourceRef"], "refs/tags/6000.4.12f1");
        assert_eq!(exact["exactMatch"], true);
        assert_eq!(exact["commitSha"], sha);
        let mut explicit = params;
        explicit["branch"] = json!("6000.4");
        explicit["force"] = json!(true);
        let fetched = execute_fetch_from(&explicit, repo.to_str().unwrap()).unwrap();
        assert_eq!(fetched["sourceRef"], "refs/heads/6000.4");
        assert_eq!(fetched["exactMatch"], false);
        let meta = cache::read_meta("6000.4.12f1").unwrap();
        assert_eq!(meta.commit_sha.as_deref(), Some(sha.as_str()));
        assert_eq!(meta.exact_match, Some(false));
        assert!(meta.selection_reason.unwrap().contains("explicit"));
        // Explicit version works without any readable project settings.
        let fetched = execute_fetch_from(
            &json!({"version":"6000.4.99f1","projectRoot":"/missing","acceptLicense":true}),
            repo.to_str().unwrap(),
        )
        .unwrap();
        assert_eq!(fetched["sourceRef"], "refs/heads/6000.4");
        assert_eq!(fetched["exactMatch"], false);
        let error = execute_fetch_from(
            &json!({"version":"9999.9.0f1","acceptLicense":true}),
            repo.to_str().unwrap(),
        )
        .unwrap_err();
        assert!(error.to_string().contains("available branches"));
        assert!(!cache::version_dir("9999.9.0f1").unwrap().exists());
    }

    #[test]
    fn legacy_metadata_keeps_unknown_match_instead_of_claiming_exact() {
        let meta: cache::CacheMeta = serde_json::from_value(json!({
            "version":"2022.3.0f1","branch":"2022.3/staging",
            "fetched_at":"123","source_url":"https://example.invalid"
        }))
        .unwrap();
        let value = provenance(Some(&meta));
        assert_eq!(value["branch"], "2022.3/staging");
        assert!(value["commitSha"].is_null());
        assert!(value["exactMatch"].is_null());
        assert!(value["sourceRef"].is_null());
    }

    #[test]
    fn execute_view_rejects_parent_traversal_via_dispatcher() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("view-trav");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = maybe_execute_reference_tool(
            "reference_view",
            &json!({"path": "../escape.cs", "version": version}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains(".."));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_view_requires_path_param() {
        let err = maybe_execute_reference_tool("reference_view", &json!({"version": "any"}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("path"));
    }

    #[test]
    fn execute_search_requires_pattern() {
        let err = maybe_execute_reference_tool("reference_search", &json!({"version": "any"}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("pattern"));
    }

    #[test]
    fn execute_grep_requires_pattern() {
        let err = maybe_execute_reference_tool("reference_grep", &json!({"version": "any"}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("pattern"));
    }

    #[test]
    fn execute_grep_invalid_regex_returns_error() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("grep-bad");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = maybe_execute_reference_tool(
            "reference_grep",
            &json!({"pattern": "(unclosed", "version": version}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("regex"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_search_without_max_results_returns_all_hits() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("search-full");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_search",
            &json!({
                "pattern": "class",
                "version": version,
                "context": 1,
            }),
        )
        .unwrap()
        .unwrap();
        assert!(value["hits"].as_array().unwrap().len() >= 2);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_grep_supports_file_glob_filter() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("grep-glob");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_grep",
            &json!({
                "pattern": "class",
                "version": version,
                "fileGlob": "*.cs",
            }),
        )
        .unwrap()
        .unwrap();
        assert!(!value["hits"].as_array().unwrap().is_empty());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_clean_dry_run_false_actually_removes() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("clean-actual");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        for v in &["v1", "v2"] {
            std::fs::create_dir_all(base.join(v)).unwrap();
        }
        let value =
            maybe_execute_reference_tool("reference_clean", &json!({"keep": 1, "dryRun": false}))
                .unwrap()
                .unwrap();
        assert_eq!(value["dryRun"], false);
        assert_eq!(value["removed"].as_array().unwrap().len(), 1);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_fetch_force_preserves_existing_on_transport_failure() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("fetch-force");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let dest = root.join("UnityCsReference").join(version);
        assert!(dest.exists());
        let result = execute_fetch_from(
            &json!({
                "version": version,
                "branch": "definitely-not-a-real-branch-xyz",
                "force": true,
                "acceptLicense": true,
            }),
            "unsupported-test-protocol://unavailable/repo",
        );
        assert!(format!("{:#}", result.unwrap_err()).contains("ls-remote"));
        assert!(
            dest.exists(),
            "force must preserve the previous cache on failure"
        );
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_find_symbol_requires_name() {
        let err = maybe_execute_reference_tool("reference_find_symbol", &json!({}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("name"));
    }

    #[test]
    fn execute_find_symbol_rejects_unknown_kind() {
        let err = maybe_execute_reference_tool(
            "reference_find_symbol",
            &json!({"name": "Foo", "kind": "alien"}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("not allowed"));
    }

    #[test]
    fn execute_find_symbol_returns_error_when_cache_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("find-missing");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = maybe_execute_reference_tool(
            "reference_find_symbol",
            &json!({"name": "Foo", "version": "missing"}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("does not exist"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_find_symbol_returns_hits_from_fixture() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("find-hits");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_find_symbol",
            &json!({"name": "Animator", "kind": "class", "version": version}),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        let hits = value["hits"].as_array().unwrap();
        assert!(!hits.is_empty(), "Animator class should be discovered");
        assert!(hits.iter().all(|h| h["kind"] == "class"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_find_symbol_filters_namespace() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("find-ns");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_find_symbol",
            &json!({
                "name": "AnimatorInspector",
                "kind": "class",
                "namespace": "UnityEditor",
                "version": version,
            }),
        )
        .unwrap()
        .unwrap();
        let hits = value["hits"].as_array().unwrap();
        assert!(!hits.is_empty());
        assert_eq!(hits[0]["namespace"], "UnityEditor");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_find_symbol_empty_hits_for_unknown_name() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, version) = setup_cache_with_fixture("find-unknown");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_find_symbol",
            &json!({"name": "Nonexistent", "version": version}),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        assert!(value["hits"].as_array().unwrap().is_empty());
        let _ = std::fs::remove_dir_all(&root);
    }

    fn setup_two_version_cache(label: &str) -> (PathBuf, &'static str, &'static str) {
        let root = unique_temp_path(label);
        let base = root.join("UnityCsReference");
        let fixture_v1 =
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/reference-cache-v2/v1");
        let fixture_v2 =
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/reference-cache-v2/v2");
        copy_dir_recursive(&fixture_v1, &base.join("v1")).unwrap();
        copy_dir_recursive(&fixture_v2, &base.join("v2")).unwrap();
        (root, "v1", "v2")
    }

    #[test]
    fn execute_diff_requires_from_and_to() {
        let err1 = maybe_execute_reference_tool("reference_diff", &json!({"to": "v2"}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err1:#}").contains("from"));
        let err2 = maybe_execute_reference_tool("reference_diff", &json!({"from": "v1"}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err2:#}").contains("to"));
    }

    #[test]
    fn execute_diff_requires_symbol_or_path() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, from, to) = setup_two_version_cache("diff-need");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = maybe_execute_reference_tool("reference_diff", &json!({"from": from, "to": to}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("symbol"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_diff_symbol_mode_returns_diffs() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, from, to) = setup_two_version_cache("diff-sym");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_diff",
            &json!({"from": from, "to": to, "symbol": "UnityEngine.Animator"}),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        let diffs = value["diffs"].as_array().unwrap();
        assert!(!diffs.is_empty());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_diff_path_mode_returns_added_removed_changed() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, from, to) = setup_two_version_cache("diff-path");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let value = maybe_execute_reference_tool(
            "reference_diff",
            &json!({"from": from, "to": to, "path": "Runtime/Export"}),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["ok"], true);
        let added = value["added"].as_array().unwrap();
        let removed = value["removed"].as_array().unwrap();
        let changed = value["changed"].as_array().unwrap();
        assert!(added
            .iter()
            .any(|s| s["symbol"].as_str().unwrap_or("").ends_with("Awaitable")));
        assert!(removed.iter().any(|s| s["symbol"]
            .as_str()
            .unwrap_or("")
            .ends_with("LegacyAnimator")));
        assert!(!changed.is_empty());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_diff_errors_when_cache_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("diff-miss");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = maybe_execute_reference_tool(
            "reference_diff",
            &json!({"from": "missing-v1", "to": "missing-v2", "symbol": "Foo"}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("does not exist"));
    }

    #[test]
    fn execute_resolve_symbol_at_requires_project_prefix() {
        let err = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({"path": "/tmp/bad.cs", "line": 1, "column": 1}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("Assets/"));
    }

    #[test]
    fn execute_resolve_symbol_at_requires_path_line_column() {
        let e1 = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({"line": 1, "column": 1}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{e1:#}").contains("path"));
        let e2 = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({"path": "Assets/X.cs", "column": 1}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{e2:#}").contains("line"));
        let e3 = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({"path": "Assets/X.cs", "line": 1}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{e3:#}").contains("column"));
    }

    #[test]
    fn execute_resolve_symbol_at_finds_token_via_fixture() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let (root, _from, to) = setup_two_version_cache("resolve-token");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        // Create a fake project file
        let project_root = unique_temp_path("resolve-project");
        std::fs::create_dir_all(project_root.join("Assets/Scripts")).unwrap();
        std::fs::write(
            project_root.join("Assets/Scripts/Player.cs"),
            "public class Player {\n    void Update() {\n        var a = new Animator();\n    }\n}\n",
        )
        .unwrap();
        let value = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({
                "path": "Assets/Scripts/Player.cs",
                "line": 3,
                "column": 21,
                "projectRoot": project_root.to_str().unwrap(),
                "version": to,
            }),
        )
        .unwrap()
        .unwrap();
        assert_eq!(value["tokenName"], "Animator");
        let candidates = value["candidates"].as_array().unwrap();
        assert!(
            !candidates.is_empty(),
            "Animator should resolve to v2 candidate"
        );
        let _ = std::fs::remove_dir_all(&root);
        let _ = std::fs::remove_dir_all(&project_root);
    }

    #[test]
    fn execute_resolve_symbol_at_empty_token_for_blank_position() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let project_root = unique_temp_path("resolve-blank");
        std::fs::create_dir_all(project_root.join("Assets")).unwrap();
        std::fs::write(project_root.join("Assets/Foo.cs"), "// comment line\n").unwrap();
        let value = maybe_execute_reference_tool(
            "reference_resolve_symbol_at",
            &json!({
                "path": "Assets/Foo.cs",
                "line": 1,
                "column": 1,
                "projectRoot": project_root.to_str().unwrap(),
            }),
        )
        .unwrap()
        .unwrap();
        assert!(value["tokenName"].is_null() || value["tokenName"] == "");
        assert!(value["candidates"].as_array().unwrap().is_empty());
        let _ = std::fs::remove_dir_all(&project_root);
    }

    #[test]
    fn extract_token_at_cursor_handles_edge_cases() {
        let content = "var animator = new Animator();\n";
        assert_eq!(
            extract_token_at_cursor(content, 1, 5),
            Some("animator".to_string())
        );
        assert_eq!(
            extract_token_at_cursor(content, 1, 20),
            Some("Animator".to_string())
        );
        // Whitespace position returns None
        assert_eq!(extract_token_at_cursor(content, 1, 4), None);
        // Out-of-range line returns None
        assert_eq!(extract_token_at_cursor(content, 99, 1), None);
    }

    #[test]
    fn extract_token_at_cursor_skips_line_comment() {
        let content = "var x = 1; // Animator was here\n";
        // 'A' of Animator inside comment is at column 15
        assert_eq!(extract_token_at_cursor(content, 1, 15), None);
    }

    #[test]
    fn extract_token_at_cursor_skips_string_literal() {
        let content = "var s = \"Animator\"; var y = 1;\n";
        // 'A' of Animator inside string is at column 10
        assert_eq!(extract_token_at_cursor(content, 1, 10), None);
    }

    #[test]
    fn extract_token_at_cursor_returns_ident_after_closed_string() {
        let content = "var s = \"abc\"; Animator a;\n";
        // 'A' of Animator after closed string is at column 16
        assert_eq!(
            extract_token_at_cursor(content, 1, 16),
            Some("Animator".to_string())
        );
    }

    #[test]
    fn extract_token_at_cursor_handles_escaped_quote_in_string() {
        let content = "var s = \"a\\\"b\"; Animator a;\n";
        // After the escaped string closes at \"; the cursor sits on the A of Animator (column 17)
        assert_eq!(
            extract_token_at_cursor(content, 1, 17),
            Some("Animator".to_string())
        );
    }

    #[test]
    fn execute_embed_search_requires_query() {
        let err = maybe_execute_reference_tool("reference_embed_search", &json!({}))
            .unwrap()
            .unwrap_err();
        assert!(format!("{err:#}").contains("query"));
    }

    #[test]
    fn execute_embed_search_errors_when_index_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("embed-search-missing");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        std::fs::create_dir_all(base.join("v1")).unwrap();
        let err = maybe_execute_reference_tool(
            "reference_embed_search",
            &json!({"query": "animator", "version": "v1"}),
        )
        .unwrap()
        .unwrap_err();
        assert!(format!("{err:#}").contains("embedding index missing"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn execute_embed_build_errors_when_cache_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("embed-build-missing");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err =
            maybe_execute_reference_tool("reference_embed_build", &json!({"version": "not-there"}))
                .unwrap()
                .unwrap_err();
        assert!(format!("{err:#}").contains("does not exist"));
    }
}
