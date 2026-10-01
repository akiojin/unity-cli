#!/usr/bin/env bash
# Audio authoring and Editor regression tests in a disposable project.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
SOURCE_PROJECT="${REPO_ROOT}/UnityCliBridge"
PORT="${UNITY_CLI_PORT:-6481}"
VERSION=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --unity-version) VERSION="$2"; shift 2 ;;
    *) echo "Usage: $0 --unity-version X [--port N]" >&2; exit 2 ;;
  esac
done
[[ -n "${VERSION}" ]] || { echo "--unity-version is required (e.g. 6000.3.25f1, 2022.3.62f3)" >&2; exit 2; }
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
UNITY_CLI="${UNITY_CLI:-${REPO_ROOT}/target/debug/unity-cli}"
[[ -x "${UNITY_PATH}" && -x "${UNITY_CLI}" ]] || { echo "Build unity-cli and install Unity ${VERSION}." >&2; exit 1; }
if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port ${PORT} already in use; choose a free port." >&2
  exit 1
fi
RUN_DIR="$(mktemp -d /tmp/unity-cli-audio-e2e.XXXXXX)"
LOG="${RUN_DIR}/editor.log"
STOP="${RUN_DIR}/stop"
PROJECT="${RUN_DIR}/Project"
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

# Fresh project with built-in modules, Input System, Test Framework and bridge tests.
mkdir -p "${PROJECT}/Assets/Editor" "${PROJECT}/Packages" "${PROJECT}/ProjectSettings"
# Without ProjectVersion.txt Unity treats the folder as new and injects template packages.
printf 'm_EditorVersion: %s\n' "${VERSION}" >"${PROJECT}/ProjectSettings/ProjectVersion.txt"
cp -R "${SOURCE_PROJECT}/Packages/unity-cli-bridge" "${PROJECT}/Packages/unity-cli-bridge"
cp "${SOURCE_PROJECT}/Assets/Editor/UnityCliInputBatchHost.cs" "${PROJECT}/Assets/Editor/"
python3 - "${SOURCE_PROJECT}/Packages/manifest.json" "${PROJECT}/Packages/manifest.json" "${VERSION}" <<'PY'
import json,sys
source,target,version=sys.argv[1:4]
deps={k:v for k,v in json.load(open(source))['dependencies'].items() if k.startswith('com.unity.modules.')}
if version.startswith('2022.'):
    for m in ('accessibility','adaptiveperformance','vectorgraphics'):
        deps.pop(f'com.unity.modules.{m}',None)
deps['com.akiojin.unity-cli-bridge']='file:unity-cli-bridge'
# Enable Editor test discovery in the embedded bridge package.
deps['com.unity.test-framework']='1.1.33' if version.startswith('2022.') else '1.6.0'
deps['com.unity.inputsystem']='1.7.0' if version.startswith('2022.') else '1.14.2'
with open(target,'w') as f: json.dump({'dependencies':deps,'testables':['com.akiojin.unity-cli-bridge']},f,indent=2)
PY

echo "Starting Unity ${VERSION}, project=${PROJECT}, port=${PORT}, log=${LOG}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${LOG}" &
HOST_PID=$!
READY=0
for _ in {1..240}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    rg 'error CS|Safe Mode' "${LOG}" | head -20 || true
    tail -40 "${LOG}"
    echo "FAIL Unity exited before the bridge listener started" >&2
    exit 1
  fi
  if "${UNITY_CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 2000 --output json >"${RUN_DIR}/ping.json" 2>/dev/null; then
    READY=1
    break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { rg 'error CS' "${LOG}" | head -20 || true; tail -40 "${LOG}"; echo "FAIL bridge listener never started" >&2; exit 1; }
echo "PASS bridge listener started on port ${PORT}"
if rg -q 'error CS' "${LOG}"; then
  rg 'error CS' "${LOG}" | head -20
  echo "FAIL compile errors in editor log" >&2
  exit 1
fi
echo "PASS no compile errors in editor log"


python3 "${SCRIPT_DIR}/e2e-audio.py" "${UNITY_CLI}" "${PORT}" "${PROJECT}" "${RUN_DIR}" 2>&1 | tee "${RUN_DIR}/results.log"
