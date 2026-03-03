#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/sync-gh-skills.sh [options]

Sync gh-* skills from a GitHub repository into:
- global:  $CODEX_HOME/skills (default: ~/.codex/skills)
- project: ./.codex/skills

Options:
  --repo <owner/repo>      Source repository (default: akiojin/skills)
  --ref <ref>              Git ref (default: main)
  --target <both|global|project>
                           Install target (default: both)
  --check                  Only check whether global/project gh-* skills are identical
  -h, --help               Show this help
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CODEX_HOME="${CODEX_HOME:-$HOME/.codex}"
GLOBAL_SKILLS_DIR="$CODEX_HOME/skills"
PROJECT_SKILLS_DIR="$REPO_ROOT/.codex/skills"
INSTALLER_SCRIPT="$CODEX_HOME/skills/.system/skill-installer/scripts/install-skill-from-github.py"

REPO="akiojin/skills"
REF="main"
TARGET="both"
CHECK_ONLY=0
SKILLS=("gh-fix-ci" "gh-fix-issue" "gh-pr" "gh-pr-check")

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo)
      REPO="${2:-}"
      shift 2
      ;;
    --ref)
      REF="${2:-}"
      shift 2
      ;;
    --target)
      TARGET="${2:-}"
      shift 2
      ;;
    --check)
      CHECK_ONLY=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ "$TARGET" != "both" && "$TARGET" != "global" && "$TARGET" != "project" ]]; then
  echo "ERROR: --target must be one of: both, global, project" >&2
  exit 1
fi

if [[ $CHECK_ONLY -eq 1 ]]; then
  mismatch=0
  for skill in "${SKILLS[@]}"; do
    g="$GLOBAL_SKILLS_DIR/$skill"
    p="$PROJECT_SKILLS_DIR/$skill"
    if [[ ! -d "$g" || ! -d "$p" ]]; then
      echo "MISSING: $skill (global: $([[ -d "$g" ]] && echo yes || echo no), project: $([[ -d "$p" ]] && echo yes || echo no))"
      mismatch=1
      continue
    fi
    if ! diff -qr "$g" "$p" >/dev/null; then
      echo "DIFF: $skill"
      mismatch=1
      continue
    fi
    echo "OK: $skill"
  done
  exit "$mismatch"
fi

if [[ ! -f "$INSTALLER_SCRIPT" ]]; then
  echo "ERROR: Installer script not found: $INSTALLER_SCRIPT" >&2
  echo "Install/enable skill-installer first." >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

declare -a INSTALL_PATHS=()
for skill in "${SKILLS[@]}"; do
  INSTALL_PATHS+=("github/skills/$skill")
done

python3 "$INSTALLER_SCRIPT" \
  --repo "$REPO" \
  --ref "$REF" \
  --path "${INSTALL_PATHS[@]}" \
  --dest "$TMP_DIR"

sync_to_dest() {
  local dest="$1"
  local ts backup
  ts="$(date +%Y%m%d%H%M%S)"
  backup="$dest/.backup/gh-sync-$ts"

  mkdir -p "$dest"

  for skill in "${SKILLS[@]}"; do
    if [[ -d "$dest/$skill" ]]; then
      mkdir -p "$backup"
      mv "$dest/$skill" "$backup/$skill"
    fi
    cp -R "$TMP_DIR/$skill" "$dest/$skill"
    echo "Synced: $dest/$skill"
  done

  if [[ -d "$backup" ]]; then
    echo "Backup: $backup"
  fi
}

if [[ "$TARGET" == "both" || "$TARGET" == "global" ]]; then
  sync_to_dest "$GLOBAL_SKILLS_DIR"
fi

if [[ "$TARGET" == "both" || "$TARGET" == "project" ]]; then
  sync_to_dest "$PROJECT_SKILLS_DIR"
fi

