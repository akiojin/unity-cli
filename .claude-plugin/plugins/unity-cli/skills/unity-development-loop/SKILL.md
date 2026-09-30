---
name: unity-development-loop
description: Run Unity runtime development loops with gameplay-focused implementation and validation. Use when the user asks to iterate on runtime behavior, reproduce and fix a gameplay bug, or implement a Unity-side flow until acceptance criteria are met. Do not use for `.inputactions` authoring, read-only inspection, or Rust CLI-only work.
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
---

# Unity Development Loop

Run Unity-side development as short, acceptance-driven loops.
This skill chooses the smallest next change, the narrowest runtime check, and the lightest evidence set that can prove or disprove the current hypothesis.

## Use When

- The user wants to implement and verify a Unity gameplay, input, or UI scenario.
- The task needs a repeatable loop of Unity-side code changes, Play Mode execution, evidence capture, and runtime confirmation.
- The user asks to iterate on a runtime bug until acceptance criteria are satisfied.
- The request needs screenshots, short video, console inspection, or profiler data as part of Unity runtime validation.

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

## Examples

- "Implement a jump input tweak, run it in Play Mode, and keep iterating until the jump timing feels correct."
- "Fix this settings panel flow, click through the UI, capture proof, and stop when the final state matches the spec."
- "Reproduce the runtime bug, inspect logs, make the smallest fix, and rerun only the affected path."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection, instance, `unity-cli doctor` recovery, and evidence-capture baseline before starting a loop.
- [development-loop-playbook.md](references/development-loop-playbook.md): scenario-specific loop recipes (including the Play Mode hot reload preview), evidence selection rules, and exit criteria.
