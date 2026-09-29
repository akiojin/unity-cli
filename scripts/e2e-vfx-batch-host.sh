#!/usr/bin/env bash
# Real Editor VFX acceptance; retain all evidence in the printed run directory.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
PORT="${UNITY_CLI_PORT:-6484}"
WITHOUT_VFX=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --without-vfx) WITHOUT_VFX=1; shift ;;
    *) echo "Usage: $0 [--port N] [--without-vfx]" >&2; exit 2 ;;
  esac
done
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
UNITY_CLI="${UNITY_CLI:-${REPO_ROOT}/target/debug/unity-cli}"
[[ -x "${UNITY_PATH}" && -x "${UNITY_CLI}" ]] || { echo "Build unity-cli and install Unity ${VERSION}." >&2; exit 1; }
if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port ${PORT} already in use; choose a free port." >&2; exit 1
fi
RUN_DIR="$(mktemp -d /tmp/unity-cli-vfx-e2e.XXXXXX)"
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
EXTRA=(-batchmode)
METHOD=UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run
if [[ "${WITHOUT_VFX}" == 1 ]]; then
  ISOLATED="${RUN_DIR}/NoVfx"
  mkdir -p "${ISOLATED}/Assets/Editor" "${ISOLATED}/Packages"
  cp -R "${PROJECT_ROOT}/ProjectSettings" "${ISOLATED}/ProjectSettings"
  cp -R "${PROJECT_ROOT}/Packages/unity-cli-bridge" "${ISOLATED}/Packages/unity-cli-bridge"
  cp "${PROJECT_ROOT}/Assets/Editor/UnityCliInputBatchHost.cs" "${ISOLATED}/Assets/Editor/"
  python3 - "${PROJECT_ROOT}/Packages/manifest.json" "${ISOLATED}/Packages/manifest.json" <<'PY'
import json,sys
with open(sys.argv[1]) as f: data=json.load(f)
keep={'com.akiojin.unity-cli-bridge','com.unity.addressables','com.unity.inputsystem',
      'com.unity.test-framework','com.unity.ugui'}
data['dependencies']={k:v for k,v in data['dependencies'].items() if k in keep or k.startswith('com.unity.modules.')}
data['testables']=['com.akiojin.unity-cli-bridge']
with open(sys.argv[2],'w') as f: json.dump(data,f,indent=2)
PY
  # Prove package tests were included in compilation, rather than silently skipped.
  cat > "${ISOLATED}/Assets/Editor/VfxE2EAssemblyProbe.cs" <<'CS'
using System;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
public static class VfxE2EAssemblyProbe
{
    public static void Run()
    {
        var names = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name).ToArray();
        File.WriteAllLines(Environment.GetEnvironmentVariable("VFX_E2E_ASSEMBLIES"), names);
        if (!names.Contains("UnityCliBridge.Editor") || !names.Contains("UnityCliBridge.Tests"))
            throw new Exception("Bridge and package test assemblies must compile in the no-VFX fixture");
        UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run();
    }
}
CS
  PROJECT_ROOT="${ISOLATED}"
  METHOD=VfxE2EAssemblyProbe.Run
  EXTRA=(-batchmode -nographics -extraScriptingDefines UNITY_INCLUDE_TESTS)
fi
echo "Starting Unity ${VERSION}, project=${PROJECT_ROOT}, port=${PORT}, log=${LOG}"
# Graphics deliberately enabled in VFX mode: MeshToSDFBaker needs compute shaders.
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE="${PORT}" UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE="${STOP}" \
  VFX_E2E_ASSEMBLIES="${RUN_DIR}/compiled-assemblies.txt" \
  "${UNITY_PATH}" "${EXTRA[@]}" -projectPath "${PROJECT_ROOT}" \
  -executeMethod "${METHOD}" -logFile "${LOG}" &
HOST_PID=$!
READY=0
for _ in {1..300}; do
  if ! kill -0 "${HOST_PID}" 2>/dev/null; then tail -60 "${LOG}"; exit 1; fi
  if UNITY_PROJECT_ROOT="${PROJECT_ROOT}" "${UNITY_CLI}" system ping --host 127.0.0.1 --port "${PORT}" --timeout-ms 2000 --output json >"${RUN_DIR}/ping.json" 2>/dev/null; then
    READY=1; break
  fi
  sleep 2
done
[[ "${READY}" == 1 ]] || { tail -60 "${LOG}"; exit 1; }
python3 - "${PROJECT_ROOT}" "${WITHOUT_VFX}" "${RUN_DIR}" <<'PY'
import json,sys,pathlib
project=pathlib.Path(sys.argv[1])
with open(project/'Packages/packages-lock.json') as f: dependencies=json.load(f)['dependencies']
if sys.argv[2]=='1':
    assert 'com.unity.visualeffectgraph' not in dependencies, 'VFX installed transitively'
    names=(pathlib.Path(sys.argv[3])/'compiled-assemblies.txt').read_text().splitlines()
    assert 'UnityCliBridge.Tests' in names and 'UnityCliBridge.Editor' in names, names
    print('PASS VFX absent; Bridge and test assemblies compiled',flush=True)
else:
    version=dependencies['com.unity.visualeffectgraph']['version']
    assert version=='17.4.0', version
    print('VFX Graph:',version,flush=True)
PY
EXTRA=(--port "${PORT}" --unity-cli "${UNITY_CLI}" --project "${PROJECT_ROOT}" --artifacts "${RUN_DIR}")
[[ "${WITHOUT_VFX}" == 0 ]] || EXTRA+=(--without-vfx)
"${SCRIPT_DIR}/e2e-vfx.sh" "${EXTRA[@]}" 2>&1 | tee "${RUN_DIR}/results.log"
