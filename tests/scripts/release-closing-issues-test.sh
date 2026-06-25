#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
TARGET_SCRIPT="${REPO_ROOT}/scripts/release/collect-closing-issues.sh"
GH_PR_TEMPLATE="${REPO_ROOT}/.agents/skills/gh-pr/references/pr-body-template.md"
GH_PR_SKILL="${REPO_ROOT}/.agents/skills/gh-pr/SKILL.md"
RELEASE_COMMAND="${REPO_ROOT}/.claude/commands/release.md"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

assert_contains() {
  local haystack="$1"
  local needle="$2"
  if [[ "${haystack}" != *"${needle}"* ]]; then
    fail "expected to find '${needle}'"
  fi
}

assert_not_contains() {
  local haystack="$1"
  local needle="$2"
  if [[ "${haystack}" == *"${needle}"* ]]; then
    fail "did not expect to find '${needle}'"
  fi
}

[[ -f "${TARGET_SCRIPT}" ]] || fail "missing ${TARGET_SCRIPT}"

help_output="$(bash "${TARGET_SCRIPT}" --help)"
assert_contains "${help_output}" 'None` when no closable issues are found'
assert_contains "${help_output}" "Promote referenced gwt-spec issues"
assert_not_contains "${help_output}" "non-SPEC"

template_contents="$(cat "${GH_PR_TEMPLATE}")"
assert_contains "${template_contents}" "## Closing Issues"
assert_contains "${template_contents}" "Write \"None\" if no issues to close."
assert_contains "${template_contents}" "Include gwt-spec issues here when they should close on release."

skill_contents="$(cat "${GH_PR_SKILL}")"
assert_contains "${skill_contents}" "| Closing Issues | **YES** |"
assert_contains "${skill_contents}" "gwt-spec issues may appear in Closing Issues for release PRs"

release_contents="$(cat "${RELEASE_COMMAND}")"
assert_contains "${release_contents}" "scripts/release/collect-closing-issues.sh"
assert_contains "${release_contents}" "gwt-spec Issue も release PR の Closing Issues に含める"
assert_contains "${release_contents}" "Related Issues / Links にある gwt-spec も Closing Issues に昇格"

tmpdir="$(mktemp -d)"
trap 'rm -rf "${tmpdir}"' EXIT

mkdir -p "${tmpdir}/bin"

cat > "${tmpdir}/bin/git" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "log" && "$2" == "--merges" ]]; then
  cat <<'OUT'
Merge pull request #158 from akiojin/bugfix/issue-137
Merge pull request #160 from akiojin/feature/spec-owned-flow
OUT
  exit 0
fi

if [[ "$1" == "log" && "$2" == "--format=%B" ]]; then
  cat <<'OUT'
release prep

Resolves #404
OUT
  exit 0
fi

echo "unexpected git invocation: $*" >&2
exit 1
EOF

cat > "${tmpdir}/bin/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ "$1" == "pr" && "$2" == "view" ]]; then
  case "$3" in
    158)
      cat <<'OUT'
## Summary

- normal bugfix

## Closing Issues

Closes #137
Closes #201

## Related Issues / Links

- #777
OUT
      ;;
    160)
      cat <<'OUT'
## Summary

- fallback keyword only

Fixes #301

## Related Issues / Links

- #888
OUT
      ;;
    *)
      echo "unexpected pr number: $3" >&2
      exit 1
      ;;
  esac
  exit 0
fi

if [[ "$1" == "issue" && "$2" == "view" ]]; then
  case "$3" in
    137)
      printf '{"labels":[],"title":"bug: fix broken accessibility"}\n'
      ;;
    201)
      printf '{"labels":[{"name":"gwt-spec"}],"title":"gwt-spec: keep spec open"}\n'
      ;;
    301)
      printf '{"labels":[],"title":"bug: normal issue from fallback"}\n'
      ;;
    404)
      printf '{"labels":[],"title":"gwt-spec: release pipeline guard"}\n'
      ;;
    777)
      printf '{"labels":[{"name":"gwt-spec"}],"title":"gwt-spec: related-only spec"}\n'
      ;;
    888)
      printf '{"labels":[],"title":"docs: related-only normal issue"}\n'
      ;;
    *)
      echo "unexpected issue number: $3" >&2
      exit 1
      ;;
  esac
  exit 0
fi

echo "unexpected gh invocation: $*" >&2
exit 1
EOF

chmod +x "${tmpdir}/bin/git" "${tmpdir}/bin/gh"

output="$(
  PATH="${tmpdir}/bin:${PATH}" \
  bash "${TARGET_SCRIPT}" --range 'v0.9.0..HEAD'
)"

assert_contains "${output}" "Closes #137"
assert_contains "${output}" "Closes #201"
assert_contains "${output}" "Closes #301"
assert_contains "${output}" "Closes #404"
assert_contains "${output}" "Closes #777"
assert_not_contains "${output}" "Closes #888"

echo "PASS"
