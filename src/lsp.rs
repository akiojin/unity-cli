use std::collections::BTreeMap;
use std::env;
use std::io::{BufReader, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::mpsc::{self, Receiver};
use std::sync::{Mutex, OnceLock};
use std::thread;
use std::time::{Duration, Instant};

use anyhow::{anyhow, Context, Result};
use serde_json::{json, Value};

use crate::lsp_manager;
use crate::lspd;

const MAX_HEADER_BYTES: usize = 16 * 1024;
const DEFAULT_TIMEOUT_MS: u64 = 60_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum LspMode {
    Off,
    Auto,
    Required,
}

#[derive(Debug)]
struct LspCommand {
    program: String,
    args: Vec<String>,
}

struct CachedSession {
    project_root: PathBuf,
    session: LspSession,
}

fn session_cache() -> &'static Mutex<Option<CachedSession>> {
    static CACHE: OnceLock<Mutex<Option<CachedSession>>> = OnceLock::new();
    CACHE.get_or_init(|| Mutex::new(None))
}

pub fn maybe_execute(
    tool_name: &str,
    params: &Value,
    project_root: &Path,
) -> Option<Result<Value>> {
    if !matches!(
        tool_name,
        "get_symbols"
            | "find_symbol"
            | "find_refs"
            | "build_index"
            | "rename_symbol"
            | "replace_symbol_body"
            | "insert_before_symbol"
            | "insert_after_symbol"
            | "remove_symbol"
            | "validate_text_edits"
    ) {
        return None;
    }

    let mode = lsp_mode();
    if mode == LspMode::Off {
        return None;
    }

    let result = execute_via_daemon(tool_name, params, project_root);
    match (mode, result) {
        (_, Ok(value)) => Some(Ok(value)),
        (LspMode::Required, Err(error)) => Some(Err(error)),
        (LspMode::Auto, Err(_)) => None,
        (LspMode::Off, _) => None,
    }
}

fn execute_via_daemon(tool_name: &str, params: &Value, project_root: &Path) -> Result<Value> {
    let _ = lsp_manager::ensure_local(false)?;

    match lspd::call_tool(tool_name, params, project_root) {
        Ok(value) => Ok(value),
        Err(first_error) if is_daemon_transport_error(&first_error) => {
            let _ = lspd::start_background()?;
            lspd::call_tool(tool_name, params, project_root)
        }
        Err(error) => Err(error),
    }
}

fn is_daemon_transport_error(error: &anyhow::Error) -> bool {
    let text = error.to_string().to_ascii_lowercase();
    [
        "failed to connect",
        "connection refused",
        "no such file or directory",
        "returned empty response",
        "broken pipe",
        "timed out",
    ]
    .iter()
    .any(|needle| text.contains(needle))
}

pub fn execute_direct(tool_name: &str, params: &Value, project_root: &Path) -> Result<Value> {
    let first = execute_once(tool_name, params, project_root);
    match first {
        Ok(value) => Ok(value),
        Err(error) if is_retryable_session_error(&error) => {
            reset_cached_session();
            execute_once(tool_name, params, project_root)
        }
        Err(error) => Err(error),
    }
}

fn execute_once(tool_name: &str, params: &Value, project_root: &Path) -> Result<Value> {
    let canonical_root = canonical_project_root(project_root);
    let mut cache = session_cache()
        .lock()
        .map_err(|_| anyhow!("Failed to lock LSP session cache"))?;

    let should_restart = match cache.as_mut() {
        Some(cached) => cached.project_root != canonical_root || !cached.session.is_running(),
        None => true,
    };
    if should_restart {
        *cache = Some(CachedSession {
            project_root: canonical_root.clone(),
            session: LspSession::start(&canonical_root)?,
        });
    }

    let cached = cache
        .as_mut()
        .ok_or_else(|| anyhow!("Failed to initialize cached LSP session"))?;

    match tool_name {
        "get_symbols" => handle_get_symbols(&mut cached.session, &canonical_root, params),
        "find_symbol" => handle_find_symbol(&mut cached.session, &canonical_root, params),
        "find_refs" => handle_find_refs(&mut cached.session, &canonical_root, params),
        "build_index" => handle_build_index(&mut cached.session, &canonical_root, params),
        "rename_symbol" => handle_rename_symbol(&mut cached.session, &canonical_root, params),
        "replace_symbol_body" => {
            handle_replace_symbol_body(&mut cached.session, &canonical_root, params)
        }
        "insert_before_symbol" => {
            handle_insert_symbol(&mut cached.session, &canonical_root, params, false)
        }
        "insert_after_symbol" => {
            handle_insert_symbol(&mut cached.session, &canonical_root, params, true)
        }
        "remove_symbol" => handle_remove_symbol(&mut cached.session, &canonical_root, params),
        "validate_text_edits" => {
            handle_validate_text_edits(&mut cached.session, &canonical_root, params)
        }
        _ => Err(anyhow!("Unsupported LSP tool: {tool_name}")),
    }
}

fn canonical_project_root(project_root: &Path) -> PathBuf {
    project_root
        .canonicalize()
        .unwrap_or_else(|_| project_root.to_path_buf())
}

fn reset_cached_session() {
    if let Ok(mut cache) = session_cache().lock() {
        *cache = None;
    }
}

fn is_retryable_session_error(error: &anyhow::Error) -> bool {
    let text = error.to_string().to_ascii_lowercase();
    [
        "lsp process ended before response",
        "lsp request timed out",
        "failed to write lsp",
        "failed to read lsp",
        "broken pipe",
        "connection reset",
    ]
    .iter()
    .any(|needle| text.contains(needle))
}

fn handle_get_symbols(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let path = params
        .get("path")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("get_symbols requires `path`"))?;
    let rel = normalize_rel_path(path)
        .ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))?;

    if !rel.to_ascii_lowercase().ends_with(".cs") {
        return Err(anyhow!("Only .cs files are supported"));
    }

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let uri = file_uri(&abs);
    let response = session.request(
        "textDocument/documentSymbol",
        json!({
            "textDocument": {
                "uri": uri
            }
        }),
    )?;

    let mut symbols = Vec::new();
    collect_document_symbols(&response, None, &mut symbols);

    Ok(json!({
        "success": true,
        "path": rel,
        "symbols": symbols,
        "backend": "lsp"
    }))
}

fn handle_find_symbol(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let name = params
        .get("name")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("find_symbol requires `name`"))?;
    let kind_filter = params
        .get("kind")
        .and_then(Value::as_str)
        .map(|value| value.to_ascii_lowercase());
    let scope = params
        .get("scope")
        .and_then(Value::as_str)
        .unwrap_or("all")
        .to_ascii_lowercase();
    let exact = params
        .get("exact")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    let response = session.request("workspace/symbol", json!({ "query": name }))?;
    let items = response.as_array().cloned().unwrap_or_default();

    let mut grouped: BTreeMap<String, Vec<Value>> = BTreeMap::new();
    let mut total = 0usize;

    for item in items {
        let symbol_name = match item.get("name").and_then(Value::as_str) {
            Some(value) if !value.is_empty() => value,
            _ => continue,
        };

        if exact {
            if symbol_name != name {
                continue;
            }
        } else if !symbol_name.contains(name) {
            continue;
        }

        let kind_number = item.get("kind").and_then(Value::as_i64).unwrap_or(0);
        let kind = kind_from_lsp(kind_number);
        if let Some(expected_kind) = &kind_filter {
            if kind != expected_kind {
                continue;
            }
        }

        let uri = match item.pointer("/location/uri").and_then(Value::as_str) {
            Some(value) => value,
            None => continue,
        };
        let rel_path = match uri_to_rel_path(project_root, uri) {
            Some(value) => value,
            None => continue,
        };

        if !path_matches_scope(&rel_path, &scope) {
            continue;
        }

        let line = item
            .pointer("/location/range/start/line")
            .and_then(Value::as_u64)
            .unwrap_or(0) as usize
            + 1;
        let column = item
            .pointer("/location/range/start/character")
            .and_then(Value::as_u64)
            .unwrap_or(0) as usize
            + 1;

        grouped.entry(rel_path).or_default().push(json!({
            "name": symbol_name,
            "kind": kind,
            "line": line,
            "column": column
        }));
        total += 1;
    }

    let results = grouped
        .into_iter()
        .map(|(path, symbols)| json!({ "path": path, "symbols": symbols }))
        .collect::<Vec<_>>();

    Ok(json!({
        "success": true,
        "results": results,
        "total": total,
        "backend": "lsp"
    }))
}

fn handle_find_refs(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
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

    let response = session.request("unitycli/referencesByName", json!({ "name": name }))?;
    let mut refs = response.as_array().cloned().unwrap_or_default();

    refs.sort_by(|a, b| {
        let a_path = ref_path(project_root, a).unwrap_or_default();
        let b_path = ref_path(project_root, b).unwrap_or_default();
        let a_line = a.get("line").and_then(Value::as_u64).unwrap_or(0);
        let b_line = b.get("line").and_then(Value::as_u64).unwrap_or(0);
        let a_col = a.get("column").and_then(Value::as_u64).unwrap_or(0);
        let b_col = b.get("column").and_then(Value::as_u64).unwrap_or(0);
        a_path
            .cmp(&b_path)
            .then(a_line.cmp(&b_line))
            .then(a_col.cmp(&b_col))
    });

    let mut grouped: BTreeMap<String, Vec<Value>> = BTreeMap::new();
    let mut total = 0usize;
    let mut bytes = 0usize;
    let mut truncated = false;
    let mut last_path: Option<String> = None;

    for item in refs {
        let Some(path) = ref_path(project_root, &item) else {
            continue;
        };

        if let Some(cursor) = start_after {
            if path.as_str() <= cursor {
                continue;
            }
        }

        if !path_matches_scope(&path, &scope) {
            continue;
        }

        if let Some(filter) = path_filter {
            if !path.contains(filter) {
                continue;
            }
        }

        if total >= page_size {
            truncated = true;
            break;
        }

        let refs_for_file = grouped.entry(path.clone()).or_default();
        if refs_for_file.len() >= max_matches_per_file {
            continue;
        }

        let line = item.get("line").and_then(Value::as_u64).unwrap_or(1) as usize;
        let column = item.get("column").and_then(Value::as_u64).unwrap_or(1) as usize;
        let snippet = item
            .get("snippet")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();

        let entry = json!({
            "line": line,
            "column": column,
            "snippet": snippet
        });

        let entry_bytes = serde_json::to_vec(&entry)
            .context("Failed to serialize LSP reference entry")?
            .len();
        if bytes + entry_bytes > max_bytes {
            truncated = true;
            break;
        }

        bytes += entry_bytes;
        total += 1;
        last_path = Some(path);
        refs_for_file.push(entry);
    }

    let results = grouped
        .into_iter()
        .filter(|(_, references)| !references.is_empty())
        .map(|(path, references)| json!({ "path": path, "references": references }))
        .collect::<Vec<_>>();

    let mut response = json!({
        "success": true,
        "results": results,
        "total": total,
        "truncated": truncated,
        "backend": "lsp"
    });

    if truncated {
        if let Some(cursor) = last_path {
            response["cursor"] = Value::String(cursor);
        }
    }

    Ok(response)
}

fn handle_build_index(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let mut request_params = serde_json::Map::new();
    if let Some(output_path) = params.get("outputPath") {
        request_params.insert("outputPath".to_string(), output_path.clone());
    }

    let result = session.request("unitycli/buildCodeIndex", Value::Object(request_params))?;
    let success = result
        .get("success")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let count = result.get("count").and_then(Value::as_u64).unwrap_or(0);
    let rel_output = result
        .get("outputPath")
        .and_then(Value::as_str)
        .map(|value| to_project_relative_or_raw(project_root, value));

    let mut response = json!({
        "success": success,
        "indexedFiles": count,
        "indexedSymbols": count,
        "generatedAtEpochMs": now_epoch_ms(),
        "backend": "lsp"
    });

    if let Some(index_path) = rel_output {
        response["indexPath"] = Value::String(index_path);
    }

    response["raw"] = result;
    Ok(response)
}

fn require_relative_path(params: &Value, tool_name: &str) -> Result<String> {
    let path = params
        .get("relative")
        .or_else(|| params.get("path"))
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("{tool_name} requires `relative`"))?;
    normalize_rel_path(path).ok_or_else(|| anyhow!("path must start with Assets/ or Packages/"))
}

fn require_name_path(params: &Value, tool_name: &str) -> Result<String> {
    params
        .get("namePath")
        .and_then(Value::as_str)
        .map(String::from)
        .ok_or_else(|| anyhow!("{tool_name} requires `namePath`"))
}

fn get_apply(params: &Value) -> bool {
    params
        .get("apply")
        .and_then(Value::as_bool)
        .unwrap_or(false)
}

fn handle_rename_symbol(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let rel = require_relative_path(params, "rename_symbol")?;
    let name_path = require_name_path(params, "rename_symbol")?;
    let new_name = params
        .get("newName")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("rename_symbol requires `newName`"))?;
    let apply = get_apply(params);

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let result = session.request(
        "unitycli/renameByNamePath",
        json!({
            "relative": rel,
            "namePath": name_path,
            "newName": new_name,
            "apply": apply
        }),
    )?;

    Ok(wrap_lsp_write_result(result))
}

fn handle_replace_symbol_body(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let rel = require_relative_path(params, "replace_symbol_body")?;
    let name_path = require_name_path(params, "replace_symbol_body")?;
    let body = params
        .get("body")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("replace_symbol_body requires `body`"))?;
    let apply = get_apply(params);

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let result = session.request(
        "unitycli/replaceSymbolBody",
        json!({
            "relative": rel,
            "namePath": name_path,
            "body": body,
            "apply": apply
        }),
    )?;

    Ok(wrap_lsp_write_result(result))
}

fn handle_insert_symbol(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
    after: bool,
) -> Result<Value> {
    let tool_name = if after {
        "insert_after_symbol"
    } else {
        "insert_before_symbol"
    };
    let rel = require_relative_path(params, tool_name)?;
    let name_path = require_name_path(params, tool_name)?;
    let text = params
        .get("text")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("{tool_name} requires `text`"))?;
    let apply = get_apply(params);

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let method = if after {
        "unitycli/insertAfterSymbol"
    } else {
        "unitycli/insertBeforeSymbol"
    };

    let result = session.request(
        method,
        json!({
            "relative": rel,
            "namePath": name_path,
            "text": text,
            "apply": apply
        }),
    )?;

    Ok(wrap_lsp_write_result(result))
}

fn handle_remove_symbol(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let rel = require_relative_path(params, "remove_symbol")?;
    let name_path = require_name_path(params, "remove_symbol")?;
    let apply = get_apply(params);
    let fail_on_references = params.get("failOnReferences").and_then(Value::as_bool);
    let remove_empty_file = params
        .get("removeEmptyFile")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let request = build_remove_symbol_request(
        &rel,
        &name_path,
        apply,
        fail_on_references,
        remove_empty_file,
    );

    let result = session.request("unitycli/removeSymbol", request)?;

    Ok(wrap_lsp_write_result(result))
}

fn build_remove_symbol_request(
    rel: &str,
    name_path: &str,
    apply: bool,
    fail_on_references: Option<bool>,
    remove_empty_file: bool,
) -> Value {
    let mut request = json!({
        "relative": rel,
        "namePath": name_path,
        "apply": apply,
        "removeEmptyFile": remove_empty_file
    });
    if let Some(value) = fail_on_references {
        request["failOnReferences"] = json!(value);
    }
    request
}

fn handle_validate_text_edits(
    session: &mut LspSession,
    project_root: &Path,
    params: &Value,
) -> Result<Value> {
    let rel = require_relative_path(params, "validate_text_edits")?;
    let new_text = params
        .get("newText")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("validate_text_edits requires `newText`"))?;

    let abs = project_root.join(&rel);
    if !abs.exists() {
        return Err(anyhow!("File not found: {rel}"));
    }

    let result = session.request(
        "unitycli/validateTextEdits",
        json!({
            "relative": rel,
            "newText": new_text
        }),
    )?;

    let mut response = json!({ "backend": "lsp" });
    if let Some(diags) = result.get("diagnostics") {
        response["diagnostics"] = diags.clone();
        response["success"] = json!(true);
    } else {
        response["diagnostics"] = json!([]);
        response["success"] = json!(true);
    }
    Ok(response)
}

fn wrap_lsp_write_result(result: Value) -> Value {
    let mut response = result.clone();
    if response.is_object() {
        response
            .as_object_mut()
            .unwrap()
            .insert("backend".to_string(), json!("lsp"));
    }
    response
}

fn collect_document_symbols(value: &Value, container: Option<&str>, out: &mut Vec<Value>) {
    let Some(items) = value.as_array() else {
        return;
    };

    for item in items {
        let Some(name) = item.get("name").and_then(Value::as_str) else {
            continue;
        };
        if name.is_empty() {
            continue;
        }

        let kind_number = item.get("kind").and_then(Value::as_i64).unwrap_or(0);
        let line = item
            .pointer("/range/start/line")
            .and_then(Value::as_u64)
            .unwrap_or(0) as usize
            + 1;
        let column = item
            .pointer("/range/start/character")
            .and_then(Value::as_u64)
            .unwrap_or(0) as usize
            + 1;

        let mut symbol = serde_json::Map::new();
        symbol.insert("name".to_string(), Value::String(name.to_string()));
        symbol.insert(
            "kind".to_string(),
            Value::String(kind_from_lsp(kind_number).to_string()),
        );
        symbol.insert("line".to_string(), Value::Number(line.into()));
        symbol.insert("column".to_string(), Value::Number(column.into()));
        if let Some(parent_name) = container {
            symbol.insert(
                "container".to_string(),
                Value::String(parent_name.to_string()),
            );
        }
        out.push(Value::Object(symbol));

        collect_document_symbols(
            item.get("children").unwrap_or(&Value::Null),
            Some(name),
            out,
        );
    }
}

fn kind_from_lsp(kind: i64) -> &'static str {
    match kind {
        3 => "namespace",
        5 => "class",
        6 => "method",
        7 => "property",
        8 => "field",
        10 => "enum",
        11 => "interface",
        23 => "struct",
        _ => "unknown",
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
    if parts.contains(&"..") {
        return None;
    }

    Some(parts.join("/"))
}

fn file_uri(path: &Path) -> String {
    format!("file://{}", path.to_string_lossy().replace('\\', "/"))
}

fn uri_to_rel_path(project_root: &Path, uri: &str) -> Option<String> {
    if !uri.starts_with("file://") {
        return None;
    }

    let raw = uri.trim_start_matches("file://").replace('\\', "/");
    let path = PathBuf::from(raw);
    let rel = path
        .strip_prefix(project_root)
        .ok()
        .map(|value| value.to_string_lossy().replace('\\', "/"))
        .unwrap_or_else(|| path.to_string_lossy().replace('\\', "/"));

    normalize_rel_path(&rel)
}

fn ref_path(project_root: &Path, item: &Value) -> Option<String> {
    let path = item.get("path").and_then(Value::as_str)?;
    if path.starts_with("file://") {
        return uri_to_rel_path(project_root, path);
    }
    normalize_rel_path(path)
}

fn to_project_relative_or_raw(project_root: &Path, raw: &str) -> String {
    let input = PathBuf::from(raw);
    if input.is_absolute() {
        if let Ok(rel) = input.strip_prefix(project_root) {
            return rel.to_string_lossy().replace('\\', "/");
        }
    }
    raw.replace('\\', "/")
}

fn now_epoch_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|duration| duration.as_millis() as u64)
        .unwrap_or(0)
}

fn lsp_mode() -> LspMode {
    let raw = env::var("UNITY_CLI_LSP_MODE")
        .ok()
        .unwrap_or_else(|| "off".to_string())
        .to_ascii_lowercase();

    match raw.as_str() {
        "required" => LspMode::Required,
        "auto" => LspMode::Auto,
        _ => LspMode::Off,
    }
}

fn lsp_timeout() -> Duration {
    Duration::from_millis(DEFAULT_TIMEOUT_MS)
}

fn resolve_lsp_command() -> Result<LspCommand> {
    let binary = lsp_manager::ensure_local(false)?;
    Ok(LspCommand {
        program: binary.to_string_lossy().to_string(),
        args: Vec::new(),
    })
}

struct LspSession {
    child: Child,
    stdin: ChildStdin,
    rx: Receiver<Value>,
    next_id: u64,
    timeout: Duration,
}

impl LspSession {
    fn start(project_root: &Path) -> Result<Self> {
        let command = resolve_lsp_command()?;

        let mut child = Command::new(&command.program)
            .args(&command.args)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .with_context(|| {
                format!(
                    "Failed to start LSP command: {} {}",
                    command.program,
                    command.args.join(" ")
                )
            })?;

        let stdin = child
            .stdin
            .take()
            .ok_or_else(|| anyhow!("Failed to open LSP stdin"))?;
        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| anyhow!("Failed to open LSP stdout"))?;

        if let Some(mut stderr) = child.stderr.take() {
            thread::spawn(move || {
                let mut buffer = Vec::new();
                let _ = stderr.read_to_end(&mut buffer);
            });
        }

        let rx = spawn_reader(stdout);
        let mut session = Self {
            child,
            stdin,
            rx,
            next_id: 1,
            timeout: lsp_timeout(),
        };

        session.initialize(project_root)?;
        Ok(session)
    }

    fn initialize(&mut self, project_root: &Path) -> Result<()> {
        let id = self.next_request_id();
        self.write_message(&json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": "initialize",
            "params": {
                "processId": std::process::id(),
                "rootUri": file_uri(project_root),
                "capabilities": {}
            }
        }))?;
        let _ = self.wait_response(id)?;
        self.write_message(&json!({
            "jsonrpc": "2.0",
            "method": "initialized",
            "params": {}
        }))?;
        Ok(())
    }

    fn request(&mut self, method: &str, params: Value) -> Result<Value> {
        let id = self.next_request_id();
        self.write_message(&json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params
        }))?;
        self.wait_response(id)
    }

    fn terminate(&mut self) -> Result<()> {
        if self.child.try_wait()?.is_none() {
            let _ = self.child.kill();
            let _ = self.child.wait();
        }
        Ok(())
    }

    fn is_running(&mut self) -> bool {
        self.child
            .try_wait()
            .map(|status| status.is_none())
            .unwrap_or(false)
    }

    fn next_request_id(&mut self) -> u64 {
        let id = self.next_id;
        self.next_id += 1;
        id
    }

    fn write_message(&mut self, payload: &Value) -> Result<()> {
        let json = serde_json::to_vec(payload).context("Failed to serialize LSP payload")?;
        let header = format!("Content-Length: {}\r\n\r\n", json.len());
        self.stdin
            .write_all(header.as_bytes())
            .context("Failed to write LSP header")?;
        self.stdin
            .write_all(&json)
            .context("Failed to write LSP payload")?;
        self.stdin.flush().context("Failed to flush LSP stdin")?;
        Ok(())
    }

    fn wait_response(&self, id: u64) -> Result<Value> {
        let deadline = Instant::now() + self.timeout;
        loop {
            let now = Instant::now();
            if now >= deadline {
                return Err(anyhow!("LSP request timed out"));
            }
            let remaining = deadline.saturating_duration_since(now);

            let message = self
                .rx
                .recv_timeout(remaining)
                .context("LSP process ended before response")?;

            if !id_matches(&message, id) {
                continue;
            }

            if let Some(error) = message.get("error") {
                let code = error.get("code").and_then(Value::as_i64).unwrap_or(-1);
                let text = error
                    .get("message")
                    .and_then(Value::as_str)
                    .unwrap_or("unknown LSP error");
                return Err(anyhow!("LSP error ({code}): {text}"));
            }

            return Ok(message.get("result").cloned().unwrap_or(Value::Null));
        }
    }
}

impl Drop for LspSession {
    fn drop(&mut self) {
        let _ = self.terminate();
    }
}

fn id_matches(message: &Value, id: u64) -> bool {
    if let Some(value) = message.get("id") {
        if value.as_u64() == Some(id) {
            return true;
        }
        if let Some(number) = value.as_i64() {
            return number >= 0 && number as u64 == id;
        }
        if let Some(text) = value.as_str() {
            return text.parse::<u64>().ok() == Some(id);
        }
    }
    false
}

fn spawn_reader(stdout: ChildStdout) -> Receiver<Value> {
    let (tx, rx) = mpsc::channel();
    thread::spawn(move || {
        let mut reader = BufReader::new(stdout);
        loop {
            match read_message(&mut reader) {
                Ok(Some(message)) => {
                    if tx.send(message).is_err() {
                        break;
                    }
                }
                Ok(None) => break,
                Err(_) => break,
            }
        }
    });
    rx
}

fn read_message(reader: &mut BufReader<ChildStdout>) -> Result<Option<Value>> {
    let mut header = Vec::new();
    let mut byte = [0u8; 1];

    loop {
        let read = reader
            .read(&mut byte)
            .context("Failed to read LSP header byte")?;
        if read == 0 {
            if header.is_empty() {
                return Ok(None);
            }
            return Err(anyhow!("Unexpected EOF while reading LSP header"));
        }

        header.push(byte[0]);
        if header.len() > MAX_HEADER_BYTES {
            return Err(anyhow!("LSP header exceeded maximum size"));
        }

        if header.ends_with(b"\r\n\r\n") {
            break;
        }
    }

    let header_text = String::from_utf8(header).context("LSP header is not valid UTF-8")?;
    let mut content_length: Option<usize> = None;
    for line in header_text.split("\r\n") {
        let Some((key, value)) = line.split_once(':') else {
            continue;
        };
        if key.trim().eq_ignore_ascii_case("Content-Length") {
            content_length = value.trim().parse::<usize>().ok();
            break;
        }
    }

    let length = content_length.ok_or_else(|| anyhow!("LSP header missing Content-Length"))?;
    let mut body = vec![0u8; length];
    reader
        .read_exact(&mut body)
        .context("Failed to read LSP payload")?;

    let message: Value =
        serde_json::from_slice(&body).context("Failed to parse LSP payload JSON")?;
    Ok(Some(message))
}

#[cfg(test)]
mod tests {
    use anyhow::anyhow;
    use serde_json::{json, Value};
    use tempfile::tempdir;

    #[cfg(unix)]
    use std::fs;
    #[cfg(unix)]
    use std::io::BufReader;
    #[cfg(unix)]
    use std::os::unix::fs::PermissionsExt;
    #[cfg(unix)]
    use std::path::Path;
    #[cfg(unix)]
    use std::process::{Command, Stdio};
    #[cfg(unix)]
    use std::sync::{Mutex, OnceLock};

    use super::{
        build_remove_symbol_request, collect_document_symbols, execute_direct, file_uri, get_apply,
        id_matches, is_retryable_session_error, kind_from_lsp, maybe_execute, normalize_rel_path,
        path_matches_scope, read_message, ref_path, require_name_path, require_relative_path,
        reset_cached_session, to_project_relative_or_raw, uri_to_rel_path, wrap_lsp_write_result,
    };

    #[cfg(unix)]
    fn env_lock() -> &'static Mutex<()> {
        static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
        LOCK.get_or_init(|| Mutex::new(()))
    }

    #[cfg(unix)]
    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    #[cfg(unix)]
    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = std::env::var(key).ok();
            std::env::set_var(key, value);
            Self { key, previous }
        }
    }

    #[cfg(unix)]
    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(value) = &self.previous {
                std::env::set_var(self.key, value);
            } else {
                std::env::remove_var(self.key);
            }
        }
    }

    #[cfg(unix)]
    fn setup_fake_lsp_server(tools_root: &Path) -> std::path::PathBuf {
        let install_dir = tools_root
            .join("csharp-lsp")
            .join(crate::lsp_manager::detect_rid());
        fs::create_dir_all(&install_dir).expect("fake lsp install dir should be created");

        let server_path = install_dir.join(crate::lsp_manager::executable_name());
        let script = r#"#!/usr/bin/env python3
import json
import os
import sys

PLAYER_URI = os.environ["FAKE_LSP_PLAYER_URI"]
OUTPUT_PATH = os.environ["FAKE_LSP_OUTPUT_PATH"]

def read_message():
    headers = {}
    while True:
        line = sys.stdin.buffer.readline()
        if line == b"":
            return None
        if line in (b"\r\n", b"\n"):
            break
        if b":" in line:
            key, value = line.decode("utf-8").split(":", 1)
            headers[key.strip().lower()] = value.strip()

    length = int(headers.get("content-length", "0"))
    if length <= 0:
        return None

    payload = sys.stdin.buffer.read(length)
    return json.loads(payload.decode("utf-8"))

def send_message(value):
    raw = json.dumps(value, separators=(",", ":")).encode("utf-8")
    sys.stdout.buffer.write(f"Content-Length: {len(raw)}\r\n\r\n".encode("utf-8"))
    sys.stdout.buffer.write(raw)
    sys.stdout.buffer.flush()

while True:
    message = read_message()
    if message is None:
        break

    method = message.get("method")
    message_id = message.get("id")

    if method == "initialize":
        send_message({"jsonrpc": "2.0", "id": message_id, "result": {"capabilities": {"documentSymbolProvider": True}}})
    elif method == "initialized":
        continue
    elif method == "textDocument/documentSymbol":
        send_message({"jsonrpc":"2.0","id":message_id,"result":[{"name":"Player","kind":5,"range":{"start":{"line":0,"character":0}},"children":[{"name":"Move","kind":6,"range":{"start":{"line":2,"character":4}}}]}]})
    elif method == "workspace/symbol":
        send_message({"jsonrpc":"2.0","id":message_id,"result":[
            {"name":"Player","kind":5,"location":{"uri":PLAYER_URI,"range":{"start":{"line":0,"character":0}}}},
            {"name":"PlayerFactory","kind":5,"location":{"uri":PLAYER_URI,"range":{"start":{"line":5,"character":1}}}},
            {"name":"Move","kind":6,"location":{"uri":PLAYER_URI,"range":{"start":{"line":2,"character":4}}}}
        ]})
    elif method == "unitycli/referencesByName":
        send_message({"jsonrpc":"2.0","id":message_id,"result":[
            {"path":PLAYER_URI,"line":4,"column":8,"snippet":"var a = new Player();"},
            {"path":"Assets/Scripts/UserB.cs","line":7,"column":2,"snippet":"Player p;"}
        ]})
    elif method == "unitycli/buildCodeIndex":
        send_message({"jsonrpc":"2.0","id":message_id,"result":{"success":True,"count":3,"outputPath":OUTPUT_PATH}})
    elif method == "unitycli/validateTextEdits":
        send_message({"jsonrpc":"2.0","id":message_id,"result":{"diagnostics":[{"severity":"warning","message":"sample"}]}})
    elif method in ("unitycli/renameByNamePath","unitycli/replaceSymbolBody","unitycli/insertBeforeSymbol","unitycli/insertAfterSymbol","unitycli/removeSymbol"):
        send_message({"jsonrpc":"2.0","id":message_id,"result":{"success":True,"applied":True}})
    else:
        send_message({"jsonrpc":"2.0","id":message_id,"result":{}})
"#;
        fs::write(&server_path, script).expect("fake lsp server script should be written");
        let mut perms = fs::metadata(&server_path)
            .expect("fake lsp metadata should be readable")
            .permissions();
        perms.set_mode(0o755);
        fs::set_permissions(&server_path, perms).expect("fake lsp should be executable");
        server_path
    }

    #[test]
    fn normalize_rel_path_accepts_assets_packages_and_library() {
        assert_eq!(
            normalize_rel_path("Assets/Scripts/Test.cs").as_deref(),
            Some("Assets/Scripts/Test.cs")
        );
        assert_eq!(
            normalize_rel_path("Packages/com.demo/Runtime/Foo.cs").as_deref(),
            Some("Packages/com.demo/Runtime/Foo.cs")
        );
        assert_eq!(
            normalize_rel_path("Library/PackageCache/com.demo/Bar.cs").as_deref(),
            Some("Library/PackageCache/com.demo/Bar.cs")
        );
    }

    #[test]
    fn normalize_rel_path_rejects_parent_traversal() {
        assert!(normalize_rel_path("Assets/../Secrets.cs").is_none());
        assert!(normalize_rel_path("../../Secrets.cs").is_none());
    }

    #[test]
    fn retryable_session_error_detects_transport_failures() {
        let error = anyhow!("Failed to write LSP payload: Broken pipe");
        assert!(is_retryable_session_error(&error));
    }

    #[test]
    fn retryable_session_error_ignores_argument_errors() {
        let error = anyhow!("find_symbol requires `name`");
        assert!(!is_retryable_session_error(&error));
    }

    #[test]
    fn remove_symbol_request_omits_fail_on_references_when_unset() {
        let request = build_remove_symbol_request(
            "Assets/Scripts/Player.cs",
            "Player/Foo",
            false,
            None,
            false,
        );
        assert!(request.get("failOnReferences").is_none());
    }

    #[test]
    fn remove_symbol_request_includes_fail_on_references_when_set() {
        let request = build_remove_symbol_request(
            "Assets/Scripts/Player.cs",
            "Player/Foo",
            true,
            Some(false),
            true,
        );
        assert_eq!(
            request.get("failOnReferences").and_then(Value::as_bool),
            Some(false)
        );
    }

    #[test]
    fn normalize_rel_path_extracts_supported_prefix_from_absolute_like_input() {
        assert_eq!(
            normalize_rel_path("/tmp/workspace/Assets/Scripts/Test.cs").as_deref(),
            Some("Assets/Scripts/Test.cs")
        );
    }

    #[test]
    fn kind_from_lsp_maps_known_values() {
        assert_eq!(kind_from_lsp(3), "namespace");
        assert_eq!(kind_from_lsp(5), "class");
        assert_eq!(kind_from_lsp(23), "struct");
        assert_eq!(kind_from_lsp(999), "unknown");
    }

    #[test]
    fn path_matches_scope_filters_paths() {
        assert!(path_matches_scope("Assets/Scripts/A.cs", "assets"));
        assert!(!path_matches_scope("Packages/com.demo/A.cs", "assets"));
        assert!(path_matches_scope("Packages/com.demo/A.cs", "embedded"));
        assert!(path_matches_scope(
            "Library/PackageCache/com.demo/A.cs",
            "library"
        ));
        assert!(path_matches_scope("whatever", "all"));
    }

    #[test]
    fn uri_and_ref_path_helpers_resolve_project_relative_paths() {
        let root = tempdir().expect("tempdir should succeed");
        let rel = "Assets/Scripts/Player.cs";
        let abs = root.path().join(rel);

        let uri = file_uri(&abs);
        assert_eq!(uri_to_rel_path(root.path(), &uri).as_deref(), Some(rel));
        assert_eq!(
            ref_path(root.path(), &json!({ "path": uri })).as_deref(),
            Some(rel)
        );
        assert_eq!(
            ref_path(root.path(), &json!({ "path": rel })).as_deref(),
            Some(rel)
        );
    }

    #[test]
    fn uri_to_rel_path_rejects_non_file_uris() {
        let root = tempdir().expect("tempdir should succeed");
        assert!(uri_to_rel_path(root.path(), "https://example.com/file.cs").is_none());
    }

    #[test]
    fn to_project_relative_or_raw_returns_relative_for_absolute_under_root() {
        let root = tempdir().expect("tempdir should succeed");
        let abs = root.path().join("Assets/Scripts/Enemy.cs");
        let converted = to_project_relative_or_raw(
            root.path(),
            abs.to_str().expect("absolute path should be valid UTF-8"),
        );
        assert_eq!(converted, "Assets/Scripts/Enemy.cs");
        assert_eq!(
            to_project_relative_or_raw(root.path(), "Packages/com.demo/Foo.cs"),
            "Packages/com.demo/Foo.cs"
        );
    }

    #[test]
    fn require_helpers_validate_expected_fields() {
        let params = json!({
            "path": "Assets/Scripts/Player.cs",
            "namePath": "Player/Move"
        });
        assert_eq!(
            require_relative_path(&params, "rename_symbol").expect("relative path should parse"),
            "Assets/Scripts/Player.cs"
        );
        assert_eq!(
            require_name_path(&params, "rename_symbol").expect("namePath should parse"),
            "Player/Move"
        );

        let missing_name = require_name_path(&json!({}), "rename_symbol")
            .expect_err("missing namePath should fail");
        assert!(missing_name.to_string().contains("namePath"));
        assert!(
            require_relative_path(&json!({"relative": "tmp/Player.cs"}), "rename_symbol").is_err()
        );
    }

    #[test]
    fn get_apply_defaults_to_false() {
        assert!(!get_apply(&json!({})));
        assert!(get_apply(&json!({ "apply": true })));
    }

    #[test]
    fn wrap_lsp_write_result_adds_backend_to_objects_only() {
        let wrapped = wrap_lsp_write_result(json!({ "success": true }));
        assert_eq!(wrapped["backend"], "lsp");
        assert_eq!(wrap_lsp_write_result(json!(["x"])), json!(["x"]));
    }

    #[test]
    fn collect_document_symbols_flattens_nested_symbols() {
        let payload = json!([
            {
                "name": "Player",
                "kind": 5,
                "range": { "start": { "line": 1, "character": 2 } },
                "children": [
                    {
                        "name": "Move",
                        "kind": 6,
                        "range": { "start": { "line": 3, "character": 4 } }
                    }
                ]
            }
        ]);

        let mut symbols = Vec::new();
        collect_document_symbols(&payload, None, &mut symbols);
        assert_eq!(symbols.len(), 2);
        assert_eq!(symbols[0]["name"], "Player");
        assert_eq!(symbols[1]["container"], "Player");
        assert_eq!(symbols[1]["kind"], "method");
    }

    #[test]
    fn id_matches_accepts_number_and_string_id() {
        assert!(id_matches(&json!({ "id": 7 }), 7));
        assert!(id_matches(&json!({ "id": "7" }), 7));
        assert!(!id_matches(&json!({ "id": "x" }), 7));
        assert!(!id_matches(&json!({}), 7));
    }

    #[cfg(unix)]
    #[test]
    fn read_message_returns_none_when_stream_is_empty() {
        let mut child = Command::new("bash")
            .arg("-lc")
            .arg("true")
            .stdout(Stdio::piped())
            .spawn()
            .expect("child process should start");
        let stdout = child.stdout.take().expect("stdout should be piped");
        let mut reader = BufReader::new(stdout);
        assert!(read_message(&mut reader)
            .expect("read should succeed")
            .is_none());
        let _ = child.wait();
    }

    #[cfg(unix)]
    #[test]
    fn read_message_parses_content_length_framed_json() {
        let mut child = Command::new("bash")
            .arg("-lc")
            .arg("printf 'Content-Length: 8\\r\\n\\r\\n{\"id\":1}'")
            .stdout(Stdio::piped())
            .spawn()
            .expect("child process should start");
        let stdout = child.stdout.take().expect("stdout should be piped");
        let mut reader = BufReader::new(stdout);
        let message = read_message(&mut reader)
            .expect("message read should succeed")
            .expect("message should exist");
        assert_eq!(message["id"], 1);
        let _ = child.wait();
    }

    #[cfg(unix)]
    #[test]
    fn read_message_rejects_missing_content_length_header() {
        let mut child = Command::new("bash")
            .arg("-lc")
            .arg("printf 'X-Test: 1\\r\\n\\r\\n{}'")
            .stdout(Stdio::piped())
            .spawn()
            .expect("child process should start");
        let stdout = child.stdout.take().expect("stdout should be piped");
        let mut reader = BufReader::new(stdout);
        let error = read_message(&mut reader).expect_err("missing Content-Length should fail");
        assert!(error.to_string().contains("Content-Length"));
        let _ = child.wait();
    }

    #[cfg(unix)]
    #[test]
    fn execute_direct_covers_core_lsp_read_tools() {
        let _guard = env_lock().lock().expect("lock should succeed");
        reset_cached_session();

        let tools_dir = tempdir().expect("tempdir should be created");
        let project_dir = tempdir().expect("tempdir should be created");
        let player_path = project_dir.path().join("Assets/Scripts/Player.cs");
        let userb_path = project_dir.path().join("Assets/Scripts/UserB.cs");
        fs::create_dir_all(
            player_path
                .parent()
                .expect("player path should have parent directory"),
        )
        .expect("assets/scripts dir should be created");
        fs::write(
            &player_path,
            "public class Player { public void Move() {} }\n",
        )
        .expect("player fixture should be written");
        fs::write(&userb_path, "public class UserB { Player p; }\n")
            .expect("user fixture should be written");

        setup_fake_lsp_server(tools_dir.path());
        let _tools = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            tools_dir
                .path()
                .to_str()
                .expect("tools root path should be valid UTF-8"),
        );
        let _uri = EnvVarGuard::set("FAKE_LSP_PLAYER_URI", &file_uri(&player_path));
        let _out = EnvVarGuard::set(
            "FAKE_LSP_OUTPUT_PATH",
            project_dir
                .path()
                .join("Library/cache/index.json")
                .to_str()
                .expect("output path should be valid UTF-8"),
        );

        let symbols = execute_direct(
            "get_symbols",
            &json!({"path":"Assets/Scripts/Player.cs"}),
            project_dir.path(),
        )
        .expect("get_symbols should succeed");
        assert_eq!(symbols["success"], true);
        assert_eq!(symbols["backend"], "lsp");
        assert!(symbols["symbols"]
            .as_array()
            .expect("symbols should be an array")
            .iter()
            .any(|symbol| symbol["name"] == "Move"));

        let find_symbol = execute_direct(
            "find_symbol",
            &json!({"name":"Player","kind":"class","scope":"assets","exact":true}),
            project_dir.path(),
        )
        .expect("find_symbol should succeed");
        assert_eq!(find_symbol["success"], true);
        assert_eq!(find_symbol["total"], 1);

        let refs = execute_direct(
            "find_refs",
            &json!({"name":"Player","pageSize":1,"maxBytes":65536}),
            project_dir.path(),
        )
        .expect("find_refs should succeed");
        assert_eq!(refs["success"], true);
        assert_eq!(refs["truncated"], true);
        assert!(refs.get("cursor").is_some());

        let index = execute_direct("build_index", &json!({}), project_dir.path())
            .expect("build_index should succeed");
        assert_eq!(index["success"], true);
        assert_eq!(index["indexedFiles"], 3);
        assert_eq!(index["backend"], "lsp");
        reset_cached_session();
    }

    #[cfg(unix)]
    #[test]
    fn execute_direct_covers_lsp_write_tools() {
        let _guard = env_lock().lock().expect("lock should succeed");
        reset_cached_session();

        let tools_dir = tempdir().expect("tempdir should be created");
        let project_dir = tempdir().expect("tempdir should be created");
        let player_path = project_dir.path().join("Assets/Scripts/Player.cs");
        fs::create_dir_all(
            player_path
                .parent()
                .expect("player path should have parent directory"),
        )
        .expect("assets/scripts dir should be created");
        fs::write(
            &player_path,
            "public class Player { public void Move() {} }\n",
        )
        .expect("player fixture should be written");

        setup_fake_lsp_server(tools_dir.path());
        let _tools = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            tools_dir
                .path()
                .to_str()
                .expect("tools root path should be valid UTF-8"),
        );
        let _uri = EnvVarGuard::set("FAKE_LSP_PLAYER_URI", &file_uri(&player_path));
        let _out = EnvVarGuard::set(
            "FAKE_LSP_OUTPUT_PATH",
            project_dir
                .path()
                .join("Library/cache/index.json")
                .to_str()
                .expect("output path should be valid UTF-8"),
        );

        let params = json!({
            "relative":"Assets/Scripts/Player.cs",
            "namePath":"Player/Move",
            "newName":"Run",
            "body":"{ return; }",
            "text":"public void Added() {}",
            "newText":"public class Player {}",
            "apply": true
        });

        let rename = execute_direct("rename_symbol", &params, project_dir.path())
            .expect("rename_symbol should succeed");
        assert_eq!(rename["backend"], "lsp");
        assert_eq!(rename["success"], true);

        let replace = execute_direct("replace_symbol_body", &params, project_dir.path())
            .expect("replace_symbol_body should succeed");
        assert_eq!(replace["backend"], "lsp");

        let before = execute_direct("insert_before_symbol", &params, project_dir.path())
            .expect("insert_before_symbol should succeed");
        assert_eq!(before["backend"], "lsp");

        let after = execute_direct("insert_after_symbol", &params, project_dir.path())
            .expect("insert_after_symbol should succeed");
        assert_eq!(after["backend"], "lsp");

        let remove = execute_direct("remove_symbol", &params, project_dir.path())
            .expect("remove_symbol should succeed");
        assert_eq!(remove["backend"], "lsp");

        let validate = execute_direct("validate_text_edits", &params, project_dir.path())
            .expect("validate_text_edits should succeed");
        assert_eq!(validate["backend"], "lsp");
        assert_eq!(validate["success"], true);
        assert!(validate["diagnostics"].is_array());

        reset_cached_session();
    }

    #[cfg(unix)]
    #[test]
    fn maybe_execute_mode_gate_and_tool_filter_behave_as_expected() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let root = tempdir().expect("tempdir should be created");
        let tools = tempdir().expect("tempdir should be created");
        setup_fake_lsp_server(tools.path());
        let _tools = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            tools
                .path()
                .to_str()
                .expect("tools root path should be valid UTF-8"),
        );
        let params = json!({"path":"Assets/Scripts/Player.cs"});

        std::env::set_var("UNITY_CLI_LSP_MODE", "off");
        assert!(maybe_execute("get_symbols", &params, root.path()).is_none());

        std::env::set_var("UNITY_CLI_LSP_MODE", "auto");
        assert!(maybe_execute("unsupported_tool", &params, root.path()).is_none());

        std::env::set_var("UNITY_CLI_LSP_MODE", "required");
        let required_result = maybe_execute("get_symbols", &params, root.path());
        assert!(required_result.is_some());
        assert!(required_result
            .expect("required mode should return result")
            .is_err());

        std::env::remove_var("UNITY_CLI_LSP_MODE");
    }
}
