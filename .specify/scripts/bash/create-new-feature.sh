#!/usr/bin/env bash

set -euo pipefail

JSON_MODE=false
ARGS=()

for arg in "$@"; do
    case "$arg" in
        --json)
            JSON_MODE=true
            ;;
        --help|-h)
            cat << 'EOF'
Usage: create-new-feature.sh [--json] <feature_description>

Speckit 用の新規要件（SPEC-xxxxxxxx）を作成します。

Options:
  --json      JSON形式で出力
  --help, -h  ヘルプを表示
EOF
            exit 0
            ;;
        --*)
            echo "ERROR: 不明なオプション '$arg' です。--help を参照してください。" >&2
            exit 1
            ;;
        *)
            ARGS+=("$arg")
            ;;
    esac
done

FEATURE_DESCRIPTION="${ARGS[*]}"
if [[ -z "$FEATURE_DESCRIPTION" ]]; then
    echo "ERROR: 要件の説明が空です。Usage: $0 [--json] <feature_description>" >&2
    exit 1
fi

find_repo_root() {
    local dir="$1"
    while [ "$dir" != "/" ]; do
        if [ -d "$dir/.git" ] || [ -d "$dir/.specify" ]; then
            echo "$dir"
            return 0
        fi
        dir="$(dirname "$dir")"
    done
    return 1
}

generate_spec_id() {
    local uuid=""
    for _ in 1 2 3 4 5; do
        if command -v uuidgen >/dev/null 2>&1; then
            uuid="$(uuidgen 2>/dev/null || true)"
        elif [[ -r /proc/sys/kernel/random/uuid ]]; then
            uuid="$(cat /proc/sys/kernel/random/uuid 2>/dev/null || true)"
        fi

        if [[ -n "$uuid" ]]; then
            local short="${uuid:0:8}"
            short="$(echo "$short" | tr '[:upper:]' '[:lower:]')"
            echo "SPEC-$short"
            return 0
        fi
    done

    if command -v openssl >/dev/null 2>&1; then
        local short
        short="$(openssl rand -hex 4 2>/dev/null | tr '[:upper:]' '[:lower:]' || true)"
        if [[ -n "$short" ]]; then
            echo "SPEC-$short"
            return 0
        fi
    fi

    if [[ -r /dev/urandom ]] && command -v od >/dev/null 2>&1; then
        local short
        short="$(head -c 4 /dev/urandom 2>/dev/null | od -An -tx1 | tr -d ' \n' | tr '[:upper:]' '[:lower:]' || true)"
        if [[ "$short" =~ ^[a-f0-9]{8}$ ]]; then
            echo "SPEC-$short"
            return 0
        fi
    fi

    # Last resort: derive 8 hex chars from epoch seconds (non-random).
    local short
    short="$(printf '%08x' "$(( $(date +%s) & 0xffffffff ))")"
    echo "SPEC-$short"
}

SCRIPT_DIR="$(CDPATH="" cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

HAS_GIT=false
if git rev-parse --show-toplevel >/dev/null 2>&1; then
    REPO_ROOT="$(git rev-parse --show-toplevel)"
    HAS_GIT=true
else
    REPO_ROOT="$(find_repo_root "$SCRIPT_DIR")"
    if [[ -z "${REPO_ROOT:-}" ]]; then
        echo "ERROR: リポジトリルートを特定できませんでした。リポジトリ内で実行してください。" >&2
        exit 1
    fi
fi

cd "$REPO_ROOT"

SPECS_DIR="$REPO_ROOT/specs"
mkdir -p "$SPECS_DIR"

# Generate unique SPEC ID
FEATURE_ID=""
while :; do
    candidate="$(generate_spec_id)"
    if [[ ! -d "$SPECS_DIR/$candidate" ]]; then
        FEATURE_ID="$candidate"
        break
    fi
done

# Create feature directory (no branch/worktree)
FEATURE_DIR="$SPECS_DIR/$FEATURE_ID"
mkdir -p "$FEATURE_DIR/checklists"

# Initialize spec file from template
TEMPLATE="$REPO_ROOT/.specify/templates/spec-template.md"
SPEC_FILE="$FEATURE_DIR/spec.md"
if [[ -f "$TEMPLATE" ]]; then
    cp "$TEMPLATE" "$SPEC_FILE"
else
    : >"$SPEC_FILE"
fi

# Persist current feature selection for subsequent commands (branchless workflow)
mkdir -p "$REPO_ROOT/.specify"
echo "$FEATURE_ID" >"$REPO_ROOT/.specify/current-feature"

# Update specs index
SPECS_README="$SPECS_DIR/specs.md"
if [[ -x "$REPO_ROOT/.specify/scripts/bash/update-specs-readme.sh" ]]; then
    "$REPO_ROOT/.specify/scripts/bash/update-specs-readme.sh" --quiet || true
fi

if [[ "$HAS_GIT" == "true" ]]; then
    echo "[specify] Gitリポジトリを検出しましたが、ブランチは作成しません（設定による）。"
fi
echo "[specify] 要件ID: $FEATURE_ID"
echo "[specify] 説明: $FEATURE_DESCRIPTION"

if [[ "$JSON_MODE" == "true" ]]; then
    printf '{"FEATURE_ID":"%s","SPEC_FILE":"%s","FEATURE_DIR":"%s","SPECS_README":"%s"}\n' \
        "$FEATURE_ID" "$SPEC_FILE" "$FEATURE_DIR" "$SPECS_README"
else
    echo "FEATURE_ID: $FEATURE_ID"
    echo "SPEC_FILE: $SPEC_FILE"
    echo "FEATURE_DIR: $FEATURE_DIR"
    echo "SPECS_README: $SPECS_README"
fi
