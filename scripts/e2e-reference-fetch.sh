#!/usr/bin/env bash
# Verify reference_fetch against a live Editor and an isolated reference cache.
# Start an Editor listener first (see docs/development.md, Local Unity E2E).
# Downloads UnityCsReference under the task-authorized Unity Companion License.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI="${UNITY_CLI_BIN:-${REPO_ROOT}/target/debug/unity-cli}"
HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6452}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: scripts/e2e-reference-fetch.sh [--host HOST] [--port PORT]"
      echo "Requires a live Editor, python3, git, network access and UNITY_CLI_BIN (default: target/debug/unity-cli)."
      exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

if [[ ! -x "$CLI" ]]; then
  echo "ERROR: CLI not found: $CLI. Run cargo build or set UNITY_CLI_BIN." >&2
  exit 1
fi
command -v python3 >/dev/null
command -v git >/dev/null
ARTIFACT_ROOT="${REPO_ROOT}/UnityCliBridge/.unity/reference-fetch-e2e"
mkdir -p "$ARTIFACT_ROOT"
RUN_DIR="$(mktemp -d "${ARTIFACT_ROOT}/run-XXXXXX")"

python3 - "$CLI" "$HOST" "$PORT" "$RUN_DIR" <<'PY'
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

cli, host, port, artifact_path = sys.argv[1:]
artifacts = Path(artifact_path)
results = []
env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
base = [cli, "--host", host, "--port", port, "--output", "json"]


def invoke(name, args, expect_success=True):
    command = base + args
    (artifacts / f"{name}.command.json").write_text(json.dumps(command, indent=2))
    process = subprocess.run(command, env=env, capture_output=True, text=True, timeout=300)
    (artifacts / f"{name}.stdout.log").write_text(process.stdout)
    (artifacts / f"{name}.stderr.log").write_text(process.stderr)
    if not expect_success:
        assert process.returncode != 0, "Expected command to reject omitted branch"
        return process.stderr + process.stdout
    assert process.returncode == 0, process.stderr or process.stdout
    value = json.loads(process.stdout)
    assert value.get("ok") is not False and value.get("success") is not False, value
    return value


def check(name, function):
    try:
        function()
        results.append({"name": name, "passed": True})
        print(f"PASS: {name}", flush=True)
        return True
    except Exception as error:
        results.append({"name": name, "passed": False, "error": str(error)})
        print(f"FAIL: {name}: {error}", flush=True)
        return False


def editor_ready():
    invoke("editor-ping", ["system", "ping"])
    state = invoke("editor-state", ["raw", "get_editor_state", "--json", "{}"])
    assert '"isCompiling": false' in json.dumps(state), state
    assert '"isUpdating": false' in json.dumps(state), state
    info = invoke("editor-info", ["raw", "get_editor_info", "--json", "{}"])
    print("Editor: " + json.dumps(info), flush=True)


if check("live Editor readiness", editor_ready):
    with tempfile.TemporaryDirectory(prefix="unity-cli-reference-e2e-") as temporary:
        root = Path(temporary)
        project = root / "project"
        settings = project / "ProjectSettings"
        settings.mkdir(parents=True)
        (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.4.12f1\n")
        env["UNITY_CLI_CACHE_ROOT"] = str(root / "cache")
        expected_path = root / "cache" / "UnityCsReference" / "6000.4.12f1"

        def fetch(name, extra, skipped):
            params = {"projectRoot": str(project), "acceptLicense": True, **extra}
            request = artifacts / f"{name}.request.json"
            request.write_text(json.dumps(params, indent=2))
            result = invoke(name, ["raw", "reference_fetch", "--params-file", str(request)])
            assert result["ok"] is True, result
            assert result["version"] == "6000.4.12f1", result
            assert result["branch"] == "6000.4", result
            assert result.get("skipped", False) is skipped, result
            assert Path(result["path"]) == expected_path, result
            assert (expected_path / "Runtime").is_dir(), "Reference source was not downloaded"
            metadata = json.loads((expected_path / ".unity-cli-meta.json").read_text())
            assert metadata["branch"] == "6000.4", metadata
            assert metadata["version"] == "6000.4.12f1", metadata

        def omitted_branch():
            request = artifacts / "omitted-branch.request.json"
            request.write_text(json.dumps({"projectRoot": str(project), "acceptLicense": True}))
            error = invoke("omitted-branch", ["raw", "reference_fetch", "--params-file", str(request)], False)
            assert "6000.4.12f1" in error and "--branch" in error, error

        check("branch-only real fetch", lambda: fetch("branch-only", {"branch": "6000.4"}, False))
        check("branch-only cache reuse", lambda: fetch("cache-reuse", {"branch": "6000.4"}, True))
        check("omitted branch retains guidance", omitted_branch)
        check("explicit version and branch", lambda: fetch("explicit-both", {"version": "6000.4.12f1", "branch": "6000.4"}, True))

passed = sum(result["passed"] for result in results)
failed = len(results) - passed
summary = {"passed": passed, "failed": failed, "results": results}
(artifacts / "summary.json").write_text(json.dumps(summary, indent=2))
print(f"Passed: {passed}; Failed: {failed}; Total: {len(results)}")
print(f"Artifacts: {artifacts}")
sys.exit(1 if failed else 0)
PY
