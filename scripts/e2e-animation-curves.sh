#!/usr/bin/env bash
# Numeric AnimationClip curve integration checks against a dedicated Unity host.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI="${UNITY_CLI_BIN:-${REPO_ROOT}/target/release/unity-cli}"
PROJECT_ROOT="${UNITY_PROJECT_ROOT:-${REPO_ROOT}/UnityCliBridge}"
HOST="127.0.0.1"
PORT="6473"
TIMEOUT_MS="120000"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --unity-cli) CLI="$2"; shift 2 ;;
    --project-root) PROJECT_ROOT="$2"; shift 2 ;;
    --timeout-ms) TIMEOUT_MS="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
[[ -x "${CLI}" ]] || { echo "Build this checkout with cargo build --release first." >&2; exit 1; }
command -v jq >/dev/null
export UNITY_PROJECT_ROOT="${PROJECT_ROOT}"
RUN_ID="$(date +%Y%m%d-%H%M%S)-$$"
LOG="/tmp/unity-cli-animation-curves-${RUN_ID}.log"
CLIP="Assets/Scenes/Generated/E2E/AnimationCurves-${RUN_ID}.anim"
PASSED=0
FAILED=0
LAST_OUTPUT=""
trap 'echo "Animation curves E2E: ${PASSED} passed, ${FAILED} failed; log: ${LOG}"' EXIT

call() {
  local tool="$1" payload="$2" status=0
  LAST_OUTPUT="$("${CLI}" tool call "${tool}" --json "${payload}" --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --output json 2>&1)" || status=$?
  printf '%s\npayload: %s\nexit: %s\n%s\n' "${tool}" "${payload}" "${status}" "${LAST_OUTPUT}" >> "${LOG}"
  return "${status}"
}
check() {
  local description="$1" expression="$2"
  if jq -e "${expression}" >/dev/null 2>&1 <<< "${LAST_OUTPUT}"; then
    PASSED=$((PASSED + 1))
    echo "PASS: ${description}" | tee -a "${LOG}"
  else
    FAILED=$((FAILED + 1))
    echo "FAIL: ${description}: ${LAST_OUTPUT}" | tee -a "${LOG}" >&2
    exit 1
  fi
}
ok() {
  if ! call "$1" "$2"; then
    FAILED=$((FAILED + 1))
    echo "FAIL: $1: ${LAST_OUTPUT}" >&2
    exit 1
  fi
  check "$1 succeeds" 'type == "object" and (.error == null) and (.success != false)'
}
edit_payload() {
  jq -nc --arg clip "${CLIP}" --argjson root "${ROOT_ID}" --arg property "$1" --arg operation "$2" --argjson extra "$3" \
    '{clipPath:$clip,animationRoot:$root,binding:{path:"",component:"UnityEngine.Transform",property:$property},operation:$operation} + $extra'
}
read_curves() {
  ok get_animation_curves "$(jq -nc --arg clip "${CLIP}" '{clipPath:$clip}')"
}

mkdir -p "${UNITY_PROJECT_ROOT}/Assets/Scenes/Generated/E2E"
ok refresh_assets '{}'
ok create_gameobject "$(jq -nc --arg name "AnimationCurves-${RUN_ID}" '{name:$name}')"
check 'created root has integer id' '.id | type == "number"'
ROOT_ID="$(jq -r '.id' <<< "${LAST_OUTPUT}")"
ok edit_animation_curve "$(edit_payload localPosition.x set '{"createIfMissing":true,"keys":[{"time":0,"value":0,"leftTangentMode":"Linear","rightTangentMode":"Linear"},{"time":1,"value":2,"leftTangentMode":"Linear","rightTangentMode":"Linear"}]}')"
read_curves
check 'linear keys and slope encode midpoint value 1' '[.curves[] | select(.binding.property == "localPosition.x" or .binding.property == "m_LocalPosition.x")][0].keys | length == 2 and .[0].time == 0 and .[0].value == 0 and .[1].time == 1 and .[1].value == 2 and .[0].rightTangentMode == "Linear" and .[1].leftTangentMode == "Linear" and ((.[0].outTangent - 2) | fabs) < 0.0001 and ((.[1].inTangent - 2) | fabs) < 0.0001'
ORIGINAL="$(jq -c '.curves' <<< "${LAST_OUTPUT}")"
ok manage_asset_import_settings "$(jq -nc --arg clip "${CLIP}" '{action:"reimport",assetPath:$clip}')"
read_curves
check 'saved keys and interpolation survive reimport' ".curves == ${ORIGINAL}"
ok edit_animation_curve "$(edit_payload localPosition.y set '{"keys":[{"time":0,"value":7},{"time":1,"value":9}]}')"
read_curves
OTHER="$(jq -c '[.curves[] | select(.binding.property == "localPosition.y" or .binding.property == "m_LocalPosition.y")][0]' <<< "${LAST_OUTPUT}")"
ok edit_animation_curve "$(edit_payload localPosition.x upsert_keys '{"keys":[{"time":1,"value":4,"leftTangentMode":"Linear","rightTangentMode":"Linear"},{"time":2,"value":6}]}')"
read_curves
check 'existing key updated and new key inserted' '[.curves[] | select(.binding.property == "localPosition.x" or .binding.property == "m_LocalPosition.x")][0].keys | length == 3 and .[1].value == 4 and .[2].time == 2 and .[2].value == 6'
ok edit_animation_curve "$(edit_payload localPosition.x remove_keys '{"times":[1]}')"
read_curves
check 'requested key removed' '[.curves[] | select(.binding.property == "localPosition.x" or .binding.property == "m_LocalPosition.x")][0].keys | length == 2 and all(.time != 1)'
check 'other binding preserved after key updates and removal' "[.curves[] | select(.binding.property == \"localPosition.y\" or .binding.property == \"m_LocalPosition.y\")][0] == ${OTHER}"
ok edit_animation_curve "$(edit_payload localPosition.x remove_curve '{}')"
read_curves
check 'curve removed while other binding preserved' ".curves == [${OTHER}]"
call edit_animation_curve "$(edit_payload nonexistentProperty set '{"keys":[{"time":0,"value":0}]}')" || true
check 'invalid binding returns clear error' '(.error | type == "string" and length > 0)'
call edit_animation_curve "$(edit_payload localPosition.x set '{"clipPath":"Assets/Scenes/Generated/E2E/Unsupported.txt","createIfMissing":true,"keys":[{"time":0,"value":0}]}')" || true
check 'unsupported clip returns clear error' '(.error | type == "string" and length > 0)'
read_curves
check 'rejected edits preserve the original clip' ".curves == [${OTHER}]"
