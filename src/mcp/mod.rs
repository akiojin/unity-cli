//! MCP (Model Context Protocol) stdio server.
//!
//! Exposes every Unity CLI tool as an MCP tool so any MCP-compatible agent
//! (Cursor, Cherry Studio, Claude SDK, Hermes, …) can call them without
//! shelling out to the CLI manually. This keeps the open-source unity-cli as
//! the single source of truth for tool definitions and schemas, instead of a
//! per-agent bridge.
//!
//! Architecture: a thin `ServerHandler` implementation that delegates to the
//! existing `execute_tool_with_overrides` pipeline (schema validation →
//! local dispatch → remote via daemon/TCP). No tool logic is duplicated here.

use std::sync::Arc;

use anyhow::Result;
use rmcp::{
    ErrorData as McpError, RoleServer, ServerHandler, ServiceExt,
    model::*,
    service::RequestContext,
    transport::stdio,
};
use serde_json::Value;

use crate::config::RuntimeOverrides;
use crate::tool_catalog::{get_tool_spec, list_tool_specs};
use crate::tooling::tool_catalog::ToolSpec;

/// Convert a `serde_json::Value` (the catalog's schema representation) into
/// the `JsonObject` MCP expects for `input_schema`. The catalog stores each
/// schema as a JSON object; we keep only object values and fall back to an
/// empty object otherwise so a malformed schema never panics the server.
fn schema_to_object(schema: &Value) -> Arc<JsonObject> {
    match schema {
        Value::Object(map) => Arc::new(map.clone()),
        _ => Arc::new(JsonObject::new()),
    }
}

/// Build an MCP `Tool` from a catalog `ToolSpec`, annotating mutating tools.
fn spec_to_tool(spec: &ToolSpec) -> Tool {
    let mut annotations = ToolAnnotations::new();
    // Read-only hint helps agents decide when to ask for confirmation.
    annotations.read_only_hint = Some(!spec.mutating);

    let description = if spec.description.is_empty() {
        "Unity CLI tool operation"
    } else {
        spec.description
    };

    let mut tool = Tool::new(spec.name, description, schema_to_object(&spec.params_schema));
    tool.annotations = Some(annotations);
    tool
}

/// Build the runtime overrides the MCP server uses to reach Unity.
fn overrides_from_cli(cli: &crate::cli::Cli) -> RuntimeOverrides {
    RuntimeOverrides {
        host: cli.host.clone(),
        port: cli.port,
        timeout_ms: cli.timeout_ms,
        dry_run: cli.dry_run,
        project_root: None,
    }
}

/// MCP server handler backed by the unity-cli tool catalog.
#[derive(Clone)]
pub struct UnityCliHandler {
    overrides: RuntimeOverrides,
}

impl UnityCliHandler {
    pub fn new(overrides: RuntimeOverrides) -> Self {
        Self { overrides }
    }
}

impl ServerHandler for UnityCliHandler {
    fn get_info(&self) -> ServerInfo {
        ServerInfo::new(
            ServerCapabilities::builder()
                .enable_tools()
                .build(),
        )
        .with_server_info(Implementation::from_build_env())
        .with_instructions(
            "Unity CLI MCP server. Exposes all Unity Editor automation \
             tools (scene, asset, component, console, code index, …) \
             registered by the unity-cli bridge. Mutating tools require \
             Edit Mode unless AllowInPlayMode."
                .to_string(),
        )
    }

    async fn list_tools(
        &self,
        _request: Option<PaginatedRequestParams>,
        _context: RequestContext<RoleServer>,
    ) -> Result<ListToolsResult, McpError> {
        let tools: Vec<Tool> = list_tool_specs().iter().map(spec_to_tool).collect();
        Ok(ListToolsResult::with_all_items(tools))
    }

    async fn call_tool(
        &self,
        request: CallToolRequestParams,
        _context: RequestContext<RoleServer>,
    ) -> Result<CallToolResponse, McpError> {
        let tool_name = request.name.as_ref();

        // Validate the tool exists before delegating, so unknown tools get a
        // clear protocol error rather than an opaque execution failure.
        if get_tool_spec(tool_name).is_none() {
            return Err(McpError::invalid_params(
                format!("unknown Unity CLI tool: {tool_name}"),
                None,
            ));
        }

        // MCP arguments are an optional JSON object; default to empty.
        let params: Value = request
            .arguments
            .map(Value::Object)
            .unwrap_or_else(|| Value::Object(serde_json::Map::new()));

        tracing::debug!(tool = %tool_name, "MCP call_tool");

        // Reuse the shared execute_tool pipeline: schema validation, dry-run
        // interception, local dispatch, then remote (daemon → TCP fallback).
        let value =
            crate::app::runner::execute_tool_with_overrides(&self.overrides, tool_name, params)
                .await
                .map_err(|error| {
                    tracing::warn!(tool = %tool_name, %error, "MCP call_tool failed");
                    McpError::internal_error(
                        format!("unity-cli tool '{tool_name}' failed: {error:#}"),
                        None,
                    )
                })?;

        // Return structured content so agents get the raw JSON the Unity
        // bridge produced; CallToolResult::structured also surfaces it as text.
        Ok(CallToolResult::structured(value).into())
    }
}

/// Run the MCP stdio server until the client disconnects.
///
/// Logging is sent to stderr only — stdout is reserved for JSON-RPC frames
/// and must not be polluted by `tracing` or `println!`. The global tracing
/// subscriber is configured with `std::io::stderr` as its writer in
/// `init_tracing` (called by the runner before this), so no re-init is
/// needed here.
pub async fn serve_forever(cli: &crate::cli::Cli) -> Result<()> {
    tracing::info!("Starting unity-cli MCP server on stdio");

    let handler = UnityCliHandler::new(overrides_from_cli(cli));
    let service = handler
        .serve(stdio())
        .await
        .map_err(|error| anyhow::anyhow!("failed to start MCP stdio server: {error:?}"))?;

    service.waiting().await?;
    Ok(())
}
