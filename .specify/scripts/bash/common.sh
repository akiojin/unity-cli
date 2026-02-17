#!/usr/bin/env bash
# Common functions and variables for all scripts

# Get repository root, with fallback for non-git repositories
get_repo_root() {
    if git rev-parse --show-toplevel >/dev/null 2>&1; then
        git rev-parse --show-toplevel
    else
        # Fall back to script location for non-git repos
        local script_dir="$(CDPATH="" cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
        (cd "$script_dir/../../.." && pwd)
    fi
}

# Get current feature id (SPEC-xxxxxxxx) or current git branch as fallback.
# NOTE: This project does NOT create git branches as part of Speckit workflow.
get_current_branch() {
    # First check if SPECIFY_FEATURE environment variable is set
    if [[ -n "${SPECIFY_FEATURE:-}" ]]; then
        echo "$SPECIFY_FEATURE"
        return
    fi

    local repo_root
    repo_root=$(get_repo_root)

    # Then check .specify/current-feature (persistent selection across command runs)
    local current_feature_file="$repo_root/.specify/current-feature"
    if [[ -f "$current_feature_file" ]]; then
        local current_feature
        current_feature="$(cat "$current_feature_file" 2>/dev/null | tr -d '\r\n')"
        if [[ -n "$current_feature" ]]; then
            echo "$current_feature"
            return
        fi
    fi

    # Then check git if available (fallback only)
    if git rev-parse --abbrev-ref HEAD >/dev/null 2>&1; then
        git rev-parse --abbrev-ref HEAD
        return
    fi

    local specs_dir="$repo_root/specs"

    if [[ -d "$specs_dir" ]]; then
        # As a last resort, pick the newest SPEC-* directory (mtime).
        local latest_feature
        latest_feature="$(ls -1t "$specs_dir" 2>/dev/null | grep -E '^SPEC-[a-z0-9]{8}$' | head -n 1 || true)"
        if [[ -n "$latest_feature" ]]; then
            echo "$latest_feature"
            return
        fi
    fi

    echo "main"  # Final fallback
}

# Check if we have git available
has_git() {
    git rev-parse --show-toplevel >/dev/null 2>&1
}

check_feature_branch() {
    local branch="$1"
    local has_git_repo="$2"

    # For non-git repos, we can't enforce branch naming but still provide output
    if [[ "$has_git_repo" != "true" ]]; then
        echo "[specify] 警告: Gitリポジトリが検出できませんでした（ブランチ検証をスキップ）" >&2
        return 0
    fi

    # In this project, the "branch" value may actually be a feature id.
    if [[ "$branch" =~ ^SPEC-[a-z0-9]{8}$ ]]; then return 0; fi
    if [[ "$branch" =~ ^feature/(SPEC-[a-z0-9]{8})$ ]]; then return 0; fi

    # On main/master we cannot infer the active feature in branchless workflow.
    if [[ "$branch" == "main" || "$branch" == "master" ]]; then
        echo "ERROR: 対象要件が特定できません（現在のGitブランチ: '$branch'）。/speckit.specify を実行して .specify/current-feature を作成するか、環境変数 SPECIFY_FEATURE=SPEC-xxxxxxxx を設定してください。" >&2
        return 1
    fi

    echo "ERROR: 現在のGitブランチ '$branch' から要件ID（SPEC-xxxxxxxx）を特定できません。/speckit.specify を実行して .specify/current-feature を作成するか、環境変数 SPECIFY_FEATURE=SPEC-xxxxxxxx を設定してください。" >&2
    return 1
}

get_feature_dir() { echo "$1/specs/$2"; }

# Find feature directory for SPEC-xxxxxxxx naming.
find_feature_dir_by_prefix() {
    local repo_root="$1"
    local branch_name="$2"
    local specs_dir="$repo_root/specs"

    if [[ "$branch_name" =~ ^SPEC-[a-z0-9]{8}$ ]]; then
        echo "$specs_dir/$branch_name"
        return
    fi

    if [[ "$branch_name" =~ ^feature/(SPEC-[a-z0-9]{8})$ ]]; then
        echo "$specs_dir/${BASH_REMATCH[1]}"
        return
    fi

    # Fallback: use the provided name as-is (may fail later with clear error)
    echo "$specs_dir/$branch_name"
}

get_feature_paths() {
    local repo_root=$(get_repo_root)
    local current_branch=$(get_current_branch)
    local has_git_repo="false"

    if has_git; then
        has_git_repo="true"
    fi

    # Use prefix-based lookup to support multiple branches per spec
    local feature_dir=$(find_feature_dir_by_prefix "$repo_root" "$current_branch")

    cat <<EOF
REPO_ROOT='$repo_root'
CURRENT_BRANCH='$current_branch'
HAS_GIT='$has_git_repo'
FEATURE_DIR='$feature_dir'
FEATURE_SPEC='$feature_dir/spec.md'
IMPL_PLAN='$feature_dir/plan.md'
TASKS='$feature_dir/tasks.md'
RESEARCH='$feature_dir/research.md'
DATA_MODEL='$feature_dir/data-model.md'
QUICKSTART='$feature_dir/quickstart.md'
CONTRACTS_DIR='$feature_dir/contracts'
EOF
}

check_file() { [[ -f "$1" ]] && echo "  ✓ $2" || echo "  ✗ $2"; }
check_dir() { [[ -d "$1" && -n $(ls -A "$1" 2>/dev/null) ]] && echo "  ✓ $2" || echo "  ✗ $2"; }
