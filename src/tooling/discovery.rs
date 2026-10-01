//! Owned tool descriptors shared by CLI discovery and MCP tools/list.

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::tool_catalog::{list_tool_specs, ToolExecutor, ToolSpec};

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
}
