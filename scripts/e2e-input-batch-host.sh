#!/usr/bin/env bash
# Launch Unity in headless batch host mode and run the input simulation E2E suite against it.

set -euo pipefail

HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6402}"
TIMEOUT_MS="${UNITY_CLI_TIMEOUT_MS:-120000}"
UNITY_PATH="${UNITY_PATH:-}"
SUITE="input"
UNITY_CLI="${UNITY_CLI:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
RUN_ID="$(date +%Y%m%d-%H%M%S)"
HOST_LOG="/tmp/unity-cli-input-batch-host-${RUN_ID}.log"
HOST_PID=""
SHUTDOWN_FILE="/tmp/unity-cli-batch-host-stop-${PORT}"

usage() {
  cat <<EOF
Usage: scripts/e2e-input-batch-host.sh [options]

Options:
  --host <host>          Unity host (default: 127.0.0.1)
  --port <port>          Unity port (default: 6402)
  --timeout-ms <ms>      Per-command timeout in ms (default: 120000)
  --unity-path <path>    Unity binary path (default: editor version from ProjectVersion.txt)
  --suite <input|eval>   E2E suite (default: input)
  --unity-cli <path>     CLI binary to use for readiness and the selected suite
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
    --unity-path)
      UNITY_PATH="$2"
      shift 2
      ;;
    --suite)
      SUITE="$2"
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

if [[ "${SUITE}" != input && "${SUITE}" != eval ]]; then
  echo "ERROR: --suite must be input or eval" >&2
  exit 1
fi

if [[ -z "${UNITY_CLI}" ]]; then
  UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
  fi
fi
if [[ ! -x "${UNITY_CLI}" ]]; then
  echo "ERROR: unity-cli not found. Build first and pass --unity-cli." >&2
  exit 1
fi

if [[ ! -x "${UNITY_PATH}" ]]; then
  project_version_file="${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt"
  if [[ -z "${UNITY_PATH}" && -f "${project_version_file}" ]]; then
    project_editor_version="$(sed -n 's/^m_EditorVersion: //p' "${project_version_file}" | head -n 1)"
    if [[ -n "${project_editor_version}" ]]; then
      candidate="/Applications/Unity/Hub/Editor/${project_editor_version}/Unity.app/Contents/MacOS/Unity"
      if [[ -x "${candidate}" ]]; then
        UNITY_PATH="${candidate}"
      fi
    fi
  fi
fi

if [[ ! -x "${UNITY_PATH}" ]]; then
  echo "ERROR: Unity binary not found. Set UNITY_PATH or install the editor version from ${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt." >&2
  exit 1
fi

cleanup() {
  rm -f "${SHUTDOWN_FILE}" >/dev/null 2>&1 || true
  if [[ -n "${HOST_PID}" ]] && kill -0 "${HOST_PID}" >/dev/null 2>&1; then
    cli="${UNITY_CLI}"
    if [[ -n "${cli}" ]]; then
      "${cli}" tool call quit_editor --json '{}' --host "${HOST}" --port "${PORT}" --timeout-ms 10000 --output json >/dev/null 2>&1 || true
    fi
    touch "${SHUTDOWN_FILE}" >/dev/null 2>&1 || true
    sleep 2
    kill "${HOST_PID}" >/dev/null 2>&1 || true
    wait "${HOST_PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# A previous host may have exited before cleanup's stop-file fallback was read.
rm -f "${SHUTDOWN_FILE}"
echo "Launching Unity batch host on ${HOST}:${PORT}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${SHUTDOWN_FILE}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT_ROOT}" -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${HOST_LOG}" >/dev/null 2>&1 &
HOST_PID=$!

for _ in {1..180}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    echo "ERROR: Unity batch host exited before listening; log: ${HOST_LOG}" >&2
    tail -n 80 "${HOST_LOG}" >&2
    exit 1
  fi
  if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

if ! lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "ERROR: Unity batch host did not start listening on ${HOST}:${PORT}" >&2
  tail -n 120 "${HOST_LOG}" >&2 || true
  exit 1
fi

cli="${UNITY_CLI}"

if [[ -z "${cli}" ]]; then
  echo "ERROR: unity-cli not found for readiness checks." >&2
  exit 1
fi

for _ in {1..120}; do
  if "${cli}" system ping --host "${HOST}" --port "${PORT}" --output json >/dev/null 2>&1 &&
     "${cli}" tool call get_editor_state --json '{}' --host "${HOST}" --port "${PORT}" --output json >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

if ! "${cli}" system ping --host "${HOST}" --port "${PORT}" --output json >/dev/null 2>&1; then
  echo "ERROR: Unity batch host did not become ready on ${HOST}:${PORT}" >&2
  tail -n 120 "${HOST_LOG}" >&2 || true
  exit 1
fi

echo "Running ${SUITE} E2E against batch host; Editor log: ${HOST_LOG}"
if [[ "${SUITE}" == eval ]]; then
  "${SCRIPT_DIR}/e2e-eval.sh" --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --unity-cli "${cli}"
else
  "${SCRIPT_DIR}/e2e-input-tools.sh" --host "${HOST}" --port "${PORT}" --timeout-ms "${TIMEOUT_MS}" --unity-cli "${cli}"
fi

echo "Batch host log: ${HOST_LOG}"
