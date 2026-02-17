#!/usr/bin/env bash
set -euo pipefail
# デバッグ: 環境変数で有効化（例: PUBLISH_DEBUG=1）
[ "${PUBLISH_DEBUG:-0}" = "1" ] && set -x

# publish.sh <major|minor|patch> [--tags-only|--no-push] [--remote <name>]
# 単一入口で以下を実施:
# 1) バージョン更新（CLI/LSP/Unity の全て）
# 2) タグ付けとコミット＆プッシュ
# 期待動作（CI）:
#  - Release binary（各OS向けビルド）

usage() { echo "Usage: $0 <major|minor|patch> [--tags-only|--no-push] [--remote <name>]"; exit 1; }

LEVEL=${1-}
[[ "$LEVEL" =~ ^(major|minor|patch)$ ]] || usage
shift || true

# push 動作: all(既定)/tags/none
PUSH_MODE=${PUBLISH_PUSH:-all}

# オプション解析
while [ $# -gt 0 ]; do
  case "$1" in
    --tags-only)
      PUSH_MODE=tags
      ;;
    --no-push)
      PUSH_MODE=none
      ;;
    --remote)
      shift
      [ $# -gt 0 ] || { echo "[error] --remote requires a value" >&2; exit 1; }
      REMOTE="$1"
      ;;
    *)
      echo "[warn] unknown option: $1" >&2
      ;;
  esac
  shift || true
done

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
REMOTE=${REMOTE:-origin}
cd "$ROOT_DIR"

if ! command -v node >/dev/null 2>&1; then
  echo "[error] node not found" >&2
  exit 2
fi

# ──────────────────────────────────────────────
# Pre-publish validation
# ──────────────────────────────────────────────

echo "[step] pre-publish validation"

# Clean working tree check
if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "[error] Git working tree is not clean. Commit or stash changes before releasing." >&2
  exit 1
fi

if [ -n "$(git ls-files --others --exclude-standard)" ]; then
  echo "[error] Untracked files detected. Commit or remove them before releasing." >&2
  exit 1
fi

# 事前情報
CUR_VER=$(node -p "require('./package.json').version")
echo "[info] current version: $CUR_VER"

# npm version を実行（以降の同期は本スクリプトで行う）
echo "[step] bump unity-cli version ($LEVEL)"
npm version "$LEVEL" -m "chore(release): v%s" >/dev/null

NEW_VER=$(node -p "require('./package.json').version")
TAG="v$NEW_VER"
echo "[info] new version: $NEW_VER (tag: $TAG)"

# ──────────────────────────────────────────────
# Version sync across all packages
# ──────────────────────────────────────────────

# Unity パッケージの version を同期
echo "[step] sync Unity UPM package version -> $NEW_VER"
node -e '
  const fs=require("fs");
  const p="UnityCliBridge/Packages/unity-cli-bridge/package.json";
  if (!fs.existsSync(p)) process.exit(0);
  const json=JSON.parse(fs.readFileSync(p,"utf8"));
  json.version=process.argv[1];
  fs.writeFileSync(p, JSON.stringify(json,null,2)+"\n", "utf8");
' "$NEW_VER"

# LSP 側の Directory.Build.props を同期
sync_props() {
  local file="$1"; local ver="$2"
  [ -f "$file" ] || return 0
  echo "[step] sync props: $file -> $ver"
  # 既存タグを書き換え（存在しなければ追加）
  if grep -q "<Version>" "$file"; then
    sed -i.bak -E "s|<Version>[^<]*</Version>|<Version>${ver}</Version>|" "$file"
  else
    sed -i.bak -E "s|<PropertyGroup>|<PropertyGroup>\n    <Version>${ver}</Version>|" "$file"
  fi
  if grep -q "<AssemblyVersion>" "$file"; then
    sed -i.bak -E "s|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>${ver}.0</AssemblyVersion>|" "$file"
  else
    sed -i.bak -E "s|<PropertyGroup>|<PropertyGroup>\n    <AssemblyVersion>${ver}.0</AssemblyVersion>|" "$file"
  fi
  if grep -q "<FileVersion>" "$file"; then
    sed -i.bak -E "s|<FileVersion>[^<]*</FileVersion>|<FileVersion>${ver}.0</FileVersion>|" "$file"
  else
    sed -i.bak -E "s|<PropertyGroup>|<PropertyGroup>\n    <FileVersion>${ver}.0</FileVersion>|" "$file"
  fi
  if grep -q "<AssemblyInformationalVersion>" "$file"; then
    sed -i.bak -E "s|<AssemblyInformationalVersion>[^<]*</AssemblyInformationalVersion>|<AssemblyInformationalVersion>${ver}</AssemblyInformationalVersion>|" "$file"
  else
    sed -i.bak -E "s|<PropertyGroup>|<PropertyGroup>\n    <AssemblyInformationalVersion>${ver}</AssemblyInformationalVersion>|" "$file"
  fi
  rm -f "$file.bak"
}

sync_props "lsp/Directory.Build.props" "$NEW_VER"

# ──────────────────────────────────────────────
# Run tests before publishing
# ──────────────────────────────────────────────

echo "[step] running cargo test..."
cargo test || { echo "[error] cargo test failed. Fix test failures before releasing." >&2; exit 1; }

echo "[step] running dotnet test lsp/Server.Tests.csproj..."
dotnet test lsp/Server.Tests.csproj || { echo "[error] dotnet test failed. Fix test failures before releasing." >&2; exit 1; }

echo "[step] running cargo publish --dry-run..."
cargo publish --dry-run || { echo "[error] cargo publish --dry-run failed. Fix packaging issues before releasing." >&2; exit 1; }

# ──────────────────────────────────────────────
# Commit and tag
# ──────────────────────────────────────────────

# 変更ファイルをコミット（npmが自動コミットしない場合の保険）
git add package.json \
        UnityCliBridge/Packages/unity-cli-bridge/package.json \
        lsp/Directory.Build.props 2>/dev/null || true
if ! git diff --cached --quiet; then
  git commit -m "chore(release): $TAG - version sync (CLI/LSP/Unity)"
fi

# タグ作成（存在しない場合）
if git rev-parse -q --verify "$TAG" >/dev/null; then
  echo "[info] tag exists: $TAG"
else
  git tag -a "$TAG" -m "$TAG"
fi

# ──────────────────────────────────────────────
# Push
# ──────────────────────────────────────────────

# リモート接続確認
if ! git ls-remote --exit-code "$REMOTE" >/dev/null 2>&1; then
  echo "[error] remote not accessible: $REMOTE" >&2
  exit 2
fi

case "$PUSH_MODE" in
  all)
    # プッシュ（本体＋タグ）: follow-tags で関連タグも送信、その後明示的にタグ送信
    echo "[step] push commits and tag (mode=all)"
    git push --follow-tags "$REMOTE" || echo "[warn] git push --follow-tags failed; will try explicit tag push"
    git push "$REMOTE" "$TAG" || true
    ;;
  tags)
    echo "[step] push tag only (mode=tags)"
    git push "$REMOTE" "$TAG" || true
    ;;
  none)
    echo "[step] skip push (mode=none)"
    ;;
  *)
    echo "[error] unknown PUSH_MODE: $PUSH_MODE" >&2
    exit 2
    ;;
esac

# タグがリモートに存在するか検証し、必要に応じて再試行
echo "[step] verify tag on remote: $TAG"
if [ "$PUSH_MODE" = "none" ]; then
  echo "[skip] verification skipped (no push)"
elif git ls-remote --tags "$REMOTE" | awk '{print $2}' | grep -qx "refs/tags/$TAG"; then
  echo "[ok] tag exists on remote: $TAG"
else
  echo "[warn] tag not found on remote; retrying explicit push"
  for i in 1 2 3; do
    sleep $((i*2))
    git push "$REMOTE" "$TAG" && break || true
  done
  if git ls-remote --tags "$REMOTE" | awk '{print $2}' | grep -qx "refs/tags/$TAG"; then
    echo "[ok] tag exists on remote after retry: $TAG"
  else
    echo "[error] failed to push tag $TAG to $REMOTE" >&2
    exit 3
  fi
fi

echo "[done] v$NEW_VER pushed. Check GitHub Actions: unity-cli-release"
echo "- Release URL (runs): https://github.com/akiojin/unity-cli/actions/workflows/unity-cli-release.yml"
