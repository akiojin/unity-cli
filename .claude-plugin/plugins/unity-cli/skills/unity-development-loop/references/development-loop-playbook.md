# Development Loop Playbook

## Table of Contents

- Scenario framing
- Verification path selection
- Evidence selection
- Loop recipes (gameplay, UI, regression, performance, hot reload preview)
- Exit rules

## Scenario Framing

- Write the scenario as one observable runtime outcome.
- Convert that outcome into 1-3 acceptance criteria.
- Keep each iteration focused on one failing or unknown criterion.

## Verification Path Selection

- Gameplay or device input behavior:
  Use `unity-playmode-testing` to enter Play Mode, send the narrowest input, and observe the result.
- UI interaction behavior:
  Use `unity-ui-automation` to locate the target element, inspect its state, interact once, and re-read state if needed.
- Visual end-state confirmation:
  Prefer a screenshot after the target state is visible.
- Timing or animation-sensitive behavior:
  Prefer a short video clip rather than many screenshots.
- Performance suspicion or explicit performance criteria:
  Use `unity-editor-tools` to check profiler status, capture briefly, and read only the needed metrics.

## Evidence Selection

- Start with the cheapest proof that can settle the current question.
- Prefer one evidence type per iteration unless the first signal is ambiguous.
- Good defaults:
  - state change or event path: logs or UI state read
  - layout or final visual state: screenshot
  - animation or timing: short video
  - frame time or spikes: profiler

## Loop Recipes

### Gameplay Input Loop

1. Lock the scenario and acceptance criteria.
2. Apply the smallest Unity-side change.
3. Enter Play Mode and wait until runtime is ready.
4. Send the exact keyboard, mouse, gamepad, or touch input needed.
5. Capture the minimum evidence that proves the reaction.
6. Read console output if the reaction is missing or unclear.
7. Record the iteration and decide whether to stop or continue.

### UI Flow Loop

1. Lock the UI scenario and acceptance criteria.
2. Apply the smallest Unity-side or binding-side change.
3. Enter Play Mode if the UI depends on runtime state.
4. Find the target element and inspect visibility or interactability.
5. Perform one interaction at a time.
6. Capture a screenshot when final UI state matters.
7. Read console output if clicks or value changes have no effect.
8. Record the iteration and choose the next hypothesis if needed.

### Regression Triage Loop

1. Reproduce the failing scenario with the narrowest path available.
2. Read the console before making changes.
3. Fix one suspected cause.
4. Rerun only the affected scenario path.
5. Stop when acceptance criteria are restored and diagnostics are clean enough for the scope of the change.

### Performance Suspicion Loop

1. Confirm the scenario is functionally correct first.
2. Check profiler status before starting capture.
3. Record a brief profiler session around the suspected hotspot.
4. Read only the metrics needed for the current hypothesis.
5. Make one focused change.
6. Rerun the same capture path and compare.

### Hot Reload Preview Loop

Use it to try a new method body (for example a changed formula) while Play Mode keeps its scene, objects and field values.

Prerequisites, reported by `unity-cli raw hot_reload_status --json '{}'`:

- `supported: true`. `HOT_RELOAD_PACKAGE_MISSING` means the optional FastScriptReload 1.8.0 package is not installed in the project; `HOT_RELOAD_PLATFORM_UNSUPPORTED` means the Editor is neither x64 nor Apple Silicon macOS. Installing the package is the user's decision; without it, edit the file and re-enter Play Mode.
- Fast Script Reload's own auto reload and on-demand reload are disabled.
- The target is one `Assets/*.cs` file with a single non-generic, non-partial class; only bodies of existing synchronous methods change (no new fields, methods, properties or signatures).

Steps:

1. Enter Play Mode and run `unity-cli raw hot_reload --json '{"action":"begin","path":"Assets/Player.cs"}'`. Keep the returned `appliedRevision`. On `HOT_RELOAD_BASELINE_UNPROVEN`, run the `recover` action, wait for compilation, enter Play Mode and begin again.
2. Send the complete candidate source: `unity-cli --timeout-ms 120000 raw hot_reload --json '{"action":"apply","source":"<full file text>","expectedRevision":"<appliedRevision>","timeoutSeconds":10}'`.
3. Success returns a new `appliedRevision` only after every changed method was replaced and executed by the running game. A method the game does not call within `timeoutSeconds` fails with `HOT_RELOAD_VERIFICATION_FAILED`; trigger it through normal input or choose a longer timeout (1-60).
4. Verify the behavior with the usual evidence, then repeat `apply` with the latest `appliedRevision` for the next variant.
5. The preview never writes the `.cs` file. To keep the change, run `unity-cli raw hot_reload --json '{"action":"recover"}'` (stops Play and recompiles cleanly), wait until `hot_reload_status` reports `state: idle`, then apply the same edit to the file through `unity-csharp-edit`.
6. Any failure with `recoveryRequired: true` needs the `recover` action before another preview; never treat it as applied.

## Exit Rules

- Stop immediately when all acceptance criteria are satisfied by current evidence.
- Continue when at least one criterion is still failing and there is a concrete next hypothesis.
- Escalate scope only after a narrow loop fails to explain the behavior.
- Do not turn one scenario into a full exploratory session unless the user explicitly broadens the request.
