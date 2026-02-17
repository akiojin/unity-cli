#!/bin/bash
# Check that implementation plan exists and find optional design documents
# Usage: ./check-task-prerequisites.sh [--json] [--no-branch-check] [--feature-dir <abs>] [--spec <abs>]

set -e

JSON_MODE=false
NO_BRANCH_CHECK=false
FEATURE_DIR_OVERRIDE=""
FEATURE_SPEC_OVERRIDE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --json) JSON_MODE=true; shift ;;
        --no-branch-check) NO_BRANCH_CHECK=true; shift ;;
        --feature-dir) FEATURE_DIR_OVERRIDE="$2"; shift 2 ;;
        --spec) FEATURE_SPEC_OVERRIDE="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $0 [--json] [--no-branch-check] [--feature-dir <abs>] [--spec <abs>]"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

# Source common functions
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

# Get all paths (baseline from current branch)
eval $(get_feature_paths)

# Optionally bypass branch check
if [ "$NO_BRANCH_CHECK" != true ]; then
    check_feature_branch "$CURRENT_BRANCH" || true
fi

# Apply overrides if provided
if [[ -n "$FEATURE_SPEC_OVERRIDE" ]]; then
    FEATURE_DIR="$(dirname "$FEATURE_SPEC_OVERRIDE")"
elif [[ -n "$FEATURE_DIR_OVERRIDE" ]]; then
    FEATURE_DIR="$FEATURE_DIR_OVERRIDE"
fi

# Check if feature directory exists
if [[ ! -d "$FEATURE_DIR" ]]; then
    echo "ERROR: Feature directory not found: $FEATURE_DIR"
    echo "Run /specify first to create the feature structure."
    exit 1
fi

# Check for implementation plan (required)
if [[ ! -f "$IMPL_PLAN" ]]; then
    echo "ERROR: plan.md not found in $FEATURE_DIR"
    echo "Run /plan first to create the plan."
    exit 1
fi

if $JSON_MODE; then
    # Build JSON array of available docs that actually exist
    docs=()
    [[ -f "$RESEARCH" ]] && docs+=("research.md")
    [[ -f "$DATA_MODEL" ]] && docs+=("data-model.md")
    ([[ -d "$CONTRACTS_DIR" ]] && [[ -n "$(ls -A "$CONTRACTS_DIR" 2>/dev/null)" ]]) && docs+=("contracts/")
    [[ -f "$QUICKSTART" ]] && docs+=("quickstart.md")
    # join array into JSON
    json_docs=$(printf '"%s",' "${docs[@]}")
    json_docs="[${json_docs%,}]"
    printf '{"FEATURE_DIR":"%s","AVAILABLE_DOCS":%s}\n' "$FEATURE_DIR" "$json_docs"
else
    # List available design documents (optional)
    echo "FEATURE_DIR:$FEATURE_DIR"
    echo "AVAILABLE_DOCS:"

    # Use common check functions
    check_file "$RESEARCH" "research.md"
    check_file "$DATA_MODEL" "data-model.md"
    check_dir "$CONTRACTS_DIR" "contracts/"
    check_file "$QUICKSTART" "quickstart.md"
fi

# Always succeed - task generation should work with whatever docs are available
