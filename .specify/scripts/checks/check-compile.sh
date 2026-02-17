#!/usr/bin/env bash

# check-compile.sh - Rust/C# コンパイルチェック
#
# 使用方法:
#   check-compile.sh [--rust-only | --csharp-only]

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
  echo "Checking Rust compilation..."
  echo "========================================="
  cargo check
fi

if [[ "$RUST_ONLY" = false ]]; then
  if [[ -f "lsp/Server.csproj" ]]; then
    echo ""
    echo "========================================="
    echo "Checking C# compilation..."
    echo "========================================="
    dotnet build lsp/Server.csproj -c Release --nologo
  else
    echo "⚠️  lsp/Server.csproj not found, skipping C# check"
  fi
fi

echo ""
echo "✅ Compilation checks passed."
