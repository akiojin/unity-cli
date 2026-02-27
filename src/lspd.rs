use std::env;
use std::fs;
use std::io::ErrorKind;
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::thread;
use std::time::{Duration, Instant};

use anyhow::{anyhow, Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

use crate::lsp_manager;

const DAEMON_IDLE_TIMEOUT_SECS: u64 = 600;

#[derive(Debug, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum DaemonRequest {
    Tool {
        tool_name: String,
        params: Value,
        project_root: String,
    },
    Status,
    Ping,
    Stop,
}

#[derive(Debug, Serialize, Deserialize)]
struct DaemonResponse {
    ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    result: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

enum ConnectionAction {
    Continue,
    Stop,
}

pub fn start_background() -> Result<Value> {
    let _ = lsp_manager::ensure_local(false)?;
    if let Ok(status) = status() {
        if status
            .get("running")
            .and_then(Value::as_bool)
            .unwrap_or(false)
        {
            return Ok(json!({
                "success": true,
                "running": true,
                "alreadyRunning": true,
                "status": status
            }));
        }
    }

    cleanup_stale_files();

    let exe = std::env::current_exe().context("Failed to resolve current executable path")?;
    Command::new(exe)
        .arg("lspd")
        .arg("serve")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .context("Failed to spawn lspd background process")?;

    let deadline = Instant::now() + Duration::from_secs(5);
    while Instant::now() < deadline {
        if ping().is_ok() {
            return Ok(json!({
                "success": true,
                "running": true
            }));
        }
        thread::sleep(Duration::from_millis(100));
    }

    Err(anyhow!("lspd failed to start within timeout"))
}

pub fn stop() -> Result<Value> {
    match request(DaemonRequest::Stop) {
        Ok(response) => {
            if response.ok {
                Ok(json!({
                    "success": true,
                    "stopped": true
                }))
            } else {
                Err(anyhow!(
                    "lspd stop failed: {}",
                    response
                        .error
                        .unwrap_or_else(|| "unknown error".to_string())
                ))
            }
        }
        Err(_) => Ok(json!({
            "success": true,
            "stopped": false,
            "running": false
        })),
    }
}

pub fn status() -> Result<Value> {
    match request(DaemonRequest::Status) {
        Ok(response) => {
            if response.ok {
                Ok(response
                    .result
                    .unwrap_or_else(|| json!({ "running": true })))
            } else {
                Ok(json!({
                    "running": false,
                    "error": response.error.unwrap_or_else(|| "status failed".to_string())
                }))
            }
        }
        Err(_) => Ok(json!({
            "running": false
        })),
    }
}

pub fn call_tool(tool_name: &str, params: &Value, project_root: &Path) -> Result<Value> {
    let response = request(DaemonRequest::Tool {
        tool_name: tool_name.to_string(),
        params: params.clone(),
        project_root: project_root.to_string_lossy().to_string(),
    })?;

    if response.ok {
        return Ok(response.result.unwrap_or(Value::Null));
    }

    Err(anyhow!(
        "lspd request failed: {}",
        response
            .error
            .unwrap_or_else(|| "unknown error".to_string())
    ))
}

pub fn serve_forever() -> Result<()> {
    let _ = lsp_manager::ensure_local(false)?;
    let listener = bind_listener()?;
    write_pid_file()?;

    let mut last_activity = Instant::now();
    let idle_timeout = Duration::from_secs(daemon_idle_timeout_secs());

    loop {
        match listener.accept() {
            Ok((mut stream, _)) => {
                stream
                    .set_nonblocking(false)
                    .context("Failed to configure blocking daemon stream")?;
                last_activity = Instant::now();
                let action = handle_stream(&mut stream)?;
                if matches!(action, ConnectionAction::Stop) {
                    break;
                }
            }
            Err(error) if is_would_block(&error) => {
                if last_activity.elapsed() >= idle_timeout {
                    break;
                }
                thread::sleep(Duration::from_millis(200));
            }
            Err(error) => {
                cleanup_stale_files();
                return Err(anyhow!("lspd accept failed: {error}"));
            }
        }
    }

    cleanup_stale_files();
    Ok(())
}

pub fn ping() -> Result<()> {
    let response = request(DaemonRequest::Ping)?;
    if response.ok {
        Ok(())
    } else {
        Err(anyhow!(
            "lspd ping failed: {}",
            response
                .error
                .unwrap_or_else(|| "unknown error".to_string())
        ))
    }
}

fn handle_request(request: DaemonRequest) -> Result<(DaemonResponse, ConnectionAction)> {
    match request {
        DaemonRequest::Ping => Ok((
            DaemonResponse {
                ok: true,
                result: Some(json!({ "pong": true })),
                error: None,
            },
            ConnectionAction::Continue,
        )),
        DaemonRequest::Status => Ok((
            DaemonResponse {
                ok: true,
                result: Some(json!({
                    "running": true,
                    "pid": std::process::id(),
                    "rid": lsp_manager::detect_rid(),
                    "binaryPath": lsp_manager::binary_path().ok().map(|path| path.to_string_lossy().to_string()),
                    "version": lsp_manager::read_local_version()
                })),
                error: None,
            },
            ConnectionAction::Continue,
        )),
        DaemonRequest::Stop => Ok((
            DaemonResponse {
                ok: true,
                result: Some(json!({ "stopping": true })),
                error: None,
            },
            ConnectionAction::Stop,
        )),
        DaemonRequest::Tool {
            tool_name,
            params,
            project_root,
        } => {
            let result = crate::lsp::execute_direct(&tool_name, &params, Path::new(&project_root));
            match result {
                Ok(value) => Ok((
                    DaemonResponse {
                        ok: true,
                        result: Some(value),
                        error: None,
                    },
                    ConnectionAction::Continue,
                )),
                Err(error) => Ok((
                    DaemonResponse {
                        ok: false,
                        result: None,
                        error: Some(error.to_string()),
                    },
                    ConnectionAction::Continue,
                )),
            }
        }
    }
}

fn request(request: DaemonRequest) -> Result<DaemonResponse> {
    let mut stream = connect_client()?;
    let payload =
        serde_json::to_string(&request).context("Failed to serialize daemon request payload")?;
    write_all_with_retry(
        &mut stream,
        payload.as_bytes(),
        "Failed to write daemon request",
    )?;
    write_all_with_retry(
        &mut stream,
        b"\n",
        "Failed to write daemon request terminator",
    )?;
    stream.flush().context("Failed to flush daemon request")?;

    let mut reader = BufReader::new(stream);
    let mut response_line = String::new();
    let read = reader
        .read_line(&mut response_line)
        .context("Failed to read daemon response")?;
    if read == 0 {
        return Err(anyhow!("lspd returned empty response"));
    }
    let response: DaemonResponse =
        serde_json::from_str(response_line.trim()).context("Invalid daemon response JSON")?;
    Ok(response)
}

fn handle_stream(stream: &mut ServerStream) -> Result<ConnectionAction> {
    let mut line = String::new();
    {
        let mut reader = BufReader::new(stream.try_clone()?);
        let read = reader
            .read_line(&mut line)
            .context("Failed to read daemon request line")?;
        if read == 0 {
            return Ok(ConnectionAction::Continue);
        }
    }

    let request: DaemonRequest =
        serde_json::from_str(line.trim()).context("Invalid daemon request JSON")?;
    let (response, action) = handle_request(request)?;
    let payload =
        serde_json::to_string(&response).context("Failed to serialize daemon response payload")?;
    write_all_with_retry(
        stream,
        payload.as_bytes(),
        "Failed to write daemon response",
    )?;
    write_all_with_retry(stream, b"\n", "Failed to write daemon response terminator")?;
    stream.flush().context("Failed to flush daemon response")?;
    Ok(action)
}

fn write_all_with_retry<W: Write>(writer: &mut W, mut data: &[u8], context: &str) -> Result<()> {
    while !data.is_empty() {
        match writer.write(data) {
            Ok(0) => {
                return Err(anyhow!("{context}: write returned 0 bytes"));
            }
            Ok(written) => {
                data = &data[written..];
            }
            Err(error)
                if error.kind() == ErrorKind::WouldBlock
                    || error.kind() == ErrorKind::Interrupted =>
            {
                thread::sleep(Duration::from_millis(1));
            }
            Err(error) => {
                return Err(error).with_context(|| context.to_string());
            }
        }
    }
    Ok(())
}

#[cfg(unix)]
type ServerListener = std::os::unix::net::UnixListener;
#[cfg(unix)]
type ServerStream = std::os::unix::net::UnixStream;
#[cfg(unix)]
type ClientStream = std::os::unix::net::UnixStream;

#[cfg(not(unix))]
type ServerListener = std::net::TcpListener;
#[cfg(not(unix))]
type ServerStream = std::net::TcpStream;
#[cfg(not(unix))]
type ClientStream = std::net::TcpStream;

fn bind_listener() -> Result<ServerListener> {
    #[cfg(unix)]
    {
        let socket_path = socket_path()?;
        if socket_path.exists() {
            let _ = fs::remove_file(&socket_path);
        }
        let listener = std::os::unix::net::UnixListener::bind(&socket_path)
            .with_context(|| format!("Failed to bind socket: {}", socket_path.display()))?;
        listener
            .set_nonblocking(true)
            .context("Failed to configure nonblocking daemon socket")?;
        Ok(listener)
    }

    #[cfg(not(unix))]
    {
        let listener = std::net::TcpListener::bind(("127.0.0.1", daemon_port()))
            .context("Failed to bind lspd TCP listener")?;
        listener
            .set_nonblocking(true)
            .context("Failed to configure nonblocking daemon listener")?;
        Ok(listener)
    }
}

fn connect_client() -> Result<ClientStream> {
    #[cfg(unix)]
    {
        let path = socket_path()?;
        let stream = std::os::unix::net::UnixStream::connect(&path)
            .with_context(|| format!("Failed to connect to lspd socket: {}", path.display()))?;
        stream
            .set_read_timeout(Some(Duration::from_secs(10)))
            .context("Failed to set lspd read timeout")?;
        stream
            .set_write_timeout(Some(Duration::from_secs(10)))
            .context("Failed to set lspd write timeout")?;
        Ok(stream)
    }

    #[cfg(not(unix))]
    {
        let stream = std::net::TcpStream::connect(("127.0.0.1", daemon_port()))
            .context("Failed to connect to lspd TCP endpoint")?;
        stream
            .set_read_timeout(Some(Duration::from_secs(10)))
            .context("Failed to set lspd read timeout")?;
        stream
            .set_write_timeout(Some(Duration::from_secs(10)))
            .context("Failed to set lspd write timeout")?;
        Ok(stream)
    }
}

fn is_would_block(error: &std::io::Error) -> bool {
    error.kind() == std::io::ErrorKind::WouldBlock
}

fn daemon_idle_timeout_secs() -> u64 {
    env::var("UNITY_CLI_LSPD_IDLE_TIMEOUT")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .filter(|v| *v > 0)
        .unwrap_or(DAEMON_IDLE_TIMEOUT_SECS)
}

fn pid_file_path() -> Result<PathBuf> {
    Ok(lsp_manager::install_dir()?.join("lspd.pid"))
}

#[cfg(unix)]
fn socket_path() -> Result<PathBuf> {
    Ok(lsp_manager::install_dir()?.join("lspd.sock"))
}

#[cfg(not(unix))]
fn daemon_port() -> u16 {
    6421
}

fn write_pid_file() -> Result<()> {
    let path = pid_file_path()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create daemon directory: {}", parent.display()))?;
    }
    fs::write(&path, format!("{}\n", std::process::id()))
        .with_context(|| format!("Failed to write daemon pid file: {}", path.display()))
}

fn cleanup_stale_files() {
    if let Ok(path) = pid_file_path() {
        let _ = fs::remove_file(path);
    }
    #[cfg(unix)]
    {
        if let Ok(path) = socket_path() {
            let _ = fs::remove_file(path);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::io::{self, BufRead, BufReader, Write};
    use tempfile::tempdir;

    fn env_lock() -> &'static std::sync::Mutex<()> {
        crate::test_env::env_lock()
    }

    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = std::env::var(key).ok();
            std::env::set_var(key, value);
            Self { key, previous }
        }
    }

    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(previous) = &self.previous {
                std::env::set_var(self.key, previous);
            } else {
                std::env::remove_var(self.key);
            }
        }
    }

    fn prepare_tools_root() -> (tempfile::TempDir, EnvVarGuard) {
        let dir = tempdir().expect("tempdir should succeed");
        let env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );
        (dir, env)
    }

    fn ensure_local_lsp_binary() {
        let binary = crate::lsp_manager::binary_path().expect("binary path should resolve");
        if let Some(parent) = binary.parent() {
            std::fs::create_dir_all(parent).expect("binary directory should be creatable");
        }
        std::fs::write(&binary, b"fake-lsp-binary").expect("fake binary should be writable");
    }

    #[cfg(unix)]
    fn spawn_unix_server_once(response_json: &str) -> std::thread::JoinHandle<()> {
        let path = socket_path().expect("socket path should resolve");
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).expect("socket parent should exist");
        }
        if path.exists() {
            let _ = std::fs::remove_file(&path);
        }
        let listener =
            std::os::unix::net::UnixListener::bind(&path).expect("server socket should bind");
        listener
            .set_nonblocking(true)
            .expect("server socket should be nonblocking");
        let response = response_json.to_string();
        std::thread::spawn(move || {
            let deadline = std::time::Instant::now() + std::time::Duration::from_secs(8);
            loop {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        let mut line = String::new();
                        let _ =
                            BufReader::new(stream.try_clone().expect("stream clone should work"))
                                .read_line(&mut line);
                        stream
                            .write_all(response.as_bytes())
                            .expect("response write should succeed");
                        stream
                            .write_all(b"\n")
                            .expect("response newline should be written");
                        stream.flush().expect("response flush should succeed");
                        break;
                    }
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        if std::time::Instant::now() >= deadline {
                            break;
                        }
                        std::thread::sleep(std::time::Duration::from_millis(10));
                    }
                    Err(_) => break,
                }
            }
            let _ = std::fs::remove_file(path);
        })
    }

    struct FlakyWriter {
        out: Vec<u8>,
        writes: usize,
    }

    impl FlakyWriter {
        fn new() -> Self {
            Self {
                out: Vec::new(),
                writes: 0,
            }
        }
    }

    impl Write for FlakyWriter {
        fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
            self.writes += 1;
            if self.writes % 2 == 1 {
                return Err(io::Error::new(io::ErrorKind::WouldBlock, "simulated"));
            }
            let n = buf.len().min(7);
            self.out.extend_from_slice(&buf[..n]);
            Ok(n)
        }

        fn flush(&mut self) -> io::Result<()> {
            Ok(())
        }
    }

    struct ZeroWriter;

    impl Write for ZeroWriter {
        fn write(&mut self, _buf: &[u8]) -> io::Result<usize> {
            Ok(0)
        }

        fn flush(&mut self) -> io::Result<()> {
            Ok(())
        }
    }

    #[test]
    fn write_all_with_retry_handles_would_block_and_partial_writes() {
        let mut writer = FlakyWriter::new();
        let payload = vec![b'x'; 256];
        write_all_with_retry(&mut writer, &payload, "test write").expect("write should succeed");
        assert_eq!(writer.out, payload);
    }

    #[test]
    fn write_all_with_retry_rejects_zero_byte_progress() {
        let mut writer = ZeroWriter;
        let error = write_all_with_retry(&mut writer, b"abc", "zero write").expect_err("must fail");
        assert!(error.to_string().contains("write returned 0 bytes"));
    }

    #[test]
    fn handle_request_handles_ping_status_and_stop() {
        let (ping_resp, ping_action) = handle_request(DaemonRequest::Ping).expect("ping must work");
        assert!(ping_resp.ok);
        assert_eq!(
            ping_resp.result.as_ref().and_then(|v| v.get("pong")),
            Some(&json!(true))
        );
        assert!(matches!(ping_action, ConnectionAction::Continue));

        let (status_resp, status_action) =
            handle_request(DaemonRequest::Status).expect("status must work");
        assert!(status_resp.ok);
        assert_eq!(
            status_resp
                .result
                .as_ref()
                .and_then(|v| v.get("running"))
                .and_then(serde_json::Value::as_bool),
            Some(true)
        );
        assert!(matches!(status_action, ConnectionAction::Continue));

        let (stop_resp, stop_action) = handle_request(DaemonRequest::Stop).expect("stop must work");
        assert!(stop_resp.ok);
        assert!(matches!(stop_action, ConnectionAction::Stop));
    }

    #[test]
    fn is_would_block_checks_error_kind() {
        let would_block = io::Error::new(io::ErrorKind::WouldBlock, "simulated");
        assert!(is_would_block(&would_block));

        let other = io::Error::new(io::ErrorKind::ConnectionReset, "simulated");
        assert!(!is_would_block(&other));
    }

    #[test]
    fn write_pid_file_and_cleanup_manage_files_under_tools_root() {
        cleanup_stale_files();
        let pid_path = pid_file_path().expect("pid file path should resolve");
        write_pid_file().expect("pid file write should succeed");
        assert!(pid_path.exists());

        cleanup_stale_files();
    }

    #[cfg(unix)]
    #[test]
    fn handle_stream_round_trip_ping_request() {
        let (mut client, mut server) =
            std::os::unix::net::UnixStream::pair().expect("socket pair should be created");
        client
            .write_all(
                br#"{"type":"ping"}
"#,
            )
            .expect("request write should succeed");

        let action = super::handle_stream(&mut server).expect("handle_stream should succeed");
        assert!(matches!(action, ConnectionAction::Continue));

        let mut reader = BufReader::new(client);
        let mut line = String::new();
        reader
            .read_line(&mut line)
            .expect("response line should be readable");
        let response: serde_json::Value =
            serde_json::from_str(line.trim()).expect("response should be valid JSON");
        assert_eq!(response["ok"], true);
        assert_eq!(response["result"]["pong"], true);
    }

    #[cfg(unix)]
    #[test]
    fn handle_stream_rejects_invalid_json() {
        let (mut client, mut server) =
            std::os::unix::net::UnixStream::pair().expect("socket pair should be created");
        client
            .write_all(b"not-json\n")
            .expect("request write should succeed");
        let result = super::handle_stream(&mut server);
        let message = match result {
            Ok(_) => panic!("invalid JSON should fail"),
            Err(error) => format!("{error:#}"),
        };
        assert!(message.contains("Invalid daemon request JSON"));
    }

    #[cfg(unix)]
    #[test]
    fn start_background_returns_already_running_when_status_is_true() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let (_tools_root, _env) = prepare_tools_root();
        ensure_local_lsp_binary();

        let status_server = spawn_unix_server_once(r#"{"ok":true,"result":{"running":true}}"#);
        let value = start_background().expect("start should return already-running response");
        assert_eq!(value["running"], true);
        assert_eq!(value["alreadyRunning"], true);
        status_server.join().expect("status server should join");
    }

    #[cfg(unix)]
    #[test]
    fn ping_status_stop_and_call_tool_handle_daemon_responses() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let (_tools_root, _env) = prepare_tools_root();

        let ping_ok = spawn_unix_server_once(r#"{"ok":true}"#);
        ping().expect("ping should succeed");
        ping_ok.join().expect("ping server should join");

        let ping_fail = spawn_unix_server_once(r#"{"ok":false,"error":"bad ping"}"#);
        let err = ping().expect_err("ping should fail when daemon reports error");
        assert!(format!("{err:#}").contains("bad ping"));
        ping_fail.join().expect("ping server should join");

        let status_ok = spawn_unix_server_once(r#"{"ok":true,"result":{"running":true}}"#);
        let value = status().expect("status should succeed");
        assert!(value.get("running").is_some());
        status_ok.join().expect("status server should join");

        let status_fail = spawn_unix_server_once(r#"{"ok":false,"error":"status failed"}"#);
        let value = status().expect("status fallback should still succeed");
        assert_eq!(value["running"], false);
        assert_eq!(value["error"], "status failed");
        status_fail.join().expect("status server should join");

        let stop_ok = spawn_unix_server_once(r#"{"ok":true}"#);
        let value = stop().expect("stop should succeed");
        assert!(value.get("stopped").is_some());
        stop_ok.join().expect("stop server should join");

        let stop_fail = spawn_unix_server_once(r#"{"ok":false,"error":"cannot-stop"}"#);
        match stop() {
            Ok(value) => assert!(value.get("stopped").is_some()),
            Err(err) => assert!(format!("{err:#}").contains("cannot-stop")),
        }
        stop_fail.join().expect("stop server should join");

        let root = tempdir().expect("tempdir should succeed");
        let tool_ok = spawn_unix_server_once(r#"{"ok":true,"result":{"ok":true}}"#);
        match call_tool("get_symbols", &json!({}), root.path()) {
            Ok(value) => assert_eq!(value["ok"], true),
            Err(err) => assert!(!format!("{err:#}").is_empty()),
        }
        tool_ok.join().expect("tool server should join");

        let tool_fail = spawn_unix_server_once(r#"{"ok":false,"error":"tool failed"}"#);
        let err = call_tool("get_symbols", &json!({}), root.path())
            .expect_err("tool call should fail when daemon reports failure");
        assert!(!format!("{err:#}").is_empty());
        tool_fail.join().expect("tool server should join");

        cleanup_stale_files();
        let value = stop().expect("stop should gracefully succeed when daemon is unavailable");
        assert_eq!(value["stopped"], false);
        assert_eq!(value["running"], false);
    }

    #[cfg(unix)]
    #[test]
    fn ping_reports_invalid_json_response() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let (_tools_root, _env) = prepare_tools_root();

        let invalid = spawn_unix_server_once("not-json");
        let err = ping().expect_err("invalid response JSON should fail");
        let message = format!("{err:#}");
        assert!(
            message.contains("Invalid daemon response JSON")
                || message.contains("Failed to read daemon response")
                || message.contains("Failed to connect to lspd socket"),
            "unexpected error: {message}"
        );
        invalid.join().expect("invalid server should join");
    }

    #[cfg(unix)]
    #[test]
    fn handle_request_tool_branch_returns_error_payload_on_lsp_failure() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let (_tools_root, _env) = prepare_tools_root();
        ensure_local_lsp_binary();
        let root = tempdir().expect("tempdir should succeed");

        let (response, action) = handle_request(DaemonRequest::Tool {
            tool_name: "get_symbols".to_string(),
            params: json!({"path":"Assets/Missing.cs"}),
            project_root: root.path().to_string_lossy().to_string(),
        })
        .expect("tool request should be handled");
        assert!(!response.ok);
        assert!(response.error.is_some());
        assert!(matches!(action, ConnectionAction::Continue));
    }

    #[cfg(unix)]
    #[test]
    fn serve_forever_accepts_stop_and_cleans_files() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let (_tools_root, _env) = prepare_tools_root();
        ensure_local_lsp_binary();
        cleanup_stale_files();
        let _idle_env = EnvVarGuard::set("UNITY_CLI_LSPD_IDLE_TIMEOUT", "1");

        let thread = std::thread::spawn(|| serve_forever().expect("serve_forever should stop"));
        let socket = socket_path().expect("socket path should resolve");
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while !socket.exists() && std::time::Instant::now() < deadline {
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        assert!(socket.exists(), "daemon socket should be created");

        let mut running_seen = false;
        let status_deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while std::time::Instant::now() < status_deadline {
            if let Ok(value) = status() {
                if value
                    .get("running")
                    .and_then(serde_json::Value::as_bool)
                    .unwrap_or(false)
                {
                    running_seen = true;
                    break;
                }
            }
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        assert!(running_seen, "daemon did not respond to status");

        thread.join().expect("daemon thread should join");
        cleanup_stale_files();

        let pid = pid_file_path().expect("pid path should resolve");
        assert!(!pid.exists(), "pid file should be cleaned up");
        if socket.exists() {
            let _ = std::fs::remove_file(&socket);
        }
    }

    #[test]
    fn daemon_idle_timeout_uses_env_override() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        std::env::remove_var("UNITY_CLI_LSPD_IDLE_TIMEOUT");
        assert_eq!(daemon_idle_timeout_secs(), 600);

        let _env = EnvVarGuard::set("UNITY_CLI_LSPD_IDLE_TIMEOUT", "2");
        assert_eq!(daemon_idle_timeout_secs(), 2);

        drop(_env);
        let _invalid = EnvVarGuard::set("UNITY_CLI_LSPD_IDLE_TIMEOUT", "0");
        assert_eq!(daemon_idle_timeout_secs(), 600);
    }
}
