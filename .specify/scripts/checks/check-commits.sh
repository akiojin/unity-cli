#!/usr/bin/env bash

# check-commits.sh - commitlint コミットメッセージ規約チェック
#
# 使用方法:
#   check-commits.sh [--from <commit>] [--to <commit>]
#
# 戻り値:
#   0: すべてのコミットメッセージが規約に準拠
#   1: 規約違反のコミットメッセージが存在
#   2: エラー（commitlintが見つからない等）

set -e

FROM_COMMIT="origin/main"
TO_COMMIT="HEAD"

# 引数解析
while [[ $# -gt 0 ]]; do
    case "$1" in
        --from)
            FROM_COMMIT="$2"
            shift 2
            ;;
        --to)
            TO_COMMIT="$2"
            shift 2
            ;;
        --help|-h)
            echo "Usage: $0 [--from <commit>] [--to <commit>]"
            echo "  --from <commit>  Start commit (default: origin/main)"
            echo "  --to <commit>    End commit (default: HEAD)"
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

# リポジトリルートを見つける
REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
cd "$REPO_ROOT"

echo "========================================="
echo "Checking commit messages..."
echo "========================================="
echo "Range: $FROM_COMMIT..$TO_COMMIT"
echo ""

# commitlintの存在確認
if ! command -v npx &> /dev/null; then
    echo "Error: npx not found. Please install Node.js and npm." >&2
    exit 2
fi

# commitlint設定ファイルの確認
if [ ! -f ".commitlintrc.json" ] && [ ! -f "commitlint.config.js" ]; then
    echo "⚠️  Warning: commitlint config file not found"
    echo "   Expected: .commitlintrc.json or commitlint.config.js"
    echo "   Skipping commitlint check"
    exit 0
fi

# コミット範囲の取得
COMMITS=$(git log --format=%H "$FROM_COMMIT".."$TO_COMMIT" 2>/dev/null || true)

if [ -z "$COMMITS" ]; then
    echo "ℹ️  No commits found in range $FROM_COMMIT..$TO_COMMIT"
    exit 0
fi

COMMIT_COUNT=$(echo "$COMMITS" | wc -l | tr -d ' ')
echo "Checking $COMMIT_COUNT commits..."
echo ""

EXIT_CODE=0
FAILED_COMMITS=()
FEAT_COUNT=0
FIX_COUNT=0
PERF_COUNT=0
BREAKING_COUNT=0
NO_RELEASE_COUNT=0

# 各コミットメッセージをチェック
while IFS= read -r commit; do
    MESSAGE=$(git log --format=%B -n 1 "$commit")
    SUBJECT=$(git log --format=%s -n 1 "$commit")
    echo "Checking commit: ${commit:0:8}"

    # commitlintでチェック
    if ! echo "$MESSAGE" | npx commitlint --verbose 2>&1; then
        echo "❌ Commit message does not follow conventions: ${commit:0:8}"
        echo "   Message: $(echo "$MESSAGE" | head -n 1)"
        FAILED_COMMITS+=("$commit")
        EXIT_CODE=1
    else
        echo "✅ Commit ${commit:0:8} passed"

        # semantic-release影響を分析
        if echo "$SUBJECT" | grep -qE '^feat(\(.+\))?!?:'; then
            FEAT_COUNT=$((FEAT_COUNT + 1))
            echo "   → 📦 minor version up (new feature)"
        elif echo "$SUBJECT" | grep -qE '^fix(\(.+\))?:'; then
            FIX_COUNT=$((FIX_COUNT + 1))
            echo "   → 🐛 patch version up (bug fix)"
        elif echo "$SUBJECT" | grep -qE '^perf(\(.+\))?:'; then
            PERF_COUNT=$((PERF_COUNT + 1))
            echo "   → ⚡ patch version up (performance)"
        elif echo "$SUBJECT" | grep -qE '^(docs|test|chore|style|refactor|ci|build|revert)(\(.+\))?:'; then
            NO_RELEASE_COUNT=$((NO_RELEASE_COUNT + 1))
            TYPE=$(echo "$SUBJECT" | sed -E 's/^([a-z]+)(\(.+\))?:.*/\1/')
            echo "   → 📝 no version change ($TYPE)"
        fi

        # BREAKING CHANGEチェック
        if echo "$MESSAGE" | grep -qE '^BREAKING CHANGE:' || echo "$SUBJECT" | grep -qE '!:'; then
            BREAKING_COUNT=$((BREAKING_COUNT + 1))
            echo "   → 💥 BREAKING CHANGE detected (major version up)"
        fi
    fi

    echo ""
done <<< "$COMMITS"

echo "========================================="
echo "semantic-release Impact Analysis"
echo "========================================="

if [ $BREAKING_COUNT -gt 0 ]; then
    echo "🚨 Release Type: MAJOR version up"
    echo "   Reason: BREAKING CHANGE detected ($BREAKING_COUNT commits)"
elif [ $FEAT_COUNT -gt 0 ]; then
    echo "📦 Release Type: MINOR version up"
    echo "   Reason: New features added ($FEAT_COUNT commits)"
elif [ $((FIX_COUNT + PERF_COUNT)) -gt 0 ]; then
    echo "🐛 Release Type: PATCH version up"
    echo "   Reason: Bug fixes or performance improvements"
else
    echo "📝 Release Type: No version change"
    echo "   Reason: Only non-release commits (docs, chore, etc.)"
fi

echo ""
echo "CHANGELOG Entries:"
echo "  - feat commits:     $FEAT_COUNT"
echo "  - fix commits:      $FIX_COUNT"
echo "  - perf commits:     $PERF_COUNT"
echo "  - BREAKING CHANGE:  $BREAKING_COUNT"
echo "  - no-release:       $NO_RELEASE_COUNT"
echo ""
echo "Total entries in CHANGELOG: $((FEAT_COUNT + FIX_COUNT + PERF_COUNT + BREAKING_COUNT))"
echo ""

if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ All $COMMIT_COUNT commit messages follow conventions!"
else
    FAILED_COUNT=${#FAILED_COMMITS[@]}
    echo "❌ $FAILED_COUNT/$COMMIT_COUNT commit messages do not follow conventions"
    echo ""
    echo "Failed commits:"
    for commit in "${FAILED_COMMITS[@]}"; do
        echo "  - ${commit:0:8}: $(git log --format=%s -n 1 "$commit")"
    done
    echo ""
    echo "⚠️  Warning: Invalid commits will be ignored by semantic-release!"
    echo "   This may result in incomplete CHANGELOG or incorrect version."
fi

exit $EXIT_CODE
