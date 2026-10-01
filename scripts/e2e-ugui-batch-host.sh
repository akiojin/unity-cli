#!/usr/bin/env bash
# Real Editor E2E: the bridge compiles and starts in a fresh project with or
# without com.unity.ugui in its manifest, and uGUI tools work when present.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
SOURCE_PROJECT="${REPO_ROOT}/UnityCliBridge"
PORT="${UNITY_CLI_PORT:-6476}"
VERSION=""
WITHOUT_UGUI=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --unity-version) VERSION="$2"; shift 2 ;;
    --without-ugui) WITHOUT_UGUI=1; shift ;;
    *) echo "Usage: $0 --unity-version X [--port N] [--without-ugui]" >&2; exit 2 ;;
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
RUN_DIR="$(mktemp -d /tmp/unity-cli-ugui-e2e.XXXXXX)"
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
  echo "Unity ${VERSION}; without_ugui=${WITHOUT_UGUI}; artifacts: ${RUN_DIR}"
}
trap cleanup EXIT

# A fresh project: built-in modules, Test Framework and the bridge; uGUI only when requested.
mkdir -p "${PROJECT}/Assets/Editor" "${PROJECT}/Packages" "${PROJECT}/ProjectSettings"
# Without ProjectVersion.txt Unity treats the folder as new and injects template packages.
printf 'm_EditorVersion: %s\n' "${VERSION}" >"${PROJECT}/ProjectSettings/ProjectVersion.txt"
cp -R "${SOURCE_PROJECT}/Packages/unity-cli-bridge" "${PROJECT}/Packages/unity-cli-bridge"
cp "${SOURCE_PROJECT}/Assets/Editor/UnityCliInputBatchHost.cs" "${PROJECT}/Assets/Editor/"
python3 - "${SOURCE_PROJECT}/Packages/manifest.json" "${PROJECT}/Packages/manifest.json" "${VERSION}" "${WITHOUT_UGUI}" <<'PY'
import json,sys
source,target,version,without=sys.argv[1],sys.argv[2],sys.argv[3],sys.argv[4]=='1'
deps={k:v for k,v in json.load(open(source))['dependencies'].items() if k.startswith('com.unity.modules.')}
if version.startswith('2022.'):
    for m in ('accessibility','adaptiveperformance','vectorgraphics'):
        deps.pop(f'com.unity.modules.{m}',None)
deps['com.akiojin.unity-cli-bridge']='file:unity-cli-bridge'
# Every Unity new-project template ships the Test Framework; only uGUI is varied here.
deps['com.unity.test-framework']='1.1.33' if version.startswith('2022.') else '1.6.0'
if not without:
    deps['com.unity.ugui']='1.0.0' if version.startswith('2022.') else '2.0.0'
with open(target,'w') as f: json.dump({'dependencies':deps},f,indent=2)
PY

echo "Starting Unity ${VERSION}, project=${PROJECT}, port=${PORT}, log=${LOG}"
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  "${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT}" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run -logFile "${LOG}" &
HOST_PID=$!
READY=0
for _ in {1..240}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then
    grep -E 'error CS|Safe Mode' "${LOG}" | head -20 || true
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
[[ "${READY}" == 1 ]] || { grep -E 'error CS' "${LOG}" | head -20 || true; tail -40 "${LOG}"; echo "FAIL bridge listener never started" >&2; exit 1; }
echo "PASS bridge listener started on port ${PORT}"
if grep -q 'error CS' "${LOG}"; then
  grep 'error CS' "${LOG}" | head -20
  echo "FAIL compile errors in editor log" >&2
  exit 1
fi
echo "PASS no compile errors in editor log"

python3 - "${UNITY_CLI}" "${PORT}" "${PROJECT}" "${WITHOUT_UGUI}" <<'PY' 2>&1 | tee "${RUN_DIR}/results.log"
import json,os,subprocess,sys
cli,port,project,without=sys.argv[1],sys.argv[2],sys.argv[3],sys.argv[4]=='1'
def call(tool,payload):
    r=subprocess.run([cli,"raw",tool,"--json",json.dumps(payload),"--host","127.0.0.1","--port",port,
                      "--timeout-ms","120000","--output","json"],text=True,capture_output=True,
                     env={**os.environ,"UNITY_PROJECT_ROOT":project},timeout=150)
    assert r.returncode==0,(tool,r.stdout,r.stderr)
    data=json.loads(r.stdout)["data"]
    assert not data.get("error") and data.get("success") is not False,(tool,data)
    return data
info=call("get_editor_info",{})
print("PASS get_editor_info", info["unity"]["unityVersion"], flush=True)
manifest=json.load(open(os.path.join(project,"Packages","manifest.json")))["dependencies"]
assert ("com.unity.ugui" in manifest)!=without,("project manifest ugui mismatch",manifest.get("com.unity.ugui"))
print("PASS project manifest com.unity.ugui:", manifest.get("com.unity.ugui","<absent>"), flush=True)
lock=json.load(open(os.path.join(project,"Packages","packages-lock.json")))["dependencies"]
print("INFO com.unity.ugui in packages-lock:", json.dumps(lock.get("com.unity.ugui")), flush=True)
elements=call("find_ui_elements",{"uiSystem":"ugui"})
print("PASS find_ui_elements (empty scene)", json.dumps(elements), flush=True)
if without:
    sys.exit(0)
call("eval_csharp",{"mode":"statements","code":
    'var canvas = new UnityEngine.GameObject("E2ECanvas", typeof(UnityEngine.Canvas));'
    'var button = new UnityEngine.GameObject("E2EButton", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));'
    'button.transform.SetParent(canvas.transform, false);'
    'return "created";'})
found=call("find_ui_elements",{"uiSystem":"ugui","namePattern":"E2EButton"})
text=json.dumps(found)
assert "E2EButton" in text,found
print("PASS find_ui_elements finds uGUI Button", text, flush=True)
state=call("get_ui_element_state",{"elementPath":"/E2ECanvas/E2EButton","includeInteractableInfo":True})
assert "Button" in json.dumps(state),state
print("PASS get_ui_element_state reports Button", json.dumps(state), flush=True)
PY
