---
name: unity-playmode-testing
description: Control Unity PlayMode, run tests, simulate input, capture screenshots and video using unity-cli.
allowed-tools: Bash, Read, Grep, Glob
---

# PlayMode, Testing & Input Simulation

Control play/pause/stop, run tests, simulate input devices, and capture media.

## Play Control

```bash
unity-cli raw play_game --json '{}'
unity-cli raw pause_game --json '{}'
unity-cli raw stop_game --json '{}'
unity-cli raw get_editor_state --json '{}'
```

## Input Simulation

```bash
unity-cli raw input_keyboard --json '{"key":"space","action":"press"}'
unity-cli raw input_mouse --json '{"action":"click","button":"left","x":400,"y":300}'
unity-cli raw input_gamepad --json '{"button":"buttonSouth","action":"press"}'
unity-cli raw input_touch --json '{"action":"tap","x":200,"y":400}'
unity-cli raw create_input_sequence --json '{"name":"jump_once","actions":[{"type":"keyboard","action":"press","key":"space"}]}'
unity-cli raw get_current_input_state --json '{}'
```

## Screenshots & Video

```bash
unity-cli raw capture_screenshot --json '{"captureMode":"game","width":1280,"height":720}'
unity-cli raw analyze_screenshot --json '{"imagePath":"Assets/Screenshots/test.png"}'
unity-cli raw capture_video_start --json '{"captureMode":"game","fps":30,"maxDurationSec":5}'
unity-cli raw capture_video_stop --json '{}'
unity-cli raw capture_video_status --json '{}'
```

## Testing

```bash
unity-cli raw run_tests --json '{"testMode":"PlayMode"}'
unity-cli raw run_tests --json '{"testMode":"EditMode","filter":"PlayerTests"}'
unity-cli raw get_test_status --json '{}'
```

## Tips

- Wait for Play Mode before sending input-heavy sequences.
- Prefer `maxDurationSec` on `capture_video_start` for auto-stop runs.
- `testMode` values: `EditMode`, `PlayMode`, `All`.
