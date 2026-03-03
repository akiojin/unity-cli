#!/bin/bash
set -euo pipefail

echo "==========================================="
echo "Prepare Release (develop -> main)"
echo "==========================================="
echo ""

# GitHub CLIが認証済みか確認
if ! gh auth status >/dev/null 2>&1; then
  echo "Error: GitHub CLI is not authenticated"
  echo "Please run: gh auth login"
  exit 1
fi

echo "[1/2] Triggering prepare-release workflow (develop -> main PR)..."

# Always trigger the workflow from main so the latest definition is used
gh workflow run prepare-release.yml --ref main

echo "✓ Workflow triggered successfully"
echo ""

echo "[2/2] Monitoring workflow..."
echo ""
sleep 5
echo "Latest workflow runs:"
gh run list --workflow=prepare-release.yml --limit 3

echo ""
echo "==========================================="
echo "✓ develop -> main PR creation requested"
echo "==========================================="
echo ""
echo "To monitor progress, run:"
echo "  gh run watch \$(gh run list --workflow=prepare-release.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
echo ""
echo "Or visit:"
echo "  https://github.com/$(gh repo view --json nameWithOwner --jq .nameWithOwner)/actions/workflows/prepare-release.yml"
