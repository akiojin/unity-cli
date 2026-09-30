#!/usr/bin/env python3
"""23 staff-report operations, warm unityd, real GUI Editor; budgets + history gate.

Use an isolated project created by e2e-matrix.py --suites perf. All scene/assets
are confined to Assets/Scenes/Generated/E2E/Performance. Each sample includes CLI
process startup; Play/Stop also include polling until the requested state exists.
The read_csharp operation uses the CLI's local file reader, as reported explicitly.
"""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

from perf_gate import append_history, check, read_history, regressions, summarize

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = "Assets/Scenes/Generated/E2E/Performance"
TARGET = "Perf394Target"
CUBE = "Perf394Cube"
SOURCE = FIXTURE + "/PerfSource.mat"
COPIED = FIXTURE + "/PerfCopy.mat"
MOVED = FIXTURE + "/PerfMoved.mat"
READ_PATH = "Packages/unity-cli-bridge/Editor/Core/UnityCliBridgeHost.cs"
_focus_spec = importlib.util.spec_from_file_location("bench_eval_focus", ROOT / "scripts/bench-eval.py")
_focus = importlib.util.module_from_spec(_focus_spec)
_focus_spec.loader.exec_module(_focus)


def operations():
    """Order matches #371 AC-6 / #394 AC-3; each cycle reverses its transient mutations."""
    return [
        ("editor_state", "get_editor_state", {}),
        ("hierarchy", "get_hierarchy", {}),
        ("find_gameobject", "find_gameobject", {"name": TARGET, "exactMatch": True}),
        ("transform", "get_component_values", {"gameObjectName": TARGET, "componentType": "Transform"}),
        ("position", "modify_gameobject", {"path": "/" + TARGET, "position": {"x": 1, "y": 2, "z": 3}}),
        ("create_cube", "create_gameobject", {"name": CUBE, "primitiveType": "cube"}),
        ("add_component", "add_component", {"gameObjectPath": "/" + CUBE, "componentType": "Rigidbody"}),
        ("remove_component", "remove_component", {"gameObjectPath": "/" + CUBE, "componentType": "Rigidbody"}),
        ("delete_gameobject", "delete_gameobject", {"path": "/" + CUBE}),
        ("scene_info", "get_scene_info", {}),
        ("scene_save", "save_scene", {}),
        ("console", "read_console", {"count": 20}),
        ("screenshot", "capture_screenshot", {"captureMode": "game", "width": 1280, "height": 720,
                                               "encodeAsBase64": False, "osFallback": False}),
        ("material_search", "manage_asset_database", {"action": "find_assets", "filter": "t:Material",
                                                       "searchInFolders": [FIXTURE]}),
        ("asset_copy", "manage_asset_database", {"action": "copy_asset", "fromPath": SOURCE, "toPath": COPIED}),
        ("asset_move", "manage_asset_database", {"action": "move_asset", "fromPath": COPIED, "toPath": MOVED}),
        ("asset_delete", "manage_asset_database", {"action": "delete_asset", "assetPath": MOVED}),
        ("import_settings", "manage_asset_import_settings", {"action": "get", "assetPath": SOURCE}),
        ("material_modify", "modify_material", {"materialPath": SOURCE, "properties": {"_Color": [.2, .4, .8, 1]}}),
        ("time_settings", "get_project_settings", {"includeTime": True, "includePlayer": False}),
        ("read_csharp", "read", {"path": READ_PATH, "maxLines": 50}),
        ("play_until_ready", "play_game", {}),
        ("stop_until_ready", "stop_game", {}),
    ]


def validate_result(tool, result):
    if not isinstance(result, dict) or not result or result.get("error") or result.get("success") is False:
        raise ValueError(f"{tool}: failed/empty response: {result}")
    if result.get("status") in ("error", "failed", "FAIL") or result.get("fallback") == "os":
        raise ValueError(f"{tool}: invalid benchmark response: {result}")
    if tool == "capture_screenshot":
        path = result.get("path")
        if not path or not Path(path).is_file():
            raise ValueError(f"{tool}: screenshot was not written")
    if tool == "read" and not result.get("content"):
        raise ValueError("C# read returned no source")
    if tool == "modify_material" and "_Color" not in result.get("propertiesModified", []):
        raise ValueError("material color was not modified")
    if tool == "find_gameobject" and result.get("count") != 1:
        raise ValueError("fixture GameObject was not uniquely found")


def focus_matches(focus, editor_pid, frontmost_pid):
    if frontmost_pid is None:
        return False
    return (frontmost_pid == editor_pid) if focus == "frontmost" else (frontmost_pid != editor_pid)


def transition(raw, playing, timeout=60, interval=.01):
    raw("play_game" if playing else "stop_game", {})
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = raw("get_editor_state", {})
        state = result.get("state", {})
        if state.get("isPlaying") is playing and not state.get("isCompiling", False) and not state.get("isUpdating", False):
            return result
        time.sleep(interval)
    raise TimeoutError(f"Editor did not reach isPlaying={playing}")


def ensure_focus(focus, editor_pid):
    if not focus_matches(focus, editor_pid, _focus.frontmost_pid()):
        if focus == "frontmost":
            _focus.activate(editor_pid)
        else:
            subprocess.run(["osascript", "-e", 'tell application "Finder" to activate'],
                           check=True, capture_output=True, timeout=15)
            time.sleep(.5)
    if not focus_matches(focus, editor_pid, _focus.frontmost_pid()):
        raise RuntimeError(f"cannot establish {focus} condition")


class Editor:
    def __init__(self, args, env):
        self.args, self.env = args, env
        self.base = [str(args.unity_cli), "--output", "json", "--host", "127.0.0.1",
                     "--port", str(args.port), "--timeout-ms", "60000"]
        self.calls = 0
        self.last_elapsed_ms = None

    def command(self, command):
        started = time.perf_counter()
        result = subprocess.run(self.base + command, env=self.env, capture_output=True, text=True, timeout=90)
        self.last_elapsed_ms = (time.perf_counter() - started) * 1000
        if result.returncode:
            raise RuntimeError(f"{command[:2]}: exit {result.returncode}: {result.stdout[:1000]} {result.stderr[:1000]}")
        if "falling back to direct TCP" in result.stderr:
            raise RuntimeError("unityd fallback invalidated the measurement")
        return json.loads(result.stdout)

    def raw(self, tool, params):
        result = self.command(["raw", tool, "--json", json.dumps(params)])
        self.calls += 1
        validate_result(tool, result)
        return result

    def setup(self):
        # Marker is written only by the isolated matrix host; never replace a user's scene.
        if not (self.args.project / ".unity/perf-owned-project").is_file():
            raise ValueError("use an isolated project from e2e-matrix.py --suites perf (ownership marker missing)")
        info = self.raw("get_editor_info", {})
        if Path(info["projectRoot"]).resolve() != self.args.project:
            raise ValueError("Bridge project differs from --project")
        if self.raw("get_editor_state", {})["state"]["isPlaying"]:
            raise ValueError("benchmark must start outside Play Mode")
        name = "Perf394_" + str(time.time_ns())
        self.raw("create_scene", {"sceneName": name, "path": FIXTURE, "loadScene": True})
        self.raw("create_gameobject", {"name": TARGET, "primitiveType": "cube"})
        for asset in (COPIED, MOVED):
            if (self.args.project / asset).exists():
                self.raw("manage_asset_database", {"action": "delete_asset", "assetPath": asset})
        self.raw("create_material", {"materialPath": SOURCE, "shader": "Unlit/Color", "overwrite": True})
        self.raw("save_scene", {})
        return info


def measure_focus(editor, focus, args, budgets):
    started_at = datetime.now(timezone.utc).isoformat()
    results, violations, samples = {}, [], {name: [] for name, _, _ in operations()}
    report = {"started_at": started_at, "focus": focus, "results": results, "status": "FAIL",
              "measurements_complete": False,
              "violations": violations, "operations": [
                  {"name": name, "tool": tool, "params": params, "route": "local" if tool == "read" else "unityd"}
                  for name, tool, params in operations()]}
    try:
        info = editor.setup()
        pid = _focus.listener_pid(args.port)
        settings = editor.raw("get_project_settings", {"includeEditor": True})["editor"]
        conditions = {"host": platform.node(), "os": platform.mac_ver()[0], "arch": platform.machine(),
                      "unity": info["unity"]["unityVersion"], "focus": focus,
                      "sample": "CLI spawn-to-exit; warm unityd; transitions include state polling",
                      "enterPlayModeOptionsEnabled": settings["enterPlayModeOptionsEnabled"],
                      "enterPlayModeOptions": settings["enterPlayModeOptions"], "suite_version": 1}
        report["conditions"] = conditions
        daemon_before = editor.command(["unityd", "status"])
        if not daemon_before.get("running") or daemon_before.get("connections", 0) < 1:
            raise RuntimeError("warm unityd connection not established")
        report["unityd_pid"] = daemon_before["pid"]
        report["editor_pid"] = pid
        # Complete cycles keep create/delete, add/remove, copy/move/delete and Play/Stop balanced.
        for cycle in range(args.warmup + args.iterations):
            for name, tool, params in operations():
                ensure_focus(focus, pid)
                before = _focus.frontmost_pid()
                started = time.perf_counter()
                if tool in ("play_game", "stop_game"):
                    transition(editor.raw, tool == "play_game")
                    elapsed = (time.perf_counter() - started) * 1000
                else:
                    editor.raw(tool, params)
                    elapsed = editor.last_elapsed_ms
                after = _focus.frontmost_pid()
                if not all(focus_matches(focus, pid, observed) for observed in (before, after)):
                    raise RuntimeError(f"{name}: Editor focus changed during sample")
                if cycle >= args.warmup:
                    samples[name].append(elapsed)
            print(f"{conditions['unity']} {focus}: cycle {cycle + 1}/{args.warmup + args.iterations}", flush=True)
        after = editor.command(["unityd", "status"])
        if after.get("pid") != daemon_before["pid"]:
            raise RuntimeError("unityd restarted during benchmark")
        results.update({name: summarize(values) for name, values in samples.items()})
        report["measurements_complete"] = True
        keys = {name: f"editor_{focus}_{name}" for name in results}
        report["budget_keys"] = keys
        violations += check(results, {name: budgets.get(key, {}) for name, key in keys.items()})
        violations += regressions(results, conditions, read_history(args.history), args.regression_percent)
        report["status"] = "FAIL" if violations else "PASS"
    except Exception as error:
        violations.append(str(error))
        results.update({name: summarize(values) for name, values in samples.items() if values})
    finally:
        report["finished_at"] = datetime.now(timezone.utc).isoformat()
        invalid_run = any(not any(v.startswith(name + ":") for name in samples) for v in violations)
        report["passed"] = 0 if invalid_run else sum(
            1 for name, row in results.items()
            if row["n"] >= args.iterations and not any(v.startswith(name + ":") for v in violations))
        report["failed"] = 23 - report["passed"]
        # Preserve all runs. Incomplete measurements cannot form a comparable baseline.
        append_history(args.history, report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-cli", type=Path, default=ROOT / "target/release/unity-cli")
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--port", type=int, default=6509)
    parser.add_argument("--focus", choices=["frontmost", "background", "both"], default="both")
    parser.add_argument("--iterations", type=int, default=30)
    parser.add_argument("--warmup", type=int, default=3)
    parser.add_argument("--budgets", type=Path, default=ROOT / "perf-budgets.json")
    parser.add_argument("--history", type=Path, default=ROOT / ".unity/perf/editor-ops-history.jsonl")
    parser.add_argument("--out", type=Path, default=ROOT / ".unity/perf/editor-ops.json")
    parser.add_argument("--regression-percent", type=float,
                        default=os.environ.get("UNITY_CLI_PERF_REGRESSION_PERCENT", "20"))
    args = parser.parse_args()
    if platform.system() != "Darwin" or args.iterations < 30 or args.warmup < 3:
        parser.error("macOS GUI Editor, >=30 samples and >=3 warmup cycles are required")
    if not math.isfinite(args.regression_percent) or args.regression_percent < 0:
        parser.error("regression percent must be finite and non-negative")
    args.unity_cli, args.project = args.unity_cli.resolve(), args.project.resolve()
    budgets = json.loads(args.budgets.read_text())["budgets"]
    env = dict(os.environ, UNITY_PROJECT_ROOT=str(args.project), UNITY_CLI_NO_AUTO_UPDATE="1")
    editor = Editor(args, env)
    focuses = ["frontmost", "background"] if args.focus == "both" else [args.focus]
    original_frontmost = _focus.frontmost_pid()
    runs = []
    try:
        for focus in focuses:
            runs.append(measure_focus(editor, focus, args, budgets))
    finally:
        # The project is explicitly owned. Stop Play Mode after an interrupted cycle.
        if (args.project / ".unity/perf-owned-project").is_file():
            try:
                if editor.raw("get_editor_state", {})["state"]["isPlaying"]:
                    transition(editor.raw, False)
            except Exception as error:
                print(f"cleanup: {error}", file=sys.stderr)
        if original_frontmost is not None:
            try:
                _focus.activate(original_frontmost)
            except Exception as error:
                print(f"focus restoration: {error}", file=sys.stderr)
    report = {"runs": runs, "command": sys.argv, "unity_cli": str(args.unity_cli),
              "status": "PASS" if len(runs) == len(focuses) and all(r["status"] == "PASS" for r in runs) else "FAIL"}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({"status": report["status"], "out": str(args.out),
                      "runs": [{"focus": r["focus"], "passed": r["passed"], "failed": r["failed"],
                                "violations": r["violations"]} for r in runs]}, indent=2))
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
