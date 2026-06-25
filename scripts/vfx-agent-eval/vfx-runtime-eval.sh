#!/usr/bin/env bash
# Runtime (live-component) tier of the agent eval for the unity-vfx-graph skill.
#
# Where the authoring eval (vfx-agent-eval.sh) grades a built .vfx graph statically via
# vfx_describe_graph, this tier grades BEHAVIOR on a live UnityEngine.VFX.VisualEffect: for each task
# the runner builds a rig (GameObject + VisualEffect + set_asset), lets a clean-room agent drive the
# public-API ops (vfx_runtime set_float/set_texture/set_initial_event_name/...), then reads the live
# component back with vfx_runtime get_state and grades the result. Requires a live Unity bridge with a
# saved scene to load (SampleScene by default).
#
# Scope: these tasks are deterministic in the editor (parameter round-trips + initialEventName — the
# property sheet updates without entering Play mode). SPAWN behavior (aliveParticleCount > 0) needs
# Play mode + a Camera + per-frame Simulate-with-yields (an agent driving `raw` can't yield frames),
# so it is proven C#-side by the `simulate`-op PlayMode test (Runtime_SimulateOp_AdvancesSpawnUntilBurstAlive),
# not here. See scripts/vfx-agent-eval/README.md.
#
# Modes: --agent-cmd "<cmd>" runs the agent per task (env: VFX_EVAL_PROMPT, VFX_EVAL_RIG, VFX_EVAL_ASSET,
# VFX_EVAL_PARAM, VFX_EVAL_PORT). Omit it for probe-only (grade whatever state the rig already holds).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BENCHMARK_PATH="${REPO_ROOT}/tests/fixtures/vfx-agent-eval/runtime-benchmark.jsonl"
HISTORY_PATH="${REPO_ROOT}/.unity/vfx-agent-eval/runtime-history.jsonl"
SUMMARY_PATH="${REPO_ROOT}/.unity/vfx-agent-eval/runtime-summary.json"
UNITY_CLI="unity-cli"
PORT="6400"
SCENE="Assets/Scenes/SampleScene.unity"
RIG="VfxRtEvalRig"
AGENT_CMD=""
MODEL="manual"
JSON_OUTPUT=0

usage() {
  cat <<USAGE
Usage: scripts/vfx-agent-eval/vfx-runtime-eval.sh [options]

  --benchmark <path>   Runtime benchmark JSONL (default: tests/fixtures/vfx-agent-eval/runtime-benchmark.jsonl)
  --unity-cli <cmd>    unity-cli invocation (default: unity-cli)
  --port <n>           Bridge port (default: 6400)
  --scene <path>       Saved scene to load per task (default: Assets/Scenes/SampleScene.unity)
  --rig <name>         Rig GameObject name (default: VfxRtEvalRig)
  --agent-cmd <cmd>    Command that drives the live component from the prompt. Omit for probe-only.
  --model <name>       Label written to history (default: manual)
  --history <path> / --summary <path> / --json
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --benchmark) BENCHMARK_PATH="$2"; shift 2 ;;
    --unity-cli) UNITY_CLI="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --scene) SCENE="$2"; shift 2 ;;
    --rig) RIG="$2"; shift 2 ;;
    --agent-cmd) AGENT_CMD="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --history) HISTORY_PATH="$2"; shift 2 ;;
    --summary) SUMMARY_PATH="$2"; shift 2 ;;
    --json) JSON_OUTPUT=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -f "${BENCHMARK_PATH}" ]] || { echo "ERROR: benchmark not found: ${BENCHMARK_PATH}" >&2; exit 1; }

cli() { ${UNITY_CLI} raw "$1" --json "$2" --port "${PORT}" --output json 2>/dev/null; }

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

# Preflight: parse the benchmark (id \x1f fixture \x1f grader \x1f param \x1f prompt).
TASKS_FILE="${WORK_DIR}/tasks.usv"
python3 - "${BENCHMARK_PATH}" > "${TASKS_FILE}" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        r = json.loads(line)
        param = (r.get("args") or {}).get("param", "")
        print("\x1f".join([r["id"], r["fixture"], r["grader"], param, r["prompt"]]))
PY

while IFS=$'\x1f' read -r id fixture grader param prompt; do
  [[ -z "${id}" ]] && continue

  # Fresh rig each task: reload the saved scene (discards the previous rig), then build + bind.
  cli load_scene "{\"scenePath\":\"${SCENE}\"}" >/dev/null || true
  cli create_gameobject "{\"name\":\"${RIG}\"}" >/dev/null || true
  cli add_component "{\"gameObjectPath\":\"/${RIG}\",\"componentType\":\"UnityEngine.VFX.VisualEffect\"}" >/dev/null || true
  cli vfx_runtime "{\"op\":\"set_asset\",\"gameObject\":\"${RIG}\",\"assetPath\":\"${fixture}\"}" >/dev/null || true

  if [[ -n "${AGENT_CMD}" ]]; then
    VFX_EVAL_PROMPT="${prompt}" \
    VFX_EVAL_RIG="${RIG}" \
    VFX_EVAL_ASSET="${fixture}" \
    VFX_EVAL_PARAM="${param}" \
    VFX_EVAL_PORT="${PORT}" \
    bash -c "${AGENT_CMD}" || echo "WARN: agent-cmd failed for ${id}" >&2
  fi

  # Read the live component back. Pass the param name so get_state surfaces hasFloat/hasTexture for it.
  if [[ -n "${param}" ]]; then
    cli vfx_runtime "{\"op\":\"get_state\",\"gameObject\":\"${RIG}\",\"name\":\"${param}\"}" > "${WORK_DIR}/${id}.json" || echo '{}' > "${WORK_DIR}/${id}.json"
  else
    cli vfx_runtime "{\"op\":\"get_state\",\"gameObject\":\"${RIG}\"}" > "${WORK_DIR}/${id}.json" || echo '{}' > "${WORK_DIR}/${id}.json"
  fi
done < "${TASKS_FILE}"

python3 - "${BENCHMARK_PATH}" "${WORK_DIR}" "${MODEL}" "${HISTORY_PATH}" "${SUMMARY_PATH}" "${JSON_OUTPUT}" <<'PY'
import datetime as dt
import json
import pathlib
import sys

benchmark_path = pathlib.Path(sys.argv[1])
work_dir = pathlib.Path(sys.argv[2])
model = sys.argv[3]
history_path = pathlib.Path(sys.argv[4])
summary_path = pathlib.Path(sys.argv[5])
json_output = sys.argv[6] == "1"


def load_jsonl(path):
    rows = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


# ---- runtime graders: read a vfx_runtime get_state result. (state, args) -> (passed, detail) ----
def runtime_float_set(state, args):
    want = (args or {}).get("value")
    has = bool(state.get("hasFloat"))
    val = state.get("floatValue")
    ok = has and val is not None and abs(float(val) - float(want)) < 1e-3
    return ok, f"hasFloat={has}, floatValue={val} (want {want})"


def runtime_texture_set(state, args):
    has = bool(state.get("hasTexture"))
    return has, f"hasTexture={has}, textureName={state.get('textureName')}"


def runtime_initial_event(state, args):
    want = (args or {}).get("value")
    got = state.get("initialEventName")
    return got == want, f"initialEventName={got} (want {want})"


GRADERS = {
    "runtime_float_set": runtime_float_set,
    "runtime_texture_set": runtime_texture_set,
    "runtime_initial_event": runtime_initial_event,
}

bench = load_jsonl(benchmark_path)
if not bench:
    raise SystemExit("Benchmark is empty")

results = []
for row in bench:
    rid = row["id"]
    grader = GRADERS.get(row["grader"])
    result_file = work_dir / f"{rid}.json"
    passed, detail = False, ""
    if grader is None:
        detail = f"unknown grader '{row['grader']}'"
    elif not result_file.exists():
        detail = "no state captured (rig/agent failed)"
    else:
        try:
            data = json.loads(result_file.read_text(encoding="utf-8"))
            if data.get("error"):
                detail = f"tool error: {data['error']}"
            else:
                passed, detail = grader(data, row.get("args"))
        except Exception as exc:  # noqa: BLE001
            detail = f"grader exception: {exc}"
    results.append({
        "id": rid,
        "difficulty": row.get("difficulty", "unknown"),
        "grader": row["grader"],
        "oracle": row.get("oracle", ""),
        "passed": passed,
        "detail": detail,
    })

n = len(results)
passed_n = sum(1 for r in results if r["passed"])
summary = {
    "timestamp": dt.datetime.now(dt.timezone.utc).isoformat(),
    "model": model,
    "tier": "runtime",
    "benchmark": str(benchmark_path),
    "total": n,
    "passed": passed_n,
    "pass_rate": round(passed_n / n, 4) if n else 0.0,
    "results": results,
}

summary_path.parent.mkdir(parents=True, exist_ok=True)
summary_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
history_path.parent.mkdir(parents=True, exist_ok=True)
with history_path.open("a", encoding="utf-8") as f:
    f.write(json.dumps({
        "timestamp": summary["timestamp"], "model": model, "tier": "runtime",
        "total": n, "passed": passed_n, "pass_rate": summary["pass_rate"],
    }, ensure_ascii=False) + "\n")

if json_output:
    print(json.dumps(summary, ensure_ascii=False, indent=2))
else:
    print("[VFX RUNTIME EVAL]")
    print(f"  model: {model}")
    print(f"  passed: {passed_n}/{n}  (pass_rate {summary['pass_rate']:.4f})")
    for r in results:
        print(f"  [{'PASS' if r['passed'] else 'FAIL'}] {r['id']} ({r['difficulty']}) — {r['detail']}")
    print(f"  summary: {summary_path}")

# Discovery tool, not a gate.
sys.exit(0)
PY
