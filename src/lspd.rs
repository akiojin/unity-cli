use std::fs;
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
    let idle_timeout = Duration::from_secs(DAEMON_IDLE_TIMEOUT_SECS);

    loop {
        match listener.accept() {
            Ok((mut stream, _)) => {
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
    stream
        .write_all(payload.as_bytes())
        .context("Failed to write daemon request")?;
    stream
        .write_all(b"\n")
        .context("Failed to write daemon request terminator")?;
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
    stream
        .write_all(payload.as_bytes())
        .context("Failed to write daemon response")?;
    stream
        .write_all(b"\n")
        .context("Failed to write daemon response terminator")?;
    stream.flush().context("Failed to flush daemon response")?;
    Ok(action)
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
