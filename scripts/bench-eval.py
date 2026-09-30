#!/usr/bin/env python3
"""Latency benchmark and budget gate for `unity-cli editor eval` (Issue #391).

Each sample is the wall-clock time of one CLI process invocation (process
start -> exit), which is what an agent pays per tool call. This is the same
sampling method as the official-CLI comparison benchmark: warm-up runs first,
then N measured runs, nearest-rank p50/p95, and any failed call aborts the run.

The first call after an Editor domain (re)load is reported separately as
`first_call_ms`; it is not part of the warm samples.

Usage (Editor open, Bridge listening on the port):
  cargo build --release
  python3 scripts/bench-eval.py --port 6400 --iterations 100 --out eval.json

  # Gate: Editor frontmost (macOS), budget from perf-budgets.json
  python3 scripts/bench-eval.py --port 6400 --require-frontmost --activate --budget editor_eval

With `--budget KEY` the run exits 1 when p50/p95 exceed the entry KEY of
perf-budgets.json (see `--budgets`). Budgets are defined for the Editor as the
frontmost app; a background Editor is throttled by the OS.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import statistics
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from perf_gate import append_history, read_history, regressions

ROOT = Path(__file__).resolve().parent.parent


def history_conditions(conditions: dict) -> dict:
    """Compare builds on the same host without partitioning by transient ports/version."""
    keys = ("os", "arch", "unity_editor_version", "sample", "command", "editor_frontmost")
    return {"suite": "editor_eval", "host": platform.node(),
            **{key: conditions[key] for key in keys}}


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


def over_budget(summary: dict, budget: dict) -> list[str]:
    """Human-readable budget violations; empty when the run is within budget."""
    return [
        f"{key} {summary[key]} ms > budget {budget[key]} ms"
        for key in ("p50_ms", "p95_ms")
        if key in budget and summary[key] > budget[key]
    ]


def frontmost_pid() -> int | None:
    """PID of the macOS frontmost app; None where it cannot be determined."""
    if platform.system() != "Darwin":
        return None
    try:
        front = subprocess.run(["lsappinfo", "front"], capture_output=True, text=True, timeout=5).stdout.strip()
        pid = subprocess.run(["lsappinfo", "info", "-only", "pid", front], capture_output=True, text=True, timeout=5).stdout
        return int(pid.split("=", 1)[1]) if "=" in pid else None
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def listener_pid(port: int) -> int:
    """PID of the process listening on the Bridge port (the Editor under test)."""
    out = subprocess.run(["lsof", "-nP", f"-iTCP:{port}", "-sTCP:LISTEN", "-t"], capture_output=True, text=True, timeout=10).stdout.split()
    if len(set(out)) != 1:
        raise RuntimeError(f"expected exactly one listener on port {port}, found {out}")
    return int(out[0])


def activate(pid: int) -> None:
    script = f'tell application "System Events" to set frontmost of (first process whose unix id is {pid}) to true'
    subprocess.run(["osascript", "-e", script], check=True, capture_output=True, timeout=30)
    time.sleep(0.5)


def run_once(argv: list[str], env: dict, timeout_s: float) -> tuple[float, str]:
    start = time.perf_counter()
    proc = subprocess.run(argv, env=env, capture_output=True, text=True, timeout=timeout_s)
    elapsed_ms = (time.perf_counter() - start) * 1000.0
    if proc.returncode != 0:
        raise RuntimeError(f"exit {proc.returncode}: {' '.join(argv[:4])}\n{proc.stdout[:300]}\n{proc.stderr[:300]}")
    return elapsed_ms, proc.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--unity-cli", default=str(ROOT / "target/release/unity-cli"))
    parser.add_argument("--host", default=os.getenv("UNITY_CLI_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.getenv("UNITY_CLI_PORT", "6400")))
    parser.add_argument("--code", default="1+2", help="expression to evaluate")
    parser.add_argument("--expect", default="3", help="expected JSON value of the expression")
    parser.add_argument("--iterations", type=int, default=100)
    parser.add_argument("--warmup", type=int, default=3)
    parser.add_argument("--timeout", type=float, default=60.0, help="per-call timeout (s)")
    parser.add_argument("--budgets", default=str(ROOT / "perf-budgets.json"))
    parser.add_argument("--budget", help="budget entry to enforce, e.g. editor_eval")
    parser.add_argument("--require-frontmost", action="store_true",
                        help="macOS: fail unless the Editor listening on --port is the frontmost app at every sample")
    parser.add_argument("--activate", action="store_true",
                        help="with --require-frontmost: bring the Editor back to the front when another app took "
                             "focus, and re-measure samples during which focus changed")
    parser.add_argument("--out", help="write JSON here (default: stdout only)")
    parser.add_argument("--history", type=Path, help="append and check the shared Editor perf JSONL history")
    args = parser.parse_args()
    if args.activate and not args.require_frontmost:
        parser.error("--activate requires --require-frontmost")

    budget = None
    if args.budget:
        with open(args.budgets, encoding="utf-8") as fh:
            budget = json.load(fh)["budgets"][args.budget]

    env = dict(os.environ, UNITY_CLI_NO_AUTO_UPDATE="1")
    base = [args.unity_cli, "--host", args.host, "--port", str(args.port), "--output", "json"]
    argv = base + ["editor", "eval", args.code]
    expected = json.loads(args.expect)

    editor_pid = listener_pid(args.port) if args.require_frontmost else None
    frontmost = {"editor": 0, "other": 0}
    samples: list[float] = []
    first_call_ms = None
    discarded = 0
    calls = 0
    while len(samples) < args.iterations:
        if args.activate and frontmost_pid() != editor_pid:
            # Another app took focus; restore the condition outside the timed interval.
            activate(editor_pid)
        before = frontmost_pid()
        elapsed_ms, stdout = run_once(argv, env, args.timeout)
        after = frontmost_pid()
        data = json.loads(stdout)
        if data.get("state") != "completed" or data.get("value") != expected:
            raise ValueError(f"unexpected eval result at call {calls}: {stdout[:300]}")
        calls += 1
        if calls == 1:
            first_call_ms = round(elapsed_ms, 2)
        if calls <= args.warmup:
            continue
        if args.activate and (before, after) != (editor_pid, editor_pid):
            # Focus changed during the call: the sample did not meet the condition. Measure again.
            discarded += 1
            if discarded > args.iterations:
                raise RuntimeError(f"cannot keep the Editor frontmost: {discarded} samples discarded")
            continue
        samples.append(elapsed_ms)
        if editor_pid is not None:
            frontmost["editor" if (before, after) == (editor_pid, editor_pid) else "other"] += 1

    info = json.loads(run_once(base + ["raw", "get_editor_info", "--json", "{}"], env, args.timeout)[1])
    version = subprocess.run([args.unity_cli, "--version"], capture_output=True, text=True, timeout=30).stdout.strip()
    summary = summarize(samples)
    violations = over_budget(summary, budget) if budget else []
    if args.require_frontmost and frontmost["other"]:
        violations.append(f"the Editor was not the frontmost app for every sample: {frontmost}")
    report = {
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "conditions": {
            "os": f"{platform.system()} {platform.mac_ver()[0] or platform.release()}",
            "arch": platform.machine(),
            "unity_editor_version": (info.get("unity") or {}).get("unityVersion"),
            "unity_cli_version": version,
            "command": "unity-cli editor eval " + args.code,
            "iterations": args.iterations,
            "warmup": args.warmup,
            "sample": "wall-clock per CLI process invocation (spawn to exit), ms",
            "port": args.port,
            "editor_frontmost": "required" if args.require_frontmost else "not checked",
            "frontmost_app_per_sample": frontmost if args.require_frontmost else None,
            "discarded_focus_lost_samples": discarded,
        },
        "first_call_ms": first_call_ms,
        "eval": summary,
        "budget": budget,
        "status": "FAIL" if violations else "PASS",
        "violations": violations,
    }
    if args.history:
        conditions = history_conditions(report["conditions"])
        results = {"editor_eval": summary}
        violations += regressions(results, conditions, read_history(args.history),
                                  float(os.environ.get("UNITY_CLI_PERF_REGRESSION_PERCENT", "20")))
        report["status"] = "FAIL" if violations else "PASS"
        append_history(args.history, {"timestamp": report["timestamp"], "conditions": conditions,
                                      "measurements_complete": True,
                                      "results": results, "status": report["status"], "violations": violations})
    text = json.dumps(report, indent=2)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
    print(text)
    return 1 if violations else 0


if __name__ == "__main__":
    sys.exit(main())
