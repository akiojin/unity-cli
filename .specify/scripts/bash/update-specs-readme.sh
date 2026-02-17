#!/usr/bin/env bash

set -euo pipefail

QUIET=false

for arg in "$@"; do
    case "$arg" in
        --quiet)
            QUIET=true
            ;;
        --help|-h)
            cat << 'EOF'
Usage: update-specs-readme.sh [--quiet]

specs/specs.md に要件一覧（SPEC-xxxxxxxx）を自動生成します。

Options:
  --quiet     進捗出力を抑制
  --help, -h  ヘルプを表示
EOF
            exit 0
            ;;
        *)
            echo "ERROR: 不明なオプション '$arg' です。--help を参照してください。" >&2
            exit 1
            ;;
    esac
done

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

SCRIPT_DIR="$(CDPATH="" cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if git rev-parse --show-toplevel >/dev/null 2>&1; then
    REPO_ROOT="$(git rev-parse --show-toplevel)"
else
    REPO_ROOT="$(find_repo_root "$SCRIPT_DIR")"
    if [ -z "${REPO_ROOT:-}" ]; then
        echo "ERROR: リポジトリルートを特定できませんでした。リポジトリ内で実行してください。" >&2
        exit 1
    fi
fi

SPECS_DIR="$REPO_ROOT/specs"
OUTPUT_FILE="$SPECS_DIR/specs.md"

mkdir -p "$SPECS_DIR"

tmp_file="$(mktemp)"
trap 'rm -f "$tmp_file"' EXIT INT TERM

{
    echo "# 要件一覧"
    echo ""
    echo 'このファイルは `.specify/scripts/bash/update-specs-readme.sh` または `.specify/scripts/powershell/create-new-feature.ps1` により自動生成されます。'
    echo '手動編集は避けてください（再生成で上書きされます）。'
    echo ""
    echo "## ステータス別一覧"
    echo ""
} >"$tmp_file"

spec_ids=()
while IFS= read -r id; do
    [[ -n "$id" ]] || continue
    spec_ids+=("$id")
done < <(find "$SPECS_DIR" -maxdepth 1 -mindepth 1 -type d -name 'SPEC-*' -exec basename {} \; 2>/dev/null | grep -E '^SPEC-[a-z0-9]{8}$' | sort)

GROUP_ORDER=()
GROUP_ROWS=()
GROUP_COUNTS=()

get_group_index() {
    local key="$1"
    local i=0
    for existing in "${GROUP_ORDER[@]+"${GROUP_ORDER[@]}"}"; do
        if [[ "$existing" == "$key" ]]; then
            echo "$i"
            return 0
        fi
        i=$((i + 1))
    done
    echo "-1"
}

register_group() {
    local key="$1"
    local idx
    idx="$(get_group_index "$key")"
    if [[ "$idx" != "-1" ]]; then
        return 0
    fi

    GROUP_ORDER+=("$key")
    GROUP_ROWS+=("")
    GROUP_COUNTS+=(0)
}

append_group_row() {
    local key="$1"
    local row="$2"
    local idx
    idx="$(get_group_index "$key")"
    if [[ "$idx" == "-1" ]]; then
        register_group "$key"
        idx="$((${#GROUP_ORDER[@]} - 1))"
    fi

    local current="${GROUP_ROWS[$idx]}"
    if [[ -n "$current" ]]; then
        GROUP_ROWS[$idx]="${current}"$'\n'"$row"
    else
        GROUP_ROWS[$idx]="$row"
    fi
    GROUP_COUNTS[$idx]=$(( ${GROUP_COUNTS[$idx]} + 1 ))
}

if [[ ${#spec_ids[@]} -eq 0 ]]; then
    register_group "未設定"
    append_group_row "未設定" "| (なし) | - | - | - |"
else
    for id in "${spec_ids[@]+"${spec_ids[@]}"}"; do
        spec_file="$SPECS_DIR/$id/spec.md"

        title=""
        status=""
        created=""

        if [[ -f "$spec_file" ]]; then
            title="$(grep -m1 '^#' "$spec_file" 2>/dev/null | sed 's/^# *//' || true)"
            title="$(echo "$title" | sed -E 's/^(機能仕様書|Feature Specification):[[:space:]]*//' || true)"

            status="$(grep -m1 -E '^\*\*(ステータス|Status)\*\*:' "$spec_file" 2>/dev/null | sed -E 's/^\*\*(ステータス|Status)\*\*:[[:space:]]*//' || true)"
            created="$(grep -m1 -E '^\*\*(作成日|Created)\*\*:' "$spec_file" 2>/dev/null | sed -E 's/^\*\*(作成日|Created)\*\*:[[:space:]]*//' || true)"
        fi

        [[ -n "$title" ]] || title="$id"
        [[ -n "$status" ]] || status="-"
        [[ -n "$created" ]] || created="-"

        status="$(echo "$status" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g')"
        if [[ -z "$status" || "$status" == "-" ]]; then
            status="未設定"
        fi

        # Escape table separators
        title="${title//|/\\|}"
        status="${status//|/\\|}"
        created="${created//|/\\|}"

        local_row=""
        if [[ -f "$spec_file" ]]; then
            local_row="| \`$id\` | [$title]($id/spec.md) | $status | $created |"
        else
            local_row="| \`$id\` | $title | $status | $created |"
        fi

        register_group "$status"
        append_group_row "$status" "$local_row"
    done
fi

preferred_order=("進行中" "下書き" "実装完了" "完了" "未設定")
added=()

add_group_output() {
    local key="$1"
    local idx
    idx="$(get_group_index "$key")"
    local count="0"
    local rows=""
    if [[ "$idx" != "-1" ]]; then
        count="${GROUP_COUNTS[$idx]}"
        rows="${GROUP_ROWS[$idx]}"
    fi

    echo "### $key (${count}件)" >>"$tmp_file"
    echo "" >>"$tmp_file"
    echo "| 要件ID | タイトル | ステータス | 作成日 |" >>"$tmp_file"
    echo "|---|---|---|---|" >>"$tmp_file"
    if [[ -n "$rows" ]]; then
        printf '%s\n' "$rows" >>"$tmp_file"
    else
        echo "| (なし) | - | - | - |" >>"$tmp_file"
    fi
    echo "" >>"$tmp_file"
    added+=("$key")
}

for key in "${preferred_order[@]}"; do
    idx="$(get_group_index "$key")"
    if [[ "$idx" != "-1" && -n "${GROUP_ROWS[$idx]}" ]]; then
        add_group_output "$key"
    fi
done

for key in "${GROUP_ORDER[@]}"; do
    skip=false
    for existing in "${added[@]+"${added[@]}"}"; do
        if [[ "$existing" == "$key" ]]; then
            skip=true
            break
        fi
    done
    if [[ "$skip" == "false" ]]; then
        add_group_output "$key"
    fi
done

mv "$tmp_file" "$OUTPUT_FILE"

if [[ "$QUIET" != "true" ]]; then
    echo "[specify] 要件一覧を更新しました: $OUTPUT_FILE"
fi
