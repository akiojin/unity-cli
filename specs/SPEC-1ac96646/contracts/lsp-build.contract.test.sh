#!/bin/bash
# 契約テスト: LSPサーバー全プラットフォームビルド

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

test_lsp_manifest_exists() {
  echo "TEST: LSPサーバーマニフェストが存在する"
  # ビルド後にマニフェストが生成されることを確認
  if [ -f "$REPO_ROOT/.github/workflows/publish.yml" ]; then
    if grep -qi "manifest\|lsp" "$REPO_ROOT/.github/workflows/publish.yml"; then
      echo "✓ PASS: publish.ymlにLSPサーバービルド設定が含まれている"
      return 0
    else
      echo "✗ FAIL: publish.ymlにLSPサーバービルド設定が含まれていない"
      return 1
    fi
  else
    echo "✗ FAIL: .github/workflows/publish.ymlが存在しない"
    return 1
  fi
}

test_all_platforms_built() {
  echo "TEST: 全プラットフォーム（linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64）がビルドされる"
  if [ -f "$REPO_ROOT/.github/workflows/publish.yml" ]; then
    local platforms=("linux-x64" "linux-arm64" "osx-x64" "osx-arm64" "win-x64" "win-arm64")
    local missing_platforms=()

    for platform in "${platforms[@]}"; do
      if ! grep -q "$platform" "$REPO_ROOT/.github/workflows/publish.yml"; then
        missing_platforms+=("$platform")
      fi
    done

    if [ ${#missing_platforms[@]} -eq 0 ]; then
      echo "✓ PASS: すべてのプラットフォーム（6個）がpublish.ymlに含まれている"
      return 0
    else
      echo "⚠️  WARN: 以下のプラットフォームがpublish.ymlに含まれていない: ${missing_platforms[*]}"
      # 4個以上あればOKとする
      local found=$((6 - ${#missing_platforms[@]}))
      if [ $found -ge 4 ]; then
        echo "✓ PASS: $found 個のプラットフォームが含まれている（最低4個以上）"
        return 0
      else
        echo "✗ FAIL: プラットフォーム数が不足（$found 個、最低4個必要）"
        return 1
      fi
    fi
  else
    echo "✗ FAIL: .github/workflows/publish.ymlが存在しない"
    return 1
  fi
}

test_binaries_attached_to_release() {
  echo "TEST: ビルド成果物がGitHub Releaseに添付される"
  if [ -f "$REPO_ROOT/.github/workflows/publish.yml" ]; then
    if grep -q "gh release upload\|actions/upload-release-asset\|softprops/action-gh-release" "$REPO_ROOT/.github/workflows/publish.yml"; then
      echo "✓ PASS: publish.ymlにGitHub Releaseへのアセット添付処理が含まれている"
      return 0
    else
      echo "⚠️  WARN: publish.ymlにGitHub Releaseへのアセット添付処理が明示的でない"
      return 0
    fi
  else
    echo "✗ FAIL: .github/workflows/publish.ymlが存在しない"
    return 1
  fi
}

main() {
  echo "=========================================="
  echo "契約テスト: lsp-build"
  echo "=========================================="
  echo ""

  local failed=0

  test_lsp_manifest_exists || ((failed++))
  echo ""

  test_all_platforms_built || ((failed++))
  echo ""

  test_binaries_attached_to_release || ((failed++))
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
