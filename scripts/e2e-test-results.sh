#!/usr/bin/env bash
# Verify leaf-only test counts through the CLI against an already running Editor.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
export UNITY_PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
export UNITY_CLI="${UNITY_CLI:-${REPO_ROOT}/target/release/unity-cli}"

python3 - <<'PY'
import json
import os
from pathlib import Path
import subprocess
import time

output = Path(os.environ["UNITY_PROJECT_ROOT"]) / ".unity" / "test-counting-e2e" / time.strftime("%Y%m%d-%H%M%S")
output.mkdir(parents=True)
cli = [os.environ["UNITY_CLI"], "--host", os.getenv("UNITY_CLI_HOST", "127.0.0.1"),
       "--port", os.getenv("UNITY_CLI_PORT", "6400"), "--timeout-ms", "30000", "--output", "json"]

def call(tool, params):
    run = subprocess.run(cli + ["raw", tool, "--json", json.dumps(params)],
                         text=True, capture_output=True, timeout=40)
    with (output / "calls.jsonl").open("a") as log:
        log.write(json.dumps({"tool": tool, "params": params, "stdout": run.stdout,
                              "stderr": run.stderr, "exit": run.returncode}) + "\n")
    if run.returncode:
        raise RuntimeError(f"{tool}: {run.stderr} {run.stdout}")
    data = json.loads(run.stdout)
    if data.get("error") or data.get("status") == "error":
        raise RuntimeError(f"{tool}: {data}")
    return data

print(f"Artifacts: {output}", flush=True)
state = call("get_editor_state", {})
print(f"Editor: {json.dumps(state)}", flush=True)
assert not any(state["state"][key] for key in ("isCompiling", "isPlaying", "isUpdating")), state
settings = call("get_project_settings", {"includeEditor": True})["editor"]
assert settings["enterPlayModeOptionsEnabled"] and "DisableDomainReload" in settings["enterPlayModeOptions"], \
    "This E2E requires Domain Reload disabled, as configured in UnityCliBridge/ProjectSettings/EditorSettings.asset"

def run_tests(mode, fixture, export_name):
    started = call("run_tests", {"testMode": mode, "filter": fixture, "includeDetails": True,
                                 "exportPath": str(output / export_name)})
    assert started.get("status") == "running", started
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        status = call("get_test_status", {"includeTestResults": True, "includeFileContent": True})
        if status.get("status") == "completed":
            assert status.get("runId") == started.get("runId"), status
            (output / (mode + "-status.json")).write_text(json.dumps(status, indent=2))
            return status
        assert status.get("status") == "running", status
        time.sleep(1)
    raise AssertionError(f"{mode} test run did not complete within 180 seconds")

unit = run_tests("EditMode", "TestExecutionHandlerCollectorTests", "collector-export.json")
assert unit.get("totalTests") == 7 and unit.get("passedTests") == 7 and unit.get("failedTests") == 0, unit
assert unit.get("success") is True and unit["failures"] == [], unit
assert len(unit["tests"]) == 7 and all(t["status"] == "Passed" for t in unit["tests"]), unit
print("PASS: 7/7 collector regression tests; 0 failures.", flush=True)

status = run_tests("PlayMode", "TestResultCountingPlayModeTests", "export.json")

(output / "status.json").write_text(json.dumps(status, indent=2))
assert status.get("totalTests") == 2 and status.get("passedTests") == 2, status
assert status.get("success") is True and status["failures"] == [], status
assert all(status.get(key) == 0 for key in ("failedTests", "skippedTests", "inconclusiveTests")), status
expected = {"FrameAdvances", "RigidbodyFalls"}
assert len(status["tests"]) == 2 and {t["name"] for t in status["tests"]} == expected, status
assert all(t["status"] == "Passed" for t in status["tests"]), status

latest = status["latestResult"]
assert latest.get("status") == "available", latest
for summary in (latest["summary"], json.loads(latest["fileContent"]),
                json.loads((output / "export.json").read_text())):
    assert summary["totalTests"] == 2 and summary["passed"] == 2, summary
    assert all(summary[key] == 0 for key in ("failed", "skipped", "inconclusive")), summary
    assert len(summary["tests"]) == 2 and {t["name"] for t in summary["tests"]} == expected, summary
print("PASS: 2/2 PlayMode tests; CLI counts, leaf results, latest result and export agree; 0 failures.", flush=True)
PY
