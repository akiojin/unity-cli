//! Thin stdio projection of the CLI catalog and executor (MCP 2025-11-25).
pub mod configure;

use crate::{
    app::{output, runner},
    cli::Cli,
    config::RuntimeConfig,
    tool_catalog,
    transport::UnityClient,
};
use anyhow::Result;
use serde_json::{json, Value};
use std::time::Duration;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};

fn rpc_error(id: Value, code: i32, message: &str) -> Value {
    json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":message}})
}

fn catalog() -> Value {
    let tools: Vec<_> = tool_catalog::list_tool_specs()
        .into_iter()
        .map(|spec| {
            json!({
                "name":spec.name, "description":spec.description,
                "inputSchema":spec.params_schema,
                "annotations":{"readOnlyHint":!spec.mutating}
            })
        })
        .collect();
    json!({"tools":tools})
}

async fn connected(cli: &Cli) -> bool {
    let Ok(mut config) = RuntimeConfig::from_overrides(&runner::runtime_overrides_from_cli(cli))
    else {
        return false;
    };
    config.timeout = Duration::from_millis(500);
    let probe = async {
        let mut client = UnityClient::connect(&config).await?;
        client.call_tool("ping", json!({})).await
    };
    matches!(
        tokio::time::timeout(Duration::from_millis(600), probe).await,
        Ok(Ok(_))
    )
}

#[derive(Default)]
struct Session {
    negotiated: bool,
    initialized: bool,
}

impl Session {
    async fn handle(&mut self, cli: &Cli, request: Value) -> Option<Value> {
        let id = request.get("id").cloned();
        let method = request["method"].as_str();
        if request["jsonrpc"] != "2.0"
            || method.is_none()
            || id
                .as_ref()
                .is_some_and(|id| !(id.is_string() || id.is_number() || id.is_null()))
        {
            return Some(rpc_error(Value::Null, -32600, "Invalid Request"));
        }
        let method = method.unwrap();
        let Some(id) = id else {
            if method == "notifications/initialized" && self.negotiated {
                self.initialized = true;
            }
            return None;
        };
        if request
            .get("params")
            .is_some_and(|params| !params.is_object())
        {
            return Some(rpc_error(id, -32602, "params must be an object"));
        }
        let params = &request["params"];
        let result = match method {
            "initialize" => {
                if self.negotiated
                    || !params["protocolVersion"].is_string()
                    || !params["capabilities"].is_object()
                    || !params["clientInfo"].is_object()
                {
                    return Some(rpc_error(
                        id,
                        -32602,
                        "Invalid initialize parameters or already initialized",
                    ));
                }
                self.negotiated = true;
                let requested = params["protocolVersion"].as_str().unwrap();
                let version = match requested {
                    "2024-11-05" | "2025-03-26" | "2025-06-18" | "2025-11-25" => requested,
                    _ => "2025-11-25",
                };
                json!({
                    "protocolVersion":version,
                    "capabilities":{"tools":{"listChanged":true}},
                    "serverInfo":{"name":"unity-cli","version":env!("CARGO_PKG_VERSION")}
                })
            }
            "ping" => json!({}),
            _ if !self.initialized => {
                return Some(rpc_error(id, -32000, "Server not initialized"));
            }
            "tools/list" => {
                if params.get("cursor").is_some() {
                    return Some(rpc_error(id, -32602, "Pagination cursor is not supported"));
                }
                catalog()
            }
            "tools/call" => {
                let Some(name) = params["name"].as_str() else {
                    return Some(rpc_error(id, -32602, "Missing tool name"));
                };
                if !tool_catalog::is_known_tool(name) {
                    return Some(rpc_error(id, -32602, "Unknown tool"));
                }
                let args = params
                    .get("arguments")
                    .cloned()
                    .unwrap_or_else(|| json!({}));
                if !args.is_object() {
                    return Some(rpc_error(id, -32602, "arguments must be an object"));
                }
                call(cli, name, args).await
            }
            _ => return Some(rpc_error(id, -32601, "Method not found")),
        };
        Some(json!({"jsonrpc":"2.0","id":id,"result":result}))
    }
}

async fn call(cli: &Cli, name: &str, args: Value) -> Value {
    let result = runner::execute_tool(cli, name, args)
        .await
        .and_then(|data| {
            crate::failure::check_response(&data)?;
            Ok(data)
        });
    let (value, failed) = match result {
        Ok(data) => (output::envelope(name, &data, None), false),
        Err(error) => {
            let failure = output::classify(&error);
            (output::envelope(name, &failure.data, Some(&failure)), true)
        }
    };
    json!({"content":[{"type":"text","text":value.to_string()}],"isError":failed})
}

async fn send(out: &mut tokio::io::Stdout, message: Value) -> Result<()> {
    let mut line = serde_json::to_vec(&message)?;
    line.push(b'\n');
    out.write_all(&line).await?;
    out.flush().await?;
    Ok(())
}

pub async fn serve(cli: &Cli) -> Result<()> {
    let mut lines = BufReader::new(tokio::io::stdin()).lines();
    let mut out = tokio::io::stdout();
    let mut session = Session::default();
    let mut available = false;
    let mut interval = tokio::time::interval(Duration::from_secs(2));
    interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
    loop {
        tokio::select! {
            line = lines.next_line() => {
                let Some(line) = line? else { break; };
                let response = match serde_json::from_str(&line) {
                    Ok(value) => session.handle(cli, value).await,
                    Err(_) => Some(rpc_error(Value::Null, -32700, "Parse error")),
                };
                if let Some(response) = response {
                    send(&mut out, response).await?;
                }
            }
            _ = interval.tick(), if session.initialized => {
                let now = connected(cli).await;
                if now != available {
                    available = now;
                    send(&mut out, json!({"jsonrpc":"2.0","method":"notifications/tools/list_changed"})).await?;
                }
            }
        }
    }
    Ok(())
}
