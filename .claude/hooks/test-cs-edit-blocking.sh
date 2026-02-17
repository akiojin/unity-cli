#!/bin/bash

# Test suite for block-cs-edit-tools.sh Hook
# このスクリプトは block-cs-edit-tools.sh の動作をテストします

HOOK_SCRIPT=".claude/hooks/block-cs-edit-tools.sh"
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

# テスト結果を記録
record_test() {
    local test_name="$1"
    local result="$2"

    TOTAL_TESTS=$((TOTAL_TESTS + 1))

    if [ "$result" = "PASS" ]; then
        PASSED_TESTS=$((PASSED_TESTS + 1))
        echo "✅ PASS: $test_name"
    else
        FAILED_TESTS=$((FAILED_TESTS + 1))
        echo "❌ FAIL: $test_name"
    fi
}

# テストケース1: unity-cli ツールは許可
test_unity_mcp_tool_allowed() {
    local input='{"tool_name":"mcp__unity-cli__edit_structured","tool_input":{"path":"Assets/Scripts/Foo.cs"}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "unity-cli tool allowed" "PASS"
    else
        record_test "unity-cli tool allowed" "FAIL"
    fi
}

# テストケース2: Edit ツールで Unity C# ファイル編集はブロック
test_edit_unity_cs_blocked() {
    local input='{"tool_name":"Edit","tool_input":{"file_path":"Assets/Scripts/Player.cs"}}'
    local exit_code

    set +e
    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?
    set -e

    if [ $exit_code -eq 2 ]; then
        record_test "Edit tool for Unity C# file blocked" "PASS"
    else
        record_test "Edit tool for Unity C# file blocked" "FAIL"
    fi
}

# テストケース3: Write ツールで Unity C# ファイル作成はブロック
test_write_unity_cs_blocked() {
    local input='{"tool_name":"Write","tool_input":{"file_path":"Packages/unity-cli-bridge/Runtime/NewScript.cs"}}'
    local exit_code

    set +e
    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?
    set -e

    if [ $exit_code -eq 2 ]; then
        record_test "Write tool for Unity C# file blocked" "PASS"
    else
        record_test "Write tool for Unity C# file blocked" "FAIL"
    fi
}

# テストケース4: Read ツールで Unity C# ファイル読み取りは警告のみ（許可）
test_read_unity_cs_warning() {
    local input='{"tool_name":"Read","tool_input":{"file_path":"Assets/Scripts/GameManager.cs"}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "Read tool for Unity C# file warning only" "PASS"
    else
        record_test "Read tool for Unity C# file warning only" "FAIL"
    fi
}

# テストケース5: 非 Unity C# ファイルは許可
test_non_unity_cs_allowed() {
    local input='{"tool_name":"Edit","tool_input":{"file_path":"src/server.cs"}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "Non-Unity C# file allowed" "PASS"
    else
        record_test "Non-Unity C# file allowed" "FAIL"
    fi
}

# テストケース7: 非 C# ファイルは許可
test_non_cs_file_allowed() {
    local input='{"tool_name":"Edit","tool_input":{"file_path":"Assets/Scripts/data.json"}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "Non-C# file allowed" "PASS"
    else
        record_test "Non-C# file allowed" "FAIL"
    fi
}

# テストケース8: 他のツールは許可
test_other_tools_allowed() {
    local input='{"tool_name":"Bash","tool_input":{"command":"ls"}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "Other tools allowed" "PASS"
    else
        record_test "Other tools allowed" "FAIL"
    fi
}

# テストケース9: UnityCliBridge/ 配下の C# ファイルはブロック
test_unity_mcp_server_dir_blocked() {
    local input='{"tool_name":"Edit","tool_input":{"file_path":"UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ScreenshotHandler.cs"}}'
    local exit_code

    set +e
    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?
    set -e

    if [ $exit_code -eq 2 ]; then
        record_test "UnityCliBridge directory C# file blocked" "PASS"
    else
        record_test "UnityCliBridge directory C# file blocked" "FAIL"
    fi
}

# テストケース10: ファイルパスなしは許可
test_no_file_path_allowed() {
    local input='{"tool_name":"Edit","tool_input":{}}'
    local exit_code

    echo "$input" | $HOOK_SCRIPT >/dev/null 2>&1
    exit_code=$?

    if [ $exit_code -eq 0 ]; then
        record_test "No file path allowed" "PASS"
    else
        record_test "No file path allowed" "FAIL"
    fi
}

# テスト実行
echo "=========================================="
echo "Testing block-cs-edit-tools.sh Hook"
echo "=========================================="
echo ""

test_unity_mcp_tool_allowed
test_edit_unity_cs_blocked
test_write_unity_cs_blocked
test_read_unity_cs_warning
test_non_unity_cs_allowed
test_non_cs_file_allowed
test_other_tools_allowed
test_unity_mcp_server_dir_blocked
test_no_file_path_allowed

# 結果サマリー
echo ""
echo "=========================================="
echo "Test Results Summary"
echo "=========================================="
echo "Total Tests:  $TOTAL_TESTS"
echo "Passed:       $PASSED_TESTS"
echo "Failed:       $FAILED_TESTS"
echo "=========================================="

if [ $FAILED_TESTS -eq 0 ]; then
    echo "✅ All tests passed!"
    exit 0
else
    echo "❌ Some tests failed."
    exit 1
fi
