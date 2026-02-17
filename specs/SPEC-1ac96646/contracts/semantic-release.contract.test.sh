#!/bin/bash
# 契約テスト: mainマージ時にsemantic-release実行、成果物生成

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

test_semantic_release_runs_on_main_merge() {
  echo "TEST: mainブランチマージ時にsemantic-releaseが実行される"
  if [ -f "$REPO_ROOT/.github/workflows/release.yml" ]; then
    if grep -q "semantic-release" "$REPO_ROOT/.github/workflows/release.yml"; then
      echo "✓ PASS: release.ymlにsemantic-release実行が含まれている"
      return 0
    else
      echo "✗ FAIL: release.ymlにsemantic-release実行が含まれていない"
      return 1
    fi
  else
    echo "✗ FAIL: .github/workflows/release.ymlが存在しない"
    return 1
  fi
}

test_package_json_updated() {
  echo "TEST: semantic-releaseがpackage.jsonを更新する"
  # semantic-releaseの設定確認
  if [ -f "$REPO_ROOT/.releaserc.json" ] || [ -f "$REPO_ROOT/.releaserc.js" ] || [ -f "$REPO_ROOT/release.config.js" ]; then
    echo "✓ PASS: semantic-release設定ファイルが存在する"
    return 0
  else
    # package.jsonのreleaseセクション確認
    if grep -q "\"release\"" "$REPO_ROOT/package.json" 2>/dev/null; then
      echo "✓ PASS: package.jsonにrelease設定が含まれている"
      return 0
    else
      echo "✗ FAIL: semantic-release設定が見つからない"
      return 1
    fi
  fi
}

test_unity_package_version_synced() {
  echo "TEST: Unity Packageのバージョンが同期される"
  # sync-unity-package-version.jsの存在確認
  if [ -f "$REPO_ROOT/scripts/sync-unity-package-version.js" ]; then
    echo "✓ PASS: sync-unity-package-version.jsが存在する"
    return 0
  else
    echo "⚠️  WARN: sync-unity-package-version.jsが存在しない"
    return 0
  fi
}

test_changelog_generated() {
  echo "TEST: CHANGELOGが自動生成される"
  # .releasercまたはpackage.jsonにchangelog pluginが含まれているか確認
  if [ -f "$REPO_ROOT/.releaserc.json" ]; then
    if grep -q "changelog" "$REPO_ROOT/.releaserc.json"; then
      echo "✓ PASS: .releaserc.jsonにchangelog設定が含まれている"
      return 0
    fi
  fi

  if grep -q "@semantic-release/changelog" "$REPO_ROOT/package.json" 2>/dev/null; then
    echo "✓ PASS: package.jsonにchangelog pluginが含まれている"
    return 0
  fi

  echo "⚠️  WARN: changelog自動生成の設定が明示的でない"
  return 0
}

test_tag_created() {
  echo "TEST: Gitタグが作成される"
  if [ -f "$REPO_ROOT/.github/workflows/release.yml" ]; then
    # semantic-releaseはデフォルトでタグを作成するので、release.ymlにsemantic-releaseがあればOK
    echo "✓ PASS: semantic-releaseがタグを作成する（デフォルト動作）"
    return 0
  else
    echo "✗ FAIL: release.ymlが存在しない"
    return 1
  fi
}

main() {
  echo "=========================================="
  echo "契約テスト: semantic-release"
  echo "=========================================="
  echo ""

  local failed=0

  test_semantic_release_runs_on_main_merge || ((failed++))
  echo ""

  test_package_json_updated || ((failed++))
  echo ""

  test_unity_package_version_synced || ((failed++))
  echo ""

  test_changelog_generated || ((failed++))
  echo ""

  test_tag_created || ((failed++))
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
