#!/usr/bin/env python3
"""Issue #250: exercise raw run_tests across real Unity domain reloads.

Open UnityCliBridge with its listener active (see docs/development.md Local Unity
E2E), then run: python3 scripts/e2e-test-domain-reload.py --port 6450
Or launch and stop an isolated worktree host with --batch-host --port 6450.
The package must be listed in manifest.json testables. Settings are restored even
on failure; interrupted runs can use Tools/Unity CLI/Test Runner E2E/Restore Settings.
"""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parent.parent
MENU = "Tools/Unity CLI/Test Runner E2E/"
FIXTURE = "DomainReloadResultTests"
EXPECTED = {"FrameAdvances", "RigidbodyFalls"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", default=str(ROOT / "target/release/unity-cli"))
    parser.add_argument("--host", default=os.environ.get("UNITY_CLI_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("UNITY_CLI_PORT", "6400")))
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--batch-host", action="store_true", help="Launch this worktree's Unity Editor")
    parser.add_argument("--unity-path", type=Path, help="Unity executable (default: ProjectVersion.txt)")
    parser.add_argument("--startup-timeout", type=float, default=360)
    parser.add_argument("--output", type=Path, default=ROOT / ".unity/issue250-e2e.json")
    args = parser.parse_args()
    report = {"cases": [], "calls": []}
    env = dict(os.environ, UNITY_PROJECT_ROOT=os.getenv("UNITY_PROJECT_ROOT", str(ROOT / "UnityCliBridge")))
    host_process = None
    host_log = args.output.resolve().with_suffix(".host.log")
    shutdown_file = args.output.resolve().with_suffix(f".{os.getpid()}.stop")

    def listener_pids():
        result = subprocess.run(["lsof", "-nP", f"-iTCP:{args.port}", "-sTCP:LISTEN", "-t"],
                                capture_output=True, text=True, timeout=5)
        if result.returncode not in (0, 1):
            raise RuntimeError(f"Cannot identify listener ownership: {result.stderr}")
        return set(result.stdout.split())

    def call(tool, parameters):
        if args.batch_host:
            if host_process is None or host_process.poll() is not None:
                raise ConnectionError("Owned Unity host is not running")
            owners = listener_pids()
            if owners != {str(host_process.pid)}:
                raise ConnectionError(f"Port {args.port} is not owned by this Unity host: {owners}")
        command = [args.cli, "--host", args.host, "--port", str(args.port),
                   "--timeout-ms", "10000", "raw", tool, "--json", json.dumps(parameters)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=20, env=env)
        report["calls"].append({"tool": tool, "parameters": parameters,
                                "returncode": result.returncode, "stdout": result.stdout,
                                "stderr": result.stderr})
        if result.returncode:
            raise ConnectionError(result.stderr or result.stdout)
        value = json.loads(result.stdout)
        if value.get("error"):
            raise RuntimeError(json.dumps(value))
        return value

    def menu(action):
        result = call("execute_menu_item", {"action": "execute", "menuPath": MENU + action})
        assert result.get("success") is not False, result
        assert result.get("executed") is True, result

    def settings():
        return call("get_project_settings", {"includePlayer": False, "includeEditor": True})["editor"]

    def verify_results(result, exported=False):
        if exported:
            assert result["status"] == "passed", result
        else:
            assert result["success"] is True, result
        assert len(result["tests"]) == 2, result
        leaves = {test["name"]: test for test in result["tests"] if test["name"] in EXPECTED}
        assert set(leaves) == EXPECTED, result
        assert all(test["status"] == "Passed" for test in leaves.values()), leaves
        assert result["totalTests"] == 2, result
        assert result["passed" if exported else "passedTests"] == 2, result
        assert result["failed" if exported else "failedTests"] == 0, result

    failed = 0
    original = None
    try:
        if args.batch_host:
            if args.host not in ("127.0.0.1", "localhost"):
                raise ValueError("--batch-host requires --host 127.0.0.1 or localhost")
            if shutil.which("lsof") is None:
                raise RuntimeError("--batch-host requires lsof to verify listener process ownership")
            if listener_pids():
                raise RuntimeError(f"Refusing to launch: port {args.port} already has a listener")
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                probe.bind(("127.0.0.1", args.port))
            version_file = ROOT / "UnityCliBridge/ProjectSettings/ProjectVersion.txt"
            version = re.search(r"^m_EditorVersion: (.+)$", version_file.read_text(), re.MULTILINE).group(1)
            unity_path = args.unity_path or Path(
                f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")
            if not unity_path.is_file() or not os.access(unity_path, os.X_OK):
                raise RuntimeError(f"Unity executable unavailable: {unity_path}; set --unity-path")
            host_log.parent.mkdir(parents=True, exist_ok=True)
            host_env = dict(env, UNITY_CLI_ALLOW_BATCH_HOST="1", UNITY_CLI_PORT_OVERRIDE=str(args.port),
                            UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(shutdown_file))
            command = [str(unity_path), "-batchmode", "-nographics", "-projectPath",
                       str(ROOT / "UnityCliBridge"), "-executeMethod",
                       "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run", "-logFile", str(host_log)]
            host_process = subprocess.Popen(command, env=host_env, stdout=subprocess.DEVNULL,
                                            stderr=subprocess.DEVNULL)
            report["host"] = {"pid": host_process.pid, "log": str(host_log),
                              "command": command, "projectUnityVersion": version}
            deadline = time.monotonic() + args.startup_timeout
            last_error = "listener unavailable"
            while time.monotonic() < deadline:
                if host_process.poll() is not None:
                    raise RuntimeError(f"Unity exited with {host_process.returncode}; see {host_log}")
                try:
                    call("ping", {})
                    report["editorState"] = call("get_editor_state", {})
                    state = report["editorState"].get("state", report["editorState"])
                    if state.get("isCompiling") or state.get("isUpdating"):
                        raise ConnectionError("Unity is still compiling or importing assets")
                    break
                except (ConnectionError, RuntimeError, subprocess.TimeoutExpired, json.JSONDecodeError) as error:
                    last_error = str(error)
                    time.sleep(2)
            else:
                raise RuntimeError(f"Unity startup timed out: {last_error}; see {host_log}")
        report["ping"] = call("ping", {})
        original = settings()
        report["originalSettings"] = original
        for reload_enabled in (True, False):
            case = {"domainReloadEnabled": reload_enabled}
            report["cases"].append(case)
            try:
                menu("Enable Domain Reload" if reload_enabled else "Disable Domain Reload")
                configured = settings()
                case["settings"] = configured
                assert configured["enterPlayModeOptionsEnabled"] is (not reload_enabled), configured
                if not reload_enabled:
                    assert configured["enterPlayModeOptions"] == "DisableDomainReload", configured
                started = call("run_tests", {"testMode": "PlayMode", "filter": FIXTURE,
                                             "includeDetails": True})
                assert started.get("status") == "running" and started.get("runId"), started
                case["runId"] = started["runId"]
                deadline = time.monotonic() + args.timeout
                result = None
                while time.monotonic() < deadline:
                    time.sleep(3)
                    try:
                        result = call("get_test_status", {"includeTestResults": True,
                                                          "includeFileContent": True})
                    except (ConnectionError, subprocess.TimeoutExpired, json.JSONDecodeError) as error:
                        print(f"Reload reconnect: {error}", flush=True)
                        continue
                    if result.get("status") == "running":
                        assert result.get("runId") == started["runId"], result
                        continue
                    assert result.get("status") == "completed", result
                    break
                else:
                    raise AssertionError(f"Run did not complete within {args.timeout}s: {result}")
                assert result.get("runId") == started["runId"], result
                verify_results(result)
                latest = result["latestResult"]
                assert latest["status"] == "available", latest
                verify_results(json.loads(latest["fileContent"]), exported=True)
                case.update(status="pass", passed=2, failed=0, result=result)
                print(f"PASS domainReloadEnabled={reload_enabled}: 2 passed, 0 failed", flush=True)
                # RunFinished precedes the Test Framework's PlayMode teardown.
                # Starting another run before exit can discover zero tests.
                deadline = time.monotonic() + args.timeout
                while time.monotonic() < deadline:
                    try:
                        state = call("get_editor_state", {})["state"]
                        if not state["isPlaying"] and not state["isCompiling"] and not state["isUpdating"]:
                            break
                    except (ConnectionError, subprocess.TimeoutExpired, json.JSONDecodeError):
                        pass
                    time.sleep(1)
                else:
                    raise AssertionError("Test Framework did not finish PlayMode teardown")
            except Exception as error:
                failed += 1
                case.update(status="fail", error=str(error))
                print(f"FAIL domainReloadEnabled={reload_enabled}: {error}", flush=True)
    except (Exception, KeyboardInterrupt) as error:
        failed += 1
        report["error"] = str(error) or type(error).__name__
    finally:
        if original is not None:
            try:
                menu("Restore Settings")
                restored = settings()
                for key in ("enterPlayModeOptionsEnabled", "enterPlayModeOptions"):
                    assert restored[key] == original[key], restored
                report["settingsRestored"] = True
            except Exception as error:
                failed += 1
                report["settingsRestored"] = False
                report["restoreError"] = str(error)
        if host_process is not None:
            if host_process.poll() is None:
                try:
                    call("quit_editor", {})
                except Exception:
                    # The shutdown marker and process signals only target our own host.
                    shutdown_file.touch()
                try:
                    host_process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    host_process.terminate()
                    try:
                        host_process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        host_process.kill()
                        host_process.wait(timeout=10)
            shutdown_file.unlink(missing_ok=True)
            report["host"]["exitCode"] = host_process.returncode
            if host_log.exists():
                version_match = re.search(r"(?:Initialize engine version:|Unity Editor version:)\s*([^\s]+)",
                                          host_log.read_text(errors="replace"))
                if version_match:
                    report["host"]["unityVersion"] = version_match.group(1)
        report["status"] = "fail" if failed else "pass"
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n")
        print(f"Evidence: {args.output}; {report['status']}", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
