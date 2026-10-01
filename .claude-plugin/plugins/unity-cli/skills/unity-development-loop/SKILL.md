---
name: unity-development-loop
description: Run Unity runtime development loops with gameplay-focused implementation and validation. Use when the user asks to iterate on runtime behavior, reproduce and fix a gameplay bug, measure Editor operation latency, or implement a Unity-side flow until acceptance criteria are met. Do not use for `.inputactions` authoring, read-only inspection, or Rust CLI-only work.
compatibility: Requires unity-cli connected to a Unity Editor that supports Play Mode control, runtime input simulation, UI automation, and editor diagnostics.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: testing
  triggers:
    - runtime
    - gameplay
    - iterate
    - acceptance
    - latency
    - benchmark
---

# Unity Development Loop

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Run Unity-side development as short, acceptance-driven loops.
This skill chooses the smallest next change, the narrowest runtime check, and the lightest evidence set that can prove or disprove the current hypothesis.

## Use When

- The user wants to implement and verify a Unity gameplay, input, or UI scenario.
- The task needs a repeatable loop of Unity-side code changes, Play Mode execution, evidence capture, and runtime confirmation.
- The user asks to iterate on a runtime bug until acceptance criteria are satisfied.
- The request needs screenshots, short video, console inspection, or profiler data as part of Unity runtime validation.
- The user wants to measure Editor operation latency or check performance budgets in a unity-cli source checkout.

## Do Not Use When

- The task is authoring `.inputactions` assets instead of validating runtime behavior. Use `unity-input-system`.
- The task is read-only code or scene investigation with no planned change. Use the relevant inspection skill.
- The task is Rust CLI-only or does not require Unity runtime validation.
- The task is scene, prefab, or asset authoring without a runtime validation loop.

## Preferred Flow

1. Confirm the bridge answers `unity-cli system ping`. If it does not, run `unity-cli doctor --output json` and recover from its `diagnosis` first; on `SAFE_MODE`, fix the reported compile errors (`file`, `line`) before any runtime check.
2. Lock the scenario as observable runtime behavior and define 1-3 acceptance checks before editing or running anything.
3. Choose the smallest next Unity-side change and delegate implementation to `unity-csharp-edit` when code must change.
4. When only the body of an existing method changes and the Play session state is worth keeping, preview it without leaving Play Mode: check `unity-cli raw hot_reload_status --json '{}'` and, if `supported` is true, follow the Hot Reload Preview Loop in the playbook. Otherwise edit the file and re-enter Play Mode.
5. Run the narrowest runtime check through `unity-playmode-testing`, `unity-ui-automation`, or `unity-editor-tools`, depending on whether the loop is gameplay, UI, or diagnostics driven.
6. Capture only the evidence needed for the current hypothesis: state reads or logs for non-visual behavior, a screenshot for visible end state, short video for timing, and profiler data only for performance questions.
7. Record the iteration with scenario, acceptance criteria, change made, execution, evidence, observations, result, and next action.
8. Stop when the current evidence satisfies every acceptance check; otherwise continue with one concrete next hypothesis.

## Latency regression checks

For performance work in a `unity-cli` source checkout, build the release CLI and run
`python3 scripts/e2e-matrix.py --suites perf --unity-cli target/release/unity-cli`.
Select Editors with repeated `--editor` arguments; release acceptance requires
6000.3.25f1 and 2022.3.62f3 with the default `--perf-focus both`.
This launches isolated GUI projects and runs `scripts/bench-editor-ops.py` for the
staff report's 23 operations, 30 samples after 3 warmup cycles per focus condition.
It also runs the existing frontmost `editor_eval` budget with 100 samples and history.
Use an idle host and coordinate Editor focus with other agents.
The owned Game Views temporarily use `PlayUnfocused`; the benchmark restores their
previous window settings after measurement so Play does not invalidate background samples.
Background screenshots start with Finder frontmost but may activate the target Editor
through the existing `GameView.Focus()` call. JSON records this exception; a third-party
PID change or any other operation's focus change invalidates the cycle.

Inspect `matrix.json`, each `perf.json` and `perf.log`. Report the command, Unity
version, pass/fail counts and violations. `perf-budgets.json` gates p50/p95;
`.unity/perf/editor-ops-history.jsonl` also gates p50 degradation greater than 20%
against the last five complete matching runs. `UNITY_CLI_PERF_REGRESSION_PERCENT`
overrides that relative threshold for investigation; releases use the default.
Read Benchmark Policy in `docs/development.md` for baseline recovery and conditions.

Timing includes each CLI process startup while Editor/unityd remain warm. The 22
remote operations use unityd. C# file
reads use the local reader; Play/Stop include state confirmation. Do not equate
these measurements with persistent-shell timings or compare foreground and
background as if they were the same condition. After a product/benchmark change,
re-run the matrix; a historical PASS is insufficient. For Unity-free transport
regressions use `python3 scripts/bench-cli-latency.py` (CI's required latency gate).

## Examples

- "Implement a jump input tweak, run it in Play Mode, and keep iterating until the jump timing feels correct."
- "Fix this settings panel flow, click through the UI, capture proof, and stop when the final state matches the spec."
- "Reproduce the runtime bug, inspect logs, make the smallest fix, and rerun only the affected path."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection, instance, `unity-cli doctor` recovery, and evidence-capture baseline before starting a loop.
- [development-loop-playbook.md](references/development-loop-playbook.md): scenario-specific loop recipes (including the Play Mode hot reload preview), evidence selection rules, and exit criteria.
