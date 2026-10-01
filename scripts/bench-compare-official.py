#!/usr/bin/env python3
"""Same-condition latency benchmark: unity-cli vs the official Unity CLI.

Measures against ONE running macOS Unity Editor whose isolated project has
both `com.akiojin.unity-cli-bridge` and `com.unity.pipeline` installed.
See docs/comparison.md for the reproducible project preparation commands.
The project must carry the e2e-matrix.py ownership marker; setup replaces its
active scene with a generated benchmark scene and restores Game View focus
settings on exit. Never point this at a working project.

--mode process measures three operations:

  create_gameobject  - create an empty GameObject
  hierarchy          - read the active scene hierarchy
  eval               - evaluate the C# expression 1+2

Each process-mode sample is the wall-clock time of one CLI invocation (process
start -> exit), which is what an agent pays per tool call. Both tools are
measured alternately inside the same iteration (the order flips every
iteration) so scene growth and Editor load affect them equally. Any failed
call aborts the run: only fully successful runs produce results.
--mode resident measures 23 balanced operations: CLI through warm unityd vs
a persistent official NDJSON shell. Play/Stop include polling to completion.
Both modes exclude daemon/shell startup and retain all measured samples.

Usage:
  python3 scripts/bench-compare-official.py \
      --project-path /path/to/BenchProj \
      --unity-cli target/release/unity-cli \
      --official ~/.unity/bin/unity \
      --port 6400 --iterations 100 --out bench.json

Output: JSON with p50/p95/mean/min/max (ms) per tool per operation, plus
the measurement conditions.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import select
import statistics
import subprocess
import sys
import time
from datetime import datetime, timezone

OPERATIONS = ("create_gameobject", "hierarchy", "eval")


def load_editor_bench():
    spec = importlib.util.spec_from_file_location("editor_bench", Path(__file__).with_name("bench-editor-ops.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class OfficialShell:
    """Serial, correlated NDJSON requests; startup is explicitly warmed separately."""

    def __init__(self, argv, env, timeout):
        self.process = subprocess.Popen(argv, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.DEVNULL)
        self.timeout, self.sequence = timeout, 0

    def __enter__(self):
        return self

    def request(self, argv):
        self.sequence += 1
        identity = str(self.sequence)
        self.process.stdin.write((json.dumps({"id": identity, "argv": argv}) + "\n").encode())
        self.process.stdin.flush()
        deadline = time.monotonic() + self.timeout
        line = bytearray()
        # Unbuffered descriptor reads also bound partial-line and silent-child hangs.
        while not line.endswith(b"\n"):
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not select.select([self.process.stdout], [], [], remaining)[0]:
                raise TimeoutError("official NDJSON response timed out")
            byte = os.read(self.process.stdout.fileno(), 65536)
            if not byte:
                raise ValueError("official shell closed before its response")
            line.extend(byte)
        result = json.loads(line)
        if result.get("id") != identity or result.get("exitCode") != 0:
            raise ValueError(f"invalid official NDJSON response: {result}")
        envelope = result.get("envelope", {})
        if envelope.get("success") is not True:
            raise ValueError(f"official request failed: {envelope}")
        return envelope

    def __exit__(self, *_):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=.5)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            try:
                self.process.wait(timeout=.5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.process.stdout.close()


def resident_operations():
    """Native official counterparts to the existing 23-operation unityd benchmark."""
    bench = load_editor_bench()
    target, cube = {"hierarchyPath": "/" + bench.TARGET}, {"hierarchyPath": "/" + bench.CUBE}
    mappings = [
        ("editor_status", {}),
        ("get_scene_hierarchy", {}),
        ("find_gameobjects", {"name": bench.TARGET}),
        ("get_component_properties", {"target": target, "type": "Transform"}),
        ("set_transform", {"target": target, "position": [1, 2, 3]}),
        ("create_gameobject", {"name": bench.CUBE, "primitive": "Cube"}),
        ("add_component", {"target": cube, "type": "Rigidbody"}),
        ("remove_component", {"target": cube, "type": "Rigidbody"}),
        ("delete_gameobject", {"target": cube}),
        ("list_open_scenes", {}),
        ("save_scene", {}),
        ("console", {"tail": 20, "level": "log"}),
        ("screenshot", {"view": "game", "width": 1280, "height": 720}),
        ("find_assets", {"type": "Material", "search_in": [bench.FIXTURE]}),
        ("copy_asset", {"asset": bench.SOURCE, "destination": bench.COPIED}),
        ("move_asset", {"asset": bench.COPIED, "destination": bench.MOVED}),
        ("delete_asset", {"asset": bench.MOVED, "confirm": True}),
        ("get_import_settings", {"asset": bench.SOURCE}),
        ("set_material_properties", {"material": bench.SOURCE, "properties": {"_Color": [.2, .4, .8, 1]}}),
        ("get_time_settings", {}),
        ("read_text_file", {"path": "Assets/HotReloadProbe.cs"}),
        ("editor_play", {}),
        ("editor_stop", {}),
    ]
    rows = [(name, tool, params, other, other_params)
            for (name, tool, params), (other, other_params) in zip(bench.operations(), mappings)]
    # Pipeline read_text_file only permits its Assets authoring root. Both tools
    # read the same small (<50 lines) fixture copied by e2e-matrix.prepare.
    rows[20][2]["path"] = "Assets/HotReloadProbe.cs"
    return rows


def percentile(samples: list[float], pct: float) -> float:
    """Nearest-rank percentile (no interpolation)."""
    ordered = sorted(samples)
    rank = max(1, int(-(-pct * len(ordered) // 100)))  # ceil
    return ordered[rank - 1]


def summarize(samples: list[float]) -> dict:
    return {
        "n": len(samples),
        "p50_ms": round(percentile(samples, 50), 2),
        "p95_ms": round(percentile(samples, 95), 2),
        "mean_ms": round(statistics.fmean(samples), 2),
        "min_ms": round(min(samples), 2),
        "max_ms": round(max(samples), 2),
    }


def unity_cli_argv(binary: str, op: str, index: int) -> list[str]:
    if op == "create_gameobject":
        params = json.dumps({"name": f"BenchUnityCli{index}"})
        return [binary, "raw", "create_gameobject", "--json", params, "--output", "json"]
    if op == "hierarchy":
        return [binary, "raw", "get_hierarchy", "--json", "{}", "--output", "json"]
    return [binary, "editor", "eval", "1+2", "--output", "json"]


def official_argv(binary: str, project: str, op: str, index: int) -> list[str]:
    base = [binary, "command"]
    tail = ["--project-path", project, "--format", "json"]
    if op == "create_gameobject":
        return base + ["create_gameobject", "--name", f"BenchOfficial{index}"] + tail
    if op == "hierarchy":
        return base + ["get_scene_hierarchy"] + tail
    # The official eval compiles statements, so the expression needs `return`.
    return base + ["eval"] + tail + ["return 1+2;"]


def check_unity_cli(op: str, stdout: str) -> None:
    data = json.loads(stdout)
    # Accept the shared CLI contract as well as the recorded v0.17.0 payload.
    if isinstance(data, dict) and {'command', 'data', 'errors', 'warnings'} <= data.keys():
        if data.get('success') is not True or data['errors']:
            raise ValueError(f"unity-cli call failed: {stdout[:300]}")
        data = data['data']
    if not isinstance(data, dict) or not data or data.get("success") is False or data.get("error"):
        raise ValueError(f"unity-cli call failed: {stdout[:300]}")
    if op == "eval" and (data.get("value") != 3 or data.get("state") != "completed"):
        raise ValueError(f"unexpected eval result: {stdout[:300]}")
    if op == "hierarchy" and "hierarchy" not in data:
        raise ValueError(f"unexpected hierarchy result: {stdout[:300]}")


def check_official(op: str, stdout: str) -> None:
    data = json.loads(stdout)
    if not data.get("success"):
        raise ValueError(f"official call failed: {stdout[:300]}")
    result = data.get("data", {}).get("result")
    if isinstance(result, dict) and (result.get("success") is False or result.get("error")):
        raise ValueError(f"official command failed: {stdout[:300]}")
    if op == "read_text_file" and not result.get("contents"):
        raise ValueError("official C# read returned no source")
    if op == "set_material_properties" and "_Color" not in result.get("applied", []):
        raise ValueError("official material color was not modified")
    if op == "eval" and data["data"]["result"].get("result") != 3:
        raise ValueError(f"unexpected eval result: {stdout[:300]}")


def run_once(argv: list[str], env: dict, timeout_s: float) -> tuple[float, str]:
    start = time.perf_counter()
    proc = subprocess.run(argv, env=env, capture_output=True, text=True, timeout=timeout_s)
    elapsed_ms = (time.perf_counter() - start) * 1000.0
    if proc.returncode != 0:
        raise RuntimeError(
            f"exit {proc.returncode}: {' '.join(argv[:4])}\n{proc.stdout[:300]}\n{proc.stderr[:300]}"
        )
    if "falling back to direct TCP" in proc.stderr:
        raise RuntimeError("unityd fallback invalidated the measurement: " + proc.stderr)
    return elapsed_ms, proc.stdout


def tool_version(argv: list[str], env: dict) -> str:
    try:
        return subprocess.run(argv, env=env, capture_output=True, text=True, timeout=30).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def project_facts(project: str) -> dict:
    """Editor version and resolved package versions of the benchmark project."""
    facts: dict = {}
    try:
        with open(os.path.join(project, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8") as fh:
            for line in fh:
                if line.startswith("m_EditorVersion:"):
                    facts["unity_editor_version"] = line.split(":", 1)[1].strip()
    except OSError:
        pass
    try:
        with open(os.path.join(project, "Packages", "packages-lock.json"), encoding="utf-8") as fh:
            deps = json.load(fh).get("dependencies", {})
        for name in ("com.akiojin.unity-cli-bridge", "com.unity.pipeline"):
            if name in deps:
                facts[name] = deps[name].get("version")
    except (OSError, ValueError):
        pass
    return facts


def official_command(shell, project, tool, params):
    argv = ["command", tool, "--project-path", str(project), "--format", "json"]
    for key, value in params.items():
        argv += ["--" + key, value if isinstance(value, str) else json.dumps(value)]
    envelope = shell.request(argv)
    check_official(tool, json.dumps(envelope))
    result = envelope["data"]["result"]
    if tool == "screenshot" and not Path(result.get("path", "")).is_file():
        raise ValueError("official screenshot was not written")
    if tool == "find_gameobjects" and result.get("count") != 1:
        raise ValueError("official fixture GameObject was not uniquely found")
    return result


def official_transition(call, playing, timeout):
    call("editor_play" if playing else "editor_stop", {})
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = call("editor_status", {})
        if (result.get("playMode") == ("playing" if playing else "stopped")
                and result.get("compiling") is False and result.get("domainReloadInProgress") is False):
            return result
        time.sleep(.01)
    raise TimeoutError("official Editor state transition did not complete")


def measure_resident(args, env, editor, bench, pid):
    rows = resident_operations()
    samples = {tool: {row[0]: [] for row in rows} for tool in ("unity-cli", "official")}
    with OfficialShell([args.official, "shell", "--protocol", "ndjson"], env, args.timeout) as shell:
        call = lambda tool, params: official_command(shell, args.project, tool, params)
        # Initialization and lockfile discovery are outside every timed sample.
        status = call("editor_status", {})
        if Path(status["projectPath"]).resolve() != args.project:
            raise ValueError("official CLI connected to the wrong project")
        cycle, discarded = 0, 0
        while cycle < args.warmup + args.iterations:
            values = {name: {} for name in samples}
            valid = True
            order = list(samples) if cycle % 2 == 0 else list(reversed(samples))
            # Each tool runs one balanced cycle: no name/path collisions or accumulating assets.
            for name in order:
                for key, tool, params, other, other_params in rows:
                    bench.ensure_focus(args.focus, pid)
                    before = bench._focus.frontmost_pid()
                    start = time.perf_counter()
                    if name == "unity-cli":
                        if key.endswith("until_ready"):
                            bench.transition(editor.raw, key == "play_until_ready", args.timeout)
                        else:
                            editor.raw(tool, params)
                    elif key.endswith("until_ready"):
                        official_transition(call, key == "play_until_ready", args.timeout)
                    else:
                        call(other, other_params)
                    elapsed = (time.perf_counter() - start) * 1000
                    after = bench._focus.frontmost_pid()
                    allowed_capture = key == "screenshot" and args.focus == "background" and after == pid
                    valid &= bench.focus_matches(args.focus, pid, before) and (after == before or allowed_capture)
                    values[name][key] = elapsed
            if not valid:
                discarded += 1
                if discarded >= 10:
                    raise RuntimeError("Editor focus changed during 10 cycles")
                continue
            if cycle >= args.warmup:
                for name in samples:
                    for key in samples[name]:
                        samples[name][key].append(values[name][key])
            cycle += 1
            print(f"resident {args.focus}: cycle {cycle}/{args.warmup + args.iterations}", file=sys.stderr, flush=True)
        return samples, {"official_shell_pid": shell.process.pid, "discarded_focus_cycles": discarded,
                         "operations": [{"name": key, "unity_cli": {"tool": tool, "params": params},
                                         "official": {"tool": other, "params": other_params}}
                                        for key, tool, params, other, other_params in rows]}


def measure_process(args, env, editor, bench, pid):
    tools = {
        "unity-cli": lambda op, i: (unity_cli_argv(args.unity_cli, op, i), check_unity_cli),
        "official": lambda op, i: (official_argv(args.official, str(args.project), op, i), check_official),
    }
    samples = {name: {op: [] for op in OPERATIONS} for name in tools}
    discarded_pairs = {}
    for op in OPERATIONS:
        i, discarded = 0, 0
        while i < args.warmup + args.iterations:
            order = list(tools) if i % 2 == 0 else list(reversed(tools))
            values, valid = {}, True
            for name in order:
                bench.ensure_focus(args.focus, pid)
                before = bench._focus.frontmost_pid()
                argv, check = tools[name](op, i)
                elapsed_ms, stdout = run_once(argv, env, args.timeout)
                check(op, stdout)
                valid &= bench._focus.frontmost_pid() == before
                values[name] = elapsed_ms
            if op == "create_gameobject":
                # Keep hierarchy size constant; cleanup calls are outside timed samples.
                for prefix in ("BenchUnityCli", "BenchOfficial"):
                    editor.raw("delete_gameobject", {"path": "/" + prefix + str(i)})
            if not valid:
                discarded += 1
                if discarded >= 10:
                    raise RuntimeError("Editor focus changed in 10 sample pairs")
                continue
            if i >= args.warmup:
                for name in tools:
                    samples[name][op].append(values[name])
            i += 1
        print(f"done: {op}", file=sys.stderr, flush=True)
        discarded_pairs[op] = discarded
    return samples, {"discarded_focus_pairs": discarded_pairs}


def verify_coexistence(project, bridge, official):
    if (Path(bridge["projectRoot"]).resolve() != project
            or Path(official["projectPath"]).resolve() != project):
        raise ValueError("both tools must connect to --project-path")
    if bridge["unity"]["unityVersion"] != official["unityVersion"]:
        raise ValueError("tools report different Editor versions")
    if official["status"] != "ready":
        raise ValueError("official Editor is not ready for measurements")
    return {"same_project": True, "unity_version": official["unityVersion"],
            "official_state": official["status"],
            "project_identity_sha256": hashlib.sha256(str(project).encode()).hexdigest()}


def benchmark_environment(project, port):
    env = dict(os.environ)
    # Use this project's token discovery, never an inherited bypass or token.
    env.pop("UNITY_CLI_ALLOW_UNAUTHENTICATED", None)
    env.pop("UNITY_CLI_AUTH_TOKEN_FILE", None)
    env.update({
        "UNITY_CLI_HOST": "127.0.0.1",
        "UNITY_CLI_PORT": str(port),
        "UNITY_PROJECT_ROOT": str(project),
        "UNITY_CLI_TOOLS_ROOT": str(project / ".unity/comparison-tools"),
        "UNITY_CLI_NO_AUTO_UPDATE": "1",
        "UNITY_CLI_UNITYD_IDLE_TIMEOUT": "3600",
        "UNITY_NO_UPDATE_CHECK": "1",
        "UNITY_NO_CLI_INVOKED_TELEMETRY": "1",
        "UNITY_NO_CRASH_REPORT": "1",
        "UNITY_NO_CONSENT_PROMPT": "1",
    })
    return env


def host_snapshot():
    """Record host contention without capturing process arguments or credentials."""
    output = subprocess.run(["ps", "-axo", "pid=,pcpu=,comm="], check=True, capture_output=True,
                            text=True, timeout=10).stdout
    rows = []
    for line in output.splitlines():
        pid, cpu, executable = line.strip().split(None, 2)
        rows.append({"pid": int(pid), "cpu_percent": float(cpu), "executable": Path(executable).name})
    return {"timestamp": datetime.now(timezone.utc).isoformat(), "load_average": list(os.getloadavg()),
            "top_cpu_processes": sorted(rows, key=lambda row: row["cpu_percent"], reverse=True)[:10]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project-path", required=True, help="Unity project with both packages")
    parser.add_argument("--unity-cli", default="target/release/unity-cli")
    parser.add_argument("--official", default=os.path.expanduser("~/.unity/bin/unity"))
    parser.add_argument("--port", type=int, default=6400, help="unity-cli bridge port")
    parser.add_argument("--iterations", type=int, default=100)
    parser.add_argument("--warmup", type=int, default=3)
    parser.add_argument("--timeout", type=float, default=30.0, help="per-call timeout (s)")
    parser.add_argument("--out", help="write JSON here (default: stdout)")
    parser.add_argument("--mode", choices=["process", "resident"], default="process")
    parser.add_argument("--focus", choices=["frontmost", "background"], default="frontmost")
    args = parser.parse_args()
    if args.iterations < 1 or args.warmup < 0 or args.timeout <= 0:
        parser.error("iterations and timeout must be positive; warmup must be nonnegative")

    args.project = Path(args.project_path).resolve()
    project = str(args.project)
    args.unity_cli = str(Path(args.unity_cli).resolve())
    args.official = str(Path(args.official).resolve())
    env = benchmark_environment(args.project, args.port)
    fingerprints = {
        "unity_cli_sha256": hashlib.sha256(Path(args.unity_cli).read_bytes()).hexdigest(),
        "official_cli_sha256": hashlib.sha256(Path(args.official).read_bytes()).hexdigest(),
        "benchmark_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "source_commit": subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True,
                                        check=True, cwd=Path(__file__).resolve().parents[1]).stdout.strip(),
        "host_at_start": host_snapshot(),
    }
    bench = load_editor_bench()
    editor = bench.Editor(args, env)
    started_at = datetime.now(timezone.utc).isoformat()
    try:
        info = editor.setup()
        pid = bench._focus.listener_pid(args.port)
        _, official_status = run_once([args.official, "command", "editor_status", "--project-path", project,
                                       "--format", "json"], env, args.timeout)
        check_official("editor_status", official_status)
        coexistence = verify_coexistence(args.project, info, json.loads(official_status)["data"]["result"])
        state = editor.raw("get_project_settings", {"includeEditor": True})["editor"]
        daemon_before = editor.command(["unityd", "status"])
        if not daemon_before.get("running") or daemon_before.get("connections", 0) < 1:
            raise RuntimeError("warm unityd connection not established")
        measure = measure_process if args.mode == "process" else measure_resident
        samples, details = measure(args, env, editor, bench, pid)
        if editor.command(["unityd", "status"]).get("pid") != daemon_before["pid"]:
            raise RuntimeError("unityd restarted during measurement")
        host_at_end = host_snapshot()
    finally:
        editor.restore_play_focus()
    results = {name: {op: summarize(values) for op, values in by_op.items()}
               for name, by_op in samples.items()}

    report = {
        "status": "PASS",
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "started_at": started_at,
        "conditions": {
            "os": f"{platform.system()} {platform.mac_ver()[0] or platform.release()}",
            "arch": platform.machine(),
            "machine": tool_version(["sysctl", "-n", "machdep.cpu.brand_string"], env),
            "memory_bytes": tool_version(["sysctl", "-n", "hw.memsize"], env),
            "project": os.path.basename(project),
            **project_facts(project),
            "iterations": args.iterations,
            "warmup": args.warmup,
            "mode": args.mode,
            "focus": args.focus,
            "sample": ("CLI spawn-to-exit; warm unityd" if args.mode == "process" else
                       "unity-cli spawn through warm unityd; official warm NDJSON round trip; transitions include state polling"),
            "route_exceptions": {"unity-cli eval": "direct TCP (no automatic replay)",
                                 "unity-cli read_csharp": "local file read (no Editor connection)"},
            "environment_overrides": {key: value for key, value in env.items()
                                      if key in ("UNITY_CLI_NO_AUTO_UPDATE", "UNITY_NO_UPDATE_CHECK",
                                                 "UNITY_NO_CLI_INVOKED_TELEMETRY", "UNITY_NO_CRASH_REPORT",
                                                 "UNITY_NO_CONSENT_PROMPT")},
            "order": "alternating tool order per sample pair (process) or balanced 23-operation cycle (resident)",
            "enterPlayModeOptionsEnabled": state["enterPlayModeOptionsEnabled"],
            "enterPlayModeOptions": state["enterPlayModeOptions"],
            "gameViewEnterPlayModeBehavior": "PlayUnfocused",
            "backgroundScreenshot": "starts background; may activate target Editor via GameView.Focus",
            "editor_pid": pid,
            "unityd_pid": daemon_before["pid"],
            "bridge_version": json.loads((args.project / "Packages/unity-cli-bridge/package.json").read_text())["version"],
            "host_at_end": host_at_end,
            **fingerprints,
            "unity_cli_version": tool_version([args.unity_cli, "--version"], env),
            "official_cli_version": tool_version([args.official, "--version"], env),
            "unity_cli_port": args.port,
        },
        "results": results,
        "samples_ms": samples,
        "coexistence": coexistence,
        **details,
    }
    text = json.dumps(report, indent=2)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
