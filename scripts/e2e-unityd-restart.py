#!/usr/bin/env python3
"""Editor restart vs. pooled unityd connection on a real macOS Editor (Issue #384).

Each cycle keeps one unityd alive, terminates the Editor like `pkill`, restarts it on
the same port, then sends ping -> reads -> one mutating call through unityd. The first
mutating call must succeed, and it must create exactly one GameObject.
Readiness is polled over a separate direct TCP socket so the pooled connection is
still stale when the CLI sequence starts.
"""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import signal
import socket
import struct
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent


def load_prepare():
    spec = importlib.util.spec_from_file_location("e2e_matrix", ROOT / "scripts/e2e-matrix.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.prepare, module.ready


def direct_call(port, tool, params=None, timeout=5):
    """One request on its own socket; never touches the unityd pool."""
    payload = json.dumps({"id": "probe", "type": tool, "params": params or {}}).encode()
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as stream:
        stream.sendall(struct.pack(">i", len(payload)) + payload)
        header = stream.recv(4, socket.MSG_WAITALL)
        (length,) = struct.unpack(">i", header)
        body = b""
        while len(body) < length:
            chunk = stream.recv(length - len(body))
            if not chunk:
                raise ConnectionError("short response")
            body += chunk
    response = json.loads(body)
    result = response.get("result", response.get("data", response))
    return json.loads(result) if isinstance(result, str) else result


def port_is_free(port):
    # SO_REUSEADDR mirrors the Editor listener: TIME_WAIT from the killed Editor is fine.
    with socket.socket() as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            probe.bind(("127.0.0.1", port))
            return True
        except OSError:
            return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, default=ROOT / "target/release/unity-cli")
    parser.add_argument("--unity-path", type=Path, default=Path(
        "/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"))
    parser.add_argument("--port", type=int, default=6471)
    parser.add_argument("--cycles", type=int, default=5)
    parser.add_argument("--workdir", type=Path,
                        help="Reuse a prepared project (keeps Library between runs)")
    args = parser.parse_args()
    prepare, ready = load_prepare()

    workdir = args.workdir or Path(tempfile.mkdtemp(prefix="unityd-restart-"))
    workdir.mkdir(parents=True, exist_ok=True)
    project = workdir / "project"
    if not project.exists():
        prepare(args.unity_path, workdir)
    artifacts = workdir / f"run-{time.strftime('%Y%m%d-%H%M%S')}"
    artifacts.mkdir()
    runtime = artifacts / "unityd-runtime"
    runtime.mkdir()
    stop_file = artifacts / "stop-editor"
    report = {"cli": str(args.cli), "unity_path": str(args.unity_path), "port": args.port,
              "cycles": [], "commands": [], "passed": 0, "failed": 0}
    env = dict(os.environ, UNITY_CLI_TOOLS_ROOT=str(runtime), UNITY_CLI_NO_AUTO_UPDATE="1",
               UNITY_CLI_UNITYD_IDLE_TIMEOUT="3600", UNITY_PROJECT_ROOT=str(project))
    prefix = [str(args.cli.resolve()), "--output", "json", "--host", "127.0.0.1",
              "--port", str(args.port), "--timeout-ms", "30000"]
    editor = None
    generation = 0

    def cli(*command):
        result = subprocess.run(prefix + list(command), env=env, cwd=ROOT, text=True,
                                capture_output=True, timeout=60)
        report["commands"].append({"generation": generation, "args": list(command),
                                   "code": result.returncode, "stdout": result.stdout[-2000:],
                                   "stderr": result.stderr[-2000:]})
        if result.returncode:
            raise RuntimeError(f"{' '.join(command)} failed: {result.stderr.strip()}")
        return json.loads(result.stdout)

    def check(name, condition):
        report["passed" if condition else "failed"] += 1
        print(f"{'PASS' if condition else 'FAIL'} {name}", flush=True)
        if not condition:
            raise AssertionError(name)

    def launch():
        nonlocal editor, generation
        if not port_is_free(args.port):
            raise RuntimeError(f"port {args.port} is already in use")
        generation += 1
        stop_file.unlink(missing_ok=True)
        log = artifacts / f"editor-{generation}.log"
        editor = subprocess.Popen(
            [str(args.unity_path), "-batchmode", "-nographics", "-projectPath", str(project),
             "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
             "-logFile", str(log)], cwd=ROOT, stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=dict(os.environ, UNITY_CLI_ALLOW_BATCH_HOST="1", UNITY_CLI_PORT=str(args.port),
                     UNITY_CLI_PORT_OVERRIDE=str(args.port),
                     UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(stop_file)))
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline:
            if editor.poll() is not None:
                raise RuntimeError(f"Unity exited {editor.returncode}; see {log}")
            try:
                state = direct_call(args.port, "get_editor_state")
                if ready(state.get("state", state)):
                    return
            except (OSError, ValueError):
                pass
            time.sleep(2)
        raise TimeoutError(f"Unity not ready; see {log}")

    def terminate():
        # Same signal as `pkill`: the Editor's sockets are closed by the OS, not by unityd.
        editor.send_signal(signal.SIGTERM)
        try:
            editor.wait(timeout=60)
        except subprocess.TimeoutExpired:
            editor.kill()
            editor.wait(timeout=30)
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            if port_is_free(args.port):
                return
            time.sleep(1)
        raise TimeoutError("port not released")

    try:
        launch()
        cli("system", "ping")
        cli("raw", "get_hierarchy", "--json", "{}")
        status = cli("unityd", "status")
        check("warm-up: unityd holds a pooled Editor connection", status["connections"] == 1)
        daemon_pid = status["pid"]
        for cycle in range(1, args.cycles + 1):
            row = {"cycle": cycle}
            report["cycles"].append(row)
            terminate()
            launch()
            name = f"Probe384-{cycle}"
            try:
                cli("system", "ping")
                for _ in range(3):
                    cli("raw", "get_hierarchy", "--json", "{}")
                cli("raw", "create_gameobject", "--json", json.dumps({"name": name}))
                row["mutating_ok"] = True
            except RuntimeError as error:
                row["mutating_ok"] = False
                row["error"] = str(error)
            check(f"cycle {cycle}: ping -> reads -> mutating succeeds after restart",
                  row["mutating_ok"])
            found = cli("raw", "find_gameobject", "--json",
                        json.dumps({"name": name, "exactMatch": True}))
            row["objects"] = found["count"]
            check(f"cycle {cycle}: exactly one {name} exists", found["count"] == 1)
            row["daemon_pid"] = cli("unityd", "status")["pid"]
            check(f"cycle {cycle}: same unityd survived the restart",
                  row["daemon_pid"] == daemon_pid)
    except Exception as error:
        report["error"] = str(error)
        report["failed"] = max(report["failed"], 1)
        raise
    finally:
        try:
            cli("unityd", "stop")
        except Exception:
            pass
        if editor is not None and editor.poll() is None:
            stop_file.touch()
            try:
                editor.wait(timeout=30)
            except subprocess.TimeoutExpired:
                editor.kill()
                editor.wait()
        (artifacts / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        print(f"Results: {report['passed']} pass, {report['failed']} fail; {artifacts}",
              flush=True)


if __name__ == "__main__":
    main()
