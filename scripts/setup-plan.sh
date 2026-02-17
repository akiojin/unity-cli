#!/bin/bash
set -e

REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
SCRIPT="$REPO_ROOT/.specify/scripts/bash/setup-plan.sh"
if [ ! -x "$SCRIPT" ]; then
  echo "Error: Spec Kit script not found at $SCRIPT" >&2
  exit 1
fi
exec "$SCRIPT" "$@"
