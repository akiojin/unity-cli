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
        "get_symbols" | "find_symbol" | "find_refs" | "build_index"
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

    let response = session.request("mcp/referencesByName", json!({ "name": name }))?;
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

    let result = session.request("mcp/buildCodeIndex", Value::Object(request_params))?;
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

    use super::is_retryable_session_error;
    use super::normalize_rel_path;

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
}
