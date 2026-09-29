"""Real Editor eval acceptance tests. Requires a listener; no mock transport."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--host", default=os.getenv("UNITY_CLI_HOST", "127.0.0.1"))
parser.add_argument("--port", default=os.getenv("UNITY_CLI_PORT", "6400"))
parser.add_argument("--timeout-ms", default="120000")
parser.add_argument("--unity-cli", default=str(ROOT / "target/debug/unity-cli"))
args = parser.parse_args()
environment = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
prefix = [args.unity_cli, "--host", args.host, "--port", args.port,
          "--timeout-ms", args.timeout_ms, "--output", "json"]
run_id = "eval-e2e-" + uuid.uuid4().hex
passed = 0
failed = 0


def command(*parts):
    completed = subprocess.run(prefix + list(parts), text=True, capture_output=True,
                               env=environment, timeout=int(args.timeout_ms) / 1000 + 30)
    if completed.returncode:
        raise AssertionError(completed.stderr + completed.stdout)
    return json.loads(completed.stdout)


def raw(tool, **params):
    return command("raw", tool, "--json", json.dumps(params))


def evaluate(code, mode="expression", request_id=None):
    return command("editor", "eval", code, "--mode", mode,
                   "--request-id", request_id or (run_id + "-" + uuid.uuid4().hex))


def check(name, action):
    global passed, failed
    try:
        action()
        passed += 1
        print("PASS " + name, flush=True)
    except Exception as error:
        failed += 1
        print("FAIL " + name + ": " + str(error), flush=True)


def equal(actual, expected):
    assert actual == expected, f"expected {expected!r}, got {actual!r}"


def value(code, expected, mode="expression"):
    result = evaluate(code, mode)
    equal(result["state"], "completed")
    equal(result["value"], expected)


def syntax_error():
    result = evaluate("1 +")
    equal(result["state"], "compile_error")
    assert result["diagnostics"], result
    assert any(d.get("line") == 1 for d in result["diagnostics"]), result


def runtime_error():
    result = evaluate('throw new InvalidOperationException("eval-test");', "statements")
    equal(result["state"], "runtime_error")
    assert "InvalidOperationException" in result["exception"]["type"], result
    equal(result["exception"]["message"], "eval-test")


def object_descriptor():
    result = evaluate(f'GameObject.Find("{run_id}")')
    equal(result["state"], "completed")
    equal(result["value"]["name"], run_id)
    equal(result["value"]["type"], "UnityEngine.GameObject")
    assert isinstance(result["value"]["instanceId"], int), result


def once():
    code = f'GameObject.Find("{run_id}").transform.position += Vector3.right; return GameObject.Find("{run_id}").transform.position.x;'
    first = evaluate(code, "statements", run_id + "-once")
    second = evaluate(code, "statements", run_id + "-once")
    equal(first, second)
    equal(first["value"], 8)
    equal(command("editor", "eval-status", run_id + "-once"), first)
    value(f'GameObject.Find("{run_id}").transform.position.x', 8)


def captured_log():
    result = evaluate('Debug.Log("eval-captured"); return 5;', "statements")
    equal(result["value"], 5)
    assert "eval-captured" in json.dumps(result["logs"]), result


def source_files():
    return {str(p.relative_to(ROOT)): p.stat().st_mtime_ns
            for directory in (ROOT / "UnityCliBridge/Assets",
                              ROOT / "UnityCliBridge/Packages/unity-cli-bridge")
            for p in directory.rglob("*.cs")}


def play_mode():
    raw("play_game")
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        try:
            result = evaluate("Application.isPlaying")
            if result.get("state") == "completed" and result.get("value") is True:
                value("1+2", 3)
                return
        except (AssertionError, subprocess.TimeoutExpired):
            pass
        time.sleep(1)
    raise AssertionError("Editor did not reach Play Mode")


print(json.dumps(raw("get_editor_info")), flush=True)
before = source_files()
try:
    # Keep generated scenes within the documented ignored E2E directory.
    raw("create_scene", sceneName=run_id, path="Assets/Scenes/Generated/E2E/", loadScene=True)
    check("AC-1 numeric expression", lambda: value("1+2", 3))
    check("AC-3 create", lambda: value(f'new GameObject("{run_id}"); return 1;', 1, "statements"))
    check("AC-2 read scene object", lambda: value(f'GameObject.Find("{run_id}").name', run_id))
    check("Unity Object descriptor", object_descriptor)
    check("AC-3 modify", lambda: value(f'GameObject.Find("{run_id}").transform.position = new Vector3(7, 2, 3); return GameObject.Find("{run_id}").transform.position.x;', 7, "statements"))
    check("requestId deduplication and status", once)
    check("AC-4 compile diagnostics", syntax_error)
    check("AC-4 runtime exception", runtime_error)
    check("captured logs", captured_log)
    check("JSON array", lambda: value("new[] {1, 2, 3}", [1, 2, 3]))
    check("statements without return", lambda: value("var x = 3;", None, "statements"))
    check("unknown status", lambda: equal(command("editor", "eval-status", run_id + "-unknown")["state"], "unknown"))
    check("AC-5 no persistent C# files", lambda: equal(source_files(), before))
    check("Play Mode expression", play_mode)
finally:
    try:
        raw("stop_game")
        for _ in range(60):
            try:
                if evaluate("Application.isPlaying").get("value") is False:
                    break
            except (AssertionError, subprocess.TimeoutExpired):
                pass
            time.sleep(1)
        evaluate(f'var go = GameObject.Find("{run_id}"); if (go != null) UnityEngine.Object.DestroyImmediate(go);', "statements")
    except Exception as error:
        failed += 1
        print("FAIL cleanup: " + str(error), flush=True)
print(f"Eval E2E: {passed} passed, {failed} failed", flush=True)
raise SystemExit(1 if failed else 0)
