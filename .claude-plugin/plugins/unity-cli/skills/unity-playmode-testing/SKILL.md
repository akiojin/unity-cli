---
name: unity-playmode-testing
description: Drive Unity runtime verification with unity-cli. Use when the user asks to run PlayMode tests with Domain Reload, simulate keyboard, mouse, gamepad or touch input, inspect InputAction notifications in Play, record video or PNG frames, or preview method hot reload. Do not use for authoring input action assets; use `unity-input-system` instead. For URP and Bloom setup with before/after captures use `unity-urp-setup`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.3.3
  category: testing
  triggers:
    - playmode
    - editmode
    - test
    - simulate
    - screenshot
    - capture
  siblings:
    - unity-audio-setup
    - unity-localization
    - unity-2d-sprite-tilemap
    - unity-urp-setup
    - unity-ui-toolkit-build
    - unity-project-bootstrap
    - unity-input-system
    - unity-ui-automation
    - unity-editor-tools
---

# Play Mode Testing

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Control Play Mode, run EditMode/PlayMode tests, simulate input devices, and capture media. This skill is the runtime sibling of `unity-input-system` (asset authoring) and `unity-ui-automation` (UI interaction).

## Use When

- The user wants to run EditMode or PlayMode tests.
- The task requires runtime input simulation.
- The user wants screenshots or video from the current game or editor state.
- The user wants to inspect current test progress or runtime state.

## Do Not Use When

- Configure AudioClip import, AudioMixer routing and AudioSource playback as one
  verified workflow: use `unity-audio-setup`.

- Configure Locale/String Table assets and verify localized UI text: `unity-localization`.
- Build and verify a complete sprite / Tilemap / Pixel Perfect workflow: `unity-2d-sprite-tilemap`.

- Build a complete UXML/USS UI Toolkit screen and verify its interactions: use `unity-ui-toolkit-build`.

- The request starts by creating a new project and installing its bridge; use `unity-project-bootstrap` to orchestrate the complete workflow.
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

### CI test command

Use typed `unity-cli test` for a bounded CI run. It connects to the project's
existing Editor or automatically starts a headless Editor when none is running,
waits for readiness, polls to completion, writes a report, and closes only the
Editor it started. The project needs the Bridge and Unity Test Framework
(`com.unity.test-framework`); use `unity-package-management` to install an
Editor-compatible Test Framework and wait for compilation before running tests.
The Unity version must be installed/licensed (`UNITY_EDITOR_PATH` can select it).

```bash
unity-cli --project-path /path/to/project test --mode editmode --report junit --output artifacts/editmode.xml --output-format github
unity-cli --project-path /path/to/project test --mode playmode --report nunit --output artifacts/playmode.xml --timeout 600
```

`--report junit` (default) writes JUnit XML; `--report nunit` writes NUnit XML.
`--output-format github` additionally emits GitHub Actions failure annotations.
For `test`, `--output` names the report file; `--output json` is not the report
selector. `--filter` narrows tests, and `--disable-domain-reload` is optional for
PlayMode. Exit 0 means passed; exit 8 (`TEST_FAILED`) means completed with failed
tests. Other nonzero exits mean setup, compilation, timeout or execution failed.
This is a Bridge-managed host, not Unity's native `-runTests` process.

### Input simulation and InputAction notifications

Requires Input System enabled and a running Play session. Replace the asset path
with an existing input asset. Enable the intended action map in the game's runtime
code and observe its `performed`/`canceled` callback (for example, log a counter).
`get_input_actions_state` inspects asset maps/actions/bindings; it does **not** prove
that callbacks fired, and a PlayerInput runtime copy can differ from the asset.

```bash
unity-cli raw input_keyboard --json '{"key":"space","action":"press","holdSeconds":0.2}'
unity-cli raw input_gamepad --json '{"action":"button","button":"a","buttonAction":"press","holdSeconds":0.2}'
unity-cli raw input_mouse --json '{"action":"move","x":100,"y":200,"absolute":true}'
unity-cli raw input_touch --json '{"action":"tap","x":100,"y":200,"touchId":0}'
unity-cli raw create_input_sequence --json '{"sequence":[{"type":"keyboard","params":{"action":"press","key":"space","holdSeconds":0.1}},{"type":"mouse","params":{"action":"move","x":100,"y":200,"absolute":true}}],"delayBetween":80}'
unity-cli raw get_input_actions_state --json '{"assetPath":"Assets/Input/Player.inputactions","includeBindings":true}'
unity-cli raw read_console --json '{"count":20}'
```

Check the callback evidence after sending input. `holdSeconds` holds a press across
frames before release; sequence `delayBetween` is milliseconds. Device state alone
is not evidence of a gameplay notification. For gamepad sticks, x/y specify
individual processed axes; a diagonally processed Vector2 can differ.

### Video and PNG sequences

Requires a graphics-enabled Editor, a visible Game View, and Unity Recorder
(`com.unity.recorder` >= 4.0). On `RECORDER_PACKAGE_MISSING`, use
`unity-package-management` to install an Editor-compatible Recorder >= 4.0,
then wait for reload/idle compilation and verify the resolved version:

```bash
unity-cli raw package_manager --json '{"action":"install","packageId":"com.unity.recorder","version":"4.0.0"}'
unity-cli raw package_manager --json '{"action":"list"}'
unity-cli raw get_compilation_state --json '{}'
```

The example pins the minimum; select a newer compatible version when required
by the project's Editor. Headless `-nographics` cannot capture Game View video.
Start one session, allow frames to render, inspect status, then stop before starting
another. `format` accepts `mp4`, `webm`, or `png_sequence` (run the same flow for each).

```bash
unity-cli raw capture_video_start --json '{"captureMode":"game","format":"png_sequence","width":320,"height":180,"fps":10,"maxDurationSec":0}'
unity-cli raw capture_video_status --json '{}'
unity-cli raw capture_video_stop --json '{}'
```

Check `isRecording` and the final `outputPath`. For PNG sequences it names the first
frame in a unique session directory; verify subsequent PNG files exist too.

### Tests with Domain Reload enabled

Domain Reload may remain enabled in Enter Play Mode Settings. Start tests from
Edit Mode; the bridge persists and recovers the result across the reload. Poll
status until complete, including results, and inspect passed/failed counts. A
temporary disconnect during reload is not a failed test; reconnect and query status
instead of submitting a duplicate test run.

```bash
unity-cli raw run_tests --json '{"testMode":"PlayMode"}'
unity-cli raw get_test_status --json '{"includeTestResults":true}'
```

### Method hot reload preview

Requires optional FastScriptReload 1.8.0 (`com.handzlikchris.fastscriptreload`,
supported package range `[1.8.0,1.8.1)`). Use `unity-package-management` to install
this pinned Git URL as `packageId`, then verify the resolved package and idle compilation:

```bash
unity-cli raw package_manager --json '{"action":"install","packageId":"https://github.com/handzlikchris/FastScriptReload.git?path=Assets#51140b71d9e5df1de231b33ec20ee089b18bebec"}'
```

Supported hosts are Mono Editors, Unity 2022.3 or later, on x64 (Windows / macOS /
Linux) or Apple Silicon macOS. macOS ARM64/x64 and Windows x64 have real replacement
evidence; Linux hardware verification remains pending (see `docs/hot-reload.md`
in the unity-cli source). Other hosts return `HOT_RELOAD_PLATFORM_UNSUPPORTED`;
Missing packages return `HOT_RELOAD_PACKAGE_MISSING`; an installed backend with an
unavailable adapter (e.g. an unsupported version) returns `HOT_RELOAD_ADAPTER_UNAVAILABLE`.
Also requires a compiled baseline and Play Mode. Replace the path with an existing eligible script.
Disable Fast Script Reload's automatic and on-demand reload in its settings first;
the bridge rejects concurrent patchers rather than changing those preferences.
Inspect `supported` before beginning; do not report an unsupported preview as a pass.

```bash
unity-cli raw hot_reload_status --json '{}'
unity-cli raw hot_reload --json '{"action":"begin","path":"Assets/HotReloadProbe.cs"}'
unity-cli raw hot_reload --json '{"action":"recover"}'
```

`begin` returns `appliedRevision`; to apply complete candidate source and verify the
actual method change, follow the [Hot Reload Preview Loop](../unity-development-loop/references/development-loop-playbook.md#hot-reload-preview-loop).
`recover` stops Play and recompiles; poll `hot_reload_status` until idle. Preview
does not save source. Persist a verified edit through `unity-csharp-edit` afterwards.
If `begin` returns `HOT_RELOAD_BASELINE_UNPROVEN`, run `recover`, wait for idle,
then re-enter Play Mode and begin again against the freshly compiled baseline.

- "Run PlayMode tests for the player flow and report the result."
- "Enter Play Mode, press space, and capture a screenshot."
- "Record a 5 second gameplay clip and stop automatically."

## References

- [runtime-checklist.md](references/runtime-checklist.md): connection and instance prerequisites.
- [playmode-test-loop.md](references/playmode-test-loop.md): clean execution loop for entering Play Mode, sending input, waiting for results, and capturing evidence.
