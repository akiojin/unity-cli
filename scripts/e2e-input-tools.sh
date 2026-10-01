#!/usr/bin/env bash
# Deterministic E2E runner for input simulation tools against a running Unity Editor.

set -euo pipefail

HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6400}"
TIMEOUT_MS="${UNITY_CLI_TIMEOUT_MS:-120000}"
UNITY_CLI=""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${UNITY_PROJECT_ROOT:-${REPO_ROOT}/UnityCliBridge}"

RUN_ID="$(date +%Y%m%d-%H%M%S)"
LOG="/tmp/unity-cli-e2e-input-${RUN_ID}.log"
INPUT_SCENE_PATH="Assets/Scenes/Generated/E2E/UnityCli_InputSimulation_TestScene.unity"
STATUS_ELEMENT_PATH="/Canvas/InputE2E_Panel/InputE2E_StatusText"

usage() {
  cat <<EOF
Usage: scripts/e2e-input-tools.sh [options]

Options:
  --host <host>          Unity host (default: 127.0.0.1)
  --port <port>          Unity port (default: 6400)
  --timeout-ms <ms>      Per-command timeout in ms (default: 120000)
  --unity-cli <path>     unity-cli binary path

This script expects a Unity listener to already be running.
For a self-contained headless flow, use: scripts/e2e-input-batch-host.sh
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      HOST="$2"
      shift 2
      ;;
    --port)
      PORT="$2"
      shift 2
      ;;
    --timeout-ms)
      TIMEOUT_MS="$2"
      shift 2
      ;;
    --unity-cli)
      UNITY_CLI="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "${UNITY_CLI}" ]]; then
  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
  else
    UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  fi
fi

if [[ -z "${UNITY_CLI}" || ! -x "${UNITY_CLI}" ]]; then
  echo "ERROR: unity-cli not found. Build with 'cargo build --release' or install a release binary." >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required for this script." >&2
  exit 1
fi

export UNITY_PROJECT_ROOT="${PROJECT_ROOT}"

echo "Input Simulation E2E" | tee "${LOG}"
echo "  Run ID: ${RUN_ID}" | tee -a "${LOG}"
echo "  Host: ${HOST}" | tee -a "${LOG}"
echo "  Port: ${PORT}" | tee -a "${LOG}"
echo "  CLI: ${UNITY_CLI}" | tee -a "${LOG}"
echo "  UNITY_PROJECT_ROOT: ${UNITY_PROJECT_ROOT}" | tee -a "${LOG}"
echo "  Log: ${LOG}" | tee -a "${LOG}"
echo "" | tee -a "${LOG}"

LAST_OUTPUT=""

invoke_tool() {
  local tool="$1"
  local payload="${2:-'{}'}"
  "${UNITY_CLI}" tool call "${tool}" \
    --json "${payload}" \
    --host "${HOST}" \
    --port "${PORT}" \
    --timeout-ms "${TIMEOUT_MS}" \
    --output json | jq -c '.data'
}

run_tool() {
  local tool="$1"
  local payload="${2:-'{}'}"
  local output

  printf "  %-30s ... " "${tool}"
  output="$(invoke_tool "${tool}" "${payload}" 2>&1)"
  LAST_OUTPUT="${output}"

  {
    echo "===== ${tool} ====="
    echo "payload: ${payload}"
    echo "${output}"
    echo
  } >> "${LOG}"

  if ! jq -e . >/dev/null 2>&1 <<<"${output}"; then
    echo "FAIL"
    echo "Non-JSON response from ${tool}" >&2
    exit 1
  fi

  if jq -e 'type=="object" and ((has("error") and .error != null and (.error|tostring|length)>0) or (has("success") and .success == false) or (has("status") and ((.status|tostring|ascii_downcase)=="error")))' >/dev/null 2>&1 <<<"${output}"; then
    echo "FAIL"
    jq -r '.error // .message // .status // "unknown error"' <<<"${output}" >&2
    exit 1
  fi

  echo "PASS"
}

status_text() {
  invoke_tool "get_ui_element_state" "$(jq -nc --arg elementPath "${STATUS_ELEMENT_PATH}" '{elementPath:$elementPath,includeInteractableInfo:true}')" | jq -r '.text // empty'
}

status_value() {
  local key="$1"
  local text
  text="$(status_text)"
  awk -F= -v key="${key}" '$1 == key { print substr($0, length(key) + 2) }' <<<"${text}" | tail -n 1
}

wait_for_condition() {
  local description="$1"
  local command="$2"
  local attempts="${3:-60}"
  local sleep_seconds="${4:-0.2}"
  local i

  for ((i=0; i<attempts; i++)); do
    if eval "${command}" >/dev/null 2>&1; then
      return 0
    fi
    sleep "${sleep_seconds}"
  done

  echo "Timed out: ${description}" >&2
  return 1
}

wait_for_compile_idle() {
  wait_for_condition \
    "Unity compilation to settle" \
    "state=\"\$(invoke_tool get_editor_state '{}')\" && jq -e '.state.isCompiling == false and .state.isUpdating == false' >/dev/null <<<\"\${state}\""
}

wait_for_play_state() {
  local expected="$1"
  wait_for_condition \
    "Play Mode ${expected}" \
    "state=\"\$(invoke_tool get_editor_state '{}')\" && jq -e '.state.isPlaying == ${expected}' >/dev/null <<<\"\${state}\""
}

assert_json() {
  local json="$1"
  local jq_filter="$2"
  local description="$3"
  if ! jq -e "${jq_filter}" >/dev/null <<<"${json}"; then
    echo "Assertion failed: ${description}" >&2
    echo "${json}" >&2
    exit 1
  fi
}

assert_status_contains() {
  local expected="$1"
  if ! grep -Fq "${expected}" <<<"$(status_text)"; then
    echo "Assertion failed: status does not contain '${expected}'" >&2
    echo "$(status_text)" >&2
    exit 1
  fi
}

assert_status_int_ge() {
  local key="$1"
  local expected="$2"
  local actual
  actual="$(status_value "${key}")"
  if [[ -z "${actual}" || "${actual}" -lt "${expected}" ]]; then
    echo "Assertion failed: ${key} expected >= ${expected}, actual='${actual}'" >&2
    echo "$(status_text)" >&2
    exit 1
  fi
}

echo "--- Preparing input scene ---" | tee -a "${LOG}"
run_tool "execute_menu_item" '{"action":"execute","menuPath":"Tools/Unity CLI/Input Tests/Generate Input Simulation Test Scene","safetyCheck":true}'
wait_for_compile_idle
run_tool "load_scene" "$(jq -nc --arg scenePath "${INPUT_SCENE_PATH}" '{scenePath:$scenePath,loadMode:"Single"}')"
wait_for_compile_idle

echo "--- Entering Play Mode ---" | tee -a "${LOG}"
run_tool "play_game" '{"delayMs":100}'
wait_for_play_state "true"
wait_for_condition "input harness ready" "grep -Fq 'Ready=True' <<<\"\$(status_text)\""

echo "--- Keyboard ---" | tee -a "${LOG}"
run_tool "input_keyboard" '{"action":"press","key":"space"}'
state="$(invoke_tool get_current_input_state '{}')"
assert_json "${state}" '.keyboard.pressedKeys | index("space") != null' "keyboard press reflected in current state"

run_tool "input_keyboard" '{"action":"release","key":"space"}'
state="$(invoke_tool get_current_input_state '{}')"
assert_json "${state}" '.keyboard.pressedKeys | index("space") == null' "keyboard release reflected in current state"

run_tool "input_keyboard" '{"action":"type","text":"Hi"}'
wait_for_condition \
  "typed text to be observed" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.keyboard.lastTypedText == \"Hi\"' >/dev/null <<<\"\${state}\""

echo "--- Mouse ---" | tee -a "${LOG}"
run_tool "input_mouse" '{"action":"move","x":120,"y":140,"absolute":true}'
wait_for_condition \
  "mouse device reflected in current state" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.mouse != null and (.activeDevices | index(\"mouse\") != null) and (.mouse.deviceCount >= 1)' >/dev/null <<<\"\${state}\""

run_tool "input_mouse" '{"action":"click","button":"left","clickCount":1}'

run_tool "input_mouse" '{"action":"button","button":"left","buttonAction":"press","holdSeconds":0.1}'
sleep 0.15
wait_for_condition \
  "mouse hold auto-release reflected in current state" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.mouse.leftButton == false' >/dev/null <<<\"\${state}\""

run_tool "input_mouse" '{"action":"scroll","deltaX":0,"deltaY":10}'

echo "--- Gamepad ---" | tee -a "${LOG}"
run_tool "input_gamepad" '{"action":"button","button":"a","buttonAction":"press","holdSeconds":0.1}'
sleep 0.15
wait_for_condition \
  "gamepad device reflected in current state" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.gamepad != null and (.activeDevices | index(\"gamepad\") != null)' >/dev/null <<<\"\${state}\""

# Exercise the processed-axis contract through the CLI/listener boundary on both sticks.
for stick in left right; do
  for sample in '0.5 0' '0 -0.5' '0.5 0.75' '1 1' '0 0'; do
    read -r x y <<<"${sample}"
    payload="$(jq -nc --arg stick "${stick}" --argjson x "${x}" --argjson y "${y}" \
      '{action:"stick",stick:$stick,x:$x,y:$y}')"
    run_tool "input_gamepad" "${payload}"
    run_tool "get_current_input_state" '{}'
    assert_json "${LAST_OUTPUT}" \
      "(.gamepad.sticks.${stick}.x | type) == \"number\" and (.gamepad.sticks.${stick}.y | type) == \"number\" and ((.gamepad.sticks.${stick}.x - ${x}) | fabs) < 0.0001 and ((.gamepad.sticks.${stick}.y - ${y}) | fabs) < 0.0001" \
      "${stick} processed stick axes match (${x}, ${y})"
  done
done

run_tool "input_gamepad" '{"action":"trigger","trigger":"left","value":0.8}'

run_tool "input_gamepad" '{"action":"dpad","direction":"up"}'

echo "--- Touch ---" | tee -a "${LOG}"
run_tool "input_touch" '{"action":"tap","x":100,"y":200,"touchId":0}'

run_tool "input_touch" '{"action":"swipe","startX":100,"startY":100,"endX":300,"endY":120,"duration":20,"touchId":0}'
wait_for_condition \
  "touch device reflected in current state" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.touchscreen != null and (.touchscreen.deviceCount >= 1)' >/dev/null <<<\"\${state}\""

run_tool "input_touch" '{"action":"pinch","centerX":200,"centerY":200,"startDistance":80,"endDistance":180}'

run_tool "input_touch" '{"action":"multi","touches":[{"x":150,"y":180,"phase":"began"},{"x":210,"y":180,"phase":"moved"}]}'
wait_for_condition \
  "multi-touch reflected in current state" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '(.touchscreen.activeTouches | length) >= 2' >/dev/null <<<\"\${state}\""

echo "--- Input sequence ---" | tee -a "${LOG}"
run_tool "create_input_sequence" '{"sequence":[{"type":"keyboard","params":{"action":"press","key":"a","holdSeconds":0.05}},{"type":"mouse","params":{"action":"move","x":222,"y":111,"absolute":true}},{"type":"gamepad","params":{"action":"stick","stick":"left","x":0.25,"y":0.5}}],"delayBetween":80}'
assert_json "${LAST_OUTPUT}" '.success == true' "input sequence succeeded"
assert_json "${LAST_OUTPUT}" '.totalDurationMs >= 140' "input sequence applied real delay"

wait_for_condition \
  "sequence scheduled release completed" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.keyboard.pressedKeys | index(\"a\") == null' >/dev/null <<<\"\${state}\""
wait_for_condition \
  "sequence devices reflected" \
  "state=\"\$(invoke_tool get_current_input_state '{}')\" && jq -e '.mouse != null and .gamepad != null and (.activeDevices | index(\"mouse\") != null) and (.activeDevices | index(\"gamepad\") != null)' >/dev/null <<<\"\${state}\""

echo "--- Leaving Play Mode ---" | tee -a "${LOG}"
run_tool "stop_game" '{}'
wait_for_play_state "false"

echo "" | tee -a "${LOG}"
echo "Input simulation E2E passed." | tee -a "${LOG}"
