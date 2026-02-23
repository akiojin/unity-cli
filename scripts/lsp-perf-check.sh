#!/usr/bin/env bash
# scripts/lsp-perf-check.sh
# Measure and validate LSP-path performance for script/index tools.

set -u -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"

UNITY_CLI=""
RUNS=5
WARMUP=1
JSON_OUTPUT=0
SKIP_LARGE=0

# Thresholds (ms): can be overridden by env.
THRESHOLD_GET_SYMBOLS_MS="${UNITY_CLI_LSP_PERF_GET_SYMBOLS_MS:-2500}"
THRESHOLD_FIND_SYMBOL_MS="${UNITY_CLI_LSP_PERF_FIND_SYMBOL_MS:-10000}"
THRESHOLD_FIND_REFS_MS="${UNITY_CLI_LSP_PERF_FIND_REFS_MS:-4000}"
THRESHOLD_GET_SYMBOLS_GIGA_MS="${UNITY_CLI_LSP_PERF_GET_SYMBOLS_GIGA_MS:-${UNITY_CLI_LSP_PERF_GET_SYMBOLS_LARGE_MS:-20000}}"
THRESHOLD_FIND_SYMBOL_GIGA_MS="${UNITY_CLI_LSP_PERF_FIND_SYMBOL_GIGA_MS:-12000}"
THRESHOLD_FIND_REFS_GIGA_MS="${UNITY_CLI_LSP_PERF_FIND_REFS_GIGA_MS:-20000}"

usage() {
  cat <<EOF
Usage: scripts/lsp-perf-check.sh [options]

Options:
  --unity-cli <path>       unity-cli binary path
  --project-root <path>    Unity project root (default: ./UnityCliBridge)
  --runs <n>               Measurement runs per case (default: 5)
  --warmup <n>             Warmup runs per case (default: 1)
  --skip-large             Skip large-file LSP checks
  --json                   Output JSON summary
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unity-cli)
      UNITY_CLI="$2"
      shift 2
      ;;
    --project-root)
      PROJECT_ROOT="$2"
      shift 2
      ;;
    --runs)
      RUNS="$2"
      shift 2
      ;;
    --warmup)
      WARMUP="$2"
      shift 2
      ;;
    --skip-large)
      SKIP_LARGE=1
      shift
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

if [[ -z "${UNITY_CLI}" ]]; then
  if [[ -x "${REPO_ROOT}/target/release/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/release/unity-cli"
  else
    UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  fi
fi

if [[ -z "${UNITY_CLI}" || ! -x "${UNITY_CLI}" ]]; then
  echo "ERROR: unity-cli not found." >&2
  exit 1
fi

if [[ ! -d "${PROJECT_ROOT}/Assets" || ! -d "${PROJECT_ROOT}/Packages" ]]; then
  echo "ERROR: Unity project root is invalid: ${PROJECT_ROOT}" >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "ERROR: jq is required." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "ERROR: python3 is required." >&2
  exit 1
fi

if ! [[ "${RUNS}" =~ ^[0-9]+$ ]] || [[ "${RUNS}" -lt 1 ]]; then
  echo "ERROR: --runs must be a positive integer." >&2
  exit 1
fi
if ! [[ "${WARMUP}" =~ ^[0-9]+$ ]] || [[ "${WARMUP}" -lt 0 ]]; then
  echo "ERROR: --warmup must be a non-negative integer." >&2
  exit 1
fi

export UNITY_PROJECT_ROOT="${PROJECT_ROOT}"

now_ms() {
  python3 - <<'PY'
import time
print(int(time.time_ns() / 1_000_000))
PY
}

calc_stats_json() {
  local csv="$1"
  python3 - "$csv" <<'PY'
import json
import math
import sys

raw = [x for x in sys.argv[1].split(",") if x]
vals = sorted(float(x) for x in raw)
n = len(vals)
mean = sum(vals) / n
idx = int(math.ceil(0.95 * n)) - 1
idx = max(0, min(idx, n - 1))
p95 = vals[idx]
print(json.dumps({"count": n, "mean_ms": round(mean, 2), "p95_ms": round(p95, 2)}))
PY
}

run_lsp_case() {
  local case_name="$1"
  local tool="$2"
  local payload="$3"
  local threshold="$4"

  local i out rc start end elapsed
  local times=()
  local backend

  for ((i = 0; i < WARMUP; i++)); do
    UNITY_CLI_LSP_MODE=required "${UNITY_CLI}" tool call "${tool}" --json "${payload}" --output json >/dev/null 2>&1 || true
  done

  for ((i = 0; i < RUNS; i++)); do
    start="$(now_ms)"
    out="$(UNITY_CLI_LSP_MODE=required "${UNITY_CLI}" tool call "${tool}" --json "${payload}" --output json 2>&1)"
    rc=$?
    end="$(now_ms)"
    elapsed=$((end - start))

    if [[ ${rc} -ne 0 ]]; then
      CASE_FAIL_REASON="${case_name}: command failed (exit=${rc})"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    if ! jq -e . >/dev/null 2>&1 <<<"${out}"; then
      CASE_FAIL_REASON="${case_name}: non-JSON response"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    if jq -e 'type=="object" and ((has("error") and .error != null and (.error|tostring|length)>0) or (has("success") and .success == false) or (has("status") and ((.status|tostring|ascii_downcase)=="error")))' >/dev/null 2>&1 <<<"${out}"; then
      CASE_FAIL_REASON="${case_name}: tool returned error"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    backend="$(jq -r '.backend // empty' <<<"${out}")"
    if [[ "${backend}" != "lsp" ]]; then
      CASE_FAIL_REASON="${case_name}: backend is not lsp"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    times+=("${elapsed}")
  done

  local csv
  local joined=""
  for i in "${!times[@]}"; do
    if [[ "${i}" -gt 0 ]]; then
      joined+=","
    fi
    joined+="${times[$i]}"
  done
  csv="${joined}"

  local stats_json mean_ms p95_ms
  stats_json="$(calc_stats_json "${csv}")"
  mean_ms="$(jq -r '.mean_ms' <<<"${stats_json}")"
  p95_ms="$(jq -r '.p95_ms' <<<"${stats_json}")"

  CASE_RESULT_JSON="$(jq -nc \
    --arg caseName "${case_name}" \
    --arg tool "${tool}" \
    --argjson stats "${stats_json}" \
    --argjson thresholdMs "${threshold}" \
    --argjson runs "${RUNS}" \
    --argjson warmup "${WARMUP}" \
    '{
      case: $caseName,
      tool: $tool,
      runs: $runs,
      warmup: $warmup,
      mean_ms: $stats.mean_ms,
      p95_ms: $stats.p95_ms,
      threshold_ms: $thresholdMs,
      pass: ($stats.p95_ms <= $thresholdMs)
    }')"

  if (( $(python3 - <<PY
p95 = float("${p95_ms}")
thr = float("${threshold}")
print(1 if p95 <= thr else 0)
PY
) == 0 )); then
    CASE_FAIL_REASON="${case_name}: p95 ${p95_ms}ms > threshold ${threshold}ms"
    CASE_FAIL_OUTPUT="${CASE_RESULT_JSON}"
    return 1
  fi

  return 0
}

RESULTS=()
FAILED=0
FAIL_MESSAGES=()

run_and_record() {
  local case_name="$1"
  local tool="$2"
  local payload="$3"
  local threshold="$4"

  if run_lsp_case "${case_name}" "${tool}" "${payload}" "${threshold}"; then
    RESULTS+=("${CASE_RESULT_JSON}")
  else
    FAILED=$((FAILED + 1))
    FAIL_MESSAGES+=("${CASE_FAIL_REASON}")
    RESULTS+=("$(jq -nc \
      --arg caseName "${case_name}" \
      --arg tool "${tool}" \
      --arg reason "${CASE_FAIL_REASON}" \
      --arg output "${CASE_FAIL_OUTPUT}" \
      '{case:$caseName,tool:$tool,pass:false,error:$reason,raw:$output}')")
  fi
}

run_and_record "get_symbols" "get_symbols" '{"path":"Assets/Scripts/ButtonHandler.cs"}' "${THRESHOLD_GET_SYMBOLS_MS}"
run_and_record "find_symbol" "find_symbol" '{"name":"ButtonHandler","kind":"class","exact":true,"scope":"assets"}' "${THRESHOLD_FIND_SYMBOL_MS}"
run_and_record "find_refs" "find_refs" '{"name":"ButtonHandler","scope":"assets","pageSize":20}' "${THRESHOLD_FIND_REFS_MS}"

if [[ ${SKIP_LARGE} -eq 0 ]]; then
  run_and_record "get_symbols_giga" "get_symbols" '{"path":"Assets/Scripts/GigaTestFile.cs"}' "${THRESHOLD_GET_SYMBOLS_GIGA_MS}"
  run_and_record "find_symbol_giga" "find_symbol" '{"name":"GigaGameManager","kind":"class","exact":true,"scope":"assets"}' "${THRESHOLD_FIND_SYMBOL_GIGA_MS}"
  run_and_record "find_refs_giga" "find_refs" '{"name":"InventoryItem","scope":"assets","pageSize":100}' "${THRESHOLD_FIND_REFS_GIGA_MS}"
fi

results_json="$(printf '%s\n' "${RESULTS[@]}" | jq -s '.')"
summary_json="$(jq -nc \
  --arg unityCli "${UNITY_CLI}" \
  --arg projectRoot "${PROJECT_ROOT}" \
  --argjson failed "${FAILED}" \
  --argjson results "${results_json}" \
  --argjson thresholdGetSymbols "${THRESHOLD_GET_SYMBOLS_MS}" \
  --argjson thresholdFindSymbol "${THRESHOLD_FIND_SYMBOL_MS}" \
  --argjson thresholdFindRefs "${THRESHOLD_FIND_REFS_MS}" \
  --argjson thresholdGetSymbolsGiga "${THRESHOLD_GET_SYMBOLS_GIGA_MS}" \
  --argjson thresholdFindSymbolGiga "${THRESHOLD_FIND_SYMBOL_GIGA_MS}" \
  --argjson thresholdFindRefsGiga "${THRESHOLD_FIND_REFS_GIGA_MS}" \
  --argjson skipLarge "${SKIP_LARGE}" \
  '{
    success: ($failed == 0),
    unity_cli: $unityCli,
    project_root: $projectRoot,
    thresholds_ms: {
      get_symbols: $thresholdGetSymbols,
      find_symbol: $thresholdFindSymbol,
      find_refs: $thresholdFindRefs,
      get_symbols_giga: $thresholdGetSymbolsGiga,
      find_symbol_giga: $thresholdFindSymbolGiga,
      find_refs_giga: $thresholdFindRefsGiga
    },
    skip_large: ($skipLarge == 1),
    failed_cases: $failed,
    results: $results
  }')"

if [[ ${JSON_OUTPUT} -eq 1 ]]; then
  echo "${summary_json}"
else
  echo "LSP Performance Check"
  echo "  unity-cli: ${UNITY_CLI}"
  echo "  project:   ${PROJECT_ROOT}"
  echo "  runs: ${RUNS}, warmup: ${WARMUP}"
  echo ""
  echo "${summary_json}" | jq -r '.results[] | if .pass then "  PASS \(.case): mean=\(.mean_ms)ms p95=\(.p95_ms)ms (<= \(.threshold_ms)ms)" else "  FAIL \(.case): \(.error)" end'
  if [[ ${FAILED} -gt 0 ]]; then
    echo ""
    echo "LSP performance check failed (${FAILED} case[s])."
  else
    echo ""
    echo "LSP performance check passed."
  fi
fi

if [[ ${FAILED} -gt 0 ]]; then
  exit 1
fi

exit 0
