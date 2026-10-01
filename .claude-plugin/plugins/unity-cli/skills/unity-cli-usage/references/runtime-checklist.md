# Runtime Checklist

## Structured Results

Use `--output json` for automation. Stdout is one envelope on success and failure:
`{success, command, data, errors:[{code,message}], warnings}`. Unless explicitly
qualified, tool/report fields below are relative to `data`. Check `success` and
the process exit status first; parse failure codes from `errors[0].code`, not
stderr text. Bridge errors preserve their original code and payload in `data`.

Exit codes: 0 success; 1 general error; 2 invalid arguments; 3 authentication;
4 unmet precondition; 6 operation failure/response timeout; 7 unreachable Editor;
8 failed tests; 130 SIGINT; 143 SIGTERM (standard shell statuses).
Fix arguments, credentials or prerequisites before retrying. On exit 6, retry
only a safe operation; after timeout, query its job/request ID first. On exit 7,
follow Connection Recovery. On exit 8, inspect the test failures in `data`.
`run_tests` accepting a run is not a test pass: poll `get_test_status` through
completion and inspect that call's exit code.

## Binary Selection

- Prefer an installed `unity-cli` binary when it exists on `PATH` (the installer links it to `~/.local/bin/unity-cli`).
- If no binary is installed, run `scripts/install.sh` (macOS / Linux) or `scripts/install.ps1` (Windows, when published) instead of asking the user.
- Only inside a unity-cli source checkout, `cargo run -- <args>` is a development alternative.

## Project Setup

- `unity-cli setup` = binary check → `bridge install` → Editor `ping`; it prints one JSON report and exits non-zero until the Editor answers for this project.
- Tell the user before running it: it edits `Packages/manifest.json` and, when the project uses only the legacy Input Manager and the Editor is closed, sets `activeInputHandler: 2` (Both) in `ProjectSettings/ProjectSettings.asset` so the Input System dependency does not block the Editor with a restart dialog. `bridge install` is idempotent (a second run leaves both files byte-identical).
- If the Editor was already open, it may show "enable the Input System backends? (restart)"; bridge commands time out until someone answers it. Close the Editor and rerun `setup --launch-editor` instead.
- `--launch-editor` starts the Unity Hub Editor recorded in `ProjectSettings/ProjectVersion.txt` (`UNITY_EDITOR_PATH` overrides) and waits up to 900 s (`--wait-secs`).
- `editor.projectMatches: false` means another project's Editor owns the port; pass `--port`.
- `versionCheck.status: mismatch` (from `setup` or `system ping`) → `unity-cli bridge upgrade`, or update the CLI.
- Verify the binary with `unity-cli --version` before debugging higher-level workflows.

## Instance Selection

- Use `unity-cli system ping` when a single active target is expected.
- Use `unity-cli instances list` when multiple editors may be running.
- Prefer `--project-path <project>` (or running inside the project) to target one Editor; `AMBIGUOUS_EDITOR` lists candidates in `data.candidates`.
- Use `unity-cli instances set-active <host:port>` only after confirming the target is `up`.

## Command Routing

- The bootstrap-relevant typed subcommands are `system ping`, `scene create`, `instances list`, `instances set-active`. Use them when available. (`instances list` / `instances set-active` are local registry operations rather than bridge-tool wrappers.)
- The `reference *` family (`fetch`, `status`, `search`, `grep`, `view`, `find-symbol`, `diff`, `resolve-symbol-at`, `embed-build`, `embed-search`, `clean`) also has typed wrappers over the `reference_*` bridge tools — see the `unity-csharp-reference` skill. Beyond these, most bridge tools have no typed wrapper.
- For every tool without a typed wrapper, use `unity-cli raw <tool_name> --json '{...}'` (or its alias `unity-cli tool call <tool_name> --json '{...}'`). This is the primary invocation pattern, not a fallback — bridge tools such as `analyze_scene_contents`, `find_by_component`, `modify_component`, and `get_compilation_state` are all invoked this way.
- Discover tools with `unity-cli tool list --query <term> --compact`; add `--category <name>` (a `docs/tools.md` heading or slug such as `scenes`, `playback-testing`) and `--limit N` / `--offset N` to keep output small. Inspect a tool's expected JSON payload with `unity-cli tool schema <tool_name> --output json`.
- Use `--output json` when another tool or script will consume the result.

## Connection Recovery

- A failed `system ping` does not prove the Editor is closed. Run `unity-cli doctor --output json` (add `--project-path <dir>` outside the project) before any fallback such as hand-editing YAML.
- Act on the `diagnosis` code:
  - `SAFE_MODE` / `COMPILE_ERRORS`: the bridge is not loaded. Fix each `editorLog.compileErrors[]` entry (`file`, `line`, `code`) by editing the file directly, then let Unity recompile or restart the Editor.
  - `BRIDGE_NOT_INSTALLED`: run `unity-cli setup` (or `unity-cli bridge install`) to add `com.akiojin.unity-cli-bridge` to `Packages/manifest.json`.
  - `PORT_IN_USE`: `port.listenerPid` / `port.listenerProcess` hold the port. Stop it or change the bridge port and pass the same `--port`.
  - `EDITOR_NOT_RUNNING`: start the Editor for the project and wait for import to finish.
  - `SANDBOX_BLOCKED`: the local socket was denied. Retry in a permitted execution context according to the host's approval policy; do not bypass sandbox restrictions or conclude the Editor is missing. If reachability still cannot be verified, ask the user to confirm the running project and bridge connection before considering an offline fallback.
  - `BRIDGE_NOT_RESPONDING`: the Editor is still importing/compiling or the port differs from Project Settings > Unity CLI Bridge.
- Re-run `unity-cli doctor` until `diagnosis` is `OK`, then continue the workflow.
- An empty instance list or failed process probe inside a sandbox is also inconclusive. Confirm the project/host/port and the user's Editor state instead of treating restricted visibility as `EDITOR_NOT_RUNNING`.
- When the target Editor is reachable, never hand-edit serialized `.unity`, `.prefab`, or `.asset` YAML. Bridge operations keep serialized references, prefab overrides, and the live Editor state in sync.
- If recovery cannot restore the connection and offline work is appropriate, declare the fallback before editing: explain the verified limitation, affected files, alternative method, and how the result will be checked after reopening Unity. Without confirmation of an uncertain sandbox diagnosis, pause dependent edits. Do not silently substitute YAML edits for bridge calls.

## CI Notes

- Set `UNITY_CLI_HOST` and `UNITY_CLI_PORT` explicitly in CI.
- Keep JSON payloads quoted as a single shell argument.
- If connectivity fails in CI, report the resolved host and port before retrying.
