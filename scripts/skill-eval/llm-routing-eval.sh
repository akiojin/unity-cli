#!/usr/bin/env bash
# Evaluate skill routing quality from benchmark + predictions.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BENCHMARK_PATH="${REPO_ROOT}/specs/perf/skill-routing-benchmark.jsonl"
HISTORY_PATH="${REPO_ROOT}/specs/perf/skill-routing-history.jsonl"
SUMMARY_PATH="${REPO_ROOT}/specs/perf/skill-routing-summary.json"
PREDICTIONS_PATH=""
RUNNER_CMD=""
MODEL="unknown"
JSON_OUTPUT=0

usage() {
  cat <<USAGE
Usage: scripts/skill-eval/llm-routing-eval.sh [options]

Options:
  --benchmark <path>      Benchmark JSONL path
  --predictions <path>    Predictions JSONL path
  --runner-cmd <cmd>      Command to generate predictions from prompt (reads prompt from stdin, prints JSON)
  --model <name>          Model label written to history
  --history <path>        History JSONL path
  --summary <path>        Summary JSON path
  --json                  Print summary JSON to stdout

Prediction format (JSON per line):
  {
    "id": "SR-001",
    "predicted_skills": ["unity-scene-create", "unity-scene-inspect"],
    "predicted_tool": "create_scene",
    "predicted_payload_keys": ["sceneName", "path"]
  }
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --benchmark)
      BENCHMARK_PATH="$2"
      shift 2
      ;;
    --predictions)
      PREDICTIONS_PATH="$2"
      shift 2
      ;;
    --runner-cmd)
      RUNNER_CMD="$2"
      shift 2
      ;;
    --model)
      MODEL="$2"
      shift 2
      ;;
    --history)
      HISTORY_PATH="$2"
      shift 2
      ;;
    --summary)
      SUMMARY_PATH="$2"
      shift 2
      ;;
    --json)
      JSON_OUTPUT=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ ! -f "${BENCHMARK_PATH}" ]]; then
  echo "ERROR: benchmark file not found: ${BENCHMARK_PATH}" >&2
  exit 1
fi

if [[ -z "${PREDICTIONS_PATH}" && -z "${RUNNER_CMD}" ]]; then
  echo "ERROR: either --predictions or --runner-cmd is required" >&2
  exit 1
fi

python3 - "${BENCHMARK_PATH}" "${PREDICTIONS_PATH}" "${RUNNER_CMD}" "${MODEL}" "${HISTORY_PATH}" "${SUMMARY_PATH}" "${JSON_OUTPUT}" <<PY
import datetime as dt
import json
import pathlib
import subprocess
import sys

benchmark_path = pathlib.Path(sys.argv[1])
predictions_path = pathlib.Path(sys.argv[2]) if sys.argv[2] else None
runner_cmd = sys.argv[3]
model = sys.argv[4]
history_path = pathlib.Path(sys.argv[5])
summary_path = pathlib.Path(sys.argv[6])
json_output = sys.argv[7] == "1"


def load_jsonl(path: pathlib.Path):
    rows = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            rows.append(json.loads(line))
    return rows

bench = load_jsonl(benchmark_path)
if not bench:
    raise SystemExit("Benchmark is empty")

predictions = {}
if predictions_path:
    if not predictions_path.exists():
        raise SystemExit(f"Predictions file not found: {predictions_path}")
    for row in load_jsonl(predictions_path):
        predictions[row["id"]] = row

if runner_cmd:
    for row in bench:
        run = subprocess.run(
            runner_cmd,
            input=row["prompt"],
            text=True,
            shell=True,
            capture_output=True,
            check=False,
        )
        if run.returncode != 0:
            predictions[row["id"]] = {
                "id": row["id"],
                "predicted_skills": [],
                "predicted_tool": "",
                "predicted_payload_keys": [],
                "error": run.stderr.strip() or f"runner exit {run.returncode}",
            }
            continue
        out = run.stdout.strip()
        try:
            parsed = json.loads(out)
        except Exception as exc:
            predictions[row["id"]] = {
                "id": row["id"],
                "predicted_skills": [],
                "predicted_tool": "",
                "predicted_payload_keys": [],
                "error": f"invalid runner JSON: {exc}",
            }
            continue

        if isinstance(parsed, dict):
            parsed.setdefault("id", row["id"])
            parsed.setdefault("predicted_skills", [])
            parsed.setdefault("predicted_tool", "")
            parsed.setdefault("predicted_payload_keys", [])
            predictions[row["id"]] = parsed
        else:
            predictions[row["id"]] = {
                "id": row["id"],
                "predicted_skills": [],
                "predicted_tool": "",
                "predicted_payload_keys": [],
                "error": "runner output must be JSON object",
            }

score_rows = []
missing_ids = []

for row in bench:
    rid = row["id"]
    pred = predictions.get(rid)
    if pred is None:
        missing_ids.append(rid)
        pred = {
            "predicted_skills": [],
            "predicted_tool": "",
            "predicted_payload_keys": [],
            "error": "missing prediction",
        }

    expected_skills = row.get("expected_skills", [])
    expected_tool = row.get("expected_tool", "")
    expected_payload_keys = set(row.get("expected_payload_keys", []))

    predicted_skills = pred.get("predicted_skills") or []
    predicted_tool = pred.get("predicted_tool") or ""
    predicted_payload_keys = set(pred.get("predicted_payload_keys") or [])

    top1 = bool(predicted_skills and predicted_skills[0] in expected_skills)
    top2 = bool(set(predicted_skills[:2]).intersection(expected_skills))
    tool_correct = predicted_tool == expected_tool
    payload_valid = expected_payload_keys.issubset(predicted_payload_keys)

    score_rows.append({
        "id": rid,
        "difficulty": row.get("difficulty", "unknown"),
        "expected_skills": expected_skills,
        "expected_tool": expected_tool,
        "expected_payload_keys": sorted(expected_payload_keys),
        "predicted_skills": predicted_skills,
        "predicted_tool": predicted_tool,
        "predicted_payload_keys": sorted(predicted_payload_keys),
        "top1": top1,
        "top2": top2,
        "tool_correct": tool_correct,
        "payload_valid": payload_valid,
        "error": pred.get("error"),
    })

n = len(score_rows)
metric_top1 = sum(1 for r in score_rows if r["top1"]) / n
metric_top2 = sum(1 for r in score_rows if r["top2"]) / n
metric_tool = sum(1 for r in score_rows if r["tool_correct"]) / n
metric_payload = sum(1 for r in score_rows if r["payload_valid"]) / n

thresholds = {
    "top1": 0.90,
    "top2": 0.98,
    "tool_correct": 0.92,
    "payload_valid": 0.95,
}

summary = {
    "timestamp": dt.datetime.now(dt.timezone.utc).isoformat(),
    "model": model,
    "benchmark": str(benchmark_path),
    "total": n,
    "missing_predictions": len(missing_ids),
    "metrics": {
        "top1": round(metric_top1, 4),
        "top2": round(metric_top2, 4),
        "tool_correct": round(metric_tool, 4),
        "payload_valid": round(metric_payload, 4),
    },
    "thresholds": thresholds,
    "passed": {
        "top1": metric_top1 >= thresholds["top1"],
        "top2": metric_top2 >= thresholds["top2"],
        "tool_correct": metric_tool >= thresholds["tool_correct"],
        "payload_valid": metric_payload >= thresholds["payload_valid"],
    },
    "all_passed": (
        metric_top1 >= thresholds["top1"]
        and metric_top2 >= thresholds["top2"]
        and metric_tool >= thresholds["tool_correct"]
        and metric_payload >= thresholds["payload_valid"]
    ),
    "results": score_rows,
}

summary_path.parent.mkdir(parents=True, exist_ok=True)
summary_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

history_path.parent.mkdir(parents=True, exist_ok=True)
with history_path.open("a", encoding="utf-8") as f:
    compact = {
        "timestamp": summary["timestamp"],
        "model": model,
        "total": n,
        "metrics": summary["metrics"],
        "thresholds": thresholds,
        "all_passed": summary["all_passed"],
    }
    f.write(json.dumps(compact, ensure_ascii=False) + "\n")

if json_output:
    print(json.dumps(summary, ensure_ascii=False, indent=2))
else:
    print("[LLM ROUTING EVAL]")
    print(f"  model: {model}")
    print(f"  total: {n}")
    print(f"  top1: {summary['metrics']['top1']:.4f} (>= {thresholds['top1']})")
    print(f"  top2: {summary['metrics']['top2']:.4f} (>= {thresholds['top2']})")
    print(f"  tool_correct: {summary['metrics']['tool_correct']:.4f} (>= {thresholds['tool_correct']})")
    print(f"  payload_valid: {summary['metrics']['payload_valid']:.4f} (>= {thresholds['payload_valid']})")
    print(f"  summary: {summary_path}")
    print(f"  history: {history_path}")

sys.exit(0 if summary["all_passed"] else 1)
PY
