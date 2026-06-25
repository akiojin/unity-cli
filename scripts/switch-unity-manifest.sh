#!/usr/bin/env bash
set -euo pipefail

mode="${1:-}"
if [[ -z "$mode" ]]; then
  echo "usage: $0 <2022|6>" >&2
  exit 1
fi

root="UnityCliBridge/Packages"
case "$mode" in
  2022|2022.3|unity2022)
    src_manifest="$root/manifest.unity2022.json"
    src_lock="$root/packages-lock.unity2022.json"
    ;;
  6|6000|unity6)
    src_manifest="$root/manifest.unity6.json"
    src_lock="$root/packages-lock.unity6.json"
    ;;
  *)
    echo "unknown mode: $mode (use 2022 or 6)" >&2
    exit 1
    ;;
esac

if [[ ! -f "$src_manifest" || ! -f "$src_lock" ]]; then
  echo "missing manifest files for mode $mode" >&2
  exit 1
fi

cp "$src_manifest" "$root/manifest.json"
cp "$src_lock" "$root/packages-lock.json"

echo "switched Unity manifest to $mode"
