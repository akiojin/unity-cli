#!/usr/bin/env python3
"""Issue #441: real Editor test-result exits and lossless Bridge error codes.

Launches only disposable projects, retains all command/response evidence, and
terminates only the Editor and daemon processes owned by this run.
"""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import platform
import socket
import struct
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = """using NUnit.Framework;
namespace EnvelopeAcceptance {
    public class Mixed {
        [Test] public void Pass() { Assert.That(1 + 1, Is.EqualTo(2)); }
        [Test] public void Fail() { Assert.Fail("Intentional Issue 441 exit-code fixture"); }
    }
    public class Passing {
        [Test] public void Pass() { Assert.That(2 + 2, Is.EqualTo(4)); }
    }
}
"""


def load_matrix():
    spec = importlib.util.spec_from_file_location("envelope_matrix", ROOT / "scripts/e2e-matrix.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def wire_call(port, tool, params, auth):
    with socket.create_connection(("127.0.0.1", port), timeout=30) as connection:
        request = json.dumps({"id": "envelope-audit", "type": tool, "params": params, **auth}).encode()
        connection.sendall(struct.pack(">I", len(request)) + request)

        def read(size):
            data = b""
            while len(data) < size:
                chunk = connection.recv(size - len(data))
                if not chunk:
                    raise RuntimeError("Bridge closed before completing its response")
                data += chunk
            return data

        return json.loads(read(struct.unpack(">I", read(4))[0]))


def verify(args, matrix, version, destination, env):
    editor = Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/MacOS/Unity"
    _, project, _ = matrix.prepare(editor, destination)
    fixture = project / "Assets/Tests/EnvelopeAcceptance"
    fixture.mkdir(parents=True)
    (fixture / "EnvelopeAcceptance.cs").write_text(FIXTURE)
    (fixture / "EnvelopeAcceptance.asmdef").write_text(json.dumps({
        "name": "EnvelopeAcceptance", "includePlatforms": ["Editor"],
        "optionalUnityReferences": ["TestAssemblies"]}))
    env = dict(env, UNITY_PROJECT_ROOT=str(project), UNITY_CLI_ALLOW_BATCH_HOST="1",
               UNITY_CLI_PORT_OVERRIDE=str(args.port),
               UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(destination / "stop"))
    with socket.socket() as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        probe.bind(("127.0.0.1", args.port))
    command = [str(editor), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
               "-logFile", str(destination / "editor.log")]
    host = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    report = {"version": version, "launch": command, "project": str(project), "status": "FAIL"}

    def call(tool, params=None):
        command = [str(args.unity_cli), "--output", "json", "--host", "127.0.0.1",
                   "--port", str(args.port), "--timeout-ms", "30000", "raw", tool,
                   "--json", json.dumps(params or {})]
        result = subprocess.run(command, env=env, capture_output=True, text=True, timeout=40)
        with (destination / "calls.jsonl").open("a") as log:
            log.write(json.dumps({"command": command, "exit": result.returncode,
                                  "stdout": result.stdout, "stderr": result.stderr}) + "\n")
        envelope = json.loads(result.stdout)
        assert set(envelope) == {"success", "command", "data", "errors", "warnings"}, envelope
        assert envelope["success"] == (result.returncode == 0), envelope
        assert isinstance(envelope["errors"], list) and isinstance(envelope["warnings"], list), envelope
        return result.returncode, envelope

    try:
        deadline = time.monotonic() + args.startup_timeout
        while time.monotonic() < deadline:
            if host.poll() is not None:
                raise RuntimeError("Editor exited; inspect editor.log")
            try:
                owners = subprocess.run(["lsof", "-nP", f"-iTCP:{args.port}", "-sTCP:LISTEN", "-t"],
                                        capture_output=True, text=True, timeout=5)
            except subprocess.TimeoutExpired:
                # Ownership is still unproven; retry the read within the startup
                # deadline, without sending a request to an unknown listener.
                time.sleep(2)
                continue
            if set(owners.stdout.split()) == {str(host.pid)}:
                code, envelope = call("get_compilation_state")
                if code == 0 and matrix.ready(envelope["data"]):
                    break
            time.sleep(2)
        else:
            raise RuntimeError("Editor startup timed out; inspect editor.log")
        code, info = call("get_editor_info")
        assert code == 0 and info["data"]["unity"]["unityVersion"] == version, info

        # Compare the actual wire code with the CLI error, using a stable missing-job error.
        params = {"jobId": "issue-441-does-not-exist"}
        from bridge_auth import auth_fields
        auth = auth_fields(args.port, env["UNITY_CLI_EDITORS_DIR"], pid=host.pid)
        assert auth, "Owned Editor authentication file is missing"
        wire = wire_call(args.port, "get_scene_bake_status", params, auth)
        (destination / "bridge-error-wire.json").write_text(json.dumps(wire, indent=2))
        code, error = call("get_scene_bake_status", params)
        wire_code = wire.get("code") or wire.get("result", {}).get("code")
        assert wire_code, wire
        assert code == 6 and error["errors"][0]["code"] == wire_code, error
        report["bridge_error"] = {"exit": code, "wire_code": wire_code, "envelope": error}

        report["tests"] = []
        for fixture_name, expected_exit, expected_failed, expected_total in [
                ("Mixed", 8, 1, 2), ("Passing", 0, 0, 1)]:
            code, started = call("run_tests", {"testMode": "EditMode",
                                               "filter": "EnvelopeAcceptance." + fixture_name,
                                               "includeDetails": True})
            assert code == 0 and started["data"]["status"] == "running", started
            deadline = time.monotonic() + 240
            while time.monotonic() < deadline:
                code, result = call("get_test_status", {"includeTestResults": True})
                data = result["data"]
                if data.get("status") == "completed":
                    assert code == expected_exit, result
                    assert data["runId"] == started["data"]["runId"], result
                    assert data["failedTests"] == expected_failed and data["totalTests"] == expected_total, result
                    if expected_exit:
                        assert result["errors"][0]["code"] == "TEST_FAILED", result
                    report["tests"].append({"fixture": fixture_name, "exit": code, "envelope": result})
                    print("PASS", version, fixture_name, "exit", code, flush=True)
                    break
                assert code == 0 and data["status"] == "running", result
                time.sleep(1)
            else:
                raise RuntimeError("EditMode tests timed out")
        report["status"] = "PASS"
    except Exception as error:
        report["error"] = str(error)
        print("FAIL", version, str(error), flush=True)
    finally:
        matrix.stop_owned(host)
        (destination / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--versions", default="6000.3.25f1,2022.3.62f3")
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/debug/unity-cli")
    parser.add_argument("--port", type=int, default=6531)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--startup-timeout", type=int, default=900)
    args = parser.parse_args()
    args.unity_cli = args.unity_cli.resolve(strict=True)
    output = args.output or Path(tempfile.mkdtemp(prefix="unity-cli-envelope-"))
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, UNITY_CLI_TOOLS_ROOT=str(output / "tools"), UNITY_CLI_NO_AUTO_UPDATE="1",
               UNITY_CLI_EDITORS_DIR=str(output / "editors"))
    env.pop("UNITY_CLI_ALLOW_UNAUTHENTICATED", None)
    env.pop("UNITY_CLI_AUTH_TOKEN_FILE", None)
    report = {"started_at": datetime.now(timezone.utc).isoformat(), "platform": platform.platform(),
              "architecture": platform.machine(), "editors": []}
    print("Artifacts:", output, flush=True)
    try:
        matrix = load_matrix()
        for version in args.versions.split(","):
            destination = output / version
            destination.mkdir()
            report["editors"].append(verify(args, matrix, version, destination, env))
            (output / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    finally:
        subprocess.run([str(args.unity_cli), "unityd", "stop"], env=env,
                       capture_output=True, timeout=30)
    return 0 if report["editors"] and all(row["status"] == "PASS" for row in report["editors"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
