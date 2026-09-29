#!/usr/bin/env bash
# Run existing acceptance suites in isolated, version-specific Editor projects.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${SCRIPT_DIR}/e2e-matrix.py" "$@"
