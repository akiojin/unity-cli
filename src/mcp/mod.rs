//! Thin stdio projection of the CLI catalog and executor (MCP 2025-11-25).
pub mod configure;

use crate::{
    app::{output, runner},
    cli::Cli,
    config::RuntimeConfig,
    discovery::{builtin_tool_specs, discover_tool_specs, DiscoveredTool},
    tool_catalog,
};
use anyhow::Result;
use serde_json::{json, Value};
use std::time::Duration;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};

fn rpc_error(id: Value, code: i32, message: &str) -> Value {
    json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":message}})
}

fn project_catalog(specs: &[DiscoveredTool]) -> Value {
    let tools: Vec<_> = specs
        .iter()
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

async fn snapshot(cli: &Cli) -> (bool, Vec<DiscoveredTool>) {
    let Ok(mut config) = RuntimeConfig::from_overrides(&runner::runtime_overrides_from_cli(cli))
    else {
        return (false, builtin_tool_specs());
    };
    config.timeout = Duration::from_millis(500);
    match tokio::time::timeout(Duration::from_millis(600), discover_tool_specs(&config)).await {
        Ok(Ok(tools)) => (true, tools),
        _ => (false, builtin_tool_specs()),
    }
}

struct Session {
    negotiated: bool,
    initialized: bool,
    available: bool,
    published_catalog: Value,
}

impl Default for Session {
    fn default() -> Self {
        Self {
            negotiated: false,
            initialized: false,
            available: false,
            published_catalog: project_catalog(&builtin_tool_specs()),
        }
    }
}

impl Session {
    fn update_catalog(&mut self, available: bool, tools: &[DiscoveredTool]) -> bool {
        let catalog = project_catalog(tools);
        let changed = self.available != available || self.published_catalog != catalog;
        self.available = available;
        self.published_catalog = catalog;
        changed
    }

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
                let (available, tools) = snapshot(cli).await;
                self.update_catalog(available, &tools);
                self.published_catalog.clone()
            }
            "tools/call" => {
                let Some(name) = params["name"].as_str() else {
                    return Some(rpc_error(id, -32602, "Missing tool name"));
                };
                if !tool_catalog::is_known_tool(name) {
                    let (_, tools) = snapshot(cli).await;
                    if !tools.iter().any(|tool| tool.name == name) {
                        return Some(rpc_error(id, -32602, "Unknown tool"));
                    }
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
                let (available, tools) = snapshot(cli).await;
                if session.update_catalog(available, &tools) {
                    send(&mut out, json!({"jsonrpc":"2.0","method":"notifications/tools/list_changed"})).await?;
                }
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::discovery::{DiscoveredTool, ToolSource};
    use crate::tool_catalog::ToolExecutor;

    #[test]
    fn projects_owned_custom_descriptor_without_an_mcp_specific_schema() {
        let schema =
            json!({"type":"object","properties":{"name":{"type":"string"}},"required":["name"]});
        let tool = DiscoveredTool {
            name: "spawn_light".into(),
            description: "Create project light".into(),
            mutating: true,
            executor: ToolExecutor::Remote,
            params_schema: schema.clone(),
            response_schema: json!({"type":"object"}),
            source: ToolSource::Custom,
        };
        let value = project_catalog(&[tool]);
        assert_eq!(value["tools"][0]["name"], "spawn_light");
        assert_eq!(value["tools"][0]["description"], "Create project light");
        assert_eq!(value["tools"][0]["inputSchema"], schema);
        assert_eq!(value["tools"][0]["annotations"]["readOnlyHint"], false);
    }
}
