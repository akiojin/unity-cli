#!/usr/bin/env bash
# Always isolated: never install an optional backend into the working project.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PORT="${UNITY_CLI_PORT:-6484}"
EXPECT=missing
FSR_PATH=""
ARCH=""
RUN_DIR=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --expect) EXPECT="$2"; shift 2 ;;
    --fsr-path) FSR_PATH="$2"; shift 2 ;;
    --require-arch) ARCH="$2"; shift 2 ;;
    --artifacts) RUN_DIR="$2"; shift 2 ;;
    *) echo "Usage: $0 [--port N] [--expect missing|unsupported|supported] [--fsr-path /path/to/FSR/Assets] [--require-arch X64|Arm64] [--artifacts NEW_DIR]" >&2; exit 2 ;;
  esac
done
case "${EXPECT}" in missing|unsupported|supported) ;; *) exit 2 ;; esac
if [[ "${EXPECT}" != missing && ! -f "${FSR_PATH}/package.json" ]]; then
  echo "Provide the unmodified FastScriptReload 1.8.0 Assets directory with --fsr-path." >&2
  exit 2
fi
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${REPO_ROOT}/UnityCliBridge/ProjectSettings/ProjectVersion.txt")"
# An explicit Editor under <version>/Unity.app (Hub layout, or an x64 build kept elsewhere) selects its own version.
if [[ "${UNITY_PATH:-}" =~ /([0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+)/Unity\.app/ ]]; then VERSION="${BASH_REMATCH[1]}"; fi
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
UNITY_CLI="${UNITY_CLI:-${REPO_ROOT}/target/debug/unity-cli}"
[[ -x "${UNITY_PATH}" && -x "${UNITY_CLI}" ]] || { echo "Install Unity ${VERSION} and cargo build --bin unity-cli first." >&2; exit 1; }
if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port ${PORT} is already in use." >&2
  exit 1
fi
if [[ -n "${RUN_DIR}" ]]; then mkdir -p "${RUN_DIR}"; else RUN_DIR="$(mktemp -d /tmp/unity-cli-hot-reload-e2e.XXXXXX)"; fi
PROJECT="${RUN_DIR}/Project"
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
mkdir -p "${PROJECT}/Assets/Editor" "${PROJECT}/Packages"
cp -R "${REPO_ROOT}/UnityCliBridge/ProjectSettings" "${PROJECT}/ProjectSettings"
cp -R "${REPO_ROOT}/UnityCliBridge/Packages/unity-cli-bridge" "${PROJECT}/Packages/unity-cli-bridge"
cp "${REPO_ROOT}/UnityCliBridge/Assets/Editor/UnityCliInputBatchHost.cs" "${PROJECT}/Assets/Editor/"
cp "${REPO_ROOT}/tests/fixtures/hot-reload/HotReloadProbe.cs" "${PROJECT}/Assets/"
cp "${REPO_ROOT}/tests/fixtures/hot-reload/HotReloadE2EFixture.cs" "${PROJECT}/Assets/Editor/"
printf 'm_EditorVersion: %s\n' "${VERSION}" > "${PROJECT}/ProjectSettings/ProjectVersion.txt"
python3 - "${REPO_ROOT}/UnityCliBridge/Packages/manifest.json" "${PROJECT}/Packages/manifest.json" "${FSR_PATH}" "${VERSION}" "${UNITY_PATH}" <<'PY'
import json,sys
from pathlib import Path
data=json.load(open(sys.argv[1]))
keep={'com.akiojin.unity-cli-bridge','com.unity.addressables','com.unity.inputsystem','com.unity.test-framework','com.unity.ugui'}
data['dependencies']={k:v for k,v in data['dependencies'].items() if k in keep or k.startswith('com.unity.modules.')}
# The repository manifest tracks the newest Editor; older Editors need their own package lines.
resources=Path(sys.argv[5]).parent.parent/'Resources/PackageManager'
modules={p.name for p in (resources/'BuiltInPackages').iterdir() if p.is_dir()}
catalog=json.loads((resources/'Editor/manifest.json').read_text())['packages']
data['dependencies']={k:v for k,v in data['dependencies'].items() if not k.startswith('com.unity.modules.') or k in modules}
for name in ('com.unity.test-framework','com.unity.ugui'):
    data['dependencies'][name]=catalog[name]['version']
if sys.argv[4].startswith('2022.'):
    data['dependencies'].update({'com.unity.addressables':'1.22.3','com.unity.inputsystem':'1.14.2'})
if sys.argv[3]:
    fsr=Path(sys.argv[3]).resolve()
    package=json.loads((fsr/'package.json').read_text())
    assert package['version']=='1.8.0', 'Requires FastScriptReload 1.8.0'
    data['dependencies']['com.handzlikchris.fastscriptreload']='file:'+str(fsr)
with open(sys.argv[2],'w') as f: json.dump(data,f,indent=2)
PY
echo "Starting Unity ${VERSION}; project=${PROJECT}; mode=${EXPECT}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${RUN_DIR}/editor.log" &
HOST_PID=$!
READY=0
for _ in {1..180}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then tail -60 "${RUN_DIR}/editor.log"; exit 1; fi
  if "${UNITY_CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 2000 --output json >"${RUN_DIR}/ping.json" 2>/dev/null; then
    READY=1; break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { tail -60 "${RUN_DIR}/editor.log"; exit 1; }
python3 "${SCRIPT_DIR}/e2e-hot-reload.py" --project "${PROJECT}" --port "${PORT}" \
  --unity-cli "${UNITY_CLI}" --expect "${EXPECT}" ${ARCH:+--require-arch "${ARCH}"} 2>&1 | tee "${RUN_DIR}/results.log"
