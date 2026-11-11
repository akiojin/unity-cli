#!/usr/bin/env python3
"""Verify Editor lifecycle with isolated GUI/batch Editors and PID evidence (#443/#545)."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import socket
import struct
import subprocess
import tempfile
import time

from bridge_auth import auth_fields, process_alive

ROOT = Path(__file__).resolve().parents[1]


def alive(pid):
    return process_alive(pid)


def data(value):
    # Accept the shared #441 envelope as well as the pre-envelope CLI during development.
    return value.get("data", value) if isinstance(value, dict) else value


def fixture_pids(project):
    """Only PIDs for this script's disposable project, including a failed startup."""
    if os.name == "nt":
        result = subprocess.check_output([
            "powershell", "-NoProfile", "-Command",
            "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
            "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
            "Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress"], encoding="utf-8")
        processes = json.loads(result) if result.strip() else []
        if isinstance(processes, dict):
            processes = [processes]
        pids = []
        for process in processes:
            match = re.search(r'-projectPath\s+(?:"([^"]+)"|(\S+))',
                              process.get("CommandLine") or "", re.IGNORECASE)
            if match and Path(match[1] or match[2]).resolve() == project.resolve():
                pids.append(int(process["ProcessId"]))
        return pids
    processes = subprocess.check_output(["ps", "-axo", "pid=,command="], text=True)
    marker = " -projectPath " + str(project) + " "
    return [int(line.strip().split(None, 1)[0]) for line in processes.splitlines()
            if "/Unity.app/Contents/MacOS/Unity " in line and marker in line]


def prepare(editor, folder, version):
    # Do not copy Unity 6 rendering assets/settings into 2022: GUI startup
    # otherwise blocks on an unrelated URP material upgrade dialog.
    project = folder / "project"
    for name in ("Assets", "Packages", "ProjectSettings"):
        (project / name).mkdir(parents=True)
    shutil.copytree(ROOT / "UnityCliBridge/Packages/unity-cli-bridge",
                    project / "Packages/unity-cli-bridge")
    resources = (editor.parent / "Data" if os.name == "nt" else editor.parent.parent) / "Resources/PackageManager"
    catalog = json.loads((resources / "Editor/manifest.json").read_text())["packages"]
    dependencies = {"com.akiojin.unity-cli-bridge": "file:unity-cli-bridge"}
    for name in ("com.unity.ugui", "com.unity.test-framework"):
        dependencies[name] = catalog[name]["version"]
    dependencies.update({p.name: "1.0.0" for p in (resources / "BuiltInPackages").iterdir()
                         if p.is_dir() and p.name.startswith("com.unity.modules.")})
    (project / "Packages/manifest.json").write_text(json.dumps({
        "dependencies": dependencies, "testables": ["com.akiojin.unity-cli-bridge"]}, indent=2))
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + version + "\n")
    (project / "ProjectSettings/ProjectSettings.asset").write_text(
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  activeInputHandler: 1\n")
    return project


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-cli", type=Path,
                        default=ROOT / ("target/debug/unity-cli.exe" if os.name == "nt" else "target/debug/unity-cli"))
    parser.add_argument("--versions", default="6000.3.25f1,2022.3.62f3")
    parser.add_argument("--editor-root", type=Path,
                        default=Path("C:/Program Files/Unity/Hub/Editor" if os.name == "nt"
                                     else "/Applications/Unity/Hub/Editor"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--reuse-fixtures", action="store_true")
    parser.add_argument("--editmode", action="store_true")
    args = parser.parse_args()
    destination = args.output or Path(tempfile.mkdtemp(prefix="editor-lifecycle-"))
    destination.mkdir(parents=True, exist_ok=True)
    destination = destination.resolve()
    env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1",
               UNITY_CLI_EDITORS_DIR=str(destination / "editors"),
               UNITY_CLI_TOOLS_ROOT=str(destination / "tools"))
    for key in ("UNITY_PROJECT_ROOT", "UNITY_EDITOR_PATH", "UNITY_CLI_PORT", "UNITY_CLI_HOST",
                "UNITY_CLI_PORT_OVERRIDE", "UNITY_CLI_ALLOW_BATCH_HOST",
                "UNITY_CLI_AUTH_TOKEN_FILE", "UNITY_CLI_ALLOW_UNAUTHENTICATED"):
        env.pop(key, None)
    report = {"started_at": datetime.now(timezone.utc).isoformat(), "editors": []}
    print("Evidence:", destination, flush=True)

    try:
        for version in args.versions.split(","):
            folder = destination / version
            folder.mkdir(exist_ok=args.reuse_fixtures)
            executable = "Editor/Unity.exe" if os.name == "nt" else "Unity.app/Contents/MacOS/Unity"
            editor = args.editor_root / version / executable
            if args.reuse_fixtures and (folder / "project").is_dir():
                project = folder / "project"
                shutil.copytree(ROOT / "UnityCliBridge/Packages/unity-cli-bridge",
                                project / "Packages/unity-cli-bridge", dirs_exist_ok=True)
            else:
                project = prepare(editor, folder, version)
            row = {"version": version, "checks": [], "status": "FAIL"}
            report["editors"].append(row)
            pid = None
            # Pin every command to this fixture rather than another Editor's
            # default port; editor open passes the override to the Bridge.
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                port = listener.getsockname()[1]
            row["port"] = port

            def call(arguments, success=True):
                command = [str(args.unity_cli.resolve()), "--output", "json", "--project-path",
                           str(project), "--port", str(port), "--timeout-ms", "30000", *arguments]
                result = subprocess.run(command, env=env, cwd=project, capture_output=True,
                                        text=True, encoding="utf-8", timeout=330,
                                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
                with (folder / "calls.jsonl").open("a") as stream:
                    stream.write(json.dumps({"finished_at": datetime.now(timezone.utc).isoformat(),
                                             "command": command, "exit": result.returncode,
                                             "stdout": result.stdout, "stderr": result.stderr}) + "\n")
                assert (result.returncode == 0) == success, result
                value = json.loads(result.stdout) if result.stdout.strip() else {"stderr": result.stderr}
                # Keep failure envelopes intact so assertions can inspect errors[].code.
                return data(value) if success else value

            def remote(tool, params=None):
                params = params or {}
                if os.name != "nt":
                    return call(["raw", tool, "--json", json.dumps(params)])
                # Windows unityd has a shared TCP endpoint and can inherit
                # another fixture's registry. Only supplementary Bridge probes
                # use this wire helper; lifecycle commands always use the CLI.
                request = {"id": "lifecycle", "type": tool, "params": params,
                           **auth_fields(port, env["UNITY_CLI_EDITORS_DIR"], pid)}
                with socket.create_connection(("127.0.0.1", port), timeout=30) as connection:
                    def read_exact(size):
                        result = b""
                        while len(result) < size:
                            chunk = connection.recv(size - len(result))
                            if not chunk:
                                raise RuntimeError("Bridge closed before the full response")
                            result += chunk
                        return result
                    payload = json.dumps(request).encode("utf-8")
                    connection.sendall(struct.pack(">I", len(payload)) + payload)
                    size = struct.unpack(">I", read_exact(4))[0]
                    assert 0 < size <= 10 * 1024 * 1024, size
                    response = json.loads(read_exact(size))
                with (folder / "bridge-calls.jsonl").open("a") as stream:
                    stream.write(json.dumps({"tool": tool, "params": params, "response": response}) + "\n")
                assert response.get("status") != "error", response
                return response.get("result", response.get("data", response))

            def check(condition, name):
                assert condition, name
                row["checks"].append(name)
                print("PASS", version, name, flush=True)

            try:
                check(call(["editor", "status"])["state"] == "stopped", "initially stopped")
                for headless in (False, True):
                    label = "headless" if headless else "GUI"
                    launched = call(["editor", "open", "--wait-ready", "300"] +
                                    (["--headless"] if headless else []))
                    pid = launched["pid"]
                    check(launched["ready"] and alive(pid), label + " launched and ready")
                    check(Path(remote("ping")["projectPath"]).resolve() == project.resolve(),
                          label + " ping targets project")
                    status = call(["editor", "status"])
                    check(status["state"] == "ready" and status["pid"] == pid,
                          label + " status ready with matching PID")
                    result = call(["raw", "eval_csharp", "--json", json.dumps({
                        "code": "UnityEngine.Application.isBatchMode"})])
                    check(result["value"] == headless, label + " actual batch mode")
                    remote("get_hierarchy")
                    check(alive(pid), label + " hierarchy and resident process")
                    if headless and args.editmode:
                        started = remote("run_tests", {
                            "testMode": "EditMode", "includeDetails": True,
                            "filter": "UnityCliBridge.Tests.Editor.UnityCliBridgeHostConnectionTests"})
                        assert started.get("status") == "running" and started.get("runId"), started
                        deadline = time.monotonic() + 300
                        while True:
                            tests = remote("get_test_status", {"includeTestResults": True})
                            if tests.get("status") == "completed":
                                (folder / "editmode.json").write_text(json.dumps(tests, indent=2) + "\n")
                                check(tests.get("runId") == started["runId"] and tests.get("failedTests") == 0
                                      and tests.get("passedTests", 0) >= 3
                                      and tests.get("passedTests") == tests.get("totalTests"),
                                      "focused EditMode tests")
                                break
                            assert time.monotonic() < deadline, "EditMode tests timed out"
                            time.sleep(1)
                    if not headless:
                        changed = call(["raw", "eval_csharp", "--json", json.dumps({"mode": "statements", "code":
                            'new UnityEngine.GameObject("UnsavedLifecycleProbe"); '
                            'UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty('
                            'UnityEngine.SceneManagement.SceneManager.GetActiveScene()); return true;'})])
                        check(changed.get("state") == "completed" and changed.get("value") is True,
                              "dirty scene fixture created")
                        refused = call(["editor", "close"], success=False)
                        check("UNSAVED_SCENES" in json.dumps(refused) and alive(pid),
                              "dirty scene refuses close and preserves process")
                        closed = call(["editor", "close", "--force"])
                    else:
                        closed = call(["editor", "close"])
                    check(closed["closed"] and closed["pid"] == pid and not alive(pid),
                          label + " process absent after close with matching PID")
                    pid = None
                    check(call(["editor", "status"])["state"] == "stopped", label + " status stopped")
                row["status"] = "PASS"
            finally:
                owned = fixture_pids(project)
                if owned:
                    # This PID belongs to the disposable project launched by this suite.
                    subprocess.run([str(args.unity_cli.resolve()), "--project-path", str(project),
                                    "--port", str(port), "editor", "close", "--force"],
                                   env=env, capture_output=True, timeout=40)
                    for owned_pid in owned:
                        if alive(owned_pid):
                            os.kill(owned_pid, 15)
    finally:
        report["finished_at"] = datetime.now(timezone.utc).isoformat()
        (destination / "summary.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
