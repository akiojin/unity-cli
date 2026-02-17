#!/usr/bin/env bash

set -euo pipefail

REPO_URL_DEFAULT="https://github.com/github/spec-kit.git"

usage() {
  cat <<'EOF'
Usage: upgrade-speckit.sh --tag <vX.Y.Z> [--repo <url>] [--output <dir>] [--force]

GitHub Spec Kit（Speckit）の上流スナップショット（比較用）を取得します。
このスクリプトは既存の `.specify/` を上書きしません。

Options:
  --tag <tag>       取得する上流タグ（例: v0.0.90）
  --repo <url>      上流リポジトリURL（既定: https://github.com/github/spec-kit.git）
  --output <dir>    出力先（既定: <repo>/.specify/upstream/spec-kit-<tag>/）
  --force           出力先が存在する場合に削除して作り直す
  --help, -h        ヘルプを表示

Examples:
  bash scripts/upgrade-speckit.sh --tag v0.0.90
  bash scripts/upgrade-speckit.sh --tag v0.0.90 --force
EOF
}

TAG=""
REPO_URL="$REPO_URL_DEFAULT"
OUTPUT_DIR=""
FORCE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag)
      TAG="${2:-}"
      shift 2
      ;;
    --repo)
      REPO_URL="${2:-}"
      shift 2
      ;;
    --output)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    --force)
      FORCE=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: 不明な引数です: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$TAG" ]]; then
  echo "ERROR: --tag は必須です。" >&2
  usage >&2
  exit 1
fi

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

if git rev-parse --show-toplevel >/dev/null 2>&1; then
  REPO_ROOT="$(git rev-parse --show-toplevel)"
else
  REPO_ROOT="$(find_repo_root "$SCRIPT_DIR")"
fi

if [[ -z "${REPO_ROOT:-}" ]]; then
  echo "ERROR: リポジトリルートを特定できませんでした。リポジトリ内で実行してください。" >&2
  exit 1
fi

SAFE_TAG="$(echo "$TAG" | tr '/\\' '__')"

if [[ -z "$OUTPUT_DIR" ]]; then
  OUTPUT_DIR="$REPO_ROOT/.specify/upstream/spec-kit-$SAFE_TAG"
fi

case "$OUTPUT_DIR" in
  "$REPO_ROOT/.specify/upstream/"*) ;;
  *)
    echo "ERROR: --output は '$REPO_ROOT/.specify/upstream/' 配下を指定してください: $OUTPUT_DIR" >&2
    exit 1
    ;;
esac

if [[ ! -d "$REPO_ROOT/.specify" ]]; then
  echo "ERROR: .specify/ が見つかりません。Speckitが組み込まれていない可能性があります: $REPO_ROOT" >&2
  exit 1
fi

if [[ -e "$OUTPUT_DIR" ]]; then
  if [[ "$FORCE" != "true" ]]; then
    echo "ERROR: 出力先が既に存在します: $OUTPUT_DIR" >&2
    echo "削除して作り直す場合は --force を指定してください。" >&2
    exit 1
  fi
  rm -rf "$OUTPUT_DIR"
fi

tmp_root="$(mktemp -d)"
cleanup() { rm -rf "$tmp_root"; }
trap cleanup EXIT INT TERM

echo "[speckit] 上流を取得します: $REPO_URL ($TAG)"
GIT_TERMINAL_PROMPT=0 git -c advice.detachedHead=false clone -q --depth 1 --branch "$TAG" --single-branch "$REPO_URL" "$tmp_root/spec-kit"

src="$tmp_root/spec-kit"
if [[ ! -d "$src/templates" || ! -d "$src/scripts" ]]; then
  echo "ERROR: 上流に templates/ または scripts/ が見つかりませんでした: $src" >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
cp -a "$src/templates" "$OUTPUT_DIR/"
cp -a "$src/scripts" "$OUTPUT_DIR/"

upstream_commit="$(git -C "$tmp_root/spec-kit" rev-parse HEAD 2>/dev/null || true)"

cat >"$OUTPUT_DIR/UPSTREAM.md" <<EOF
# Upstream snapshot

- Repo: $REPO_URL
- Tag: $TAG
- Commit: ${upstream_commit:-unknown}
- GeneratedAt: $(date -u +"%Y-%m-%dT%H:%M:%SZ")

このディレクトリは比較用のスナップショットです（コミットしない想定）。
EOF

echo "[speckit] スナップショットを作成しました: $OUTPUT_DIR"
echo "[speckit] 例: git diff --no-index .specify/templates \"$OUTPUT_DIR/templates\""
