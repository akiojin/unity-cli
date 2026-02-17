#!/usr/bin/env bash

# check-tasks.sh - tasks.mdの全タスク完了チェック
#
# 使用方法:
#   check-tasks.sh <tasks.md のパス>
#
# 戻り値:
#   0: すべてのタスクが完了
#   1: 未完了のタスクが存在
#   2: エラー（ファイルが存在しない等）

set -e

# 引数チェック
if [ $# -ne 1 ]; then
    echo "Usage: $0 <tasks.md path>" >&2
    echo "Example: $0 specs/SPEC-12345678/tasks.md" >&2
    exit 2
fi

TASKS_FILE="$1"

# ファイル存在チェック
if [ ! -f "$TASKS_FILE" ]; then
    echo "Error: tasks.md not found: $TASKS_FILE" >&2
    exit 2
fi

echo "Checking tasks in: $TASKS_FILE"

# 未完了タスクのパターン: - [ ] または -[ ]（スペースの有無）
UNCOMPLETED=$(grep -E '^\s*-\s*\[\s*\]' "$TASKS_FILE" || true)

if [ -n "$UNCOMPLETED" ]; then
    echo ""
    echo "❌ Uncompleted tasks found:"
    echo "$UNCOMPLETED"
    echo ""

    # 完了済みと未完了の数をカウント
    COMPLETED_COUNT=$(grep -cE '^\s*-\s*\[x\]' "$TASKS_FILE" || echo "0")
    UNCOMPLETED_COUNT=$(grep -cE '^\s*-\s*\[\s*\]' "$TASKS_FILE" || echo "0")
    TOTAL=$((COMPLETED_COUNT + UNCOMPLETED_COUNT))

    echo "Progress: $COMPLETED_COUNT/$TOTAL tasks completed"
    exit 1
fi

# すべて完了している場合
COMPLETED_COUNT=$(grep -cE '^\s*-\s*\[x\]' "$TASKS_FILE" || echo "0")

if [ "$COMPLETED_COUNT" -eq 0 ]; then
    echo "⚠️  Warning: No tasks found in $TASKS_FILE"
    echo "   This might be okay if tasks are not yet defined."
    exit 0
fi

echo "✅ All $COMPLETED_COUNT tasks are completed!"
exit 0
