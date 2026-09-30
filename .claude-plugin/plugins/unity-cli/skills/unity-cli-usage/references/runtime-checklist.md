# Runtime Checklist

## Binary Selection

- Prefer an installed `unity-cli` binary when it exists on `PATH`.
- If the repo is the current workspace and no global binary is installed, use `cargo run -- <args>`.
- Verify the binary with `unity-cli --version` before debugging higher-level workflows.

## Instance Selection

- Use `unity-cli system ping` when a single active target is expected.
- Use `unity-cli instances list` when multiple editors may be running.
- Use `unity-cli instances set-active <host:port>` only after confirming the target is `up`.

## Command Routing

- The bootstrap-relevant typed subcommands are `system ping`, `scene create`, `instances list`, `instances set-active`. Use them when available. (`instances list` / `instances set-active` are local registry operations rather than bridge-tool wrappers.)
- The `reference *` family (`fetch`, `status`, `search`, `grep`, `view`, `find-symbol`, `diff`, `resolve-symbol-at`, `embed-build`, `embed-search`, `clean`) also has typed wrappers over the `reference_*` bridge tools — see the `unity-csharp-reference` skill. Beyond these, most bridge tools have no typed wrapper.
- For every tool without a typed wrapper, use `unity-cli raw <tool_name> --json '{...}'` (or its alias `unity-cli tool call <tool_name> --json '{...}'`). This is the primary invocation pattern, not a fallback — bridge tools such as `analyze_scene_contents`, `find_by_component`, `modify_component`, and `get_compilation_state` are all invoked this way.
- Discover tools with `unity-cli tool list`. Inspect a tool's expected JSON payload with `unity-cli tool schema <tool_name> --output json`.
- Use `--output json` when another tool or script will consume the result.

## Connection Recovery

- A failed `system ping` does not prove the Editor is closed. Run `unity-cli doctor --output json` (add `--project-path <dir>` outside the project) before any fallback such as hand-editing YAML.
- Act on the `diagnosis` code:
  - `SAFE_MODE` / `COMPILE_ERRORS`: the bridge is not loaded. Fix each `editorLog.compileErrors[]` entry (`file`, `line`, `code`) by editing the file directly, then let Unity recompile or restart the Editor.
  - `BRIDGE_NOT_INSTALLED`: add `com.akiojin.unity-cli-bridge` to `Packages/manifest.json`.
  - `PORT_IN_USE`: `port.listenerPid` / `port.listenerProcess` hold the port. Stop it or change the bridge port and pass the same `--port`.
  - `EDITOR_NOT_RUNNING`: start the Editor for the project and wait for import to finish.
  - `SANDBOX_BLOCKED`: the local socket was denied. Re-run outside the sandbox; do not conclude the Editor is missing.
  - `BRIDGE_NOT_RESPONDING`: the Editor is still importing/compiling or the port differs from Project Settings > Unity CLI Bridge.
- Re-run `unity-cli doctor` until `diagnosis` is `OK`, then continue the workflow.

## CI Notes

- Set `UNITY_CLI_HOST` and `UNITY_CLI_PORT` explicitly in CI.
- Keep JSON payloads quoted as a single shell argument.
- If connectivity fails in CI, report the resolved host and port before retrying.
