#!/usr/bin/env bash
# Graphics-enabled batchmode is required for real lightmap baking.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
PORT="${UNITY_CLI_PORT:-6477}"
TARGETS="lighting,navmesh-legacy,navmesh-surface,occlusion"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --targets) TARGETS="$2"; shift 2 ;;
    *) echo "Usage: $0 [--port N] [--targets comma-separated-targets]" >&2; exit 2 ;;
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
RUN_DIR="$(mktemp -d /tmp/unity-cli-bake-e2e.XXXXXX)"
# develop auto-starts unityd: isolate this run from other Editor connections.
export UNITY_CLI_TOOLS_ROOT="${RUN_DIR}/tools"
export UNITY_CLI_NO_AUTO_UPDATE=1
LOG="${RUN_DIR}/editor.log"
STOP="${RUN_DIR}/stop"
HOST_PID=""
cleanup() {
  "${UNITY_CLI}" unityd stop >/dev/null 2>&1 || true
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
echo "Starting Unity ${VERSION}, project=${PROJECT_ROOT}, port=${PORT}, log=${LOG}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  "${UNITY_PATH}" -batchmode -projectPath "${PROJECT_ROOT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${LOG}" &
HOST_PID=$!
READY=0
for _ in {1..180}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    tail -60 "${LOG}"
    exit 1
  fi
  # The pooled connection retains its first timeout; match the bake client.
  if "${UNITY_CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 120000 --output json >"${RUN_DIR}/ping.json" 2>/dev/null; then
    READY=1
    break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { tail -60 "${LOG}"; exit 1; }
"${SCRIPT_DIR}/e2e-bake.sh" --port "${PORT}" --unity-cli "${UNITY_CLI}" --project-root "${PROJECT_ROOT}" --targets "${TARGETS}" 2>&1 | tee "${RUN_DIR}/results.log"
