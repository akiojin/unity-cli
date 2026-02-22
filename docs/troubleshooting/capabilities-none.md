# Capabilities: none

`unity-cli` is a CLI, not an MCP server.  
If your client expects MCP capabilities directly, run the client against its expected MCP endpoint and use `unity-cli` as the command execution backend.

## Typical Cause

- Client configuration still points to legacy `unity-mcp-server` settings.

## Fix

1. Remove legacy MCP server launch config.
2. Configure command execution to call `unity-cli`.
3. Verify with:

```bash
unity-cli --output json system ping
```

1. Confirm Unity connection values:

```bash
echo "$UNITY_CLI_HOST:$UNITY_CLI_PORT"
```

If you still have old scripts, replace all legacy MCP-prefixed environment variables with `UNITY_CLI_*` before running `unity-cli`.
