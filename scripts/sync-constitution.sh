#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/sync-constitution.sh [--from-specify|--check]

Sync the project constitution between:
- docs/constitution.md (source of truth)
- .specify/memory/constitution.md (mirror for Spec Kit / Claude commands)

Options:
  --from-specify  Copy from .specify/memory/constitution.md to docs/constitution.md
  --check         Diff and exit non-zero if different
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DOCS_PATH="$REPO_ROOT/docs/constitution.md"
SPECIFY_PATH="$REPO_ROOT/.specify/memory/constitution.md"

MODE="to-specify"
if [[ "${1:-}" == "--from-specify" ]]; then
  MODE="from-specify"
  shift
elif [[ "${1:-}" == "--check" ]]; then
  MODE="check"
  shift
elif [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
elif [[ "${1:-}" != "" ]]; then
  echo "ERROR: Unknown option: $1" >&2
  usage >&2
  exit 1
fi

if [[ "$MODE" == "check" ]]; then
  diff -u "$DOCS_PATH" "$SPECIFY_PATH"
  exit 0
fi

if [[ "$MODE" == "to-specify" ]]; then
  mkdir -p "$(dirname "$SPECIFY_PATH")"
  cp "$DOCS_PATH" "$SPECIFY_PATH"
  echo "Synced: docs → .specify"
  exit 0
fi

mkdir -p "$(dirname "$DOCS_PATH")"
cp "$SPECIFY_PATH" "$DOCS_PATH"
echo "Synced: .specify → docs"
