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
    use std::io::{BufRead, BufReader, Write};
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
                            .expect("response newline write should succeed");
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

    #[cfg(unix)]
    #[test]
    fn status_stop_and_ping_handle_response_shapes() {
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("runtime should build");
        runtime.block_on(async {
            let mut pool = ConnectionPool::new();
            let (status_resp, status_action) = handle_request(DaemonRequest::Status, &mut pool)
                .await
                .expect("status request should succeed");
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

            let (stop_resp, stop_action) = handle_request(DaemonRequest::Stop, &mut pool)
                .await
                .expect("stop request should succeed");
            assert!(stop_resp.ok);
            assert!(matches!(stop_action, ConnectionAction::Stop));

            let (ping_resp, ping_action) = handle_request(DaemonRequest::Ping, &mut pool)
                .await
                .expect("ping request should succeed");
            assert!(ping_resp.ok);
            assert!(matches!(ping_action, ConnectionAction::Continue));
        });

        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let home = tempdir().expect("tempdir should succeed");
        let _home = EnvVarGuard::set(
            "HOME",
            home.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );
        cleanup_stale_files();
        let status_value = status().expect("status should return fallback when daemon is absent");
        assert_eq!(status_value["running"], false);

        let stop_value = stop().expect("stop should gracefully succeed when daemon is unavailable");
        assert_eq!(stop_value["stopped"], false);
        assert_eq!(stop_value["running"], false);

        let ping_err = ping().expect_err("ping should fail when daemon is absent");
        assert!(!format!("{ping_err:#}").is_empty());
    }

    #[cfg(unix)]
    #[test]
    fn start_background_returns_already_running_when_status_reports_running() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let home = tempdir().expect("tempdir should succeed");
        let _home = EnvVarGuard::set(
            "HOME",
            home.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        let status_server = spawn_unix_server_once(r#"{"ok":true,"result":{"running":true}}"#);
        let value = start_background().expect("start should return already running");
        assert_eq!(value["alreadyRunning"], true);
        assert_eq!(value["running"], true);
        status_server.join().expect("status server should join");
    }

    #[cfg(unix)]
    #[test]
    fn write_pid_and_cleanup_use_temp_home() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let home = tempdir().expect("tempdir should succeed");
        let _home = EnvVarGuard::set(
            "HOME",
            home.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        cleanup_stale_files();
        write_pid_file().expect("pid file should be written");
        let pid = pid_file_path().expect("pid path should resolve");
        assert!(pid.exists());
        cleanup_stale_files();
        assert!(!pid.exists());
    }

    #[cfg(unix)]
    #[test]
    fn idle_timeout_env_override_and_invalid_values() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let _timeout = EnvVarGuard::set("UNITY_CLI_UNITYD_IDLE_TIMEOUT", "42");
        assert_eq!(idle_timeout_secs(), 42);
        drop(_timeout);

        let _invalid = EnvVarGuard::set("UNITY_CLI_UNITYD_IDLE_TIMEOUT", "0");
        assert_eq!(idle_timeout_secs(), 600);
    }

    #[cfg(unix)]
    #[test]
    fn serve_forever_exits_after_idle_timeout() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let home = tempdir().expect("tempdir should succeed");
        let _home = EnvVarGuard::set(
            "HOME",
            home.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );
        let _timeout = EnvVarGuard::set("UNITY_CLI_UNITYD_IDLE_TIMEOUT", "1");

        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("runtime should build");
        runtime
            .block_on(serve_forever())
            .expect("serve_forever should exit cleanly");

        let socket = socket_path().expect("socket path should resolve");
        let pid = pid_file_path().expect("pid path should resolve");
        assert!(!socket.exists(), "socket should be cleaned up");
        assert!(!pid.exists(), "pid should be cleaned up");
    }

    #[cfg(unix)]
    #[test]
    fn try_call_tool_and_batch_map_daemon_errors() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let home = tempdir().expect("tempdir should succeed");
        let _home = EnvVarGuard::set(
            "HOME",
            home.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );
        let config = RuntimeConfig {
            host: "127.0.0.1".to_string(),
            port: 9,
            timeout: Duration::from_millis(20),
        };
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("runtime should build");

        let tool_ok = spawn_unix_server_once(r#"{"ok":true,"result":{"answer":1}}"#);
        match runtime.block_on(try_call_tool("ping", &json!({}), &config)) {
            Ok(value) => assert_eq!(value["answer"], 1),
            Err(err) => assert!(err.is_transport()),
        }
        tool_ok.join().expect("tool server should join");

        let tool_failed = spawn_unix_server_once(r#"{"ok":false,"error":"tool failed"}"#);
        let err = runtime
            .block_on(try_call_tool("ping", &json!({}), &config))
            .expect_err("tool call should fail");
        assert!(err.is_transport() || err.to_string().contains("tool failed"));
        tool_failed.join().expect("tool server should join");

        cleanup_stale_files();
        let err = runtime
            .block_on(try_call_tool("ping", &json!({}), &config))
            .expect_err("missing daemon should produce transport error");
        assert!(err.is_transport());

        let batch_ok = spawn_unix_server_once(r#"{"ok":true,"result":[{"ok":true}]}"#);
        match runtime.block_on(try_batch(
            vec![BatchItem {
                tool: "ping".to_string(),
                params: json!({}),
            }],
            &config,
        )) {
            Ok(value) => assert!(value.is_array()),
            Err(err) => assert!(err.is_transport()),
        }
        batch_ok.join().expect("batch server should join");

        let batch_failed = spawn_unix_server_once(r#"{"ok":false,"error":"batch failed"}"#);
        let err = runtime
            .block_on(try_batch(
                vec![BatchItem {
                    tool: "ping".to_string(),
                    params: json!({}),
                }],
                &config,
            ))
            .expect_err("batch should fail");
        assert!(err.is_transport() || err.to_string().contains("batch failed"));
        batch_failed.join().expect("batch server should join");
    }

    #[cfg(unix)]
    #[test]
    fn handle_request_and_stream_cover_tool_batch_and_json_errors() {
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("runtime should build");
        runtime.block_on(async {
            let mut pool = ConnectionPool::new();
            let (tool_response, tool_action) = handle_request(
                DaemonRequest::Tool {
                    tool_name: "ping".to_string(),
                    params: json!({}),
                    host: "127.0.0.1".to_string(),
                    port: 9,
                    timeout_ms: 20,
                },
                &mut pool,
            )
            .await
            .expect("tool request should complete");
            assert!(!tool_response.ok);
            assert!(tool_response.error.is_some());
            assert!(matches!(tool_action, ConnectionAction::Continue));

            let (batch_response, batch_action) = handle_request(
                DaemonRequest::Batch {
                    commands: vec![BatchItem {
                        tool: "ping".to_string(),
                        params: json!({}),
                    }],
                    host: "127.0.0.1".to_string(),
                    port: 9,
                    timeout_ms: 20,
                },
                &mut pool,
            )
            .await
            .expect("batch request should complete");
            assert!(batch_response.ok);
            let results = batch_response
                .result
                .as_ref()
                .and_then(Value::as_array)
                .expect("batch response should contain array");
            assert_eq!(results[0]["ok"], false);
            assert!(matches!(batch_action, ConnectionAction::Continue));

            let (client, server) =
                tokio::net::UnixStream::pair().expect("unix stream pair should be created");
            drop(client);
            let action = handle_async_stream(server, &mut pool)
                .await
                .expect("empty stream should continue");
            assert!(matches!(action, ConnectionAction::Continue));

            let (mut client, server) =
                tokio::net::UnixStream::pair().expect("unix stream pair should be created");
            tokio::io::AsyncWriteExt::write_all(
                &mut client,
                br#"{"type":"ping"}
"#,
            )
            .await
            .expect("request write should succeed");
            let action = handle_async_stream(server, &mut pool)
                .await
                .expect("ping stream should succeed");
            assert!(matches!(action, ConnectionAction::Continue));
            let mut reader = tokio::io::BufReader::new(client);
            let mut line = String::new();
            tokio::io::AsyncBufReadExt::read_line(&mut reader, &mut line)
                .await
                .expect("response should be readable");
            let response: Value =
                serde_json::from_str(line.trim()).expect("response should be valid JSON");
            assert_eq!(response["ok"], true);
            assert_eq!(response["result"]["pong"], true);

            let (mut client, server) =
                tokio::net::UnixStream::pair().expect("unix stream pair should be created");
            tokio::io::AsyncWriteExt::write_all(&mut client, b"not-json\n")
                .await
                .expect("request write should succeed");
            let err = match handle_async_stream(server, &mut pool).await {
                Ok(_) => panic!("invalid request should fail"),
                Err(err) => err,
            };
            assert!(format!("{err:#}").contains("Invalid unityd request JSON"));
        });
    }
}
