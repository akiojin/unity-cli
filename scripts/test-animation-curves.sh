#!/usr/bin/env bash
# Focused Editor regressions with a structured result gate (zero tests is failure).
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${PROJECT_ROOT}/ProjectSettings/ProjectVersion.txt")"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity}"
[[ -x "${UNITY_PATH}" ]] || { echo "Unity not found: ${UNITY_PATH}" >&2; exit 1; }
RUN_DIR="$(mktemp -d /tmp/unity-cli-animation-tests.XXXXXX)"
echo "Unity ${VERSION}; results: ${RUN_DIR}"
"${UNITY_PATH}" -batchmode -nographics -projectPath "${PROJECT_ROOT}" \
  -runTests -testPlatform EditMode \
  -testFilter 'AnimationCurveHandlerTests;AssetManagementHandlerTests;BridgeCommandRouterTests' \
  -testResults "${RUN_DIR}/results.xml" -logFile "${RUN_DIR}/editor.log"
python3 - "${RUN_DIR}/results.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
tests = root.findall('.//test-case')
for test in tests:
    print(f"{test.get('result')}: {test.get('fullname')}")
print(f"Unity Editor tests: {root.get('passed')} passed, {root.get('failed')} failed, {root.get('skipped')} skipped")
assert tests and all(t.get('result') == 'Passed' for t in tests), root.attrib
assert any('AnimationCurveHandlerTests' in t.get('fullname', '') for t in tests)
PY
