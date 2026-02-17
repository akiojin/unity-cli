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
unity-cli raw playmode_wait_for_state --json '{"state":"playing","timeoutMs":10000}'
```

## Input Simulation

```bash
unity-cli raw input_keyboard --json '{"key":"space","action":"press"}'
unity-cli raw input_mouse --json '{"action":"click","button":"left","position":{"x":400,"y":300}}'
unity-cli raw input_gamepad --json '{"button":"buttonSouth","action":"press"}'
unity-cli raw input_touch --json '{"action":"tap","position":{"x":200,"y":400}}'
```

## Screenshots & Video

```bash
unity-cli raw capture_screenshot --json '{"savePath":"Assets/Screenshots/test.png"}'
unity-cli raw analyze_screenshot --json '{"imagePath":"Assets/Screenshots/test.png"}'
unity-cli raw capture_video_start --json '{"savePath":"Assets/Videos/test.mp4"}'
unity-cli raw capture_video_stop --json '{}'
unity-cli raw capture_video_status --json '{}'
unity-cli raw video_capture_for --json '{"durationMs":5000,"savePath":"Assets/Videos/clip.mp4"}'
```

## Testing

```bash
unity-cli raw run_tests --json '{"testMode":"playMode"}'
unity-cli raw run_tests --json '{"testMode":"editMode","testFilter":"PlayerTests"}'
unity-cli raw get_test_status --json '{}'
```

## Tips

- Wait for `playing` state before sending input.
- Use `video_capture_for` for timed recordings.
- `testMode` values: `playMode`, `editMode`.
