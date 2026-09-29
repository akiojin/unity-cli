"""Real Editor bake acceptance: persisted artifacts and usable navigation."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--host", default="127.0.0.1")
parser.add_argument("--port", default="6477")
parser.add_argument("--unity-cli", default=str(ROOT / "target/debug/unity-cli"))
parser.add_argument("--project-root", default=str(ROOT / "UnityCliBridge"))
parser.add_argument("--targets", default="lighting,navmesh-legacy,navmesh-surface,occlusion")
parser.add_argument("--bake-timeout", type=int, default=600)
args = parser.parse_args()
passed = 0
ASSETS = "Assets/Scenes/Generated/E2E/Baking"


def check(condition, description):
    global passed
    assert condition, description
    passed += 1
    print("PASS", description, flush=True)


def call(tool, payload, error=None, busy_deadline=None):
    if busy_deadline is None:
        busy_deadline = time.monotonic() + 30
    result = subprocess.run(
        [args.unity_cli, "raw", tool, "--json", json.dumps(payload), "--host", args.host,
         "--port", args.port, "--timeout-ms", "120000", "--output", "json"],
        capture_output=True, text=True, timeout=150,
        env={**os.environ, "UNITY_PROJECT_ROOT": args.project_root})
    output = result.stdout + result.stderr
    try:
        envelope = json.loads(result.stdout)
    except ValueError:
        envelope = {}
    # Fixture creation triggers imports that may outlive ExecuteMenuItem.
    if envelope.get("code") == "EDITOR_BUSY" and time.monotonic() < busy_deadline:
        time.sleep(0.5)
        return call(tool, payload, error, busy_deadline)
    if error:
        check(result.returncode != 0 or bool(envelope.get("error")) or envelope.get("status") == "error",
              tool + " returns a genuine error")
        check(error in output, f"{tool} rejects with {error}: {output}")
        return None
    assert result.returncode == 0, (tool, output)
    data = json.loads(result.stdout)
    assert not data.get("error") and data.get("status") != "error" and data.get("success") is not False, (tool, data)
    print(tool, json.dumps(data), flush=True)
    return data


def menu(action):
    result = call("execute_menu_item", {"menuPath": "Tools/Unity CLI/Baking/" + action})
    check(result.get("executed") is True, action)


def wait_job(job_id):
    deadline = time.monotonic() + args.bake_timeout
    while True:
        status = call("get_scene_bake_status", {"jobId": job_id})
        check(status.get("jobId") == job_id, "stable job identity")
        if status.get("status") != "running":
            return status
        assert time.monotonic() < deadline, ("bake timeout", status)
        time.sleep(1)


def run():
    call("get_editor_info", {})
    call("get_scene_bake_status", {"jobId": "e2e-unknown-job"}, "JOB_NOT_FOUND")
    names = {"lighting": "Lighting", "navmesh-legacy": "Legacy", "navmesh-surface": "Surface", "occlusion": "Occlusion"}
    targets = args.targets.split(",")
    assert all(target in names for target in targets), args.targets
    for target in targets:
        name = names[target]
        menu("Prepare " + name)
        scene = f"{ASSETS}/{name}/{name}.unity"
        request = {"target": target, "scenePath": scene}
        if target == "navmesh-surface":
            call("start_scene_bake", request, "surfacePath")
            call("start_scene_bake", {**request, "surfacePath": "/DoesNotExist"}, "SURFACE_NOT_FOUND")
            request["surfacePath"] = "/BakeSurface"
        call("start_scene_bake", {**request, "scenePath": f"{ASSETS}/Missing.unity"}, "SCENE_MISMATCH")
        initial = call("start_scene_bake", request)
        check(initial.get("status") == "running" and bool(initial.get("jobId")), target + " starts tracked asynchronous job")
        job_id = initial["jobId"]
        # BAKE_BUSY is verified within one Editor tick by the backend unit suite;
        # a second CLI process would race genuine completion of small fixtures.
        status = wait_job(job_id)
        if target == "occlusion" and status.get("status") != "succeeded":
            menu("Validate Occlusion")
            print("FAILED-BAKE RELOAD EVIDENCE", (Path(args.project_root) / ASSETS / "validation.json").read_text(), flush=True)
        check(status.get("status") == "succeeded", f"{target} genuine success: {status}")
        check(bool(status.get("unityVersion")) and status.get("backend") == target and bool(status.get("phase")),
              target + " reports version, backend and phase")
        artifacts = status.get("artifacts", [])
        check(bool(artifacts), target + " reports nonempty artifact paths")
        for path in artifacts:
            check((Path(args.project_root) / path).is_file(), target + " artifact exists: " + path)
        evidence_path = Path(args.project_root) / ASSETS / "validation.json"
        evidence_path.unlink(missing_ok=True)
        menu("Validate " + name)
        evidence = json.loads(evidence_path.read_text())
        check(evidence.get("passed") is True, f"{target} persisted reload validation: {evidence}")
        check(evidence.get("target") == name, "fresh validation evidence belongs to " + target)
        print("EVIDENCE", json.dumps(evidence), flush=True)
        final = call("get_scene_bake_status", {"jobId": job_id})
        check(final.get("status") == "succeeded" and final.get("artifacts") == artifacts,
              target + " terminal result remains queryable after scene reload")
        if target == "navmesh-surface":
            before = {path: (Path(args.project_root) / path).stat().st_mtime_ns for path in artifacts}
            rebake = call("start_scene_bake", request)
            check(rebake.get("jobId") != job_id, "rebake has an independent job identity")
            refreshed = wait_job(rebake["jobId"])
            check(refreshed.get("status") == "succeeded", "same-scene rebake succeeds")
            check(any(path not in before or (Path(args.project_root) / path).stat().st_mtime_ns > before[path]
                      for path in refreshed.get("artifacts", [])), "rebake writes fresh artifacts")
            evidence_path.unlink(missing_ok=True)
            menu("Validate Surface")
            check(json.loads(evidence_path.read_text()).get("passed") is True, "rebake persists usable NavMesh")
            previous_asset = json.loads(evidence_path.read_text())["persistedAsset"]
            menu("Exclude Surface Geometry")
            rejected = wait_job(call("start_scene_bake", request)["jobId"])
            check(rejected.get("status") == "failed", "empty rebake cannot reuse old NavMesh as success")
            evidence_path.unlink(missing_ok=True)
            menu("Validate Surface")
            preserved = json.loads(evidence_path.read_text())
            check(preserved.get("passed") is True and preserved.get("persistedAsset") == previous_asset,
                  "failed rebake preserves prior usable Surface data and scene assignment")
    if "navmesh-surface" in targets:
        menu("Prepare NoOutput")
        started = call("start_scene_bake", {"target": "navmesh-surface", "surfacePath": "/BakeSurface",
                       "scenePath": f"{ASSETS}/NoOutput/NoOutput.unity"})
        failed = wait_job(started["jobId"])
        check(failed.get("status") == "failed", "backend completion without navigable output is a failed job")
        check(bool(failed.get("code")) and bool(failed.get("reason")), "no-output job reports diagnostic code and reason")
        check(not failed.get("artifacts"), "failed no-output job does not report successful artifacts")
        no_output = Path(args.project_root) / ASSETS / "NoOutput"
        check(not list(no_output.rglob("*.asset")), "no-output job does not persist unusable NavMeshData")
    menu("Prepare Empty")
    empty = f"{ASSETS}/Empty/Empty.unity"
    for target in (t for t in targets if t != "navmesh-surface"):
        call("start_scene_bake", {"target": target, "scenePath": empty}, "NO_GEOMETRY")
    files = list((Path(args.project_root) / ASSETS / "Empty").rglob("*"))
    check(not any(p.suffix in (".exr", ".asset") for p in files), "empty scene rejection creates no bake artifacts")


try:
    run()
except Exception as exc:
    print("FAIL", repr(exc), file=sys.stderr)
    print(f"Baking E2E: passed={passed} failed=1", flush=True)
    sys.exit(1)
print(f"Baking E2E: passed={passed} failed=0", flush=True)
