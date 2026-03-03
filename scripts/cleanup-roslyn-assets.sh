#!/usr/bin/env bash
set -euo pipefail

# Remove legacy roslyn-cli assets from GitHub Releases without deleting the releases themselves.
# Requirements:
#  - gh CLI authenticated (GH_TOKEN or gh auth login)
#  - jq available
#
# Usage:
#  REPO="owner/repo" DRY_RUN=1 ./scripts/cleanup-roslyn-assets.sh
#  REPO="owner/repo" ./scripts/cleanup-roslyn-assets.sh

REPO="${REPO:-${GITHUB_REPOSITORY:-}}"
DRY_RUN="${DRY_RUN:-1}"
MATCH_RE='roslyn-cli|roslyncli|roslyn_cli'

if [[ -z "$REPO" ]]; then
  echo "REPO is required (e.g., owner/repo)" >&2
  exit 1
fi

echo "[cleanup] Target repository: $REPO"
echo "[cleanup] Dry run: $DRY_RUN (1=yes, 0=no)"

mapfile -t TAGS < <(gh api -H "Accept: application/vnd.github+json" \
  repos/$REPO/releases --paginate \
  --jq '.[] | select(.assets[]? != null) | .tag_name' | sort -u)

total_removed=0
for TAG in "${TAGS[@]}"; do
  json=$(gh api -H "Accept: application/vnd.github+json" repos/$REPO/releases/tags/$TAG)
  # shellcheck disable=SC2207
  asset_ids=($(echo "$json" | jq -r \
    --arg re "$MATCH_RE" '.assets[]? | select(.name | test($re; "i")) | .id'))
  # shellcheck disable=SC2207
  asset_names=($(echo "$json" | jq -r \
    --arg re "$MATCH_RE" '.assets[]? | select(.name | test($re; "i")) | .name'))

  if [[ ${#asset_ids[@]} -eq 0 ]]; then
    continue
  fi

  echo "[cleanup] Tag: $TAG — matched ${#asset_ids[@]} asset(s)"
  for i in "${!asset_ids[@]}"; do
    id="${asset_ids[$i]}"
    name="${asset_names[$i]}"
    echo "  - $name (asset id=$id)"
    if [[ "$DRY_RUN" != "1" ]]; then
      gh api -X DELETE -H "Accept: application/vnd.github+json" \
        repos/$REPO/releases/assets/$id || true
      (( total_removed++ ))
    fi
  done
done

if [[ "$DRY_RUN" == "1" ]]; then
  echo "[cleanup] Dry-run complete. Set DRY_RUN=0 to actually remove assets."
else
  echo "[cleanup] Removed $total_removed asset(s)."
fi

