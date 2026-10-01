"""Shared, fail-closed latency budget and append-only history checks."""
import json
import math
from pathlib import Path
import statistics


def summarize(samples):
    if not samples or any(not math.isfinite(v) or v < 0 for v in samples):
        raise ValueError("expected finite non-negative latency samples")
    ordered = sorted(samples)
    return {"n": len(ordered), "p50_ms": ordered[math.ceil(len(ordered) * .50) - 1],
            "p95_ms": ordered[math.ceil(len(ordered) * .95) - 1],
            "min_ms": min(ordered), "max_ms": max(ordered)}


def check(results, budgets):
    failures = []
    for name, result in results.items():
        for metric in ("p50_ms", "p95_ms"):
            limit = budgets.get(name, {}).get(metric)
            if not isinstance(limit, (int, float)) or not math.isfinite(limit) or limit <= 0:
                failures.append(f"{name}: missing/invalid {metric} budget")
            elif result[metric] > limit:
                failures.append(f"{name}: {metric} {result[metric]:.3f} ms > {limit} ms")
    if not results:
        failures.append("no measured operations")
    return failures


def regressions(results, conditions, history, percent):
    if not math.isfinite(percent) or percent < 0:
        raise ValueError("regression percent must be finite and non-negative")
    rows = [row for row in history if row.get("measurements_complete", True)
            and row.get("conditions") == conditions
            and all(name in row.get("results", {}) for name in results)][-5:]
    failures = []
    if len(rows) < 5:
        return failures
    for name, result in results.items():
        if not all(name in row["results"] for row in rows):
            continue
        baseline = statistics.median(row["results"][name]["p50_ms"] for row in rows)
        if result["p50_ms"] > baseline * (1 + percent / 100):
            failures.append(f"{name}: p50 {result['p50_ms']:.3f} ms > last-five median "
                            f"{baseline:.3f} ms + {percent}%")
    return failures


def read_history(path):
    path = Path(path)
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()] if path.exists() else []


def append_history(path, report):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a") as stream:
        stream.write(json.dumps(report, separators=(",", ":")) + "\n")
