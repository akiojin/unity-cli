#!/usr/bin/env bash

set -euo pipefail

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

find_repo_root() {
  local dir="$1"
  while [[ "$dir" != "/" ]]; do
    if [[ -d "$dir/.git" || -d "$dir/.specify" ]]; then
      echo "$dir"
      return 0
    fi
    dir="$(dirname "$dir")"
  done
  return 1
}

SCRIPT_DIR="$(CDPATH="" cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(find_repo_root "$SCRIPT_DIR" || true)"
[[ -n "${REPO_ROOT:-}" ]] || fail "リポジトリルートを特定できませんでした。リポジトリ内で実行してください。"

search() {
  local pattern="$1"
  shift || true

  if command -v rg >/dev/null 2>&1; then
    rg -n "$pattern" "$@"
    return $?
  fi

  # grep fallback (for environments without ripgrep)
  grep -R -n -E "$pattern" "$@"
}

SPECKIT_COMMANDS=(
  analyze
  checklist
  clarify
  constitution
  implement
  plan
  specify
  tasks
  taskstoissues
)

CLAUDE_SPECKIT_FILES=( "$REPO_ROOT/.claude/commands/speckit."*.md )

for cmd in "${SPECKIT_COMMANDS[@]}"; do
  tpl="$REPO_ROOT/.specify/templates/commands/$cmd.md"
  cla="$REPO_ROOT/.claude/commands/speckit.$cmd.md"

  [[ -f "$tpl" ]] || fail "テンプレートが見つかりません: $tpl"
  [[ -f "$cla" ]] || fail "Claudeコマンドが見つかりません: $cla"
done

# specs/README.md は使わない（specs/specs.md を正とする）
if search 'specs/README\.md' \
  "${CLAUDE_SPECKIT_FILES[@]}" \
  "$REPO_ROOT/.specify/templates/commands" \
  "$REPO_ROOT/.github/workflows/specs-readme.yml" >/dev/null 2>&1; then
  search 'specs/README\.md' \
    "${CLAUDE_SPECKIT_FILES[@]}" \
    "$REPO_ROOT/.specify/templates/commands" \
    "$REPO_ROOT/.github/workflows/specs-readme.yml" || true
  fail "specs/README.md への参照が残っています（specs/specs.md に統一してください）"
fi

if ! search 'specs/specs\.md' "$REPO_ROOT/.github/workflows/specs-readme.yml" >/dev/null 2>&1; then
  fail ".github/workflows/specs-readme.yml が specs/specs.md を検証していません"
fi

# Speckitはブランチ/チェックアウトをしない（ドキュメント内にも残さない）
branch_ops_pattern='git[[:space:]]+(checkout|switch)([[:space:]]|$)|checkout[[:space:]]+-b|switch[[:space:]]+-c'
if search "$branch_ops_pattern" \
  "${CLAUDE_SPECKIT_FILES[@]}" \
  "$REPO_ROOT/.specify/templates/commands" >/dev/null 2>&1; then
  search "$branch_ops_pattern" \
    "${CLAUDE_SPECKIT_FILES[@]}" \
    "$REPO_ROOT/.specify/templates/commands" || true
  fail "Speckit関連ドキュメントにブランチ/チェックアウト操作が含まれています（ブランチレス運用の不変条件違反）"
fi

echo "[specify] OK: speckit command invariants"
