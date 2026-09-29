#!/usr/bin/env python3
"""Exercise on-demand unityd with a real Unity batch host (Issue #253)."""

import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time


def main():
    root = Path(__file__).resolve().parent.parent
    project = root / "UnityCliBridge"
    version = (project / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split()[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, default=root / "target/debug/unity-cli")
    parser.add_argument("--unity-path", type=Path,
                        default=Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"))
    parser.add_argument("--port", type=int, default=6453)
    args = parser.parse_args()
    artifacts = project / ".unity" / f"unityd-{time.strftime('%Y%m%d-%H%M%S')}"
    artifacts.mkdir(parents=True)
    report = {"project_unity_version": version, "commands": [], "checks": [], "passed": 0, "failed": 0}
    editor = None

    with tempfile.TemporaryDirectory(prefix="unityd-e2e-") as runtime:
        env = dict(os.environ, UNITY_CLI_TOOLS_ROOT=runtime,
                   UNITY_CLI_NO_AUTO_UPDATE="1", UNITY_CLI_UNITYD_IDLE_TIMEOUT="10",
                   UNITY_PROJECT_ROOT=str(project), RUST_LOG="debug")
        stop_file = Path(runtime) / "stop-editor"
        prefix = [str(args.cli.resolve()), "--output", "json", "--host", "127.0.0.1",
                  "--port", str(args.port), "--timeout-ms", "30000"]

        def cli(*command):
            started = time.perf_counter()
            result = subprocess.run(prefix + list(command), env=env, cwd=root,
                                    text=True, capture_output=True, timeout=45)
            elapsed = (time.perf_counter() - started) * 1000
            report["commands"].append({"args": list(command), "elapsed_ms": elapsed,
                                       "code": result.returncode, "stdout": result.stdout,
                                       "stderr": result.stderr})
            if result.returncode:
                raise RuntimeError(f"{command}: {result.stderr}\n{result.stdout}")
            return json.loads(result.stdout), elapsed

        def check(name, condition):
            report["checks"].append({"name": name, "pass": bool(condition)})
            report["passed" if condition else "failed"] += 1
            print(f"{'PASS' if condition else 'FAIL'} {name}", flush=True)
            if not condition:
                raise AssertionError(name)

        try:
            # Refuse to take over another agent's listener/project instance.
            with socket.socket() as probe:
                probe.bind(("127.0.0.1", args.port))
            launch = [str(args.unity_path), "-batchmode", "-nographics", "-projectPath", str(project),
                      "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
                      "-logFile", str(artifacts / "editor.log")]
            report["editor_command"] = launch
            editor = subprocess.Popen(launch, cwd=root, stdout=subprocess.DEVNULL,
                                      stderr=subprocess.DEVNULL,
                                      env=dict(os.environ, UNITY_CLI_ALLOW_BATCH_HOST="1",
                                               UNITY_CLI_PORT_OVERRIDE=str(args.port),
                                               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(stop_file)))
            deadline = time.monotonic() + 360
            while time.monotonic() < deadline:
                if editor.poll() is not None:
                    raise RuntimeError(f"Unity exited {editor.returncode}; see editor.log")
                try:
                    with socket.create_connection(("127.0.0.1", args.port), timeout=1):
                        break
                except OSError:
                    time.sleep(1)
            else:
                raise TimeoutError("Unity listener not ready within 360 seconds")

            editor_log = (artifacts / "editor.log").read_text(errors="replace")
            actual_version = re.search(r"Unity Editor version:\s+(\S+)", editor_log)
            if not actual_version:
                raise RuntimeError("Editor log did not report its actual version")
            report["unity_version"] = actual_version.group(1)

            status, _ = cli("unityd", "status")
            check("status does not start daemon", status["running"] is False)
            cold, cold_ms = cli("system", "ping", "--message", "cold")
            status, _ = cli("unityd", "status")
            check("AC-1 cold ping uses daemon", status["running"] and status["connections"] == 1)
            first_pid = status["pid"]
            warm, warm_ms = cli("system", "ping", "--message", "warm")
            status, _ = cli("unityd", "status")
            check("warm ping reuses process and connection", status["pid"] == first_pid and status["connections"] == 1)
            report.update(cold_ms=cold_ms, warm_ms=warm_ms, cold_response=cold, warm_response=warm)

            # Leave headroom for Editor refresh and process scheduling under load.
            # Polling status during this wait would itself keep the daemon alive.
            time.sleep(11)
            status, _ = cli("unityd", "status")
            check("idle daemon exited", status["running"] is False)
            cli("raw", "ping", "--json", '{"message":"after-idle"}')
            status, _ = cli("unityd", "status")
            check("AC-2 next operation restarts daemon", status["running"] and status["pid"] != first_pid)

            cli("unityd", "stop")
            time.sleep(0.3)
            commands = [("system", "ping", "--message", f"agent-{i}") for i in range(6)]
            with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
                results = list(pool.map(lambda command: cli(*command), commands))
            status, _ = cli("unityd", "status")
            check("AC-3 six concurrent first operations share one connection", len(results) == 6 and status["connections"] == 1)
            pid = status["pid"]
            cli("batch", "--json", '[{"tool":"ping","params":{"message":"batch"}}]')
            after, _ = cli("unityd", "status")
            check("batch reuses daemon", after["pid"] == pid)
            # A wrong target must fail rather than silently using the pooled Editor.
            with socket.socket() as unused:
                unused.bind(("127.0.0.1", 0))
                bad_port = unused.getsockname()[1]
                bad = subprocess.run([str(args.cli.resolve()), "--port", str(bad_port),
                                      "--timeout-ms", "500", "system", "ping"], env=env,
                                     cwd=root, capture_output=True, text=True, timeout=10)
            check("AC-4 wrong port does not reach pooled Editor", bad.returncode != 0)
            timings = "\n".join(item["stderr"] for item in report["commands"])
            check("AC-5 route and startup diagnostics", "startup_ms" in timings and "operation_ms" in timings)
        except Exception as error:
            report["error"] = str(error)
            if not report["failed"]:
                report["failed"] = 1
            raise
        finally:
            try:
                cli("unityd", "stop")
            except Exception:
                pass
            if editor is not None and editor.poll() is None:
                stop_file.touch()
                try:
                    editor.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    editor.terminate()
                    try:
                        editor.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        editor.kill()
                        editor.wait()
            (artifacts / "result.json").write_text(json.dumps(report, indent=2) + "\n")
            print(f"Results: {report['passed']} pass, {report['failed']} fail; {artifacts}", flush=True)


if __name__ == "__main__":
    main()
