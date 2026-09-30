---
name: unity-playmode-testing
description: Drive Unity runtime verification with unity-cli. Use when the user asks to enter or exit play-mode, run an editmode or playmode test, simulate keyboard, mouse, gamepad, or touch input, capture a screenshot or video, or inspect current test status. Do not use for authoring input action assets; use `unity-input-system` instead.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.0
  category: testing
  triggers:
    - playmode
    - editmode
    - test
    - simulate
    - screenshot
    - capture
  siblings:
    - unity-input-system
    - unity-ui-automation
    - unity-editor-tools
---

# Play Mode Testing

Control Play Mode, run EditMode/PlayMode tests, simulate input devices, and capture media. This skill is the runtime sibling of `unity-input-system` (asset authoring) and `unity-ui-automation` (UI interaction).

## Use When

- The user wants to run EditMode or PlayMode tests.
- The task requires runtime input simulation.
- The user wants screenshots or video from the current game or editor state.
- The user wants to inspect current test progress or runtime state.

## Do Not Use When

- The task is editing input action assets; use `unity-input-system`.
- The task targets only UI element interaction without runtime gameplay; use `unity-ui-automation`.
- The work is purely static scene or code inspection; use the corresponding read-only skill.

## Preferred Flow

1. Confirm editor state with `get_editor_state` before entering Play Mode or running tests.
2. Enter Play Mode or start tests, then wait until the runtime is ready before sending input.
3. Capture screenshots or short video only after the target state is visible.
4. Stop Play Mode or recording cleanly and report the final status.
5. If `capture_screenshot` returns `"fallback": "os"`, the Editor did not respond (often a modal dialog). Inspect the desktop image, resolve the dialog, then retry. Pass `"osFallback": false` to get the timeout error instead.

```bash
unity-cli raw play_game --json '{}'
unity-cli raw input_keyboard --json '{"key":"space","action":"press"}'
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720}'
unity-cli raw run_tests --json '{"testMode":"PlayMode"}'
unity-cli raw get_test_status --json '{}'
unity-cli raw stop_game --json '{}'
```

## Examples

- "Run PlayMode tests for the player flow and report the result."
- "Enter Play Mode, press space, and capture a screenshot."
- "Record a 5 second gameplay clip and stop automatically."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [playmode-test-loop.md](references/playmode-test-loop.md): clean execution loop for entering Play Mode, sending input, waiting for results, and capturing evidence.
