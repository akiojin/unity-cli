# Runtime Checklist

## Binary Selection

- Prefer an installed `unity-cli` binary when it exists on `PATH`.
- If this repository is the current workspace and no global binary is installed, use `cargo run -- <args>`.
- Verify the binary with `unity-cli --version` before debugging a runtime loop.

## Instance Selection

- Use `unity-cli system ping` when a single active Unity Editor is expected.
- Use `unity-cli instances list` when multiple editors may be running.
- Use `unity-cli instances set-active <host:port>` only after confirming the target is `up`.

## Command Routing

- Use `unity-csharp-edit` for Unity-side code changes inside the loop.
- Use `unity-playmode-testing` or `unity-ui-automation` for narrow runtime checks.
- Use `unity-editor-tools` for console, profiler, and editor-state diagnostics.

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

## Evidence Defaults

- Prefer logs or state reads for non-visual behavior.
- Prefer a screenshot for final visual confirmation.
- Prefer short video only when timing or motion matters.
- Prefer profiler capture only for explicit performance questions.
