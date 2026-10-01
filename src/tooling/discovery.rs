//! Owned tool descriptors shared by CLI discovery and MCP tools/list.

use anyhow::{anyhow, bail, Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::HashSet;

use crate::config::RuntimeConfig;
use crate::tool_catalog::{list_tool_specs, ToolExecutor, ToolSpec};
use crate::tool_index::{category_slug, filter_tools, ToolListFilter};
use crate::transport::{UnityClient, UnityCommandError};

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum ToolSource {
    Builtin,
    Custom,
}

/// The discovery wire contract. `params_schema` is the JSON Schema for the
/// argument object (MCP calls it `inputSchema`). Names and descriptions are
/// owned because Editor tools are only known after a domain load.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiscoveredTool {
    pub name: String,
    pub description: String,
    pub mutating: bool,
    pub executor: ToolExecutor,
    pub params_schema: Value,
    pub response_schema: Value,
    pub source: ToolSource,
}

impl From<ToolSpec> for DiscoveredTool {
    fn from(spec: ToolSpec) -> Self {
        Self {
            name: spec.name.to_owned(),
            description: spec.description.to_owned(),
            mutating: spec.mutating,
            executor: spec.executor,
            params_schema: spec.params_schema,
            response_schema: spec.response_schema,
            source: ToolSource::Builtin,
        }
    }
}

/// Offline catalog. Connected discovery extends this with the selected
/// Editor's custom descriptors; builtins always retain their definitions.
pub fn builtin_tool_specs() -> Vec<DiscoveredTool> {
    list_tool_specs().into_iter().map(Into::into).collect()
}

/// Read the selected Editor's registry on each call so recompilation and
/// endpoint changes cannot leave a stale custom schema behind. No custom code
/// is invoked by discovery. Old bridges without `list_tools` remain usable.
pub async fn discover_tool_specs(config: &RuntimeConfig) -> Result<Vec<DiscoveredTool>> {
    let mut client = UnityClient::connect(config).await?;
    let result = match client.call_tool("list_tools", json!({})).await {
        Ok(value) => value,
        Err(error)
            if error
                .downcast_ref::<UnityCommandError>()
                .is_some_and(|failure| failure.response["code"] == "UNKNOWN_COMMAND") =>
        {
            return Ok(builtin_tool_specs())
        }
        Err(error) => return Err(error),
    };
    let custom = result
        .get("tools")
        .filter(|tools| tools.is_array())
        .ok_or_else(|| anyhow!("Invalid list_tools response: expected a tools array"))?;
    let custom: Vec<DiscoveredTool> = serde_json::from_value(custom.clone())
        .context("Invalid custom tool descriptor from list_tools")?;
    merge_tool_specs(custom)
}

fn merge_tool_specs(mut custom: Vec<DiscoveredTool>) -> Result<Vec<DiscoveredTool>> {
    let mut tools = builtin_tool_specs();
    let mut names: HashSet<String> = tools
        .iter()
        .map(|tool| tool.name.to_ascii_lowercase())
        .collect();
    custom.sort_by(|a, b| a.name.cmp(&b.name));
    for tool in custom {
        if !valid_tool_name(&tool.name)
            || tool.source != ToolSource::Custom
            || tool.executor != ToolExecutor::Remote
            || tool.params_schema["type"] != "object"
        {
            bail!("Invalid custom tool descriptor: {}", tool.name);
        }
        if !names.insert(tool.name.to_ascii_lowercase()) {
            bail!(
                "Custom tool name conflicts with another tool: {}",
                tool.name
            );
        }
        tools.push(tool);
    }
    Ok(tools)
}

fn valid_tool_name(name: &str) -> bool {
    let mut bytes = name.bytes();
    matches!(bytes.next(), Some(b'a'..=b'z'))
        && bytes.all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'_')
}

/// Apply the same filtering/pagination after merging both sources. The
/// `custom` category selects project tools; existing categories retain their
/// meaning and builtin order. Compact output still uses the short summaries.
pub fn filter_discovered_tools(
    tools: Vec<DiscoveredTool>,
    filter: &ToolListFilter<'_>,
) -> Result<Vec<DiscoveredTool>, String> {
    let custom_only = filter
        .category
        .is_some_and(|value| category_slug(value) == "custom");
    let builtins = filter_tools(&ToolListFilter {
        category: if custom_only { None } else { filter.category },
        query: filter.query,
        ..Default::default()
    })
    .map_err(|message| format!("{message}, custom"))?;
    let query = filter.query.map(str::to_ascii_lowercase);
    Ok(tools
        .into_iter()
        .filter(|tool| match tool.source {
            ToolSource::Builtin => {
                !custom_only && builtins.iter().any(|entry| entry.name == tool.name)
            }
            ToolSource::Custom => {
                (filter.category.is_none() || custom_only)
                    && query.as_ref().is_none_or(|query| {
                        tool.name.contains(query)
                            || tool.description.to_ascii_lowercase().contains(query)
                    })
            }
        })
        .skip(filter.offset)
        .take(filter.limit.unwrap_or(usize::MAX))
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn builtin_descriptors_keep_schemas_and_mark_their_source() {
        let tools = builtin_tool_specs();
        assert_eq!(tools.len(), crate::tool_catalog::TOOL_NAMES.len());
        let ping = tools.iter().find(|tool| tool.name == "ping").unwrap();
        let value = serde_json::to_value(ping).unwrap();
        assert_eq!(value["source"], "builtin");
        assert_eq!(value["executor"], "remote");
        assert_eq!(value["params_schema"]["type"], "object");
        assert!(!ping.mutating);
    }

    #[test]
    fn custom_descriptor_roundtrips_owned_names_and_schema() {
        let value = json!({
            "name": "spawn_light", "description": "Create a light",
            "source": "custom", "executor": "remote", "mutating": true,
            "params_schema": {"type":"object", "properties":{"name":{"type":"string"}}, "required":["name"]},
            "response_schema": {"type":"object"}
        });
        let tool: DiscoveredTool = serde_json::from_value(value.clone()).unwrap();
        assert_eq!(tool.source, ToolSource::Custom);
        assert_eq!(serde_json::to_value(tool).unwrap(), value);
    }

    #[test]
    fn bridge_reserved_names_match_the_cli_catalog_including_local_tools() {
        let source = std::fs::read_to_string(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/UnityCliBridge/Packages/unity-cli-bridge/Editor/Tools/BuiltinToolNames.cs"
        ))
        .expect("generate the Bridge reservation list with scripts/generate-builtin-tool-names.py");
        let names: Vec<_> = source
            .lines()
            .filter_map(|line| {
                line.trim()
                    .strip_prefix('"')
                    .and_then(|line| line.strip_suffix("\","))
            })
            .collect();
        assert_eq!(
            names,
            crate::tool_catalog::TOOL_NAMES,
            "run scripts/generate-builtin-tool-names.py after changing TOOL_NAMES"
        );
    }

    #[test]
    fn list_tools_is_a_read_only_remote_builtin() {
        let tool = crate::tool_catalog::get_tool_spec("list_tools")
            .expect("discovery protocol is a builtin");
        assert!(!tool.mutating);
        assert_eq!(tool.executor, ToolExecutor::Remote);
        assert_eq!(
            tool.params_schema,
            json!({"type":"object", "properties":{}, "additionalProperties":false})
        );
    }
}
