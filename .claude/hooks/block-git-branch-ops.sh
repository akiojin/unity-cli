#!/bin/bash

# Claude Code PreToolUse Hook: Block git branch operations
# このスクリプトは git checkout, git switch, git branch, git worktree コマンドをブロックします

# 配列内に値が含まれているかを判定
contains_element() {
    local needle="$1"
    shift
    for element in "$@"; do
        if [ "$element" = "$needle" ]; then
            return 0
        fi
    done
    return 1
}

# git branch コマンドが参照系かどうかを判定
is_read_only_git_branch() {
    local branch_args="$1"

    branch_args=$(echo "$branch_args" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
    if [ -z "$branch_args" ]; then
        return 0
    fi

    local -a branch_tokens=()
    if command -v python3 >/dev/null 2>&1; then
        local tokens_json
        tokens_json=$(
            BRANCH_ARGS="$branch_args" python3 - <<'PY' 2>/dev/null
import os
import shlex
import json

args = os.environ.get("BRANCH_ARGS", "")
try:
    tokens = shlex.split(args)
except ValueError:
    tokens = []

print(json.dumps(tokens))
PY
        )
        if [ -z "$tokens_json" ]; then
            tokens_json='[]'
        fi
        mapfile -t branch_tokens < <(printf '%s\n' "$tokens_json" | jq -r '.[]')
    else
        # Pythonが利用できない環境向けフォールバック
        read -r -a branch_tokens <<< "$branch_args"
    fi

    local dangerous_flags=(-d -D --delete -m -M --move -c -C --copy --create-reflog --set-upstream-to --unset-upstream --track --no-track --edit-description -f --force)
    local expect_value_flags=(--list -l --contains --merged --no-merged --points-at --format --sort --abbrev)

    local expect_value=""
    for token in "${branch_tokens[@]}"; do
        if [ -z "$token" ]; then
            continue
        fi

        if [ -n "$expect_value" ]; then
            if [[ "$token" == -* ]]; then
                expect_value=""
            else
                expect_value=""
                continue
            fi
        fi

        if [ "$token" = "--" ]; then
            return 1
        fi

        if [[ "$token" == -* ]]; then
            local option_name="$token"
            local inline_value=""

            if [[ "$token" == *=* ]]; then
                option_name="${token%%=*}"
                inline_value="${token#*=}"
            fi

            if [[ "$option_name" == -* && "$option_name" != --* && ${#option_name} -gt 2 && "$option_name" != -*=* ]]; then
                local short_flags="${option_name#-}"
                local i
                for ((i = 0; i < ${#short_flags}; i++)); do
                    local short_flag="-${short_flags:i:1}"
                    if contains_element "$short_flag" "${dangerous_flags[@]}"; then
                        return 1
                    fi
                    if contains_element "$short_flag" "${expect_value_flags[@]}"; then
                        expect_value="$short_flag"
                    fi
                done
                continue
            fi

            if contains_element "$option_name" "${dangerous_flags[@]}"; then
                return 1
            fi

            if contains_element "$option_name" "${expect_value_flags[@]}"; then
                if [ -z "$inline_value" ]; then
                    expect_value="$option_name"
                fi
                continue
            fi

            continue
        fi

        return 1
    done

    return 0
}

# stdinからJSON入力を読み取り
json_input=$(cat)

# ツール名を確認
tool_name=$(echo "$json_input" | jq -r '.tool_name // empty')

# Bashツール以外は許可
if [ "$tool_name" != "Bash" ]; then
    exit 0
fi

# コマンドを取得
command=$(echo "$json_input" | jq -r '.tool_input.command // empty')

# 演算子で連結された各コマンドを個別にチェックするために分割
# &&, ||, ;, |, |&, &, 改行などで区切って先頭トークンを判定する
command_segments=$(printf '%s\n' "$command" | sed -E 's/\|&/\n/g; s/\|\|/\n/g; s/&&/\n/g; s/[;|&]/\n/g')

while IFS= read -r segment; do
    # リダイレクトやheredoc以降を落としてトリミング
    trimmed_segment=$(echo "$segment" | sed 's/[<>].*//; s/<<.*//' | xargs)

    # 空行はスキップ
    if [ -z "$trimmed_segment" ]; then
        continue
    fi

    # インタラクティブrebase禁止 (git rebase -i origin/main)
    if printf '%s' "$trimmed_segment" | grep -qE '^git[[:space:]]+rebase\b'; then
        if printf '%s' "$trimmed_segment" | grep -qE '(^|[[:space:]])(-i|--interactive)([[:space:]]|$)' &&
           printf '%s' "$trimmed_segment" | grep -qE '(^|[[:space:]])origin/main([[:space:]]|$)'; then
            cat <<EOF
{
  "decision": "block",
  "reason": "🚫 Interactive rebase against origin/main is not allowed",
  "stopReason": "Interactive rebase against origin/main initiated by LLMs is blocked because it frequently fails and disrupts sessions.\n\nBlocked command: $command"
}
EOF

            echo "🚫 Blocked: $command" >&2
            echo "Reason: Interactive rebase against origin/main is not allowed in Worktree." >&2
            exit 2
        fi
    fi

    # ブランチ切り替え/作成/worktreeコマンドをチェック
    if echo "$trimmed_segment" | grep -qE '^git\s+(checkout|switch|branch|worktree)\b'; then
        if echo "$trimmed_segment" | grep -qE '^git\s+branch\b'; then
            branch_args=$(echo "$trimmed_segment" | sed -E 's/^git[[:space:]]+branch//')
            if is_read_only_git_branch "$branch_args"; then
                continue
            fi
        fi
        # JSON応答を返す
        cat <<EOF
{
  "decision": "block",
  "reason": "🚫 Branch switching, creation, and worktree commands are not allowed",
  "stopReason": "Worktree is designed to complete work on the launched branch. Branch operations such as git checkout, git switch, git branch, and git worktree cannot be executed.\n\nBlocked command: $command"
}
EOF

    # stderrにもメッセージを出力
    echo "🚫 Blocked: $command" >&2
    echo "Reason: Worktree is designed to complete work on the launched branch." >&2

    exit 2  # ブロック
    fi
done <<< "$command_segments"

# 許可
exit 0
