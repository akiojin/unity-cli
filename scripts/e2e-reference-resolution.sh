#!/usr/bin/env bash
# Live Editor + public UnityCsReference resolution E2E (isolated temporary caches).
# Start the Editor listener first; see docs/development.md, Local Unity E2E.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI="${UNITY_CLI_BIN:-${REPO_ROOT}/target/debug/unity-cli}"
HOST="${UNITY_CLI_HOST:-127.0.0.1}"
PORT="${UNITY_CLI_PORT:-6400}"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"
ARTIFACT_ROOT="${REPO_ROOT}/UnityCliBridge/.unity/reference-resolution-e2e"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host|--port|--cli|--project-root|--artifacts)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Missing value for $1" >&2
        exit 1
      fi
      case "$1" in
        --host) HOST="$2" ;;
        --port) PORT="$2" ;;
        --cli) CLI="$2" ;;
        --project-root) PROJECT_ROOT="$2" ;;
        --artifacts) ARTIFACT_ROOT="$2" ;;
      esac
      shift 2 ;;
    -h|--help)
      echo "Usage: scripts/e2e-reference-resolution.sh [--host HOST] [--port PORT] [--cli PATH] [--project-root PATH] [--artifacts DIR]"
      echo "Requires a live Editor, python3, git, and network access. Downloads UnityCsReference under its Unity Companion License."
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
mkdir -p "$ARTIFACT_ROOT"
RUN_DIR="$(mktemp -d "${ARTIFACT_ROOT}/run-XXXXXX")"

python3 - "$CLI" "$HOST" "$PORT" "$PROJECT_ROOT" "$RUN_DIR" <<'PY'
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile

cli, host, port, project_path, artifact_path = sys.argv[1:]
project = Path(project_path).resolve()
artifacts = Path(artifact_path)
results = []
env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
base = [str(Path(cli).resolve()), "--host", host, "--port", port, "--output", "json"]


def invoke(name, args):
    command = base + args
    (artifacts / f"{name}.command.json").write_text(json.dumps(command, indent=2))
    process = subprocess.run(command, env=env, capture_output=True, text=True, timeout=600)
    (artifacts / f"{name}.stdout.log").write_text(process.stdout)
    (artifacts / f"{name}.stderr.log").write_text(process.stderr)
    assert process.returncode == 0, process.stderr or process.stdout
    value = json.loads(process.stdout)
    assert value.get("ok") is not False and value.get("success") is not False, value
    return value


def raw(name, tool, params):
    request = artifacts / f"{name}.request.json"
    request.write_text(json.dumps(params, indent=2))
    return invoke(name, ["raw", tool, "--params-file", str(request)])


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
    state = raw("editor-state", "get_editor_state", {})
    assert '"isCompiling": false' in json.dumps(state), state
    assert '"isUpdating": false' in json.dumps(state), state
    raw("editor-info", "get_editor_info", {})
    assert (project / "ProjectSettings" / "ProjectVersion.txt").is_file(), project


def read_version(root):
    contents = (root / "ProjectSettings" / "ProjectVersion.txt").read_text()
    match = re.search(r"^m_EditorVersion:\s*(\S+)", contents, re.MULTILINE)
    assert match, contents
    return match.group(1)


def scenario(name, root, cache_root, explicit=False, exact_match=None):
    env["UNITY_CLI_CACHE_ROOT"] = str(cache_root)
    version = read_version(root)
    params = {"projectRoot": str(root), "acceptLicense": True}
    if explicit:
        params["branch"] = "6000.4"
    fetched = raw(f"{name}-fetch", "reference_fetch", params)
    assert fetched["ok"] is True and not fetched.get("skipped", False), fetched
    assert fetched["version"] == version, fetched
    assert isinstance(fetched["exactMatch"], bool), fetched
    for key in ("branch", "sourceRef", "commitSha", "selectionReason", "path"):
        assert isinstance(fetched[key], str) and fetched[key], fetched
    assert re.fullmatch(r"[0-9a-fA-F]{40}", fetched["commitSha"]), fetched
    if exact_match is not None:
        assert fetched["exactMatch"] is exact_match, fetched
    if explicit:
        assert fetched["branch"] == "6000.4", fetched
    cache = cache_root / "UnityCsReference" / version
    assert Path(fetched["path"]) == cache, fetched
    assert (cache / "Runtime").is_dir(), "Reference source was not downloaded"
    metadata = json.loads((cache / ".unity-cli-meta.json").read_text())
    (artifacts / f"{name}.metadata.json").write_text(json.dumps(metadata, indent=2))
    fields = {
        "version": "version", "branch": "branch", "sourceRef": "source_ref",
        "commitSha": "commit_sha", "exactMatch": "exact_match",
        "selectionReason": "selection_reason",
    }
    for output_key, disk_key in fields.items():
        assert fetched[output_key] == metadata[disk_key], (output_key, fetched, metadata)
    actual_sha = subprocess.run(
        ["git", "-C", str(cache), "rev-parse", "HEAD"],
        capture_output=True, text=True, check=True,
    ).stdout.strip()
    assert fetched["commitSha"] == actual_sha, (fetched, actual_sha)
    status = raw(f"{name}-status", "reference_status", {})
    entry = next(item for item in status["versions"] if item["version"] == version)
    for key in (*fields, "path"):
        assert entry[key] == fetched[key], (key, entry, fetched)
    grep = raw(f"{name}-grep", "reference_grep", {
        "projectRoot": str(root), "pattern": "class Animator", "fileGlob": "*.cs",
    })
    assert grep["version"] == version and grep["hits"], grep
    reused = raw(f"{name}-reuse", "reference_fetch", params)
    assert reused["skipped"] is True, reused
    for key in (*fields, "path"):
        assert reused[key] == fetched[key], (key, reused, fetched)


with tempfile.TemporaryDirectory(prefix="unity-cli-reference-resolution-") as temporary:
    temporary = Path(temporary)
    env["UNITY_CLI_CACHE_ROOT"] = str(temporary / "editor-cache")
    if check("live Editor readiness", editor_ready):
        fixture = temporary / "fixture"
        settings = fixture / "ProjectSettings"
        settings.mkdir(parents=True)
        (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.4.12f1\n")
        check("real project automatic fetch/status/grep", lambda: scenario(
            "project-auto", project, temporary / "project-cache"))
        check("6000.4.12f1 automatic exact tag/status/grep", lambda: scenario(
            "fixture-auto", fixture, temporary / "fixture-cache", exact_match=True))
        check("6000.4 explicit public ref/status/grep", lambda: scenario(
            "explicit", fixture, temporary / "explicit-cache", explicit=True, exact_match=False))

passed = sum(result["passed"] for result in results)
failed = len(results) - passed
(artifacts / "summary.json").write_text(json.dumps({
    "passed": passed, "failed": failed, "results": results,
}, indent=2))
print(f"Passed: {passed}; Failed: {failed}; Total: {len(results)}")
print(f"Artifacts: {artifacts}")
sys.exit(1 if failed else 0)
PY
