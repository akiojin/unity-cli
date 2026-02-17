#!/bin/bash
# SessionEnd/PreCompact Hook: CLAUDE.md改善提案の自動実行
# セッション終了時またはコンテキスト圧縮前に会話履歴を自動分析

set -euo pipefail

# 無限ループ防止: 環境変数フラグ
if [ "${SUGGEST_CLAUDE_MD_RUNNING:-}" = "1" ]; then
    echo "⏭️ 既にCLAUDE.md分析が実行中のため、スキップします" >&2
    exit 0
fi

# 無限ループ防止: タイムスタンプベース（5分クールダウン）
LAST_SUGGEST_MARKER=".claude/.last-suggest-timestamp"
CURRENT_TIME=$(date +%s)

if [ -f "$LAST_SUGGEST_MARKER" ]; then
    LAST_TIME=$(cat "$LAST_SUGGEST_MARKER")
    DIFF=$((CURRENT_TIME - LAST_TIME))

    if [ $DIFF -lt 300 ]; then
        echo "⏭️ 前回のCLAUDE.md分析から5分未満のため、スキップします（${DIFF}秒経過）" >&2
        exit 0
    fi
fi

# Hook入力を受信（SessionEnd/PreCompactからのペイロード）
json_input=$(cat)

# セッションID重複チェック
SESSION_ID=$(echo "$json_input" | jq -r '.sessionId // empty' 2>/dev/null || echo "")
LAST_SESSION_FILE=".claude/.last-session-id"

if [ -n "$SESSION_ID" ] && [ -f "$LAST_SESSION_FILE" ]; then
    LAST_SESSION=$(cat "$LAST_SESSION_FILE")
    if [ "$LAST_SESSION" = "$SESSION_ID" ]; then
        echo "⏭️ 同一セッション（$SESSION_ID）のため、CLAUDE.md分析をスキップします" >&2
        exit 0
    fi
fi

# タイムスタンプとセッションIDを記録
echo "$CURRENT_TIME" > "$LAST_SUGGEST_MARKER"
if [ -n "$SESSION_ID" ]; then
    echo "$SESSION_ID" > "$LAST_SESSION_FILE"
fi

# 環境変数フラグを設定して新しいプロセスでCLAUDE.md分析を実行
# （メインセッションのコンテキストウィンドウに影響しない）
echo "🤖 セッション終了を検知しました。CLAUDE.md改善提案を生成します..." >&2
echo "   （このプロセスはバックグラウンドで実行されます）" >&2

# バックグラウンドで実行（メインセッションをブロックしない）
(
    export SUGGEST_CLAUDE_MD_RUNNING=1

    # 会話履歴分析スクリプトを実行
    if scripts/suggest-claude-md.sh > /dev/null 2>&1; then
        echo "✅ CLAUDE.md改善提案の生成が完了しました" >&2
    else
        echo "❌ CLAUDE.md改善提案の生成に失敗しました" >&2
    fi
) &

# Hookは即座に終了（バックグラウンドプロセスは継続）
exit 0
