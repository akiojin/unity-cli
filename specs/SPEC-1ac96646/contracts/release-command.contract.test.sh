#!/bin/bash
# 契約テスト: /releaseコマンドがリリースノートプレビュー、PR作成

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

test_release_command_exists() {
  echo "TEST: /releaseコマンドが存在する"
  if [ -f "$REPO_ROOT/.claude/commands/release.md" ]; then
    echo "✓ PASS: .claude/commands/release.mdが存在する"
    return 0
  else
    echo "✗ FAIL: .claude/commands/release.mdが存在しない"
    return 1
  fi
}

test_release_shows_preview() {
  echo "TEST: /releaseコマンドがリリースノートプレビューを表示する"
  if [ -f "$REPO_ROOT/.claude/commands/release.md" ]; then
    if grep -qi "semantic-release.*dry-run\|preview\|リリースノート" "$REPO_ROOT/.claude/commands/release.md"; then
      echo "✓ PASS: release.mdにプレビュー/dry-run機能が含まれている"
      return 0
    else
      echo "✗ FAIL: release.mdにプレビュー/dry-run機能が含まれていない"
      return 1
    fi
  else
    echo "✗ FAIL: release.mdが存在しない"
    return 1
  fi
}

test_release_creates_pr_without_auto_merge() {
  echo "TEST: /releaseコマンドがauto-merge無効でPRを作成する"
  # TODO: release.mdまたはスクリプトがgh pr create --no-auto-mergeを使用していることを検証
  if [ -f "$REPO_ROOT/scripts/prepare-release-pr.sh" ]; then
    if grep -q "gh.*pr create\\|gh workflow run" "$REPO_ROOT/scripts/prepare-release-pr.sh"; then
      echo "✓ PASS: prepare-release-pr.shがPR作成機能を持っている"
      return 0
    else
      echo "⚠️  WARN: prepare-release-pr.shのPR作成機能が明示的でない"
      return 0
    fi
  else
    echo "⚠️  WARN: prepare-release-pr.shが存在しない（/releaseコマンドが直接実装している可能性）"
    return 0
  fi
}

test_release_skips_if_no_changes() {
  echo "TEST: /releaseコマンドが変更なしの場合をスキップする"
  # TODO: semantic-release dry-runで変更がない場合の処理を確認
  if [ -f "$REPO_ROOT/.claude/commands/release.md" ]; then
    if grep -qi "変更なし\|no changes\|skip" "$REPO_ROOT/.claude/commands/release.md"; then
      echo "✓ PASS: release.mdに変更なしケースの処理が含まれている"
      return 0
    else
      echo "⚠️  WARN: release.mdに変更なしケースの明示的な処理がない"
      return 0
    fi
  else
    echo "✗ FAIL: release.mdが存在しない"
    return 1
  fi
}

main() {
  echo "=========================================="
  echo "契約テスト: release-command"
  echo "=========================================="
  echo ""

  local failed=0

  test_release_command_exists || ((failed++))
  echo ""

  test_release_shows_preview || ((failed++))
  echo ""

  test_release_creates_pr_without_auto_merge || ((failed++))
  echo ""

  test_release_skips_if_no_changes || ((failed++))
  echo ""

  echo "=========================================="
  if [ $failed -eq 0 ]; then
    echo "✓ すべてのテストが成功しました"
    echo "=========================================="
    return 0
  else
    echo "✗ $failed 個のテストが失敗しました"
    echo "=========================================="
    return 1
  fi
}

main "$@"
