#!/usr/bin/env python3
"""Player build E2E against a real Editor (optionally launch the batch host)."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import socket
import shutil
import struct
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "UnityCliBridge"


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=6428)
    parser.add_argument("--launch", action="store_true")
    parser.add_argument("--cli", default=str(ROOT / "target/debug/unity-cli"))
    parser.add_argument("--unity")
    parser.add_argument("--project-path", type=Path, default=PROJECT)
    parser.add_argument("--target", choices=("StandaloneOSX", "StandaloneWindows64"), default="StandaloneOSX")
    parser.add_argument("--isolated-project", action="store_true", help="Launch directly in an already isolated --project-path")
    parser.add_argument("--editmode", action="store_true", help="Run relevant EditMode tests before launching the isolated host")
    args = parser.parse_args(argv)
    if args.editmode and not args.launch:
        parser.error("--editmode requires --launch")
    return args


def preflight_status(response):
    code = response.get("code")
    if code == "BUILD_MODULE_MISSING":
        return "UNSUPPORTED"
    assert code == "INVALID_OUTPUT_PATH", response
    return "SUPPORTED"


def launch_target_args(unity, target):
    module = Path(unity).resolve().parent.parent / "PlaybackEngines/WindowsStandaloneSupport"
    if target == "StandaloneWindows64" and module.is_dir():
        return ["-buildTarget", "Win64"]
    return []


def valid_artifacts(target, output, report):
    artifacts = [Path(path).resolve() for path in report.get("artifacts", [])]
    if Path(report.get("outputPath", "")).resolve() != output.resolve():
        return False
    if not artifacts or not all(path.is_file() for path in artifacts):
        return False
    if target == "StandaloneWindows64":
        data = output.with_name(output.stem + "_Data")
        return (output.is_file() and data.is_dir() and output.resolve() in artifacts
                and any(data.resolve() in path.parents for path in artifacts))
    return output.is_dir() and (output / "Contents/Resources/Data").is_dir()


def main(argv=None):
    global PROJECT
    args = parse_args(argv)
    PROJECT = args.project_path.resolve()
    if args.launch and not args.isolated_project:
        import fcntl
        (ROOT / ".unity").mkdir(exist_ok=True)
        project_lock = (ROOT / ".unity/player-build-project.lock").open("w")
        fcntl.flock(project_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        # Unity import/build callbacks can serialize settings and assets. Use a copy
        # so verification never writes those generated changes into the checkout.
        source_project = PROJECT
        PROJECT = ROOT / ".unity/player-build-project"
        for folder in ("Assets", "Packages", "ProjectSettings"):
            destination = PROJECT / folder
            if destination.exists():
                shutil.rmtree(destination)
            shutil.copytree(source_project / folder, destination, ignore=shutil.ignore_patterns("Generated"))
    version = (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split(": ")[1]
    unity = args.unity or f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
    run = PROJECT / ".unity/player-build-e2e" / uuid.uuid4().hex
    run.mkdir(parents=True)
    stop = run / "stop"
    host = None
    checks = []
    status = "FAIL"
    fixture = PROJECT / "Assets/Scenes/Generated/E2E/Editor/PlayerBuildFailure.cs"
    failure_marker = run / "fail-build"
    compile_error = fixture.parent / "PlayerBuildCompileError.cs"

    def check(name, condition):
        checks.append({"name": name, "pass": bool(condition)})
        print(f"{'PASS' if condition else 'FAIL'} {name}", flush=True)
        assert condition, name

    def tcp(tool, params):
        request_id = uuid.uuid4().hex
        data = json.dumps({"id": request_id, "type": tool, "params": params}).encode()
        with socket.create_connection(("127.0.0.1", args.port), timeout=5) as conn:
            conn.settimeout(5)
            conn.sendall(struct.pack(">I", len(data)) + data)
            def read(n):
                result = b""
                while len(result) < n:
                    part = conn.recv(n - len(result))
                    if not part:
                        raise EOFError("Bridge disconnected")
                    result += part
                return result
            result = json.loads(read(struct.unpack(">I", read(4))[0]))
            assert result["id"] == request_id
            return result

    def cli(tool, params, success=True):
        proc = subprocess.run([args.cli, "raw", tool, "--json", json.dumps(params),
                               "--host", "127.0.0.1", "--port", str(args.port),
                               "--timeout-ms", "10000", "--output", "json"],
                              capture_output=True, text=True, timeout=20,
                              env=dict(os.environ, UNITY_PROJECT_ROOT=str(PROJECT)))
        (run / f"{len(checks)}-{tool}.json").write_text(proc.stdout + proc.stderr)
        assert (proc.returncode == 0) == success, proc.stdout + proc.stderr
        return json.loads(proc.stdout)["data"] if proc.stdout.strip() else None

    def settings():
        return {str(p.relative_to(PROJECT)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in (PROJECT / "ProjectSettings").glob("*") if p.is_file()}

    try:
        if args.editmode:
            xml = run / "editmode.xml"
            test_filter = ";".join(("UnityCliBridge.Tests.PlayerBuildHandlerTests",
                                    "UnityCliBridge.Tests.Editor.Core.BridgeCommandRouterTests",
                                    "UnityCliBridge.Tests.Editor.UnityCliBridgeHostConnectionTests"))
            unit = subprocess.run([unity, "-batchmode", "-nographics", "-projectPath", str(PROJECT),
                                   "-runTests", "-testPlatform", "EditMode", "-testFilter", test_filter,
                                   "-testResults", str(xml), "-logFile", str(run / "editmode.log")],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=600)
            check("EditMode process succeeds", unit.returncode == 0 and xml.exists())
            unit_results = ET.parse(xml).getroot()
            check("EditMode tests pass", int(unit_results.attrib["passed"]) > 0 and int(unit_results.attrib["failed"]) == 0)
            print(f"EditMode: {unit_results.attrib['passed']} passed / {unit_results.attrib['failed']} failed", flush=True)
        if args.launch:
            with socket.socket() as probe:
                probe.bind(("127.0.0.1", args.port))
            fixture.parent.mkdir(parents=True, exist_ok=True)
            fixture.write_text('''using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
public class PlayerBuildFailure : IPreprocessBuildWithReport {
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) {
        var marker = Environment.GetEnvironmentVariable("UNITY_CLI_PLAYER_BUILD_FAILURE_MARKER");
        if (!string.IsNullOrEmpty(marker) && File.Exists(marker))
            throw new BuildFailedException("Intentional Player E2E build failure");
    }
}
''')
            env = dict(os.environ, UNITY_CLI_ALLOW_BATCH_HOST="1",
                       UNITY_PROJECT_ROOT=str(PROJECT),
                       UNITY_CLI_PORT_OVERRIDE=str(args.port),
                       UNITY_CLI_PLAYER_BUILD_FAILURE_MARKER=str(failure_marker),
                       UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=str(stop))
            host_command = [unity, "-batchmode", "-nographics", "-projectPath", str(PROJECT),
                            "-executeMethod", "UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run",
                            "-logFile", str(run / "editor.log")]
            host_command[1:1] = launch_target_args(unity, args.target)
            host = subprocess.Popen(host_command, env=env,
                                    stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 600
        while True:
            try:
                if tcp("ping", {})["status"] == "success":
                    break
            except (OSError, EOFError):
                pass
            if time.monotonic() > deadline or (host and host.poll() is not None):
                raise RuntimeError(f"Editor unavailable; see {run / 'editor.log'}")
            time.sleep(2)
        check("Editor listener ready", True)
        with socket.create_connection(("127.0.0.1", args.port), timeout=10) as conn:
            conn.settimeout(10)
            expected_ids = set()
            for index in range(40):
                request_id = f"pipeline-{index}"
                expected_ids.add(request_id)
                wire = json.dumps({"id": request_id, "type": "ping" if index % 2 else "get_build_status",
                                   "params": {"buildId": "missing", "message": "x" * 20000}}).encode()
                conn.sendall(struct.pack(">I", len(wire)) + wire)
            def read_frame_bytes(n):
                data = b""
                while len(data) < n:
                    chunk = conn.recv(n - len(data))
                    if not chunk:
                        raise EOFError("Pipelined response disconnected")
                    data += chunk
                return data
            seen = set()
            for _ in expected_ids:
                size = struct.unpack(">I", read_frame_bytes(4))[0]
                assert size < 1000000, "Corrupt response frame length"
                seen.add(json.loads(read_frame_bytes(size))["id"])
            check("mixed same-socket replies preserve framing and IDs", seen == expected_ids)
        scene_name = "PlayerBuild_" + run.name
        scene = f"Assets/Scenes/Generated/E2E/{scene_name}.unity"
        cli("create_scene", {"sceneName": scene_name, "path": "Assets/Scenes/Generated/E2E/"})
        cli("save_scene", {"scenePath": scene})
        before = settings()
        player_name = "Player.exe" if args.target == "StandaloneWindows64" else "Player.app"
        output = run / "player" / player_name
        params = {"target": args.target, "scenes": [scene], "outputPath": str(output)}
        check("unknown build rejected", tcp("get_build_status", {"buildId": "missing"})["code"] == "BUILD_NOT_FOUND")
        cli("get_build_status", {"buildId": "missing"}, success=False)
        check("unknown build CLI exits nonzero", True)
        check("missing parameters rejected", tcp("build_player", {})["code"] == "INVALID_BUILD_PARAMETERS")
        preflight = tcp("build_player", dict(params, outputPath=str(run / "bad.invalid")))
        (run / "build-preflight.json").write_text(json.dumps(preflight, indent=2))
        if preflight_status(preflight) == "UNSUPPORTED":
            unsupported = tcp("build_player", params)
            (run / "build-status.json").write_text(json.dumps(unsupported, indent=2))
            check("missing module returns BUILD_MODULE_MISSING", unsupported.get("code") == "BUILD_MODULE_MISSING")
            status = "UNSUPPORTED"
            print(f"UNSUPPORTED {args.target}: BUILD_MODULE_MISSING; real build remains unverified", flush=True)
            return 2
        check("invalid output rejected", True)
        started = tcp("build_player", params)
        check("accepted is queued, not succeeded", started.get("result", {}).get("state") == "queued")
        build_id = started["result"]["buildId"]
        saw_running = False
        deadline = time.monotonic() + 900
        while True:
            snapshot = tcp("get_build_status", {"buildId": build_id})
            result = snapshot.get("result") or snapshot.get("details")
            state = result["state"]
            if state == "running":
                if not saw_running:
                    check("busy requests rejected while build runs", tcp("create_gameobject", {"name": "MustNotExist"})["code"] == "BUILD_BUSY")
                    cli("get_build_status", {"buildId": build_id})
                    saw_running = True
            if state not in ("queued", "running"):
                break
            assert time.monotonic() < deadline, "Build timed out"
            time.sleep(0.2)
        (run / "build-status.json").write_text(json.dumps(snapshot, indent=2))
        check("polling responds during actual build", saw_running)
        check("BuildReport succeeded", state == "succeeded" and result["reportResult"] == "Succeeded")
        check("report has no errors", result["totalErrors"] == 0)
        check("artifacts exist", bool(result["artifacts"]) and all(Path(p).exists() for p in result["artifacts"]))
        check("requested output and required data agree with BuildReport", valid_artifacts(args.target, output, result))
        success_cli = cli("get_build_status", {"buildId": build_id})
        report_fields = ("buildId", "state", "reportResult", "totalErrors", "totalWarnings",
                         "errors", "warnings", "outputPath", "artifacts",
                         "changedProjectSettings")
        check("CLI agrees with successful report", all(success_cli[key] == result[key] for key in report_fields) and
              math.isclose(success_cli["durationSeconds"], result["durationSeconds"], abs_tol=1e-6))
        after = settings()
        changed = sorted(p for p in before.keys() | after.keys() if before.get(p) != after.get(p))
        check("scene build settings unchanged", before.get("ProjectSettings/EditorBuildSettings.asset") == after.get("ProjectSettings/EditorBuildSettings.asset"))
        check("Unity build setting changes explicitly reported", changed == sorted(result["changedProjectSettings"]))
        check("existing output rejected", tcp("build_player", params)["code"] == "INVALID_OUTPUT_PATH")
        if args.launch:
            failure_marker.touch()
            failed_start = tcp("build_player", dict(params, outputPath=str(run / "failed" / player_name)))
            failed_id = failed_start["result"]["buildId"]
            deadline = time.monotonic() + 180
            while True:
                failed = tcp("get_build_status", {"buildId": failed_id})
                if failed["status"] == "error":
                    break
                assert time.monotonic() < deadline, "Failed build did not settle"
                time.sleep(0.2)
            check("real BuildReport failure stays failed", failed["details"]["state"] == "failed" and failed["details"]["reportResult"] == "Failed")
            check("BuildReport errors preserved", failed["details"]["totalErrors"] > 0 and bool(failed["details"]["errors"]))
            failed_cli = cli("get_build_status", {"buildId": failed_id}, success=False)
            check("failed CLI preserves report and exits nonzero", failed_cli is not None and
                  all(failed_cli["details"][key] == failed["details"][key] for key in report_fields) and
                  math.isclose(failed_cli["details"]["durationSeconds"], failed["details"]["durationSeconds"], abs_tol=1e-6))
            failure_marker.unlink()
        if args.target == "StandaloneOSX":
            executable = next((output / "Contents/MacOS").iterdir())
            player = subprocess.Popen([str(executable), "-batchmode", "-nographics", "-logFile", str(run / "player.log")],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
            try:
                time.sleep(5)
                check("Mac headless process starts", player.poll() is None and (run / "player.log").exists())
            finally:
                if player.poll() is None:
                    player.terminate()
                player.wait(timeout=20)
        if args.launch:
            interrupted_start = tcp("build_player", dict(params, outputPath=str(run / "interrupted" / player_name)))
            interrupted_id = interrupted_start["result"]["buildId"]
            # Kill only this test's own Editor after acceptance; the durable queued record
            # must become interrupted rather than being reported as successful on restart.
            host.kill()
            host.wait(timeout=20)
            host_command[-1] = str(run / "editor-restarted.log")
            host = subprocess.Popen(host_command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
            deadline = time.monotonic() + 180
            while True:
                try:
                    interrupted = tcp("get_build_status", {"buildId": interrupted_id})
                    break
                except (OSError, EOFError):
                    assert time.monotonic() < deadline, "Editor restart timed out"
                    time.sleep(2)
            check("restart marks unfinished job interrupted", interrupted["details"]["state"] == "interrupted")
            interrupted_cli = cli("get_build_status", {"buildId": interrupted_id}, success=False)
            check("interrupted CLI preserves details and exits nonzero", interrupted_cli["details"]["state"] == "interrupted")
            compile_error.write_text("#error Intentional Player build E2E compilation failure\n")
            # The restarted Editor may still be in its initial refresh/compile and not
            # answer within the socket timeout; retry transport failures until the deadline.
            deadline = time.monotonic() + 90
            while True:
                try:
                    tcp("refresh_assets", {})
                    break
                except (OSError, EOFError):
                    assert time.monotonic() < deadline, "refresh_assets timed out"
                    time.sleep(2)
            while True:
                try:
                    compilation = tcp("build_player", dict(params, outputPath=str(run / "compile" / player_name)))
                except (OSError, EOFError):
                    assert time.monotonic() < deadline, "Compilation failure not observed"
                    time.sleep(2)
                    continue
                if compilation.get("code") == "BUILD_COMPILATION_ERROR":
                    break
                assert "result" not in compilation, "Build accepted despite compilation error fixture"
                assert time.monotonic() < deadline, "Compilation failure not observed"
                time.sleep(1)
            check("compilation error is not success", compilation["status"] == "error")
        print("Windows hardware: pending owner verification. Graphics/input: not tested.", flush=True)
        status = "PASS"
    except BaseException as exc:
        checks.append({"name": "suite completion", "pass": False, "error": str(exc)})
        raise
    finally:
        if host and host.poll() is None:
            stop.touch()
            try:
                host.wait(timeout=20)
            except subprocess.TimeoutExpired:
                host.terminate()
                host.wait(timeout=20)
        if args.launch and fixture.exists():
            fixture.unlink()
            fixture.with_suffix(".cs.meta").unlink(missing_ok=True)
        if args.launch and compile_error.exists():
            compile_error.unlink()
            compile_error.with_suffix(".cs.meta").unlink(missing_ok=True)
        summary = {"unityVersion": version, "target": args.target, "projectPath": str(PROJECT),
                   "status": status, "checks": checks,
                   "passed": sum(c["pass"] for c in checks), "failed": sum(not c["pass"] for c in checks)}
        (run / "summary.json").write_text(json.dumps(summary, indent=2))
        print(f"Evidence: {run}", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
