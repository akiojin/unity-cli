"""Real Editor hot reload acceptance checks, run in an isolated fixture project."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--project", type=Path, required=True)
parser.add_argument("--port", default="6484")
parser.add_argument("--unity-cli", required=True)
parser.add_argument("--expect", choices=["missing", "unsupported", "supported"], required=True)
args = parser.parse_args()
passed = 0


def check(condition, name):
    global passed
    assert condition, name
    passed += 1
    print("PASS", name, flush=True)


def call(tool, payload, error=None):
    result = subprocess.run([args.unity_cli, "raw", tool, "--json", json.dumps(payload),
                             "--host", "127.0.0.1", "--port", args.port,
                             "--timeout-ms", "120000", "--output", "json"],
                            capture_output=True, text=True, timeout=150)
    if error:
        check(result.returncode != 0 and error in result.stdout + result.stderr,
              tool + " rejects: " + error + " / " + result.stderr.strip())
        return None
    assert result.returncode == 0, (tool, result.stdout, result.stderr)
    data = json.loads(result.stdout)
    assert not data.get("error") and data.get("success") is not False, (tool, data)
    return data


def menu(name):
    return call("execute_menu_item", {"menuPath": "Tools/Unity CLI/Hot Reload/" + name})


def snapshot():
    menu("Snapshot E2E Fixture")
    return json.loads((args.project / "Library/HotReloadE2E/snapshot.json").read_text())


def same_state(before, expected_value):
    after = snapshot()
    check(after["playing"] and after["value"] == expected_value, "Play continues with expected calculation")
    check({k: v for k, v in before.items() if k != "value"} ==
          {k: v for k, v in after.items() if k != "value"},
          "scene/object identity, position, HP, score, static and nonserialized state preserved")


def recover_and_verify():
    call("hot_reload", {"action": "recover"})
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        time.sleep(1)
        try:
            status = call("hot_reload_status", {})
            state = call("get_editor_state", {})["state"]
            if status["state"] == "idle" and not status["recoveryRequired"] and not state["isCompiling"]:
                check(not state["isPlaying"], "recovery stops Play without restarting")
                break
        except (AssertionError, OSError):
            continue
    else:
        raise AssertionError("Recovery did not finish clean compilation / Domain Reload")
    ledger = json.loads((args.project / "Library/UnityCliHotReloadProvenance.json").read_text())
    proof = ledger["Assembly-CSharp"]
    source_path = (args.project / "Assets/HotReloadProbe.cs").resolve()
    source_hash = next((value for path, value in proof["Sources"].items() if Path(path).resolve() == source_path), None)
    check(source_hash == hashlib.sha256(source_path.read_bytes()).hexdigest(),
          "recovery records compiled source checksum for next session")
    check(proof["AssemblyHash"] == hashlib.sha256((args.project / "Library/ScriptAssemblies/Assembly-CSharp.dll").read_bytes()).hexdigest(),
          "recovery checksum matches actual compiled assembly")
    call("play_game", {})
    time.sleep(2)
    restored = snapshot()
    check(restored["playing"] and restored["value"] == 6,
          "new Play session executes original disk baseline after recovery")


def failed_compilation_cannot_certify_baseline():
    call("stop_game", {})
    time.sleep(2)
    path = args.project / "Assets/HotReloadProbe.cs"
    original = path.read_bytes()
    try:
        path.write_bytes(b"#error ISSUE284_EXPECTED_COMPILATION_FAILURE\n" + original)
        call("refresh_assets", {})
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            time.sleep(1)
            try:
                state = call("get_compilation_state", {"includeMessages": True})
                if not state["isCompiling"] and state["errorCount"] > 0:
                    break
            except AssertionError:
                continue
        else:
            raise AssertionError("Expected isolated compilation failure was not observed")
        ledger = json.loads((args.project / "Library/UnityCliHotReloadProvenance.json").read_text())
        check("Assembly-CSharp" not in ledger, "failed real compilation cannot certify mismatched source as baseline")
    finally:
        path.write_bytes(original)
        call("refresh_assets", {})
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        time.sleep(1)
        try:
            ledger = json.loads((args.project / "Library/UnityCliHotReloadProvenance.json").read_text())
            state = call("get_editor_state", {})["state"]
            if "Assembly-CSharp" in ledger and not state["isCompiling"] and not state["isUpdating"]:
                break
        except (AssertionError, OSError, ValueError):
            continue
    else:
        raise AssertionError("Restored baseline compilation did not finish")
    # Rebuild and prove the legitimate restored baseline is accepted by the ledger.
    recover_and_verify()


def run():
    info = call("get_editor_info", {})
    print("Unity:", info["unity"]["unityVersion"], "mode:", args.expect, flush=True)
    status = call("hot_reload_status", {})
    print("Backend:", json.dumps(status), flush=True)
    menu("Create E2E Fixture")
    if args.expect != "missing":
        menu("Configure E2E Backend")
    call("play_game", {})
    time.sleep(2)
    before = snapshot()
    check(before["playing"] and before["value"] == 6, "baseline calculation runs in real Play Mode")
    source_path = args.project / "Assets/HotReloadProbe.cs"
    source = source_path.read_text()
    if args.expect != "supported":
        code = "HOT_RELOAD_PACKAGE_MISSING" if args.expect == "missing" else "HOT_RELOAD_PLATFORM_UNSUPPORTED"
        check(status["supported"] is False and status["appliedRevision"] is None,
              "unavailable backend never claims an applied revision")
        call("hot_reload", {"action": "begin", "path": "Assets/HotReloadProbe.cs"}, code)
        call("hot_reload", {"action": "apply", "source": source.replace("input * 2", "input * 5"),
                            "expectedRevision": "invalid"}, code)
        same_state(before, 6)
        check(source_path.read_text() == source, "rejected preview leaves disk source unchanged")
        if args.expect == "unsupported":
            compile_output = args.project / "Library/HotReloadE2E/compile.json"
            compile_output.unlink(missing_ok=True)
            menu("Compile E2E Candidate")
            deadline = time.monotonic() + 90
            while not compile_output.exists() and time.monotonic() < deadline:
                time.sleep(0.5)
            compiled = json.loads(compile_output.read_text())
            check(compiled["success"] and "Calculate" in (compiled.get("method") or ""),
                  "real FSR compiles instrumented temporary candidate without native patching: " + str(compiled))
            same_state(before, 6)
            recover_and_verify()
            failed_compilation_cannot_certify_baseline()
        return

    check(status["supported"] is True, "backend supports this actual Editor")
    # Capture a compile-time source/assembly proof, then enter a fresh Play session.
    recover_and_verify()
    before = snapshot()
    begun = call("hot_reload", {"action": "begin", "path": "Assets/HotReloadProbe.cs"})
    revision = begun["appliedRevision"]
    assert revision
    changed = source.replace("input * 2", "input * 5")
    call("hot_reload", {"action": "apply", "source": changed, "expectedRevision": "stale"}, "STALE")
    call("hot_reload", {"action": "apply", "source": source.replace("return input * 2;", "return !!!;"),
                        "expectedRevision": revision}, "SYNTAX")
    call("hot_reload", {"action": "apply", "source": source.replace("public int hp", "public float hp"),
                        "expectedRevision": revision}, "UNSUPPORTED")
    check(call("hot_reload_status", {})["appliedRevision"] == revision,
          "preflight failures preserve verified revision")
    same_state(before, 6)
    applied = call("hot_reload", {"action": "apply", "source": changed, "expectedRevision": revision})
    check(applied["appliedRevision"] != revision and applied["appliedRevision"] is not None,
          "new revision follows verified natural execution")
    same_state(before, 15)
    check(source_path.read_text() == source, "successful preview leaves disk source unchanged")
    # One method runs naturally, the other does not: must never claim whole-version success.
    call("hot_reload", {"action": "apply", "source": changed.replace("input * 5", "input * 7").replace("input + 1", "input + 4"),
                        "expectedRevision": applied["appliedRevision"], "timeoutSeconds": 30}, "HOT_RELOAD_VERIFICATION_FAILED")
    failed = call("hot_reload_status", {})
    check(failed["appliedRevision"] is None and failed["recoveryRequired"],
          "unverified partial transaction clears whole-version claim and requires recovery")
    recover_and_verify()


try:
    run()
    print(f"RESULT pass={passed} fail=0", flush=True)
except Exception:
    print(f"RESULT pass={passed} fail=1", flush=True)
    raise
finally:
    try:
        call("stop_game", {})
    except Exception:
        pass  # Recovery may already have reloaded the listener.
    if args.expect != "missing":
        try:
            menu("Restore E2E Backend")
        except Exception as error:
            print("Backend preference restore failed:", error, flush=True)
