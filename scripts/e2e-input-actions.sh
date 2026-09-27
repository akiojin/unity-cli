#!/usr/bin/env bash
# Verify InputAction types through the CLI against a running local Unity Editor.
set -euo pipefail

HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6400}"
TIMEOUT_MS="${UNITY_CLI_TIMEOUT_MS:-120000}"
UNITY_CLI=""
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
PASSED=0
FAILED=0
FIXTURE_DIR=""

usage() {
  cat <<EOF
Usage: scripts/e2e-input-actions.sh [options]
  --host <host>       Unity host (default: 127.0.0.1)
  --port <port>       Unity port (default: 6400)
  --timeout-ms <ms>   Per-command timeout (default: 120000)
  --unity-cli <path>  CLI binary path

The listener must serve this checkout's UnityCliBridge project.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --timeout-ms) TIMEOUT_MS="$2"; shift 2 ;;
    --unity-cli) UNITY_CLI="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "${UNITY_CLI}" ]]; then
  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
  else
    UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  fi
fi
if [[ ! -x "${UNITY_CLI}" ]]; then
  echo "ERROR: Build with cargo build --release or provide --unity-cli." >&2
  exit 1
fi
command -v jq >/dev/null
export UNITY_PROJECT_ROOT="${PROJECT_ROOT}"
LOG="$(mktemp /tmp/unity-cli-e2e-input-actions.XXXXXX)"

invoke_tool() {
  local output
  output="$("${UNITY_CLI}" tool call "$1" --json "$2" \
    --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --output json)" || return 1
  printf '%s\n%s\n' "$1" "${output}" >> "${LOG}"
  jq -e 'type == "object" and (.error == null) and (.success != false) and (.status != "error")' \
    >/dev/null <<<"${output}" || { printf '%s\n' "${output}" >&2; return 1; }
  printf '%s\n' "${output}"
}

cleanup() {
  local result=$?
  if [[ -n "${FIXTURE_DIR}" ]]; then
    # Only this run's mktemp directory and its Unity metadata belong to us.
    rm -f "${FIXTURE_DIR}/Fixture.inputactions" "${FIXTURE_DIR}/Fixture.inputactions.meta"
    if rmdir "${FIXTURE_DIR}"; then
      rm -f "${FIXTURE_DIR}.meta"
    fi
    invoke_tool refresh_assets '{}' >> "${LOG}" 2>&1 || true
  fi
  echo "Passed: ${PASSED}; Failed: ${FAILED}; Log: ${LOG}"
  return "${result}"
}
trap cleanup EXIT

"${UNITY_CLI}" system ping --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --output json >> "${LOG}"
mkdir -p "${PROJECT_ROOT}/Assets/Scenes/Generated/E2E"
FIXTURE_DIR="$(mktemp -d "${PROJECT_ROOT}/Assets/Scenes/Generated/E2E/InputActions.XXXXXX")"
ASSET_PATH="${FIXTURE_DIR#${PROJECT_ROOT}/}/Fixture.inputactions"
printf '%s\n' '{"name":"Fixture","maps":[],"controlSchemes":[]}' > "${FIXTURE_DIR}/Fixture.inputactions"
invoke_tool refresh_assets '{}' >/dev/null

assert_action() {
  local map_name="$1" action_name="$2" expected_type="$3" expected_control="$4" state
  if state="$(invoke_tool get_input_actions_state "$(jq -nc --arg assetPath "${ASSET_PATH}" '{assetPath:$assetPath,includeBindings:false}')")" &&
    jq -e --arg map "${map_name}" --arg action "${action_name}" \
      --arg type "${expected_type}" --arg control "${expected_control}" \
      '[.actionMaps[] | select(.name == $map) | .actions[] | select(.name == $action)] |
       length == 1 and .[0].type == $type and (.[0].expectedControlType // "") == $control' \
      >/dev/null <<<"${state}"; then
    echo "PASS ${map_name}/${action_name}: ${expected_type}, expectedControlType=${expected_control}"
    PASSED=$((PASSED + 1))
  else
    echo "FAIL ${map_name}/${action_name}: expected ${expected_type}, expectedControlType=${expected_control}" >&2
    printf '%s\n' "${state}" >&2
    FAILED=$((FAILED + 1))
  fi
}

# Test both public creation routes, including their omitted-type defaults.
for route in create_action_map add_input_action; do
  for requested_type in Button PassThrough Value omitted; do
    map_name="${route}_${requested_type}"
    expected_type="${requested_type}"
    case "${requested_type}" in
      Button|omitted) expected_type=Button; expected_control=Button ;;
      Value) expected_control=Vector2 ;;
      PassThrough) expected_control="" ;;
    esac
    payload="$(jq -nc --arg assetPath "${ASSET_PATH}" --arg mapName "${map_name}" '{assetPath:$assetPath,mapName:$mapName}')"
    if [[ "${route}" == create_action_map ]]; then
      payload="$(jq -c --arg type "${requested_type}" '. + {actions:[({name:"TestAction"} + (if $type == "omitted" then {} else {type:$type} end))]}' <<<"${payload}")"
      invoke_tool create_action_map "${payload}" >/dev/null
    else
      invoke_tool create_action_map "${payload}" >/dev/null
      payload="$(jq -c --arg type "${requested_type}" '. + {actionName:"TestAction"} + (if $type == "omitted" then {} else {actionType:$type} end)' <<<"${payload}")"
      invoke_tool add_input_action "${payload}" >/dev/null
    fi
    assert_action "${map_name}" TestAction "${expected_type}" "${expected_control}"
  done
done

[[ "${FAILED}" -eq 0 ]]
