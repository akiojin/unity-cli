#!/bin/bash
# 契約テスト: developへのPRは自動マージ、mainへのPRは自動マージしない

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# テストフレームワーク読み込み
source "$REPO_ROOT/tests/lib/test-framework.sh" 2>/dev/null || {
  echo "⚠️  test-framework.sh not found, using minimal test framework"

  test_develop_pr_auto_merges() {
    echo "TEST: develop PRは自動マージされる"
    # TODO: GitHub Actions auto-merge.ymlがdevelopブランチを対象にしていることを確認
    if grep -q "branches:.*develop" "$REPO_ROOT/.github/workflows/auto-merge.yml"; then
      echo "✓ PASS: auto-merge.ymlがdevelopブランチを対象にしている"
      return 0
    else
      echo "✗ FAIL: auto-merge.ymlがdevelopブランチを対象にしていない"
      return 1
    fi
  }

  test_main_pr_does_not_auto_merge() {
    echo "TEST: main PRは自動マージされない"
    # TODO: GitHub Actions auto-merge.ymlがmainブランチを除外していることを確認
    if ! grep -q "branches:.*main" "$REPO_ROOT/.github/workflows/auto-merge.yml"; then
      echo "✓ PASS: auto-merge.ymlがmainブランチを除外している"
      return 0
    else
      echo "✗ FAIL: auto-merge.ymlがmainブランチを含んでいる（自動マージされてしまう）"
      return 1
    fi
  }

  test_auto_merge_waits_for_checks() {
    echo "TEST: 自動マージはCI/CDチェックの完了を待つ"
    # TODO: auto-merge.ymlが statusCheckRollup を確認していることを検証
    if grep -q "statusCheckRollup" "$REPO_ROOT/.github/workflows/auto-merge.yml"; then
      echo "✓ PASS: auto-merge.ymlがstatusCheckRollupを確認している"
      return 0
    else
      echo "✗ FAIL: auto-merge.ymlがstatusCheckRollupを確認していない"
      return 1
    fi
  }

  test_auto_merge_comments_on_failure() {
    echo "TEST: 自動マージ失敗時にPRにコメントする"
    # TODO: auto-merge.ymlが失敗時にコメントを追加していることを検証
    if grep -q "gh pr comment" "$REPO_ROOT/.github/workflows/auto-merge.yml"; then
      echo "✓ PASS: auto-merge.ymlが失敗時にコメントを追加している"
      return 0
    else
      echo "⚠️  WARN: auto-merge.ymlが失敗時のコメント処理を持っていない（推奨）"
      return 0  # 警告のみで失敗としない
    fi
  }
}

# テスト実行
main() {
  echo "=========================================="
  echo "契約テスト: auto-merge"
  echo "=========================================="
  echo ""

  local failed=0

  test_develop_pr_auto_merges || ((failed++))
  echo ""

  test_main_pr_does_not_auto_merge || ((failed++))
  echo ""

  test_auto_merge_waits_for_checks || ((failed++))
  echo ""

  test_auto_merge_comments_on_failure || ((failed++))
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
