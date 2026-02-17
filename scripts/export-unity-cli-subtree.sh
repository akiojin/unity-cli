#!/usr/bin/env bash
set -euo pipefail

cat <<'MSG'
This repository is already the standalone `unity-cli` repository.
Subtree export is no longer required.

Use regular Git push instead:
  git push origin <branch>
MSG
