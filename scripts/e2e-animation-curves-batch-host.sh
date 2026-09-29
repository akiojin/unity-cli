#!/usr/bin/env bash
# Own a dedicated Unity process and TCP port for the animation curve E2E suite.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
CLI="${REPO_ROOT}/target/release/unity-cli"
PORT=6473
UNITY_PATH="${UNITY_PATH:-}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --unity-path) UNITY_PATH="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
[[ "${PORT}" =~ ^[0-9]+$ ]] && (( PORT > 0 && PORT < 65536 )) || { echo 'Invalid port' >&2; exit 1; }
[[ -x "${CLI}" ]] || { echo 'Run cargo build --release first.' >&2; exit 1; }
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
[[ -x "${UNITY_PATH}" ]] || { echo "Unity not found: ${UNITY_PATH}" >&2; exit 1; }
if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port ${PORT} already has a listener; refusing to use it." >&2
  exit 1
fi
RUN_ID="$(date +%Y%m%d-%H%M%S)-$$"
HOST_LOG="/tmp/unity-cli-animation-host-${RUN_ID}.log"
SHUTDOWN_FILE="/tmp/unity-cli-animation-stop-${RUN_ID}"
HOST_PID=""
cleanup() {
  if [[ -n "${HOST_PID}" ]] && kill -0 "${HOST_PID}" 2>/dev/null; then
    touch "${SHUTDOWN_FILE}"
    for _ in {1..15}; do
      kill -0 "${HOST_PID}" 2>/dev/null || break
      sleep 1
    done
    if kill -0 "${HOST_PID}" 2>/dev/null; then kill "${HOST_PID}" 2>/dev/null || true; fi
    wait "${HOST_PID}" 2>/dev/null || true
  fi
  rm -f "${SHUTDOWN_FILE}"
  echo "Unity host log: ${HOST_LOG}"
}
trap cleanup EXIT
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${SHUTDOWN_FILE}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT_ROOT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${HOST_LOG}" >/dev/null 2>&1 &
HOST_PID=$!
READY=0
for _ in {1..180}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    echo 'Unity exited before readiness.' >&2
    tail -n 80 "${HOST_LOG}" >&2 || true
    exit 1
  fi
  if lsof -nP -a -p "${HOST_PID}" -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1 &&
     "${CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 10000 --output json >/dev/null 2>&1; then
    READY=1
    break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { echo 'Unity did not become ready.' >&2; tail -n 80 "${HOST_LOG}" >&2; exit 1; }
echo "Unity ${VERSION}; binary ${UNITY_PATH}; owned PID ${HOST_PID}; port ${PORT}"
"${SCRIPT_DIR}/e2e-animation-curves.sh" --port "${PORT}"
touch "${SHUTDOWN_FILE}"
for _ in {1..30}; do
  kill -0 "${HOST_PID}" 2>/dev/null || break
  sleep 1
done
if kill -0 "${HOST_PID}" 2>/dev/null; then echo 'Unity did not shut down cleanly.' >&2; exit 1; fi
wait "${HOST_PID}"
HOST_PID=""
