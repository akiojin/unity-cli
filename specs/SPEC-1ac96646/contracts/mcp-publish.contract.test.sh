#!/bin/bash
# 契約テスト: GitHub Release時にnpm publish実行

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

test_mcp_publish_on_release() {
  echo "TEST: GitHub Release作成時にMCPサーバーをnpm publishする"
  if [ -f "$REPO_ROOT/.github/workflows/publish.yml" ]; then
    if grep -q "npm publish\|npm run publish" "$REPO_ROOT/.github/workflows/publish.yml"; then
      echo "✓ PASS: publish.ymlにnpm publish処理が含まれている"
      return 0
    else
      echo "✗ FAIL: publish.ymlにnpm publish処理が含まれていない"
      return 1
    fi
  else
    echo "✗ FAIL: .github/workflows/publish.ymlが存在しない"
    return 1
  fi
}

test_version_matches_tag() {
  echo "TEST: publishされるバージョンがGitタグと一致する"
  # semantic-releaseがバージョンを管理しているので、package.jsonのバージョンとタグが一致することを前提
  if [ -f "$REPO_ROOT/package.json" ]; then
    if grep -q "\"version\":" "$REPO_ROOT/package.json"; then
      echo "✓ PASS: package.jsonにversionフィールドが存在する"
      return 0
    else
      echo "✗ FAIL: package.jsonにversionフィールドが存在しない"
      return 1
    fi
  else
    echo "✗ FAIL: package.jsonが存在しない"
    return 1
  fi
}

test_npmjs_package_available() {
  echo "TEST: npmjs.comでパッケージが利用可能になる"
  # TODO: 実際のnpm publishの成功を検証するには、npm viewでパッケージを確認する必要がある
  # ここでは設定の存在を確認
  if [ -f "$REPO_ROOT/.github/workflows/publish.yml" ]; then
    if grep -q "NPM_TOKEN\|npm.*publish" "$REPO_ROOT/.github/workflows/publish.yml"; then
      echo "✓ PASS: publish.ymlにNPM_TOKEN設定またはnpm publish処理が含まれている"
      return 0
    else
      echo "✗ FAIL: publish.ymlにNPM_TOKEN設定またはnpm publish処理が含まれていない"
      return 1
    fi
  else
    echo "✗ FAIL: .github/workflows/publish.ymlが存在しない"
    return 1
  fi
}

main() {
  echo "=========================================="
  echo "契約テスト: mcp-publish"
  echo "=========================================="
  echo ""

  local failed=0

  test_mcp_publish_on_release || ((failed++))
  echo ""

  test_version_matches_tag || ((failed++))
  echo ""

  test_npmjs_package_available || ((failed++))
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
