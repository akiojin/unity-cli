#!/usr/bin/env bash
# Verify recording formats against an already running, graphics-enabled Unity Editor.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export UNITY_CLI_BIN="${UNITY_CLI_BIN:-${SCRIPT_DIR}/../target/release/unity-cli}"
python3 - "$@" <<'PY'
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

parser = argparse.ArgumentParser(description="Validate video formats using a live Unity Game View")
parser.add_argument("--host", default=os.environ.get("UNITY_CLI_HOST", "127.0.0.1"))
parser.add_argument("--port", default=os.environ.get("UNITY_CLI_PORT", "6400"))
parser.add_argument("--unity-cli", default=os.environ["UNITY_CLI_BIN"])
parser.add_argument("--timeout-ms", default="120000")
parser.add_argument("--artifact-dir")
args = parser.parse_args()
cli = shutil.which(args.unity_cli)
if not cli:
    parser.error("unity-cli not found; set UNITY_CLI_BIN or use --unity-cli")
artifacts = Path(args.artifact_dir or tempfile.mkdtemp(prefix="unity-cli-video-formats-"))
artifacts.mkdir(parents=True, exist_ok=True)
log = (artifacts / "run.log").open("w")
passed = failed = calls = 0
original_playing = None
output_paths = set()

def report(message):
    print(message, flush=True)
    print(message, file=log, flush=True)

def call(tool, params=None, expect_error=False, allow_cli_validation=False):
    global calls
    calls += 1
    stem = f"{calls:03d}-{tool}"
    request = artifacts / f"{stem}.request.json"
    request.write_text(json.dumps(params or {}))
    command = [cli, "--host", args.host, "--port", str(args.port),
               "--timeout-ms", args.timeout_ms, "raw", tool,
               "--params-file", str(request), "--output", "json"]
    result = subprocess.run(command, capture_output=True, text=True,
                            timeout=int(args.timeout_ms) / 1000 + 15)
    (artifacts / f"{stem}.response.json").write_text(result.stdout)
    (artifacts / f"{stem}.stderr.log").write_text(result.stderr)
    print(f"{stem}: exit={result.returncode} {result.stdout.strip()} {result.stderr.strip()}",
          file=log, flush=True)
    try:
        envelope = json.loads(result.stdout)
        data = envelope["data"]
    except ValueError as error:
        if (expect_error and allow_cli_validation and result.returncode != 0
                and "format" in result.stderr.lower()
                and any(word in result.stderr.lower() for word in ("invalid", "enum", "must be", "not one of"))):
            return {"error": result.stderr.strip(), "source": "CLI validation"}
        raise AssertionError(f"{tool}: non-JSON response: {result.stdout} {result.stderr}") from error
    if expect_error:
        assert result.returncode != 0 and envelope["success"] is False, envelope
        assert envelope["errors"], f"{tool}: expected explicit error: {envelope}"
        if data is None:
            assert allow_cli_validation and result.returncode == 2, envelope
            assert envelope["errors"][0]["code"] == "INVALID_ARGUMENT", envelope
        return envelope
    else:
        assert isinstance(data, dict), f"{tool}: expected response object: {data}"
        assert result.returncode == 0 and not data.get("error"), f"{tool}: {data} {result.stderr}"
    return data

def playing():
    return bool(call("get_editor_state").get("state", {}).get("isPlaying", False))

def set_playing(expected):
    if playing() != expected:
        call("play_game" if expected else "stop_game")
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        if playing() == expected:
            return
        time.sleep(1)
    raise AssertionError(f"Timed out waiting for isPlaying={expected}")

def inactive():
    assert call("capture_video_status").get("isRecording") is False

def error_case(params, allow_cli_validation=False):
    call("capture_video_start", params, expect_error=True, allow_cli_validation=allow_cli_validation)
    inactive()

def recording(fmt):
    start = call("capture_video_start", {
        "captureMode": "game", "format": fmt, "width": 320, "height": 180,
        "fps": 10, "maxDurationSec": 0,
    })
    assert start.get("isRecording") is True, f"Recorder did not start: {start}"
    assert call("capture_video_status").get("isRecording") is True
    time.sleep(2)
    stop = call("capture_video_stop")
    inactive()
    assert stop.get("outputPath"), f"Missing outputPath: {stop}"
    path = Path(stop["outputPath"])
    assert path.is_file() and path.stat().st_size > 0, f"Missing/empty output: {path}"
    assert path.resolve() not in output_paths, f"Reused output path across sessions: {path}"
    output_paths.add(path.resolve())
    actual = fmt.lower()
    extension = ".png" if actual == "png_sequence" else f".{actual}"
    assert path.suffix.lower() == extension, f"Wrong extension: {path}"
    with path.open("rb") as source:
        prefix = source.read(4096)
    header = prefix[:32]
    if actual == "mp4":
        assert header[4:8] == b"ftyp", f"Not MP4: {header.hex()}"
    elif actual == "webm":
        assert header[:4] == bytes.fromhex("1a45dfa3"), f"Not WebM: {header.hex()}"
        assert b"webm" in prefix, "Missing WebM document type"
    else:
        frames = sorted(path.parent.glob("*.png"))
        assert len(frames) > 1, f"Expected multiple PNG frames: {frames}"
        assert path.parent.name != "capture", "PNG frames require a unique session directory"
        assert path == frames[0], f"outputPath must identify first frame: {path}"
        for frame in frames:
            with frame.open("rb") as source:
                assert source.read(8) == bytes.fromhex("89504e470d0a1a0a"), f"Not PNG: {frame}"
        report(f"PNG sequence: {len(frames)} frames in {path.parent}")
    report(f"Output {fmt}: {path} ({path.stat().st_size} bytes, header={header.hex()})")

def test(name, action):
    global passed, failed
    try:
        action()
        passed += 1
        report(f"PASS {name}")
    except Exception as error:
        failed += 1
        report(f"FAIL {name}: {error}")
        try:
            if call("capture_video_status").get("isRecording"):
                call("capture_video_stop")
        except Exception as cleanup_error:
            report(f"Recording cleanup failed: {cleanup_error}")

report(f"Video format E2E: {args.host}:{args.port}; artifacts={artifacts}")
try:
    original_playing = playing()
    assert call("capture_video_status").get("isRecording") is False, "Existing recording is active"
    set_playing(False)
    test("recording outside Play Mode returns error", lambda: error_case({"format": "mp4"}))
    set_playing(True)
    time.sleep(2)
    test("invalid format returns error", lambda: error_case({"format": "invalid"}, allow_cli_validation=True))
    for fmt in ("mp4", "webm", "png_sequence", "mp4"):
        test(f"{fmt} file format and session lifecycle", lambda fmt=fmt: recording(fmt))
except Exception as error:
    failed += 1
    report(f"FAIL setup: {error}")
finally:
    if original_playing is not None:
        try:
            set_playing(original_playing)
        except Exception as error:
            failed += 1
            report(f"FAIL restore Play Mode: {error}")
    summary = {"passed": passed, "failed": failed, "total": passed + failed,
               "artifacts": str(artifacts), "host": args.host, "port": args.port}
    (artifacts / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    report(f"Passed: {passed}; Failed: {failed}; Total: {passed + failed}")
    log.close()
sys.exit(1 if failed else 0)
PY
