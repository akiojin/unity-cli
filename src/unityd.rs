use std::collections::HashMap;
use std::fs;
use std::io::{BufRead, BufReader, Write};
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::thread;
use std::time::{Duration, Instant};

use anyhow::{anyhow, Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use thiserror::Error;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt};

use crate::config::RuntimeConfig;
use crate::transport::UnityClient;

const DEFAULT_IDLE_TIMEOUT_SECS: u64 = 600;

#[derive(Debug, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum DaemonRequest {
    Tool {
        tool_name: String,
        params: Value,
        host: String,
        port: u16,
        timeout_ms: u64,
    },
    Batch {
        commands: Vec<BatchItem>,
        host: String,
        port: u16,
        timeout_ms: u64,
    },
    Status,
    Ping,
    Stop,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct BatchItem {
    pub tool: String,
    pub params: Value,
}

#[derive(Debug, Serialize, Deserialize)]
struct DaemonResponse {
    ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    result: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

#[derive(Debug, Error)]
pub enum DaemonCallError {
    #[error(transparent)]
    Transport(#[from] anyhow::Error),
    #[error("unityd request failed: {0}")]
    RequestFailed(String),
}

impl DaemonCallError {
    pub fn is_transport(&self) -> bool {
        matches!(self, Self::Transport(_))
    }
}

struct ConnectionPool {
    connections: HashMap<(String, u16), UnityClient>,
}

impl ConnectionPool {
    fn new() -> Self {
        Self {
            connections: HashMap::new(),
        }
    }

    async fn get_or_connect(
        &mut self,
        host: &str,
        port: u16,
        timeout: Duration,
    ) -> Result<&mut UnityClient> {
        let key = (host.to_string(), port);
        if !self.connections.contains_key(&key) {
            let config = RuntimeConfig {
                host: host.to_string(),
                port,
                timeout,
            };
            let client = UnityClient::connect(&config).await?;
            self.connections.insert(key.clone(), client);
        }
        Ok(self.connections.get_mut(&key).unwrap())
    }

    fn remove(&mut self, host: &str, port: u16) {
        self.connections.remove(&(host.to_string(), port));
    }
}

fn tools_dir() -> Result<PathBuf> {
    let home = dirs::home_dir().ok_or_else(|| anyhow!("Cannot determine home directory"))?;
    let dir = home.join(".unity").join("tools");
    fs::create_dir_all(&dir)
        .with_context(|| format!("Failed to create tools directory: {}", dir.display()))?;
    Ok(dir)
}

fn pid_file_path() -> Result<PathBuf> {
    Ok(tools_dir()?.join("unityd.pid"))
}

#[cfg(unix)]
fn socket_path() -> Result<PathBuf> {
    Ok(tools_dir()?.join("unityd.sock"))
}

#[cfg(not(unix))]
fn daemon_port() -> u16 {
    6422
}

fn idle_timeout_secs() -> u64 {
    std::env::var("UNITY_CLI_UNITYD_IDLE_TIMEOUT")
        .ok()
        .and_then(|v| v.parse::<u64>().ok())
        .filter(|v| *v > 0)
        .unwrap_or(DEFAULT_IDLE_TIMEOUT_SECS)
}

fn write_pid_file() -> Result<()> {
    let path = pid_file_path()?;
    fs::write(&path, format!("{}\n", std::process::id()))
        .with_context(|| format!("Failed to write unityd pid file: {}", path.display()))
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

pub fn start_background() -> Result<Value> {
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
        .arg("unityd")
        .arg("serve")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .context("Failed to spawn unityd background process")?;

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

    Err(anyhow!("unityd failed to start within timeout"))
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
                    "unityd stop failed: {}",
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

pub async fn try_call_tool(
    tool_name: &str,
    params: &Value,
    config: &RuntimeConfig,
) -> std::result::Result<Value, DaemonCallError> {
    let response = request(DaemonRequest::Tool {
        tool_name: tool_name.to_string(),
        params: params.clone(),
        host: config.host.clone(),
        port: config.port,
        timeout_ms: config.timeout.as_millis() as u64,
    })?;

    if response.ok {
        return Ok(response.result.unwrap_or(Value::Null));
    }

    Err(DaemonCallError::RequestFailed(
        response
            .error
            .unwrap_or_else(|| "unknown error".to_string()),
    ))
}

pub async fn try_batch(
    commands: Vec<BatchItem>,
    config: &RuntimeConfig,
) -> std::result::Result<Value, DaemonCallError> {
    let response = request(DaemonRequest::Batch {
        commands,
        host: config.host.clone(),
        port: config.port,
        timeout_ms: config.timeout.as_millis() as u64,
    })?;

    if response.ok {
        return Ok(response.result.unwrap_or(Value::Null));
    }

    Err(DaemonCallError::RequestFailed(
        response
            .error
            .unwrap_or_else(|| "unknown error".to_string()),
    ))
}

fn ping() -> Result<()> {
    let response = request(DaemonRequest::Ping)?;
    if response.ok {
        Ok(())
    } else {
        Err(anyhow!(
            "unityd ping failed: {}",
            response
                .error
                .unwrap_or_else(|| "unknown error".to_string())
        ))
    }
}

fn request(req: DaemonRequest) -> Result<DaemonResponse> {
    let mut stream = connect_client()?;
    let payload =
        serde_json::to_string(&req).context("Failed to serialize unityd request payload")?;
    stream
        .write_all(payload.as_bytes())
        .context("Failed to write unityd request")?;
    stream
        .write_all(b"\n")
        .context("Failed to write unityd request terminator")?;
    stream.flush().context("Failed to flush unityd request")?;

    let mut reader = BufReader::new(stream);
    let mut response_line = String::new();
    let read = reader
        .read_line(&mut response_line)
        .context("Failed to read unityd response")?;
    if read == 0 {
        return Err(anyhow!("unityd returned empty response"));
    }
    let response: DaemonResponse =
        serde_json::from_str(response_line.trim()).context("Invalid unityd response JSON")?;
    Ok(response)
}

#[cfg(unix)]
fn connect_client() -> Result<std::os::unix::net::UnixStream> {
    let path = socket_path()?;
    let stream = std::os::unix::net::UnixStream::connect(&path)
        .with_context(|| format!("Failed to connect to unityd socket: {}", path.display()))?;
    stream
        .set_read_timeout(Some(Duration::from_secs(60)))
        .context("Failed to set unityd read timeout")?;
    stream
        .set_write_timeout(Some(Duration::from_secs(10)))
        .context("Failed to set unityd write timeout")?;
    Ok(stream)
}

#[cfg(not(unix))]
fn connect_client() -> Result<std::net::TcpStream> {
    let stream = std::net::TcpStream::connect(("127.0.0.1", daemon_port()))
        .context("Failed to connect to unityd TCP endpoint")?;
    stream
        .set_read_timeout(Some(Duration::from_secs(60)))
        .context("Failed to set unityd read timeout")?;
    stream
        .set_write_timeout(Some(Duration::from_secs(10)))
        .context("Failed to set unityd write timeout")?;
    Ok(stream)
}

pub async fn serve_forever() -> Result<()> {
    let idle_timeout = Duration::from_secs(idle_timeout_secs());
    let mut pool = ConnectionPool::new();

    #[cfg(unix)]
    let listener = {
        let path = socket_path()?;
        if path.exists() {
            let _ = fs::remove_file(&path);
        }
        let listener = tokio::net::UnixListener::bind(&path)
            .with_context(|| format!("Failed to bind unityd socket: {}", path.display()))?;
        listener
    };

    #[cfg(not(unix))]
    let listener = {
        let listener = tokio::net::TcpListener::bind(("127.0.0.1", daemon_port()))
            .await
            .context("Failed to bind unityd TCP listener")?;
        listener
    };

    write_pid_file()?;

    let mut last_activity = Instant::now();

    loop {
        let accept_result =
            tokio::time::timeout(Duration::from_millis(200), listener.accept()).await;

        match accept_result {
            Ok(Ok((stream, _))) => {
                last_activity = Instant::now();
                let action = handle_async_stream(stream, &mut pool).await?;
                if matches!(action, ConnectionAction::Stop) {
                    break;
                }
            }
            Ok(Err(error)) => {
                cleanup_stale_files();
                return Err(anyhow!("unityd accept failed: {error}"));
            }
            Err(_) => {
                // Timeout on accept - check idle
                if last_activity.elapsed() >= idle_timeout {
                    break;
                }
            }
        }
    }

    cleanup_stale_files();
    Ok(())
}

enum ConnectionAction {
    Continue,
    Stop,
}

#[cfg(unix)]
type AsyncStream = tokio::net::UnixStream;
#[cfg(not(unix))]
type AsyncStream = tokio::net::TcpStream;

async fn handle_async_stream(
    stream: AsyncStream,
    pool: &mut ConnectionPool,
) -> Result<ConnectionAction> {
    let (reader, mut writer) = tokio::io::split(stream);
    let mut buf_reader = tokio::io::BufReader::new(reader);
    let mut line = String::new();

    let read = buf_reader
        .read_line(&mut line)
        .await
        .context("Failed to read unityd request line")?;
    if read == 0 {
        return Ok(ConnectionAction::Continue);
    }

    let req: DaemonRequest =
        serde_json::from_str(line.trim()).context("Invalid unityd request JSON")?;
    let (response, action) = handle_request(req, pool).await?;
    let payload =
        serde_json::to_string(&response).context("Failed to serialize unityd response payload")?;
    writer
        .write_all(payload.as_bytes())
        .await
        .context("Failed to write unityd response")?;
    writer
        .write_all(b"\n")
        .await
        .context("Failed to write unityd response terminator")?;
    writer
        .flush()
        .await
        .context("Failed to flush unityd response")?;
    Ok(action)
}

async fn handle_request(
    req: DaemonRequest,
    pool: &mut ConnectionPool,
) -> Result<(DaemonResponse, ConnectionAction)> {
    match req {
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
                    "connections": pool.connections.len(),
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
            host,
            port,
            timeout_ms,
        } => {
            let timeout = Duration::from_millis(timeout_ms);
            let result = match pool.get_or_connect(&host, port, timeout).await {
                Ok(client) => client.call_tool(&tool_name, params).await,
                Err(e) => {
                    pool.remove(&host, port);
                    Err(e)
                }
            };

            match result {
                Ok(value) => Ok((
                    DaemonResponse {
                        ok: true,
                        result: Some(value),
                        error: None,
                    },
                    ConnectionAction::Continue,
                )),
                Err(error) => {
                    pool.remove(&host, port);
                    Ok((
                        DaemonResponse {
                            ok: false,
                            result: None,
                            error: Some(error.to_string()),
                        },
                        ConnectionAction::Continue,
                    ))
                }
            }
        }
        DaemonRequest::Batch {
            commands,
            host,
            port,
            timeout_ms,
        } => {
            let timeout = Duration::from_millis(timeout_ms);
            let mut results = Vec::with_capacity(commands.len());

            for item in commands {
                let result = match pool.get_or_connect(&host, port, timeout).await {
                    Ok(client) => client.call_tool(&item.tool, item.params).await,
                    Err(e) => {
                        pool.remove(&host, port);
                        Err(e)
                    }
                };

                match result {
                    Ok(value) => {
                        results.push(json!({ "ok": true, "result": value }));
                    }
                    Err(error) => {
                        pool.remove(&host, port);
                        results.push(json!({ "ok": false, "error": error.to_string() }));
                    }
                }
            }

            Ok((
                DaemonResponse {
                    ok: true,
                    result: Some(Value::Array(results)),
                    error: None,
                },
                ConnectionAction::Continue,
            ))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn daemon_request_ping_round_trip() {
        let req = DaemonRequest::Ping;
        let json = serde_json::to_string(&req).unwrap();
        let parsed: DaemonRequest = serde_json::from_str(&json).unwrap();
        assert!(matches!(parsed, DaemonRequest::Ping));
    }

    #[test]
    fn daemon_request_tool_round_trip() {
        let req = DaemonRequest::Tool {
            tool_name: "ping".to_string(),
            params: json!({"message": "hello"}),
            host: "localhost".to_string(),
            port: 6400,
            timeout_ms: 30000,
        };
        let json = serde_json::to_string(&req).unwrap();
        let parsed: DaemonRequest = serde_json::from_str(&json).unwrap();
        match parsed {
            DaemonRequest::Tool {
                tool_name,
                host,
                port,
                timeout_ms,
                ..
            } => {
                assert_eq!(tool_name, "ping");
                assert_eq!(host, "localhost");
                assert_eq!(port, 6400);
                assert_eq!(timeout_ms, 30000);
            }
            _ => panic!("expected Tool variant"),
        }
    }

    #[test]
    fn daemon_request_batch_round_trip() {
        let req = DaemonRequest::Batch {
            commands: vec![
                BatchItem {
                    tool: "ping".to_string(),
                    params: json!({}),
                },
                BatchItem {
                    tool: "get_scene".to_string(),
                    params: json!({"name": "Main"}),
                },
            ],
            host: "localhost".to_string(),
            port: 6400,
            timeout_ms: 30000,
        };
        let json = serde_json::to_string(&req).unwrap();
        let parsed: DaemonRequest = serde_json::from_str(&json).unwrap();
        match parsed {
            DaemonRequest::Batch { commands, .. } => {
                assert_eq!(commands.len(), 2);
                assert_eq!(commands[0].tool, "ping");
                assert_eq!(commands[1].tool, "get_scene");
            }
            _ => panic!("expected Batch variant"),
        }
    }

    #[test]
    fn daemon_response_round_trip() {
        let response = DaemonResponse {
            ok: true,
            result: Some(json!({"pong": true})),
            error: None,
        };
        let json = serde_json::to_string(&response).unwrap();
        let parsed: DaemonResponse = serde_json::from_str(&json).unwrap();
        assert!(parsed.ok);
        assert!(parsed.result.is_some());
        assert!(parsed.error.is_none());
    }

    #[test]
    fn daemon_response_error_round_trip() {
        let response = DaemonResponse {
            ok: false,
            result: None,
            error: Some("something went wrong".to_string()),
        };
        let json = serde_json::to_string(&response).unwrap();
        let parsed: DaemonResponse = serde_json::from_str(&json).unwrap();
        assert!(!parsed.ok);
        assert!(parsed.result.is_none());
        assert_eq!(parsed.error.as_deref(), Some("something went wrong"));
    }

    #[test]
    fn tools_dir_creates_directory() {
        let dir = tools_dir().expect("tools_dir should succeed");
        assert!(dir.exists());
        assert!(dir.is_dir());
        assert!(dir.ends_with(".unity/tools"));
    }

    #[test]
    fn pid_file_path_is_under_tools_dir() {
        let path = pid_file_path().expect("pid_file_path should succeed");
        assert!(path.to_string_lossy().contains(".unity/tools/unityd.pid"));
    }

    #[cfg(unix)]
    #[test]
    fn socket_path_is_under_tools_dir() {
        let path = socket_path().expect("socket_path should succeed");
        assert!(path.to_string_lossy().contains(".unity/tools/unityd.sock"));
    }

    #[test]
    fn daemon_call_error_classification() {
        let transport = DaemonCallError::Transport(anyhow!("socket closed"));
        assert!(transport.is_transport());

        let request_failed = DaemonCallError::RequestFailed("tool failed".to_string());
        assert!(!request_failed.is_transport());
    }

    #[test]
    fn idle_timeout_default() {
        std::env::remove_var("UNITY_CLI_UNITYD_IDLE_TIMEOUT");
        assert_eq!(idle_timeout_secs(), 600);
    }

    #[test]
    fn daemon_request_status_round_trip() {
        let req = DaemonRequest::Status;
        let json = serde_json::to_string(&req).unwrap();
        let parsed: DaemonRequest = serde_json::from_str(&json).unwrap();
        assert!(matches!(parsed, DaemonRequest::Status));
    }

    #[test]
    fn daemon_request_stop_round_trip() {
        let req = DaemonRequest::Stop;
        let json = serde_json::to_string(&req).unwrap();
        let parsed: DaemonRequest = serde_json::from_str(&json).unwrap();
        assert!(matches!(parsed, DaemonRequest::Stop));
    }
}
