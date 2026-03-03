#!/bin/bash

# Claude Code PreToolUse Hook: Block file operations outside worktree
# このスクリプトは Worktree ディレクトリ外へのファイル操作をブロックします

# Worktreeのルートディレクトリを取得
WORKTREE_ROOT=$(git rev-parse --show-toplevel 2>/dev/null)
if [ -z "$WORKTREE_ROOT" ]; then
    # gitリポジトリでない場合は現在のディレクトリを使用
    WORKTREE_ROOT=$(pwd)
fi

# パスが Worktree 配下かどうかを判定
is_within_worktree() {
    local target_path="$1"

    # 空のパスはWorktree外とみなす
    if [ -z "$target_path" ]; then
        return 1
    fi

    # ホームディレクトリはWorktree外
    if [ "$target_path" = "~" ]; then
        return 1
    fi

    # 相対パスを絶対パスに変換
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

# コマンドから全てのパス引数を抽出
extract_file_paths() {
    local cmd="$1"

    # Python3が利用可能な場合はshlexを使用
    if command -v python3 >/dev/null 2>&1; then
        python3 -c "
import sys
import shlex

cmd = '''$cmd'''
try:
    tokens = shlex.split(cmd)
    # 最初のトークン(コマンド名)をスキップし、オプション(-で始まる)以外を抽出
    paths = [t for t in tokens[1:] if not t.startswith('-')]
    for p in paths:
        print(p)
except:
    pass
" 2>/dev/null
    else
        # フォールバック: awkで引数を抽出
        echo "$cmd" | awk '{for(i=2;i<=NF;i++) if($i !~ /^-/) print $i}'
    fi
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
command_segments=$(printf '%s\n' "$command" | sed -E 's/\|&/\n/g; s/\|\|/\n/g; s/&&/\n/g; s/[;|&]/\n/g')

while IFS= read -r segment; do
    # リダイレクトやheredoc以降を落としてトリミング
    trimmed_segment=$(echo "$segment" | sed 's/[<>].*//; s/<<.*//' | xargs)

    # 空行はスキップ
    if [ -z "$trimmed_segment" ]; then
        continue
    fi

    # ファイル操作コマンドをチェック
    if echo "$trimmed_segment" | grep -qE '^(mkdir|rmdir|rm|touch|cp|mv)\b'; then
        # パス引数を抽出
        file_paths=$(extract_file_paths "$trimmed_segment")

        # 各パスがWorktree配下かチェック
        while IFS= read -r path; do
            if [ -z "$path" ]; then
                continue
            fi

            if ! is_within_worktree "$path"; then
                # JSON応答を返す
                cat <<EOF
{
  "decision": "block",
  "reason": "🚫 File operations outside worktree are not allowed",
  "stopReason": "Worktree is designed to complete work within the launched directory. File operations outside the worktree cannot be executed.\n\nWorktree root: $WORKTREE_ROOT\nTarget path: $path\nBlocked command: $command\n\nInstead, use absolute paths within worktree, e.g., 'mkdir ./new-dir' or 'rm ./file.txt'"
}
EOF

                # stderrにもメッセージを出力
                echo "🚫 Blocked: $command" >&2
                echo "Reason: File operation outside worktree ($path) is not allowed." >&2
                echo "Worktree root: $WORKTREE_ROOT" >&2

                exit 2  # ブロック
            fi
        done <<< "$file_paths"
    fi
done <<< "$command_segments"

# 許可
exit 0
