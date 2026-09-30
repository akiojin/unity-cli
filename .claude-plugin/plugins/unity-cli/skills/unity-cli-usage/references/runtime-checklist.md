# Runtime Checklist

## Binary Selection

- Prefer an installed `unity-cli` binary when it exists on `PATH` (the installer links it to `~/.local/bin/unity-cli`).
- If no binary is installed, run `scripts/install.sh` (macOS / Linux) or `scripts/install.ps1` (Windows, when published) instead of asking the user.
- Only inside a unity-cli source checkout, `cargo run -- <args>` is a development alternative.

## Project Setup

- `unity-cli setup` = binary check → `bridge install` → Editor `ping`; it prints one JSON report and exits non-zero until the Editor answers for this project.
- Tell the user before running it: it edits `Packages/manifest.json`. `bridge install` is idempotent (a second run leaves the file byte-identical).
- `--launch-editor` starts the Unity Hub Editor recorded in `ProjectSettings/ProjectVersion.txt` (`UNITY_EDITOR_PATH` overrides) and waits up to 900 s (`--wait-secs`).
- `editor.projectMatches: false` means another project's Editor owns the port; pass `--port`.
- `versionCheck.status: mismatch` (from `setup` or `system ping`) → `unity-cli bridge upgrade`, or update the CLI.
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

## CI Notes

- Set `UNITY_CLI_HOST` and `UNITY_CLI_PORT` explicitly in CI.
- Keep JSON payloads quoted as a single shell argument.
- If connectivity fails in CI, report the resolved host and port before retrying.
