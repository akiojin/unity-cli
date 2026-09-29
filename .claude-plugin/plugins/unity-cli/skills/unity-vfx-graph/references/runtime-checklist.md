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

- Prefer typed subcommands for stable workflows such as `system`, `scene`, and `instances`.
- Use `raw` when only the low-level tool exists or you need an exact tool payload.
- Use `--output json` when another tool or script will consume the result.

## VFX Runtime Verification (`vfx_runtime`)

- The rig is a scene `GameObject` with a `UnityEngine.VFX.VisualEffect` component; create it with
  `create_gameobject` + `add_component` before any `vfx_runtime` call.
- Bind the asset first (`set_asset`), which loads the `VisualEffectAsset` and calls `Reinit`. Other ops
  fail until an asset is bound.
- The exposed-parameter value round-trip (`set_float` then `get_state` → `hasFloat`/`floatValue`) is
  deterministic in edit mode and needs no play-mode. `aliveParticleCount` only advances while the effect
  simulates (play-mode or a focused editor), so treat a `0` count as "not simulating", not "failed".
- `name` for `set_*`/`get_state` is the parameter's exposed name, not its type. Object-typed params
  (`set_texture`/`set_mesh`) and any value round-trip only survive into the runtime sheet if the exposed
  param is **used** in the graph (wired into a consuming slot) — an unused exposed param is stripped at compile.
- Per-instance Initial Event Name: `set_initial_event_name` (`name` = the event, `""` suppresses auto-play)
  overrides the asset default at the component level; read it back via `get_state` → `initialEventName`.
- **Headless particle behaviour** (output events, `aliveParticleCount` > 0): a culled/unrendered effect
  barely simulates and never dispatches output events. In a PlayMode test, frame the rig with a `Camera`
  and advance one step per rendered frame — either `vfx.Simulate(0.05f, 1)` directly or the
  `vfx_runtime` `simulate` op (`deltaTime`/`steps`) — with a frame yield between steps. A single
  multi-`steps` `simulate` call in edit mode does NOT spawn (no render/yield). Output-event CPU callbacks
  come from `VisualEffect.outputEventReceived`; `args.nameId == Shader.PropertyToID(eventName)`.

## CI Notes

- Set `UNITY_CLI_HOST` and `UNITY_CLI_PORT` explicitly in CI.
- Keep JSON payloads quoted as a single shell argument.
- If connectivity fails in CI, report the resolved host and port before retrying.
