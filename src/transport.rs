use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Value};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::time::timeout;

use crate::config::RuntimeConfig;

const MAX_FRAME_BYTES: i32 = 10 * 1024 * 1024;

pub struct UnityClient {
    stream: TcpStream,
    timeout: std::time::Duration,
    next_id: u64,
}

impl UnityClient {
    pub async fn connect(config: &RuntimeConfig) -> Result<Self> {
        let stream = timeout(
            config.timeout,
            TcpStream::connect((config.host.as_str(), config.port)),
        )
        .await
        .with_context(|| {
            format!(
                "Connection timeout while connecting to Unity at {}:{}",
                config.host, config.port
            )
        })??;

        Ok(Self {
            stream,
            timeout: config.timeout,
            next_id: 1,
        })
    }

    pub async fn call_tool(&mut self, tool_name: &str, params: Value) -> Result<Value> {
        if !params.is_object() {
            bail!("Tool parameters must be a JSON object");
        }

        let request = json!({
          "id": self.next_id.to_string(),
          "type": tool_name,
          "params": params,
        });
        self.next_id += 1;

        self.send_framed(&request).await?;
        let response = self.read_response().await?;
        normalize_response(response)
    }

    async fn send_framed(&mut self, request: &Value) -> Result<()> {
        let payload = serde_json::to_vec(request)?;
        let payload_len = i32::try_from(payload.len()).context("Request payload too large")?;

        let mut frame = Vec::with_capacity(4 + payload.len());
        frame.extend_from_slice(&payload_len.to_be_bytes());
        frame.extend_from_slice(&payload);

        timeout(self.timeout, self.stream.write_all(&frame))
            .await
            .context("Timed out while sending command to Unity")??;
        Ok(())
    }

    async fn read_response(&mut self) -> Result<Value> {
        let mut header = [0_u8; 4];
        timeout(self.timeout, self.stream.read_exact(&mut header))
            .await
            .context("Timed out while waiting for Unity response header")??;

        let expected_len = i32::from_be_bytes(header);
        if (1..=MAX_FRAME_BYTES).contains(&expected_len) {
            let mut payload = vec![0_u8; expected_len as usize];
            timeout(self.timeout, self.stream.read_exact(&mut payload))
                .await
                .context("Timed out while reading Unity response payload")??;
            return parse_json(&payload);
        }

        // Fallback for unframed JSON responses seen in tests/debug outputs.
        let mut buffer = header.to_vec();
        let mut chunk = [0_u8; 1024];

        for _ in 0..20 {
            if let Ok(value) = parse_json(&buffer) {
                return Ok(value);
            }

            match timeout(
                std::time::Duration::from_millis(250),
                self.stream.read(&mut chunk),
            )
            .await
            {
                Ok(Ok(0)) => break,
                Ok(Ok(read)) => {
                    buffer.extend_from_slice(&chunk[..read]);
                    if buffer.len() > MAX_FRAME_BYTES as usize {
                        bail!("Unframed response exceeded max size");
                    }
                }
                Ok(Err(err)) => return Err(err.into()),
                Err(_) => {
                    if let Ok(value) = parse_json(&buffer) {
                        return Ok(value);
                    }
                }
            }
        }

        parse_json(&buffer)
    }
}

fn parse_json(bytes: &[u8]) -> Result<Value> {
    let text = std::str::from_utf8(bytes).context("Unity response was not valid UTF-8")?;
    let trimmed = text.trim();
    if trimmed.is_empty() {
        return Err(anyhow!("Unity response was empty"));
    }
    Ok(serde_json::from_str(trimmed).context("Unity response was not valid JSON")?)
}

fn normalize_response(response: Value) -> Result<Value> {
    let status = response
        .get("status")
        .and_then(Value::as_str)
        .map(|value| value.to_ascii_lowercase());

    let success = response.get("success").and_then(Value::as_bool);

    let error_message = response
        .get("error")
        .and_then(Value::as_str)
        .map(ToString::to_string)
        .or_else(|| {
            if matches!(status.as_deref(), Some("error")) {
                Some("Unity command returned status=error".to_string())
            } else {
                None
            }
        });

    if let Some(error) = error_message {
        let code = response
            .get("code")
            .and_then(Value::as_str)
            .unwrap_or("UNKNOWN_ERROR");
        bail!("{error} (code: {code})");
    }

    if matches!(success, Some(false)) {
        let code = response
            .get("code")
            .and_then(Value::as_str)
            .unwrap_or("UNKNOWN_ERROR");
        bail!("Unity command failed (code: {code})");
    }

    if let Some(result) = response.get("result") {
        return Ok(parse_embedded_json(result.clone()));
    }

    if let Some(data) = response.get("data") {
        return Ok(parse_embedded_json(data.clone()));
    }

    Ok(response)
}

fn parse_embedded_json(value: Value) -> Value {
    match value {
        Value::String(text) => serde_json::from_str::<Value>(&text).unwrap_or(Value::String(text)),
        other => other,
    }
}

#[cfg(test)]
mod tests {
    use super::UnityClient;
    use crate::config::RuntimeConfig;
    use serde_json::{json, Value};
    use std::time::Duration;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;
    use tokio::task::JoinHandle;

    async fn spawn_mock_server<F>(handler: F) -> (u16, JoinHandle<()>)
    where
        F: FnOnce(Value) -> Value + Send + 'static,
    {
        let listener = TcpListener::bind(("127.0.0.1", 0))
            .await
            .expect("listener bind must succeed");
        let port = listener
            .local_addr()
            .expect("listener should have local addr")
            .port();

        let server = tokio::spawn(async move {
            let (mut socket, _) = listener.accept().await.expect("accept must succeed");

            let mut len_buf = [0_u8; 4];
            socket
                .read_exact(&mut len_buf)
                .await
                .expect("request header must be readable");
            let payload_len = i32::from_be_bytes(len_buf);
            let mut payload = vec![0_u8; payload_len as usize];
            socket
                .read_exact(&mut payload)
                .await
                .expect("request payload must be readable");

            let request: Value =
                serde_json::from_slice(&payload).expect("request payload must be valid JSON");
            let response = handler(request);
            let response_bytes =
                serde_json::to_vec(&response).expect("response serialization must succeed");
            let mut frame = Vec::with_capacity(4 + response_bytes.len());
            frame.extend_from_slice(&(response_bytes.len() as i32).to_be_bytes());
            frame.extend_from_slice(&response_bytes);
            socket
                .write_all(&frame)
                .await
                .expect("response frame write must succeed");
        });

        (port, server)
    }

    #[tokio::test]
    async fn call_tool_returns_result_on_success() {
        let (port, server) = spawn_mock_server(|request| {
            assert_eq!(request["type"], "ping");
            assert_eq!(request["params"]["message"], "hello");
            json!({
                "id": request["id"],
                "status": "success",
                "result": { "ok": true, "echo": "hello" }
            })
        })
        .await;

        let config = RuntimeConfig {
            host: "127.0.0.1".to_string(),
            port,
            timeout: Duration::from_millis(500),
        };
        let mut client = UnityClient::connect(&config)
            .await
            .expect("client should connect");
        let result = client
            .call_tool("ping", json!({ "message": "hello" }))
            .await
            .expect("tool call should succeed");

        assert_eq!(result["ok"], true);
        assert_eq!(result["echo"], "hello");
        server.await.expect("server task should complete");
    }

    #[tokio::test]
    async fn call_tool_returns_error_on_failure_response() {
        let (port, server) = spawn_mock_server(|request| {
            json!({
                "id": request["id"],
                "status": "error",
                "error": "boom",
                "code": "E_FAIL"
            })
        })
        .await;

        let config = RuntimeConfig {
            host: "127.0.0.1".to_string(),
            port,
            timeout: Duration::from_millis(500),
        };
        let mut client = UnityClient::connect(&config)
            .await
            .expect("client should connect");
        let error = client
            .call_tool("ping", json!({}))
            .await
            .expect_err("tool call must fail");
        let msg = format!("{error:#}");

        assert!(msg.contains("boom"));
        assert!(msg.contains("E_FAIL"));
        server.await.expect("server task should complete");
    }
}
