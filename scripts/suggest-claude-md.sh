#!/bin/bash
# CLAUDE.md改善提案スクリプト
# 会話履歴を分析してCLAUDE.mdへの追記候補を提案します

set -euo pipefail

# カラー出力用
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# プロジェクト名を取得（Worktree対応）
get_project_name() {
    local project_path=$(pwd)
    # .worktrees配下の場合は親ディレクトリを含める
    if [[ "$project_path" == *".worktrees"* ]]; then
        # /path/to/unity-cli/.worktrees/feature-xxx → -unity-cli--worktrees-feature-xxx
        project_path=$(echo "$project_path" | sed 's|^/|-|' | tr '/' '-')
    else
        # 通常のパス → -unity-cli
        project_path=$(basename "$project_path" | sed 's/^/-/')
    fi
    echo "$project_path"
}

# Claude Codeの会話履歴ディレクトリを取得
get_conversation_dir() {
    local project_name=$(get_project_name)
    local claude_dir="$HOME/.claude/projects/$project_name"

    if [ ! -d "$claude_dir" ]; then
        echo -e "${RED}❌ 会話履歴ディレクトリが見つかりません: $claude_dir${NC}" >&2
        return 1
    fi

    echo "$claude_dir"
}

# 最新のJSONLファイルを取得
get_latest_jsonl() {
    local conversation_dir=$1

    # 最新のJSONLファイルを探す（更新日時順）
    local latest_jsonl=$(find "$conversation_dir" -name "*.jsonl" -type f -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2-)

    if [ -z "$latest_jsonl" ]; then
        echo -e "${RED}❌ 会話履歴ファイル（JSONL）が見つかりません${NC}" >&2
        return 1
    fi

    echo "$latest_jsonl"
}

# 会話履歴を抽出
extract_conversation() {
    local jsonl_file=$1

    # jqで会話履歴を抽出（user/assistantのメッセージのみ）
    jq -r 'select(.message.role == "user" or .message.role == "assistant") |
           "\(.message.role): \(.message.content // "")"' "$jsonl_file" 2>/dev/null || {
        echo -e "${RED}❌ JSONL解析に失敗しました${NC}" >&2
        return 1
    }
}

# 会話履歴を分析してCLAUDE.md改善提案を生成
analyze_conversation() {
    local conversation=$1

    cat <<'EOF'
以下の会話履歴を分析し、CLAUDE.mdに追記すべきルールや知識を提案してください。

## 分析観点

1. **プロジェクト独自のルール**
   - 標準的な実装方法ではなく、このプロジェクト特有の方法や制約
   - 他のプロジェクトでは一般的でないが、このプロジェクトでは必須のパターン

2. **繰り返し指摘される内容**
   - 同じ指摘や修正が複数回出現している
   - ルール化することで今後の混乱を防げる

3. **複数箇所で統一すべき実装**
   - 関連する箇所で実装を揃えるべきパターン
   - コード規約やアーキテクチャの統一

## 提案フォーマット

各提案には以下を含めてください：
- **カテゴリ**: 開発ワークフロー/技術実装/コミット規約/テスト/その他
- **優先度**: 高/中/低
- **提案理由**: なぜこのルールが必要か（会話中の具体例を引用）
- **追記内容**: CLAUDE.mdに追加する具体的なマークダウンテキスト

---

会話履歴:

EOF
    echo "$conversation"
}

# メイン処理
main() {
    echo -e "${GREEN}🔍 会話履歴を分析してCLAUDE.md改善提案を生成します...${NC}"
    echo ""

    # 会話履歴ディレクトリを取得
    conversation_dir=$(get_conversation_dir)
    if [ $? -ne 0 ]; then
        exit 1
    fi

    echo -e "${YELLOW}📁 会話履歴ディレクトリ: $conversation_dir${NC}"

    # 最新のJSONLファイルを取得
    jsonl_file=$(get_latest_jsonl "$conversation_dir")
    if [ $? -ne 0 ]; then
        exit 1
    fi

    echo -e "${YELLOW}📄 最新の会話ファイル: $(basename "$jsonl_file")${NC}"
    echo ""

    # 会話履歴を抽出
    conversation=$(extract_conversation "$jsonl_file")
    if [ $? -ne 0 ]; then
        exit 1
    fi

    # 会話履歴の行数をカウント
    line_count=$(echo "$conversation" | wc -l)
    echo -e "${YELLOW}📊 会話履歴: ${line_count}行${NC}"
    echo ""

    # 会話履歴を分析（プロンプトを生成）
    analysis_prompt=$(analyze_conversation "$conversation")

    # Claude Code内で実行されている場合は、分析プロンプトをそのまま出力
    # （Claude Codeが自動的にLLMに送信して分析結果を返す）
    echo -e "${GREEN}📝 以下のプロンプトでCLAUDE.md改善提案を生成してください：${NC}"
    echo ""
    echo "---"
    echo "$analysis_prompt"
    echo "---"
    echo ""
    echo -e "${YELLOW}💡 上記のプロンプトを使って、会話履歴を分析し、CLAUDE.mdへの追記候補を提案してください。${NC}"
    echo -e "${YELLOW}   提案内容を確認後、「この提案をCLAUDE.mdに追記してください」と指示してください。${NC}"
}

# スクリプト実行
main "$@"
