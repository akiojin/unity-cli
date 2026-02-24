#!/usr/bin/env bash
# scripts/lsp-perf-check.sh
# Measure and validate LSP-path performance for script/index tools.
# Always runs the full case set and appends history to specs/perf/lsp-history.jsonl.

set -u -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_ROOT="${REPO_ROOT}/UnityCliBridge"

UNITY_CLI=""
RUNS=5
WARMUP=1
JSON_OUTPUT=0

TOKENIZER_NAME="o200k_base"
HISTORY_FILE="${REPO_ROOT}/specs/perf/lsp-history.jsonl"

# Thresholds (ms): can be overridden by env.
THRESHOLD_GET_SYMBOLS_MS="${UNITY_CLI_LSP_PERF_GET_SYMBOLS_MS:-2500}"
THRESHOLD_FIND_SYMBOL_MS="${UNITY_CLI_LSP_PERF_FIND_SYMBOL_MS:-10000}"
THRESHOLD_FIND_REFS_MS="${UNITY_CLI_LSP_PERF_FIND_REFS_MS:-4000}"
THRESHOLD_GET_SYMBOLS_GIGA_MS="${UNITY_CLI_LSP_PERF_GET_SYMBOLS_GIGA_MS:-20000}"
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
  elif [[ -x "${REPO_ROOT}/target/debug/unity-cli" ]]; then
    UNITY_CLI="${REPO_ROOT}/target/debug/unity-cli"
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

if ! python3 - <<'PY' >/dev/null 2>&1
import importlib.util
import sys
sys.exit(0 if importlib.util.find_spec("tiktoken") else 1)
PY
then
  echo "ERROR: python package 'tiktoken' is required (pip install tiktoken)." >&2
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
print(json.dumps({"count": n, "mean": round(mean, 2), "p95": round(p95, 2)}))
PY
}

calc_size_json() {
  TOKENIZER_NAME_ENV="${TOKENIZER_NAME}" python3 -c '
import json
import os
import sys
import tiktoken

tokenizer = os.environ["TOKENIZER_NAME_ENV"]
text = sys.stdin.read()
enc = tiktoken.get_encoding(tokenizer)

print(json.dumps({
    "bytes": len(text.encode("utf-8")),
    "chars": len(text),
    "tokens": len(enc.encode(text)),
}))
'
}

collect_csv() {
  local arr_name="$1"
  local joined=""
  local idx
  set +u
  eval "for idx in \"\${!${arr_name}[@]}\"; do
    if [[ \"\$idx\" -gt 0 ]]; then
      joined+=\",\"
    fi
    joined+=\"\${${arr_name}[\$idx]}\"
  done"
  set -u
  printf '%s' "${joined}"
}

run_lsp_case() {
  local case_name="$1"
  local tool="$2"
  local payload="$3"
  local threshold="$4"

  local i out rc start end elapsed normalized size_json
  local backend failure_reason
  local times=()
  local response_bytes=()
  local response_chars=()
  local response_tokens=()

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
      failure_reason="$(jq -r '.error // .message // .status // "unknown error"' <<<"${out}" 2>/dev/null)"
      CASE_FAIL_REASON="${case_name}: tool returned error (${failure_reason})"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    backend="$(jq -r '.backend // empty' <<<"${out}")"
    if [[ "${backend}" != "lsp" ]]; then
      CASE_FAIL_REASON="${case_name}: backend is not lsp"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    normalized="$(jq -c . <<<"${out}" 2>/dev/null || true)"
    if [[ -z "${normalized}" ]]; then
      CASE_FAIL_REASON="${case_name}: failed to normalize JSON output"
      CASE_FAIL_OUTPUT="${out}"
      return 1
    fi

    size_json="$(printf '%s' "${normalized}" | calc_size_json 2>&1)"
    rc=$?
    if [[ ${rc} -ne 0 ]] || ! jq -e . >/dev/null 2>&1 <<<"${size_json}"; then
      CASE_FAIL_REASON="${case_name}: failed to calculate size metrics"
      CASE_FAIL_OUTPUT="${size_json}"
      return 1
    fi

    response_bytes+=("$(jq -r '.bytes' <<<"${size_json}")")
    response_chars+=("$(jq -r '.chars' <<<"${size_json}")")
    response_tokens+=("$(jq -r '.tokens' <<<"${size_json}")")
    times+=("${elapsed}")
  done

  local csv_times csv_bytes csv_chars csv_tokens
  local time_stats_json bytes_stats_json chars_stats_json tokens_stats_json
  local mean_ms p95_ms

  csv_times="$(collect_csv times)"
  csv_bytes="$(collect_csv response_bytes)"
  csv_chars="$(collect_csv response_chars)"
  csv_tokens="$(collect_csv response_tokens)"

  time_stats_json="$(calc_stats_json "${csv_times}")"
  bytes_stats_json="$(calc_stats_json "${csv_bytes}")"
  chars_stats_json="$(calc_stats_json "${csv_chars}")"
  tokens_stats_json="$(calc_stats_json "${csv_tokens}")"

  mean_ms="$(jq -r '.mean' <<<"${time_stats_json}")"
  p95_ms="$(jq -r '.p95' <<<"${time_stats_json}")"

  CASE_RESULT_JSON="$(jq -nc \
    --arg caseName "${case_name}" \
    --arg tool "${tool}" \
    --argjson runs "${RUNS}" \
    --argjson warmup "${WARMUP}" \
    --argjson thresholdMs "${threshold}" \
    --argjson meanMs "${mean_ms}" \
    --argjson p95Ms "${p95_ms}" \
    --argjson responseBytes "$(jq -r '.mean' <<<"${bytes_stats_json}")" \
    --argjson responseChars "$(jq -r '.mean' <<<"${chars_stats_json}")" \
    --argjson responseTokens "$(jq -r '.mean' <<<"${tokens_stats_json}")" \
    --argjson responseBytesP95 "$(jq -r '.p95' <<<"${bytes_stats_json}")" \
    --argjson responseCharsP95 "$(jq -r '.p95' <<<"${chars_stats_json}")" \
    --argjson responseTokensP95 "$(jq -r '.p95' <<<"${tokens_stats_json}")" \
    '{
      case: $caseName,
      tool: $tool,
      runs: $runs,
      warmup: $warmup,
      mean_ms: $meanMs,
      p95_ms: $p95Ms,
      threshold_ms: $thresholdMs,
      pass: ($p95Ms <= $thresholdMs),
      response_bytes: $responseBytes,
      response_chars: $responseChars,
      response_tokens_o200k: $responseTokens,
      response_bytes_p95: $responseBytesP95,
      response_chars_p95: $responseCharsP95,
      response_tokens_o200k_p95: $responseTokensP95
    }'
  )"

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

run_and_record() {
  local case_name="$1"
  local tool="$2"
  local payload="$3"
  local threshold="$4"

  if run_lsp_case "${case_name}" "${tool}" "${payload}" "${threshold}"; then
    RESULTS+=("${CASE_RESULT_JSON}")
  else
    FAILED=$((FAILED + 1))
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
run_and_record "get_symbols_giga" "get_symbols" '{"path":"Assets/Scripts/GigaTestFile.cs"}' "${THRESHOLD_GET_SYMBOLS_GIGA_MS}"
run_and_record "find_symbol_giga" "find_symbol" '{"name":"GigaGameManager","kind":"class","exact":true,"scope":"assets"}' "${THRESHOLD_FIND_SYMBOL_GIGA_MS}"
run_and_record "find_refs_giga" "find_refs" '{"name":"InventoryItem","scope":"assets","pageSize":100}' "${THRESHOLD_FIND_REFS_GIGA_MS}"

results_json="$(printf '%s\n' "${RESULTS[@]}" | jq -s '.')"
summary_json="$(jq -nc \
  --arg unityCli "${UNITY_CLI}" \
  --arg projectRoot "${PROJECT_ROOT}" \
  --arg tokenizer "${TOKENIZER_NAME}" \
  --arg historyFile "${HISTORY_FILE}" \
  --argjson failed "${FAILED}" \
  --argjson results "${results_json}" \
  --argjson thresholdGetSymbols "${THRESHOLD_GET_SYMBOLS_MS}" \
  --argjson thresholdFindSymbol "${THRESHOLD_FIND_SYMBOL_MS}" \
  --argjson thresholdFindRefs "${THRESHOLD_FIND_REFS_MS}" \
  --argjson thresholdGetSymbolsGiga "${THRESHOLD_GET_SYMBOLS_GIGA_MS}" \
  --argjson thresholdFindSymbolGiga "${THRESHOLD_FIND_SYMBOL_GIGA_MS}" \
  --argjson thresholdFindRefsGiga "${THRESHOLD_FIND_REFS_GIGA_MS}" \
  '{
    success: ($failed == 0),
    unity_cli: $unityCli,
    project_root: $projectRoot,
    tokenizer: $tokenizer,
    history_file: $historyFile,
    thresholds_ms: {
      get_symbols: $thresholdGetSymbols,
      find_symbol: $thresholdFindSymbol,
      find_refs: $thresholdFindRefs,
      get_symbols_giga: $thresholdGetSymbolsGiga,
      find_symbol_giga: $thresholdFindSymbolGiga,
      find_refs_giga: $thresholdFindRefsGiga
    },
    failed_cases: $failed,
    results: $results
  }')"

TIMESTAMP_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
GIT_COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD 2>/dev/null || echo "unknown")"
GIT_BRANCH="$(git -C "${REPO_ROOT}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")"
HOST_OS="$(uname -s 2>/dev/null || echo "unknown")"
HOST_ARCH="$(uname -m 2>/dev/null || echo "unknown")"
UNITY_CLI_VERSION="$("${UNITY_CLI}" --version 2>/dev/null || echo "unknown")"
LSP_VERSION="$("${UNITY_CLI}" lspd status --output json 2>/dev/null | jq -r '.version // "unknown"' 2>/dev/null || echo "unknown")"

mkdir -p "$(dirname "${HISTORY_FILE}")"
HISTORY_ENTRY="$(jq -nc \
  --argjson summary "${summary_json}" \
  --arg timestampUtc "${TIMESTAMP_UTC}" \
  --arg gitCommit "${GIT_COMMIT}" \
  --arg gitBranch "${GIT_BRANCH}" \
  --arg hostOs "${HOST_OS}" \
  --arg hostArch "${HOST_ARCH}" \
  --arg unityCliVersion "${UNITY_CLI_VERSION}" \
  --arg lspVersion "${LSP_VERSION}" \
  '$summary + {
    timestamp_utc: $timestampUtc,
    git_commit: $gitCommit,
    git_branch: $gitBranch,
    host_os: $hostOs,
    host_arch: $hostArch,
    unity_cli_version: $unityCliVersion,
    lsp_version: $lspVersion
  }')"

printf '%s\n' "${HISTORY_ENTRY}" >> "${HISTORY_FILE}" || {
  echo "ERROR: failed to append history file: ${HISTORY_FILE}" >&2
  exit 1
}

if [[ ${JSON_OUTPUT} -eq 1 ]]; then
  echo "${summary_json}"
else
  echo "LSP Performance Check"
  echo "  unity-cli: ${UNITY_CLI}"
  echo "  project:   ${PROJECT_ROOT}"
  echo "  runs: ${RUNS}, warmup: ${WARMUP}"
  echo "  tokenizer: ${TOKENIZER_NAME}"
  echo ""
  echo "${summary_json}" | jq -r '.results[] | if .pass then "  PASS \(.case): mean=\(.mean_ms)ms p95=\(.p95_ms)ms (<= \(.threshold_ms)ms), tokens=\(.response_tokens_o200k)" else "  FAIL \(.case): \(.error)" end'
  echo ""
  echo "History appended: ${HISTORY_FILE}"
  if [[ ${FAILED} -gt 0 ]]; then
    echo "LSP performance check failed (${FAILED} case[s])."
  else
    echo "LSP performance check passed."
  fi
fi

if [[ ${FAILED} -gt 0 ]]; then
  exit 1
fi

exit 0
