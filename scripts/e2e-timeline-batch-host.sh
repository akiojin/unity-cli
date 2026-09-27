#!/usr/bin/env bash
# Self-contained real Editor E2E, optionally in a project without Timeline.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
PORT="${UNITY_CLI_PORT:-6474}"
WITHOUT_TIMELINE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --without-timeline) WITHOUT_TIMELINE=1; shift ;;
    *) echo "Usage: $0 [--port N] [--without-timeline]" >&2; exit 2 ;;
  esac
done
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
UNITY_CLI="${UNITY_CLI:-${REPO_ROOT}/target/debug/unity-cli}"
[[ -x "${UNITY_PATH}" && -x "${UNITY_CLI}" ]] || { echo "Build unity-cli and install Unity ${VERSION}." >&2; exit 1; }
if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port ${PORT} already in use; choose a free port." >&2
  exit 1
fi
RUN_DIR="$(mktemp -d /tmp/unity-cli-timeline-e2e.XXXXXX)"
LOG="${RUN_DIR}/editor.log"
STOP="${RUN_DIR}/stop"
HOST_PID=""
cleanup() {
  if [[ -n "${HOST_PID}" ]] && kill -0 "${HOST_PID}" 2>/dev/null; then
    touch "${STOP}"
    for _ in {1..15}; do
      kill -0 "${HOST_PID}" 2>/dev/null || break
      sleep 1
    done
    kill "${HOST_PID}" 2>/dev/null || true
    wait "${HOST_PID}" 2>/dev/null || true
  fi
  echo "Unity ${VERSION}; artifacts: ${RUN_DIR}"
}
trap cleanup EXIT
EXTRA=(--port "${PORT}" --unity-cli "${UNITY_CLI}")
if [[ "${WITHOUT_TIMELINE}" == 1 ]]; then
  ISOLATED="${RUN_DIR}/NoTimeline"
  mkdir -p "${ISOLATED}/Assets/Editor" "${ISOLATED}/Packages"
  cp -R "${PROJECT_ROOT}/ProjectSettings" "${ISOLATED}/ProjectSettings"
  cp -R "${PROJECT_ROOT}/Packages/unity-cli-bridge" "${ISOLATED}/Packages/unity-cli-bridge"
  cp "${PROJECT_ROOT}/Assets/Editor/UnityCliInputBatchHost.cs" "${ISOLATED}/Assets/Editor/"
  # A minimal fixture avoids transitive Timeline dependencies in project tooling.
  python3 - "${PROJECT_ROOT}/Packages/manifest.json" "${ISOLATED}/Packages/manifest.json" <<'PY'
import json,sys
data=json.load(open(sys.argv[1]))
keep={'com.akiojin.unity-cli-bridge','com.unity.addressables','com.unity.inputsystem',
      'com.unity.test-framework','com.unity.ugui'}
data['dependencies']={k:v for k,v in data['dependencies'].items() if k in keep or k.startswith('com.unity.modules.')}
with open(sys.argv[2],'w') as f: json.dump(data,f,indent=2)
PY
  PROJECT_ROOT="${ISOLATED}"
  EXTRA+=(--without-timeline)
fi
echo "Starting Unity ${VERSION}, project=${PROJECT_ROOT}, port=${PORT}, log=${LOG}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT_ROOT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${LOG}" &
HOST_PID=$!
READY=0
for _ in {1..180}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    tail -60 "${LOG}"
    exit 1
  fi
  if "${UNITY_CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 2000 --output json >"${RUN_DIR}/ping.json" 2>/dev/null; then
    READY=1
    break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { tail -60 "${LOG}"; exit 1; }
if [[ "${WITHOUT_TIMELINE}" == 1 ]]; then
  python3 - "${PROJECT_ROOT}/Packages/packages-lock.json" <<'PY'
import json,sys
dependencies=json.load(open(sys.argv[1]))['dependencies']
assert 'com.unity.timeline' not in dependencies, 'Timeline was installed transitively'
assert 'com.unity.recorder' not in dependencies, 'Recorder brings Timeline transitively'
print('PASS package lock proves Timeline and Recorder absent', flush=True)
PY
fi
"${SCRIPT_DIR}/e2e-timeline.sh" "${EXTRA[@]}" 2>&1 | tee "${RUN_DIR}/results.log"
