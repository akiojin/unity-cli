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


def two_phase_operations(declarations, conditions):
    """Operations declared two-phase (perf-budgets.json `two_phase_p50`) for exactly these conditions."""
    operations = set()
    for entry in declarations:
        declared, names = entry.get("conditions"), entry.get("operations")
        if not declared or not names:
            raise ValueError("two-phase declaration needs conditions and operations")
        if all(key in conditions and conditions[key] == value for key, value in declared.items()):
            operations.update(names)
    return operations


def _baseline(values, current, percent):
    """Median of the latest five p50s; of the current value's own phase when the history has two."""
    ordered = sorted(values)
    plain = statistics.median(values[-5:]), ""
    if ordered[0] <= 0:
        return plain
    low, high = max(zip(ordered, ordered[1:]), key=lambda edge: edge[1] / edge[0])
    # Phases closer than the threshold itself are one phase: the plain rule already tolerates that gap.
    if high <= low * (1 + percent / 100):
        return plain
    boundary = math.sqrt(low * high)
    phase = [value for value in values if (value > boundary) == (current > boundary)]
    return statistics.median(phase[-5:]), "high-phase " if current > boundary else "low-phase "


def regressions(results, conditions, history, percent, two_phase=()):
    if not math.isfinite(percent) or percent < 0:
        raise ValueError("regression percent must be finite and non-negative")
    matching = [row for row in history if row.get("measurements_complete", True)
                and row.get("conditions") == conditions
                and all(name in row.get("results", {}) for name in results)]
    failures = []
    if len(matching) < 5:
        return failures
    for name, result in results.items():
        values = [row["results"][name]["p50_ms"] for row in matching]
        # Background update scheduling moves declared operations between a fast and a slow phase.
        baseline, phase = _baseline(values, result["p50_ms"], percent) if name in two_phase \
            else (statistics.median(values[-5:]), "")
        if result["p50_ms"] > baseline * (1 + percent / 100):
            failures.append(f"{name}: p50 {result['p50_ms']:.3f} ms > last-five {phase}median "
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
