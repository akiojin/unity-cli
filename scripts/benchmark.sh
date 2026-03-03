#!/usr/bin/env bash
#
# benchmark.sh - Measure unity-cli performance baseline
#
# Usage:
#   ./scripts/benchmark.sh              # Run all benchmarks
#   ./scripts/benchmark.sh --json       # Output machine-readable JSON
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Configuration ──────────────────────────────────────────────

WARMUP=3
RUNS=10
JSON_OUTPUT=false

for arg in "$@"; do
  case "$arg" in
    --json) JSON_OUTPUT=true ;;
    *) ;;
  esac
done

# ── Resolve binary ─────────────────────────────────────────────

if command -v unity-cli >/dev/null 2>&1; then
  BINARY="unity-cli"
elif [ -f "$PROJECT_DIR/target/release/unity-cli" ]; then
  BINARY="$PROJECT_DIR/target/release/unity-cli"
elif [ -f "$PROJECT_DIR/target/debug/unity-cli" ]; then
  BINARY="$PROJECT_DIR/target/debug/unity-cli"
else
  echo "ERROR: unity-cli binary not found. Run 'cargo build --release' first." >&2
  exit 1
fi

BINARY_PATH="$(command -v "$BINARY" 2>/dev/null || echo "$BINARY")"

# ── Environment info ───────────────────────────────────────────

OS_NAME="$(uname -s)"
OS_VERSION="$(uname -r)"
ARCH="$(uname -m)"
RUST_VERSION="$(rustc --version 2>/dev/null || echo 'unknown')"
CLI_VERSION="$("$BINARY" --version 2>/dev/null || echo 'unknown')"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# ── Benchmark helpers ──────────────────────────────────────────

HAS_HYPERFINE=false
if command -v hyperfine >/dev/null 2>&1; then
  HAS_HYPERFINE=true
fi

# Run a benchmark using hyperfine if available, otherwise use a simple time loop.
# Arguments:
#   $1 - benchmark name
#   $2 - command to benchmark
# Outputs:
#   Sets BENCH_MEAN_MS and BENCH_STDDEV_MS
run_benchmark() {
  local name="$1"
  local cmd="$2"

  if $HAS_HYPERFINE; then
    local json_file
    json_file="$(mktemp)"
    hyperfine \
      --warmup "$WARMUP" \
      --runs "$RUNS" \
      --export-json "$json_file" \
      --shell=none \
      -- $cmd 2>/dev/null

    BENCH_MEAN_MS="$(python3 -c "
import json, sys
data = json.load(open('$json_file'))
mean_s = data['results'][0]['mean']
print(f'{mean_s * 1000:.2f}')
" 2>/dev/null || echo "0.00")"

    BENCH_STDDEV_MS="$(python3 -c "
import json, sys
data = json.load(open('$json_file'))
stddev_s = data['results'][0]['stddev']
print(f'{stddev_s * 1000:.2f}')
" 2>/dev/null || echo "0.00")"

    rm -f "$json_file"
  else
    # Fallback: use bash built-in time
    local total_ms=0
    local measurements=()

    # Warmup
    for ((i = 0; i < WARMUP; i++)); do
      $cmd >/dev/null 2>&1 || true
    done

    # Measure
    for ((i = 0; i < RUNS; i++)); do
      local start_ns end_ns elapsed_ms
      start_ns="$(python3 -c 'import time; print(int(time.time_ns()))' 2>/dev/null || date +%s%N)"
      $cmd >/dev/null 2>&1 || true
      end_ns="$(python3 -c 'import time; print(int(time.time_ns()))' 2>/dev/null || date +%s%N)"
      elapsed_ms=$(( (end_ns - start_ns) / 1000000 ))
      measurements+=("$elapsed_ms")
      total_ms=$((total_ms + elapsed_ms))
    done

    BENCH_MEAN_MS="$(python3 -c "
vals = [${measurements[*]// /,}]
mean = sum(vals) / len(vals)
print(f'{mean:.2f}')
" 2>/dev/null || echo "$((total_ms / RUNS))")"

    BENCH_STDDEV_MS="$(python3 -c "
import math
vals = [${measurements[*]// /,}]
mean = sum(vals) / len(vals)
variance = sum((x - mean) ** 2 for x in vals) / len(vals)
print(f'{math.sqrt(variance):.2f}')
" 2>/dev/null || echo "0.00")"
  fi
}

# ── Run benchmarks ─────────────────────────────────────────────

if ! $JSON_OUTPUT; then
  echo "=== unity-cli Performance Benchmark ==="
  echo "Binary:        $BINARY_PATH"
  echo "CLI version:   $CLI_VERSION"
  echo "Rust version:  $RUST_VERSION"
  echo "OS:            $OS_NAME $OS_VERSION ($ARCH)"
  echo "Timestamp:     $TIMESTAMP"
  echo "Tool:          $(if $HAS_HYPERFINE; then echo 'hyperfine'; else echo 'bash time loop'; fi)"
  echo "Warmup: $WARMUP  Runs: $RUNS"
  echo ""
fi

# Benchmark 1: --help output
run_benchmark "help" "$BINARY --help"
HELP_MEAN="$BENCH_MEAN_MS"
HELP_STDDEV="$BENCH_STDDEV_MS"

if ! $JSON_OUTPUT; then
  echo "[help]     mean=${HELP_MEAN}ms  stddev=${HELP_STDDEV}ms"
fi

# Benchmark 2: tool list (no Unity connection required)
run_benchmark "tool_list" "$BINARY tool list"
TOOL_LIST_MEAN="$BENCH_MEAN_MS"
TOOL_LIST_STDDEV="$BENCH_STDDEV_MS"

if ! $JSON_OUTPUT; then
  echo "[tool_list] mean=${TOOL_LIST_MEAN}ms  stddev=${TOOL_LIST_STDDEV}ms"
fi

# Benchmark 3: system ping (requires Unity connection - skip if unavailable)
PING_MEAN="null"
PING_STDDEV="null"
PING_SKIPPED=true

if $BINARY system ping --timeout-ms 1000 >/dev/null 2>&1; then
  run_benchmark "system_ping" "$BINARY system ping"
  PING_MEAN="$BENCH_MEAN_MS"
  PING_STDDEV="$BENCH_STDDEV_MS"
  PING_SKIPPED=false

  if ! $JSON_OUTPUT; then
    echo "[ping]     mean=${PING_MEAN}ms  stddev=${PING_STDDEV}ms"
  fi
else
  if ! $JSON_OUTPUT; then
    echo "[ping]     skipped (Unity Editor not reachable)"
  fi
fi

# ── Output JSON ────────────────────────────────────────────────

if $JSON_OUTPUT; then
  cat <<ENDJSON
{
  "timestamp": "$TIMESTAMP",
  "environment": {
    "os": "$OS_NAME",
    "os_version": "$OS_VERSION",
    "arch": "$ARCH",
    "rust_version": "$RUST_VERSION",
    "cli_version": "$CLI_VERSION",
    "binary": "$BINARY_PATH",
    "benchmark_tool": "$(if $HAS_HYPERFINE; then echo 'hyperfine'; else echo 'bash'; fi)",
    "warmup": $WARMUP,
    "runs": $RUNS
  },
  "results": {
    "help": {
      "mean_ms": $HELP_MEAN,
      "stddev_ms": $HELP_STDDEV
    },
    "tool_list": {
      "mean_ms": $TOOL_LIST_MEAN,
      "stddev_ms": $TOOL_LIST_STDDEV
    },
    "system_ping": {
      "mean_ms": $PING_MEAN,
      "stddev_ms": $PING_STDDEV,
      "skipped": $PING_SKIPPED
    }
  }
}
ENDJSON
else
  echo ""
  echo "Done. Use --json for machine-readable output."
fi
