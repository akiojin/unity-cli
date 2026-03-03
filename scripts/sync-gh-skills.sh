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
  --method <auto|installer|git>
                           Fetch method (default: auto)
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
METHOD="auto"
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
    --method)
      METHOD="${2:-}"
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

if [[ "$METHOD" != "auto" && "$METHOD" != "installer" && "$METHOD" != "git" ]]; then
  echo "ERROR: --method must be one of: auto, installer, git" >&2
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

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

declare -a INSTALL_PATHS=()
for skill in "${SKILLS[@]}"; do
  INSTALL_PATHS+=("github/skills/$skill")
done

fetch_with_installer() {
  if [[ ! -f "$INSTALLER_SCRIPT" ]]; then
    return 1
  fi
  python3 "$INSTALLER_SCRIPT" \
    --repo "$REPO" \
    --ref "$REF" \
    --path "${INSTALL_PATHS[@]}" \
    --dest "$TMP_DIR"
}

fetch_with_git_sparse_checkout() {
  local repo_dir="$TMP_DIR/.repo"
  if ! command -v git >/dev/null 2>&1; then
    echo "ERROR: git command is required for --method git" >&2
    return 1
  fi

  git clone --depth 1 --filter=blob:none --sparse "https://github.com/$REPO.git" "$repo_dir" >/dev/null
  if [[ "$REF" != "main" ]]; then
    git -C "$repo_dir" fetch --depth 1 origin "$REF" >/dev/null
    git -C "$repo_dir" checkout FETCH_HEAD >/dev/null
  fi
  git -C "$repo_dir" sparse-checkout set "${INSTALL_PATHS[@]}" >/dev/null

  for skill in "${SKILLS[@]}"; do
    if [[ ! -d "$repo_dir/github/skills/$skill" ]]; then
      echo "ERROR: Skill path not found in repo: github/skills/$skill" >&2
      return 1
    fi
    cp -R "$repo_dir/github/skills/$skill" "$TMP_DIR/$skill"
  done
}

if [[ "$METHOD" == "installer" ]]; then
  if ! fetch_with_installer; then
    echo "ERROR: --method installer was requested but installer script is unavailable or failed." >&2
    echo "Expected: $INSTALLER_SCRIPT" >&2
    exit 1
  fi
elif [[ "$METHOD" == "git" ]]; then
  fetch_with_git_sparse_checkout
else
  if ! fetch_with_installer; then
    echo "INFO: installer method unavailable/failed. Falling back to git sparse-checkout." >&2
    fetch_with_git_sparse_checkout
  fi
fi

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
