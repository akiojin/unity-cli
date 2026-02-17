#!/usr/bin/env bash
# scripts/e2e-test.sh — Run E2E test scenarios against a running Unity Editor
# Make executable: chmod +x scripts/e2e-test.sh

set -euo pipefail

HOST="127.0.0.1"
PORT="8080"
UNITY_CLI="./target/release/unity-cli"
LOG="/tmp/unity-cli-e2e-$(date +%Y%m%d-%H%M%S).log"
PASSED=0
FAILED=0

while [[ $# -gt 0 ]]; do
  case $1 in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    *)      echo "Unknown option: $1"; exit 1 ;;
  esac
done

if [[ ! -x "$UNITY_CLI" ]]; then
  UNITY_CLI="$(command -v unity-cli 2>/dev/null || true)"
  if [[ -z "$UNITY_CLI" ]]; then
    echo "ERROR: unity-cli not found. Run 'cargo build --release' first."
    exit 1
  fi
fi

echo "E2E Test Suite"
echo "  Host: $HOST"
echo "  Port: $PORT"
echo "  CLI:  $UNITY_CLI"
echo "  Log:  $LOG"
echo ""

run_test() {
  local name="$1"
  shift
  echo -n "  $name ... "
  if "$@" >> "$LOG" 2>&1; then
    echo "PASS"
    PASSED=$((PASSED + 1))
  else
    echo "FAIL"
    FAILED=$((FAILED + 1))
  fi
}

echo "--- Scenarios ---"

run_test "system ping" \
  "$UNITY_CLI" system ping --host "$HOST" --port "$PORT"

run_test "raw create_scene" \
  "$UNITY_CLI" raw create_scene --json '{"sceneName":"E2ETest"}' --host "$HOST" --port "$PORT"

run_test "tool list" \
  "$UNITY_CLI" tool list --host "$HOST" --port "$PORT"

echo ""
echo "--- Summary ---"
echo "  Passed: $PASSED"
echo "  Failed: $FAILED"
echo "  Total:  $((PASSED + FAILED))"

if [[ $FAILED -gt 0 ]]; then
  echo ""
  echo "Some tests failed. See log: $LOG"
  exit 1
fi

echo ""
echo "All tests passed."
