#!/usr/bin/env bash

# check-tests.sh - テスト実行チェック
#
# 使用方法:
#   check-tests.sh [--rust-only | --csharp-only]

set -euo pipefail

RUST_ONLY=false
CSHARP_ONLY=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rust-only)
      RUST_ONLY=true
      shift
      ;;
    --csharp-only)
      CSHARP_ONLY=true
      shift
      ;;
    --help|-h)
      echo "Usage: $0 [--rust-only | --csharp-only]"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
if [[ ! -f "$REPO_ROOT/Cargo.toml" && -f "$REPO_ROOT/unity-cli/Cargo.toml" ]]; then
  REPO_ROOT="$REPO_ROOT/unity-cli"
fi
cd "$REPO_ROOT"

if [[ "$CSHARP_ONLY" = false ]]; then
  echo "========================================="
  echo "Running Rust tests..."
  echo "========================================="
  cargo test
fi

if [[ "$RUST_ONLY" = false ]]; then
  if [[ -f "lsp/Server.Tests.csproj" ]]; then
    echo ""
    echo "========================================="
    echo "Running C# LSP tests..."
    echo "========================================="
    dotnet test lsp/Server.Tests.csproj
  else
    echo "⚠️  lsp/Server.Tests.csproj not found, skipping C# tests"
  fi
fi

echo ""
echo "✅ Test checks passed."
