#!/usr/bin/env bash
# Agent-mode (outcome) eval for the unity-vfx-graph skill.
#
# Unlike the routing eval (scripts/skill-eval/), this grades whether an agent can actually BUILD
# the right graph from a natural-language prompt, using ONLY the unity-vfx-graph skill. For each
# task it: copies the pristine fixture to a throwaway probe, lets a clean-room agent drive the
# graph (the pluggable --agent-cmd), then reads the result back with `vfx_describe_graph` and
# grades it against structural assertions (the "oracle"). It is an on-demand DISCOVERY tool
# (surfacing skill/docs ergonomics gaps), NOT a hard CI gate — agent non-determinism makes it a
# thermometer, not a tripwire. Requires a live Unity bridge (the UnityCliBridge project open).
#
# Two modes:
#   --agent-cmd "<cmd>"  Full loop: set up probe -> run the agent on the prompt -> describe -> grade.
#                        The command is invoked once per task with these env vars:
#                          VFX_EVAL_PROMPT       the natural-language task
#                          VFX_EVAL_TARGET       the asset the agent should build/produce (probe .vfx,
#                                                or, for the SDF task, the output .asset path to create)
#                          VFX_EVAL_SOURCE       the source fixture (== TARGET for graph tasks; the mesh
#                                                for the SDF task)
#                          VFX_EVAL_PROJECT_DIR  the Unity project dir (paths above are project-relative)
#                          VFX_EVAL_PORT         the bridge port
#   (omit --agent-cmd)   Probe-only: skip the agent step and grade probes that ALREADY exist at the
#                        target paths (the manual clean-room-subagent flow — build probes by hand,
#                        then score). Used to validate the grader/oracle wiring independently.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BENCHMARK_PATH="${REPO_ROOT}/tests/fixtures/vfx-agent-eval/benchmark.jsonl"
HISTORY_PATH="${REPO_ROOT}/.unity/vfx-agent-eval/history.jsonl"
SUMMARY_PATH="${REPO_ROOT}/.unity/vfx-agent-eval/summary.json"
PROJECT_DIR="${REPO_ROOT}/UnityCliBridge"
PROBE_DIR="Assets/_VfxProbe"
UNITY_CLI="unity-cli"
PORT="6400"
AGENT_CMD=""
MODEL="manual"
KEEP_PROBES=0
JSON_OUTPUT=0

usage() {
  cat <<USAGE
Usage: scripts/vfx-agent-eval/vfx-agent-eval.sh [options]

Options:
  --benchmark <path>     Benchmark JSONL (default: tests/fixtures/vfx-agent-eval/benchmark.jsonl)
  --project-dir <path>   Unity project dir holding Assets/ (default: <repo>/UnityCliBridge)
  --probe-dir <rel>      Probe folder under the project (default: Assets/_VfxProbe)
  --unity-cli <cmd>      unity-cli invocation (default: unity-cli)
  --port <n>             Bridge port (default: 6400)
  --agent-cmd <cmd>      Command that drives the graph from the prompt (see header). Omit for probe-only.
  --model <name>         Label written to history (default: manual)
  --history <path>       History JSONL path
  --summary <path>       Summary JSON path
  --keep-probes          Do not delete the probe folder afterwards
  --json                 Print summary JSON to stdout
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --benchmark) BENCHMARK_PATH="$2"; shift 2 ;;
    --project-dir) PROJECT_DIR="$2"; shift 2 ;;
    --probe-dir) PROBE_DIR="$2"; shift 2 ;;
    --unity-cli) UNITY_CLI="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --agent-cmd) AGENT_CMD="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --history) HISTORY_PATH="$2"; shift 2 ;;
    --summary) SUMMARY_PATH="$2"; shift 2 ;;
    --keep-probes) KEEP_PROBES=1; shift ;;
    --json) JSON_OUTPUT=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -f "${BENCHMARK_PATH}" ]] || { echo "ERROR: benchmark not found: ${BENCHMARK_PATH}" >&2; exit 1; }
[[ -d "${PROJECT_DIR}" ]] || { echo "ERROR: project dir not found: ${PROJECT_DIR}" >&2; exit 1; }

cli() { ${UNITY_CLI} raw "$1" --json "$2" --port "${PORT}" --output json 2>/dev/null; }

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

mkdir -p "${PROJECT_DIR}/${PROBE_DIR}"

# Preflight: parse the benchmark into a tab-free, unit-separated task list (python3 is required for
# scoring anyway, so we rely on it here too instead of fragile shell JSON parsing).
TASKS_FILE="${WORK_DIR}/tasks.usv"
python3 - "${BENCHMARK_PATH}" > "${TASKS_FILE}" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        r = json.loads(line)
        # id \x1f fixture \x1f grader \x1f prompt
        print("\x1f".join([r["id"], r["fixture"], r["grader"], r["prompt"]]))
PY

# ---- orchestration: per task, build/refresh the probe, run the agent, capture a result JSON ----
while IFS=$'\x1f' read -r id fixture grader prompt; do
  [[ -z "${id}" ]] && continue

  ext="${fixture##*.}"
  if [[ "${ext}" == "vfx" ]]; then
    target="${PROBE_DIR}/${id}.vfx"
    cp "${PROJECT_DIR}/${fixture}" "${PROJECT_DIR}/${target}"
    source="${target}"
  else
    # mesh-input task (SDF): agent must CREATE this output asset; source stays the read-only fixture.
    target="${PROBE_DIR}/${id}_out.asset"
    source="${fixture}"
  fi
  cli refresh_assets '{}' >/dev/null || true

  if [[ -n "${AGENT_CMD}" ]]; then
    VFX_EVAL_PROMPT="${prompt}" \
    VFX_EVAL_TARGET="${target}" \
    VFX_EVAL_SOURCE="${source}" \
    VFX_EVAL_PROJECT_DIR="${PROJECT_DIR}" \
    VFX_EVAL_PORT="${PORT}" \
    bash -c "${AGENT_CMD}" || echo "WARN: agent-cmd failed for ${id}" >&2
    cli refresh_assets '{}' >/dev/null || true
  fi

  # Capture the result the grader will read.
  if [[ "${grader}" == "sdf_asset_exists" ]]; then
    if [[ -f "${PROJECT_DIR}/${target}" ]]; then
      printf '{"asset_exists": true, "target": "%s"}\n' "${target}" > "${WORK_DIR}/${id}.json"
    else
      printf '{"asset_exists": false, "target": "%s"}\n' "${target}" > "${WORK_DIR}/${id}.json"
    fi
  else
    cli vfx_describe_graph "{\"assetPath\":\"${target}\"}" > "${WORK_DIR}/${id}.json" || echo '{}' > "${WORK_DIR}/${id}.json"
  fi
done < "${TASKS_FILE}"

# ---- scoring (Python; mirrors scripts/skill-eval/llm-routing-eval.sh) ----
python3 - "${BENCHMARK_PATH}" "${WORK_DIR}" "${MODEL}" "${HISTORY_PATH}" "${SUMMARY_PATH}" "${JSON_OUTPUT}" <<'PY'
import datetime as dt
import json
import pathlib
import re
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


def contexts(d):
    return d.get("contexts", []) or []


def blocks(ctx):
    return ctx.get("blocks", []) or []


def btype(b):
    return (b.get("type") or b.get("name") or "")


def linked_to_parameter(slot):
    if not slot or not slot.get("hasLink"):
        return False
    for l in slot.get("links", []) or []:
        node = l.get("node") or {}
        if node.get("kind") == "parameter":
            return True
    return False


# ---- graders: each returns (passed: bool, detail: str). Ported from the proven manual oracles. ----
def turbulence_in_update(d, args):
    for c in contexts(d):
        if c.get("contextType") == "Update":
            hit = [btype(b) for b in blocks(c) if re.search("turbulence", btype(b), re.I)]
            if hit:
                return True, f"Update has {hit}"
    return False, "no Turbulence block on Update"


def exposed_param_into_spawner(d, args):
    params = d.get("parameters", []) or []
    has_float = any(
        p.get("exposed") and (
            p.get("parameterType") == "Single"
            or any(s.get("valueType") == "Single" for s in (p.get("outputSlots") or []))
        )
        for p in params
    )
    linked = False
    detail = ""
    for c in contexts(d):
        if c.get("contextType") != "Spawner":
            continue
        for b in blocks(c):
            for s in b.get("inputSlots", []) or []:
                if linked_to_parameter(s):
                    linked = True
                    detail = f"{btype(b)}.{s.get('name')}"
    return (has_float and linked), f"exposedFloat={has_float}, link={detail or 'none'}"


def second_system_on_event(d, args):
    particle_ids = {
        c.get("dataInstanceId")
        for c in contexts(d)
        if c.get("contextType") in ("Init", "Update", "Output") and c.get("dataInstanceId") is not None
    }
    events = [c for c in contexts(d) if c.get("contextType") == "Event"]
    burst = any(
        c.get("contextType") == "Spawner" and any(re.search("burst", btype(b), re.I) for b in blocks(c))
        for c in contexts(d)
    )
    ok = len(particle_ids) >= 2 and len(events) >= 1 and burst
    return ok, f"particleSystems={len(particle_ids)}, events={len(events)}, burst={burst}"


def operator_subgraph_color_input(d, args):
    for o in d.get("operators", []) or []:
        if re.search("subgraph", o.get("type", ""), re.I) and (o.get("settings") or {}).get("m_Subgraph"):
            color_port = any(
                re.search("color", s.get("name", ""), re.I) and re.search("color", s.get("valueType", ""), re.I)
                for s in (o.get("inputSlots") or [])
            )
            if color_port:
                return True, f"{o.get('name')} has a Color input port"
            return False, "subgraph operator present but no Color input port"
    return False, "no subgraph operator referenced"


def block_activation_from_bool(d, args):
    params = d.get("parameters", []) or []
    has_bool = any(
        p.get("exposed") and (
            p.get("parameterType") == "Boolean"
            or any(s.get("valueType") == "Boolean" for s in (p.get("outputSlots") or []))
        )
        for p in params
    )
    linked = False
    detail = ""
    for c in contexts(d):
        for b in blocks(c):
            if linked_to_parameter(b.get("activationSlot")):
                linked = True
                detail = f"{c.get('contextType')}/{btype(b)}"
    return (has_bool and linked), f"exposedBool={has_bool}, activationLink={detail or 'none'}"


def custom_template_named(d, args):
    t = d.get("template")
    name = (args or {}).get("name")
    ok = bool(t) and t.get("name") == name
    return ok, f"template={t}"


def sdf_asset_exists(d, args):
    ok = bool(d.get("asset_exists"))
    return ok, f"target={d.get('target')}, exists={ok}"


def hlsl_operator(d, args):
    ops = [o for o in (d.get("operators") or []) if (o.get("settings") or {}).get("m_HLSLCode")]
    return bool(ops), f"customHlslOps={len(ops)}"


def gpu_event_chain(d, args):
    gpu = any(c.get("contextType") == "SpawnerGPU" for c in contexts(d))
    particle_ids = {
        c.get("dataInstanceId")
        for c in contexts(d)
        if c.get("contextType") in ("Init", "Update", "Output") and c.get("dataInstanceId") is not None
    }
    ok = gpu and len(particle_ids) >= 2
    return ok, f"gpuEventCtx={gpu}, particleSystems={len(particle_ids)}"


def params_in_category(d, args):
    cat = (args or {}).get("category")
    need = (args or {}).get("count", 2)
    matched = [
        p for p in (d.get("parameters") or [])
        if p.get("exposed") and p.get("category") == cat and (
            p.get("parameterType") == "Single"
            or any(s.get("valueType") == "Single" for s in (p.get("outputSlots") or []))
        )
    ]
    return len(matched) >= need, f"floatsInCategory[{cat}]={len(matched)} (need {need})"


def instancing_custom(d, args):
    inst = d.get("instancing") or {}
    cap = (args or {}).get("capacity")
    ok = inst.get("mode") == "Custom" and inst.get("capacity") == cap
    return ok, f"instancing={inst}"


def custom_attribute_set(d, args):
    name = (args or {}).get("name")
    defined = any((a.get("attributeName") or a.get("name")) == name for a in (d.get("customAttributes") or []))
    set_in_init = False
    for c in contexts(d):
        if c.get("contextType") != "Init":
            continue
        for b in blocks(c):
            if str((b.get("settings") or {}).get("attribute", "")).lower() == str(name).lower():
                set_in_init = True
    return (defined and set_in_init), f"defined={defined}, setInInit={set_in_init}"


def flipbook_output(d, args):
    for c in contexts(d):
        if c.get("contextType") != "Output":
            continue
        s = c.get("settings") or {}
        if s.get("uvMode") == "Flipbook":
            on = bool(s.get("flipbookMotionVectors")) or bool(s.get("flipbookBlendFrames"))
            return on, f"uvMode=Flipbook, mv={s.get('flipbookMotionVectors')}, blend={s.get('flipbookBlendFrames')}"
    return False, "no Output with uvMode=Flipbook"


def sticky_note_titled(d, args):
    title = ((args or {}).get("title") or "").lower()
    matched = [n for n in (d.get("stickyNotes") or []) if title in (n.get("title") or "").lower()]
    return bool(matched), f"matchingNotes={len(matched)}"


def system_named(d, args):
    name = (args or {}).get("name")
    ok = any(c.get("systemName") == name for c in contexts(d))
    return ok, f"namedAs[{name}]={ok}"


GRADERS = {
    "turbulence_in_update": turbulence_in_update,
    "exposed_param_into_spawner": exposed_param_into_spawner,
    "second_system_on_event": second_system_on_event,
    "operator_subgraph_color_input": operator_subgraph_color_input,
    "block_activation_from_bool": block_activation_from_bool,
    "custom_template_named": custom_template_named,
    "sdf_asset_exists": sdf_asset_exists,
    "hlsl_operator": hlsl_operator,
    "gpu_event_chain": gpu_event_chain,
    "params_in_category": params_in_category,
    "instancing_custom": instancing_custom,
    "custom_attribute_set": custom_attribute_set,
    "flipbook_output": flipbook_output,
    "sticky_note_titled": sticky_note_titled,
    "system_named": system_named,
}

bench = load_jsonl(benchmark_path)
if not bench:
    raise SystemExit("Benchmark is empty")

results = []
for row in bench:
    rid = row["id"]
    grader = GRADERS.get(row["grader"])
    result_file = work_dir / f"{rid}.json"
    detail = ""
    passed = False
    if grader is None:
        detail = f"unknown grader '{row['grader']}'"
    elif not result_file.exists():
        detail = "no result captured (agent or describe failed)"
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
pass_rate = passed_n / n if n else 0.0

summary = {
    "timestamp": dt.datetime.now(dt.timezone.utc).isoformat(),
    "model": model,
    "benchmark": str(benchmark_path),
    "total": n,
    "passed": passed_n,
    "pass_rate": round(pass_rate, 4),
    "results": results,
}

summary_path.parent.mkdir(parents=True, exist_ok=True)
summary_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

history_path.parent.mkdir(parents=True, exist_ok=True)
with history_path.open("a", encoding="utf-8") as f:
    f.write(json.dumps({
        "timestamp": summary["timestamp"],
        "model": model,
        "total": n,
        "passed": passed_n,
        "pass_rate": summary["pass_rate"],
    }, ensure_ascii=False) + "\n")

if json_output:
    print(json.dumps(summary, ensure_ascii=False, indent=2))
else:
    print("[VFX AGENT EVAL]")
    print(f"  model: {model}")
    print(f"  passed: {passed_n}/{n}  (pass_rate {summary['pass_rate']:.4f})")
    for r in results:
        mark = "PASS" if r["passed"] else "FAIL"
        print(f"  [{mark}] {r['id']} ({r['difficulty']}) — {r['detail']}")
    print(f"  summary: {summary_path}")

# Discovery tool, not a gate: always exit 0 so a nightly never reds on agent non-determinism.
sys.exit(0)
PY

if [[ "${KEEP_PROBES}" -eq 0 ]]; then
  rm -rf "${PROJECT_DIR:?}/${PROBE_DIR}" "${PROJECT_DIR}/${PROBE_DIR}.meta" 2>/dev/null || true
  cli refresh_assets '{}' >/dev/null || true
fi
