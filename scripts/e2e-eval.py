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
parser.add_argument("--soak-iterations", type=int, default=1000,
                    help="consecutive evaluations in the memory/assembly bound check")
args = parser.parse_args()
environment = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
prefix = [args.unity_cli, "--host", args.host, "--port", args.port,
          "--timeout-ms", args.timeout_ms, "--output", "json"]
run_id = "eval-e2e-" + uuid.uuid4().hex
probe = "EvalE2EProbe" + uuid.uuid4().hex
# Bounds for --soak-iterations consecutive evaluations of SOAK_SNIPPETS distinct snippets (Issue #391).
SOAK_SNIPPETS = 4
MAX_SOAK_EMITTED_ASSEMBLIES = 2 * SOAK_SNIPPETS  # one recompile per snippet if the reference set changes
MAX_SOAK_LOADED_ASSEMBLIES = 16
MAX_SOAK_MEMORY_GROWTH_BYTES = 64 * 1024 * 1024
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


def stats(collect=False):
    return raw("get_eval_stats", collect=collect)


def compilation_reuse():
    code = f"40+2 /* {run_id} */"
    before = stats()
    for _ in range(3):
        value(code, 42)
    after = stats()
    assert after["emittedAssemblies"] - before["emittedAssemblies"] <= 2, (before, after)
    assert after["compileCacheHits"] - before["compileCacheHits"] >= 1, (before, after)
    assert after["referenceCount"] > 0, after


def new_assembly():
    equal(evaluate(probe + ".Value")["state"], "compile_error")
    built = evaluate(
        'var path = System.IO.Path.GetFullPath("Temp/' + probe + '.dll");'
        'var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public static class ' + probe + ' { public static int Value => 42; }");'
        "var refs = new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };"
        "var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);"
        "using (var file = System.IO.File.Create(path)) {"
        'var emit = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("' + probe + '", new[] { tree }, refs, options).Emit(file);'
        'if (!emit.Success) throw new Exception(string.Join(";", emit.Diagnostics)); }'
        "System.Reflection.Assembly.LoadFrom(path); return 1;", "statements")
    equal(built["state"], "completed")
    # The same source failed to compile above: the new assembly must invalidate the caches.
    value(probe + ".Value", 42)


def domain_reload():
    project_type = "typeof(UnityCliBridge.Handlers.EvalHandler).Name"
    value(project_type, "EvalHandler")
    before = evaluate("1+2", request_id=run_id + "-before-reload")
    equal(before["value"], 3)
    assert stats()["evaluations"] > 0
    equal(evaluate("UnityEditor.EditorUtility.RequestScriptReload(); return 1;", "statements")["state"], "completed")
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        try:
            # Counters are per domain: a fresh domain has forgotten the request stored above.
            if command("editor", "eval-status", run_id + "-before-reload")["state"] == "unknown":
                break
        except (AssertionError, subprocess.TimeoutExpired):
            pass
        time.sleep(1)
    else:
        raise AssertionError("Domain Reload did not happen")
    deadline = time.monotonic() + 120
    while evaluate("1+2").get("state") == "busy" and time.monotonic() < deadline:
        time.sleep(1)
    value("1+2", 3)
    value(project_type, "EvalHandler")
    value("UnityEngine.Application.unityVersion.Length > 0", True)
    # The probe assembly was loaded only in the previous domain; stale references would still resolve it.
    equal(evaluate(probe + ".Value")["state"], "compile_error")


def soak():
    snippets = [(f"{i}+{i} /* soak {run_id} */", 2 * i) for i in range(SOAK_SNIPPETS)]
    before = stats(collect=True)
    for n in range(args.soak_iterations):
        code, expected = snippets[n % SOAK_SNIPPETS]
        value(code, expected)
    after = stats(collect=True)
    measured = {
        "iterations": args.soak_iterations,
        "evaluations": after["evaluations"] - before["evaluations"],
        "emittedAssemblies": after["emittedAssemblies"] - before["emittedAssemblies"],
        "loadedAssemblies": after["loadedAssemblies"] - before["loadedAssemblies"],
        "managedMemoryGrowthBytes": after["managedMemoryBytes"] - before["managedMemoryBytes"],
        "storedResults": after["storedResults"],
    }
    print("soak " + json.dumps(measured), flush=True)
    equal(measured["evaluations"], args.soak_iterations)
    assert measured["emittedAssemblies"] <= MAX_SOAK_EMITTED_ASSEMBLIES, measured
    assert measured["loadedAssemblies"] <= MAX_SOAK_LOADED_ASSEMBLIES, measured
    assert measured["managedMemoryGrowthBytes"] <= MAX_SOAK_MEMORY_GROWTH_BYTES, measured
    assert measured["storedResults"] <= after["maxStoredResults"], measured


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
    check("#391 identical source reuses its compilation", compilation_reuse)
    check("#391 AC-3 newly loaded assembly resolves", new_assembly)
    check("#391 AC-3 types resolve across Domain Reload", domain_reload)
    check(f"#391 AC-4 {args.soak_iterations} consecutive evaluations stay within bounds", soak)
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
        evaluate(f'var go = GameObject.Find("{run_id}"); if (go != null) UnityEngine.Object.DestroyImmediate(go);'
                 f' System.IO.File.Delete("Temp/{probe}.dll");', "statements")
    except Exception as error:
        failed += 1
        print("FAIL cleanup: " + str(error), flush=True)
print(f"Eval E2E: {passed} passed, {failed} failed", flush=True)
raise SystemExit(1 if failed else 0)
