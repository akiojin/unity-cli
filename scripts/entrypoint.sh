#!/bin/bash
set -e

# Normalize line endings for Windows-mounted files (best-effort)
if command -v dos2unix >/dev/null 2>&1; then
for f in /unity-cli/scripts/*.sh; do
    [ -f "$f" ] && dos2unix "$f" >/dev/null 2>&1 || true
  done
fi

# Git設定（node:22-bookwormにはGitが含まれている）
# グローバルGit設定（安全なディレクトリを追加）
git config --global --add safe.directory /unity-cli

# ユーザー名とメールの設定（環境変数から）
if [ -n "${GITHUB_USERNAME:-}" ]; then
    git config --global user.name "$GITHUB_USERNAME"
fi

if [ -n "${GIT_USER_EMAIL:-}" ]; then
    git config --global user.email "$GIT_USER_EMAIL"
fi

# Git認証ファイルを環境変数から作成
if [ -n "${GITHUB_USERNAME:-}" ] && [ -n "${GITHUB_PERSONAL_ACCESS_TOKEN:-}" ]; then
    echo "https://${GITHUB_USERNAME}:${GITHUB_PERSONAL_ACCESS_TOKEN}@github.com" > /root/.git-credentials
    chmod 600 /root/.git-credentials
    git config --global credential.helper store
fi

# GitHub CLI 認証（GITHUB_TOKEN が設定されている場合）
if [ -n "${GITHUB_TOKEN:-}" ]; then
    echo "$GITHUB_TOKEN" | gh auth login --with-token 2>/dev/null && \
        gh auth setup-git && \
        echo "✅ GitHub CLI authenticated via GITHUB_TOKEN" || \
        echo "⚠️  GitHub CLI authentication failed"
fi

# .codexディレクトリのセットアップ
# auth.jsonをホストと同期（クロスプラットフォーム対応）
if [ -f /root/.codex-host/auth.json ]; then
    # auth.jsonが誤ってディレクトリとして作成されている場合は削除
    if [ -d /root/.codex/auth.json ]; then
        echo "⚠️  Removing incorrectly created auth.json directory"
        rm -rf /root/.codex/auth.json
    fi

    # ホストのauth.jsonが存在しない、または空、またはホスト側が新しい場合はコピー
    if [ ! -f /root/.codex/auth.json ] || [ ! -s /root/.codex/auth.json ] || [ /root/.codex-host/auth.json -nt /root/.codex/auth.json ]; then
        cp /root/.codex-host/auth.json /root/.codex/auth.json
        chmod 600 /root/.codex/auth.json
        echo "✅ Codex auth.json synced from host"
    else
        echo "✅ Codex auth.json is up to date"
    fi
else
    echo "ℹ️  INFO: Codex auth.json not found on host (optional)"
fi

echo "🚀 Docker environment is ready!"
echo ""

exec "$@"
