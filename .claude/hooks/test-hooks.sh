#!/bin/bash

# Claude Worktree Hooks Test Suite
# TDD: このテストが先に作成され、Hooksの動作を検証します

set -e

HOOKS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKTREE_ROOT=$(git rev-parse --show-toplevel 2>/dev/null || pwd)

# テスト結果のカウンター
TESTS_RUN=0
TESTS_PASSED=0
TESTS_FAILED=0

# カラー出力
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# ヘルパー関数: テスト実行
run_test() {
    local test_name="$1"
    local hook_script="$2"
    local tool_name="$3"
    local command="$4"
    local expected_block="$5"  # "block" または "allow"

    TESTS_RUN=$((TESTS_RUN + 1))

    echo -e "\n${YELLOW}[TEST ${TESTS_RUN}]${NC} ${test_name}"

    # Hookスクリプトを実行
    local output
    local exit_code=0
    output=$(echo "{\"tool_name\":\"${tool_name}\",\"tool_input\":{\"command\":\"${command}\"}}" | \
             bash "${hook_script}" 2>&1) || exit_code=$?

    # 結果を判定（exit code 2 = block, exit code 0 = allow）
    if [ "$expected_block" = "block" ]; then
        if [ $exit_code -eq 2 ] || echo "$output" | grep -q '"decision":"block"'; then
            echo -e "${GREEN}✓ PASS${NC} - Command blocked as expected"
            TESTS_PASSED=$((TESTS_PASSED + 1))
            return 0
        else
            echo -e "${RED}✗ FAIL${NC} - Expected block but command was allowed"
            echo "Exit code: $exit_code"
            echo "Output: $output"
            TESTS_FAILED=$((TESTS_FAILED + 1))
            return 1
        fi
    else
        if [ $exit_code -eq 2 ] || echo "$output" | grep -q '"decision":"block"'; then
            echo -e "${RED}✗ FAIL${NC} - Expected allow but command was blocked"
            echo "Exit code: $exit_code"
            echo "Output: $output"
            TESTS_FAILED=$((TESTS_FAILED + 1))
            return 1
        else
            echo -e "${GREEN}✓ PASS${NC} - Command allowed as expected"
            TESTS_PASSED=$((TESTS_PASSED + 1))
            return 0
        fi
    fi
}

echo "=========================================="
echo "  Claude Worktree Hooks Test Suite"
echo "=========================================="
echo "Worktree Root: ${WORKTREE_ROOT}"
echo ""

# ========================================
# block-cd-command.sh のテスト
# ========================================
echo -e "\n${YELLOW}=== Testing block-cd-command.sh ===${NC}"

run_test \
    "cd .. should be blocked (outside worktree)" \
    "${HOOKS_DIR}/block-cd-command.sh" \
    "Bash" \
    "cd .." \
    "block"

run_test \
    "cd src should be allowed (within worktree)" \
    "${HOOKS_DIR}/block-cd-command.sh" \
    "Bash" \
    "cd src" \
    "allow"

run_test \
    "cd /tmp should be blocked (outside worktree)" \
    "${HOOKS_DIR}/block-cd-command.sh" \
    "Bash" \
    "cd /tmp" \
    "block"

# ========================================
# block-git-branch-ops.sh のテスト
# ========================================
echo -e "\n${YELLOW}=== Testing block-git-branch-ops.sh ===${NC}"

run_test \
    "git checkout should be blocked" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git checkout main" \
    "block"

run_test \
    "git switch should be blocked" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git switch develop" \
    "block"

run_test \
    "git branch -d should be blocked" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git branch -d feature/test" \
    "block"

run_test \
    "git branch --list should be allowed (read-only)" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git branch --list" \
    "allow"

run_test \
    "git worktree should be blocked" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git worktree add ../test" \
    "block"

run_test \
    "git status should be allowed" \
    "${HOOKS_DIR}/block-git-branch-ops.sh" \
    "Bash" \
    "git status" \
    "allow"

# ========================================
# block-file-ops.sh のテスト
# ========================================
echo -e "\n${YELLOW}=== Testing block-file-ops.sh ===${NC}"

run_test \
    "mkdir outside worktree should be blocked" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "mkdir /tmp/test" \
    "block"

run_test \
    "mkdir inside worktree should be allowed" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "mkdir test-dir" \
    "allow"

run_test \
    "touch outside worktree should be blocked" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "touch /tmp/test.txt" \
    "block"

run_test \
    "touch inside worktree should be allowed" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "touch test.txt" \
    "allow"

run_test \
    "rm outside worktree should be blocked" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "rm /tmp/test.txt" \
    "block"

run_test \
    "cp outside worktree should be blocked" \
    "${HOOKS_DIR}/block-file-ops.sh" \
    "Bash" \
    "cp test.txt /tmp/test.txt" \
    "block"

# ========================================
# 結果サマリー
# ========================================
echo ""
echo "=========================================="
echo "  Test Results Summary"
echo "=========================================="
echo "Total Tests:  ${TESTS_RUN}"
echo -e "${GREEN}Passed:       ${TESTS_PASSED}${NC}"
echo -e "${RED}Failed:       ${TESTS_FAILED}${NC}"
echo ""

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "${GREEN}All tests passed! ✓${NC}"
    exit 0
else
    echo -e "${RED}Some tests failed. Please review the output above.${NC}"
    exit 1
fi
