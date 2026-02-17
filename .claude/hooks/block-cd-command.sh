#!/bin/bash

# Claude Code PreToolUse Hook: Block cd command outside worktree
# このスクリプトは Worktree ディレクトリ外への cd コマンドをブロックします

# Worktreeのルートディレクトリを取得
WORKTREE_ROOT=$(git rev-parse --show-toplevel 2>/dev/null)
if [ -z "$WORKTREE_ROOT" ]; then
    # gitリポジトリでない場合は現在のディレクトリを使用
    WORKTREE_ROOT=$(pwd)
fi

# パスが Worktree 配下かどうかを判定
is_within_worktree() {
    local target_path="$1"

    # 空のパスはホームディレクトリとみなす
    if [ -z "$target_path" ] || [ "$target_path" = "~" ]; then
        return 1  # ホームディレクトリはWorktree外
    fi

    # 相対パスを絶対パスに変換（realpathがない環境を考慮）
    if [[ "$target_path" = /* ]]; then
        # 絶対パスの場合はそのまま
        local abs_path="$target_path"
    else
        # 相対パスの場合は現在のディレクトリ基準で解決
        local abs_path
        abs_path=$(cd -- "$target_path" 2>/dev/null && pwd)
        if [ -z "$abs_path" ]; then
            # ディレクトリが存在しない場合は現在のディレクトリからの相対パスとして計算
            abs_path="$(pwd)/$target_path"
        fi
    fi

    # シンボリックリンクを解決して正規化
    if command -v realpath >/dev/null 2>&1; then
        local resolved_path
        resolved_path=$(realpath -m "$abs_path" 2>/dev/null) && abs_path="$resolved_path"
    fi

    # Worktreeルートのプレフィックスチェック
    case "$abs_path" in
        "$WORKTREE_ROOT"|"$WORKTREE_ROOT"/*)
            return 0  # Worktree配下
            ;;
        *)
            return 1  # Worktree外
            ;;
    esac
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

    # cdコマンドをチェック（cd、builtin cd、command cdなど）
    if echo "$trimmed_segment" | grep -qE '^(builtin[[:space:]]+)?(command[[:space:]]+)?cd\b'; then
        # cd のターゲットパスを抽出
        target_path=$(echo "$trimmed_segment" | sed -E 's/^(builtin[[:space:]]+)?(command[[:space:]]+)?cd[[:space:]]+//' | awk '{print $1}')

        # ターゲットパスがWorktree配下かチェック
        if ! is_within_worktree "$target_path"; then
            # JSON応答を返す
            cat <<EOF
{
  "decision": "block",
  "reason": "🚫 cd command outside worktree is not allowed",
  "stopReason": "Worktree is designed to complete work within the launched directory. Directory navigation outside the worktree using cd command cannot be executed.\n\nWorktree root: $WORKTREE_ROOT\nTarget path: $target_path\nBlocked command: $command\n\nInstead, use absolute paths to execute commands, e.g., 'git -C /path/to/repo status' or '/path/to/script.sh'"
}
EOF

            # stderrにもメッセージを出力
            echo "🚫 Blocked: $command" >&2
            echo "Reason: Navigation outside worktree ($target_path) is not allowed." >&2
            echo "Worktree root: $WORKTREE_ROOT" >&2

            exit 2  # ブロック
        fi
    fi
done <<< "$command_segments"

# 許可
exit 0
