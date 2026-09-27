#!/usr/bin/env bash
# Run Timeline acceptance tests against a real Unity Editor listener.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${SCRIPT_DIR}/e2e-timeline.py" "$@"
