---
name: gh-pr
description: "Create or update GitHub Pull Requests with the gh CLI, including deciding whether to create a new PR or only push based on existing PR merge status. Use when the user asks to open/create/edit a PR, generate a PR body/template, or says 'PRを出して/PR作成/gh pr'. Defaults: base=develop, head=current branch (same-branch only; never create/switch branches)."
---

# GH PR

## Overview

Create or update GitHub Pull Requests with the gh CLI using a detailed body template and strict same-branch rules.

## Decision rules (must follow)

1. **Do not create or switch branches.** Always use the current branch as the PR head.
2. **Check local working tree state before push/PR operations.**
   - `git status --porcelain`
   - If output is non-empty (tracked or untracked changes), pause and ask the user what to do.
   - Present 3 options: continue as-is, abort, or manual cleanup then rerun.
   - **Do not** run `git stash`, `git commit`, or `git clean` automatically unless explicitly requested.
3. **Check for an existing PR for the current head branch.**
   - `gh pr list --head <head> --state all --json number,state,mergedAt,updatedAt,url,title,mergeCommit`
4. **If no PR exists** → create a new PR.
5. **If any PR exists and is NOT merged** (`mergedAt` is null) → push only and finish (do **not** create a new PR).
   - This applies to OPEN or CLOSED (unmerged) PRs.
   - Only update title/body/labels if the user explicitly requests changes.
6. **If all PRs for the head are merged** → check for post-merge commits (see below).
7. **If multiple PRs exist for the head** → use the most recently updated PR for reporting, but the create vs push decision is based on `mergedAt`.

## Post-merge commit check (critical)

When all PRs for the head branch are merged, you **must** check whether there are new commits after the merge:

1. **Get the merge commit SHA** of the most recent merged PR.
2. **Count commits after the merge**: `git rev-list --count <merge_commit>..HEAD`
3. **Decision**:
   - If new commits exist → create a new PR (these changes are not in the base branch)
   - If no new commits → report "No changes since last merge" and finish (do **not** create an empty PR)

### Why this matters

- **Scenario A**: PR merged → user makes local changes → pushes → changes are NOT in the merged PR
  - Without this check, the changes would be lost or require manual intervention
- **Scenario B**: PR merged → user says "create PR" without new changes → would create empty/duplicate PR
  - This check prevents unnecessary PR creation

## PR title rules (must follow)

1. **Format**: `<type>(<scope>): <subject>` — Conventional Commits 形式に準拠する。
2. **type**: `feat` / `fix` / `docs` / `chore` / `refactor` / `test` / `ci` / `perf` のいずれか。
3. **scope**: 省略可。変更の影響範囲を端的に示す（例: `gui`, `core`, `pty`）。
4. **subject**: 70文字以内。命令形（imperative mood）で書く（例: "add …" / "fix …"）。先頭大文字禁止、末尾ピリオド禁止。
5. ブランチ名にプレフィックス（`feat/`, `fix/` など）がある場合、**タイトルの type と一致させる**。

## PR body rules (must follow)

### Section classification

| Section | Required | Notes |
|---------|----------|-------|
| Summary | **YES** | 1-3 bullet points。"what" と "why" を両方含める |
| Changes | **YES** | ファイル/モジュール単位で変更内容を列挙 |
| Testing | **YES** | 実行したコマンドまたは手動テスト手順を具体的に記載 |
| Related Issues / Links | **YES** | Issue番号、spec、または "None" を明記 |
| Checklist | **YES** | 全項目を確認してチェック/N-A を付ける |
| Context | Conditional | 3ファイル以上の変更、または非自明な変更理由がある場合は必須 |
| Risk / Impact | Conditional | 破壊的変更・パフォーマンス影響・ロールバック手順がある場合は必須 |
| Screenshots | Conditional | UI 変更がある場合のみ必須 |
| Deployment | Optional | デプロイ手順がある場合のみ記載 |
| Notes | Optional | レビュアーへの補足がある場合のみ記載 |

### Validation (agent must check before creating PR)

1. **Required セクションに `TODO` が残っていたら PR を作成してはならない。**
2. Conditional セクションが該当しない場合は、セクション自体を削除する（空の TODO を残さない）。
3. Summary の各 bullet は **1文で完結** させる。曖昧な表現（"いくつかの変更", "various fixes"）を禁止する。
4. Changes は **変更ファイルまたはモジュール名を含む** 具体的な記述にする。
5. Testing は **再現可能な手順** を書く（"テスト済み" のような曖昧な記述を禁止）。
6. Checklist の未チェック項目には理由コメントを付ける（例: `- [ ] Docs updated — N/A: no user-facing change`）。
7. Related Issues は `#123` 形式または URL で記載する。該当なしの場合は "None" と明記する。

## Issue/PR Comment Formatting (must follow)

- Final comment text must not contain escaped newline literals such as `\n`.
- Use real line breaks in comment bodies. Do not rely on escaped sequences for formatting.
- Before posting, verify the final body does not accidentally include escaped control sequences (`\n`, `\t`).
- If a raw escape sequence must be shown for explanation, include it only inside a fenced code block and clarify it is intentional.

## Issue Progress Comment Template (required for issue-based work)

When work is tracked in GitHub Issues, progress updates must use this template:

```markdown
Progress
- ...

Done
- ...

Next
- ...
```

- Post updates at least when starting work, after meaningful progress, and when blocked/unblocked.
- In `Next`, explicitly state blockers or the immediate next action.

## Workflow (recommended)

1. **Confirm repo + branches**
   - Repo root: `git rev-parse --show-toplevel`
   - Current branch (head): `git rev-parse --abbrev-ref HEAD`
   - Base branch defaults to `develop` unless user specifies.

2. **Check local working tree state (preflight)**
   - Run `git status --porcelain`.
   - If empty, continue.
   - If non-empty, show detected files and ask the user to choose:
     - Continue as-is
     - Abort
     - Manual cleanup first (`git commit` / `git stash` / `git clean`) and rerun
   - Proceed only when the user explicitly chooses continue.

3. **Fetch latest remote state**
   - `git fetch origin` to ensure accurate comparison

4. **Check existing PR for head branch**
   - Use decision rules above to pick action.
   - Treat `mergedAt` as the source of truth for "merged".

5. **If all PRs are merged, perform post-merge commit check**
   - Get merge commit: `gh pr list --head <head> --state merged --json mergeCommit -q '.[0].mergeCommit.oid'`
   - Count new commits: `git rev-list --count <merge_commit>..HEAD`
   - If 0 → finish with message "No new changes since merge"
   - If >0 → proceed to create new PR

6. **Ensure the head branch is pushed**
   - If no upstream: `git push -u origin <head>`
   - Otherwise: `git push`

7. **Collect PR inputs (for new PR or explicit update)**
   - Title, Summary, Context, Changes, Testing, Risk/Impact, Deployment, Screenshots, Related Links, Notes
   - Optional: labels, reviewers, assignees, draft

8. **Build PR body from template**
  - Read the template from the gh-pr skill path (not the current project path):
    - `GH_PR_SKILL_DIR="${GH_PR_SKILL_DIR:-$HOME/.codex/skills/gh-pr}"`
    - `PR_BODY_TEMPLATE="${GH_PR_SKILL_DIR}/references/pr-body-template.md"`
  - Read `${PR_BODY_TEMPLATE}` and fill all required placeholders.
  - **Conditional セクションが該当しない場合はセクションごと削除する。**
  - **テンプレート内の `<!-- GUIDE: ... -->` コメントは最終出力から削除する。**
  - **Required セクションに TODO が残っている場合は PR を作成せず、ユーザーに不足情報を確認する。**

9. **Create or update the PR**
   - Create: `gh pr create -B <base> -H <head> --title "<title>" --body-file <file>`
   - Update (only if user asked): `gh pr edit <number> --title "<title>" --body-file <file>`

10. **Return PR URL**
   - `gh pr view <number> --json url -q .url`

11. **Post-PR CI/merge check (automatic).**
   - After PR creation or push, load `github/skills/gh-fix-ci/SKILL.md` and
     follow its workflow to inspect CI status, merge state, and review feedback.
   - If all CI checks are still pending, poll (30s interval) until complete.
   - If conflicts, review issues, or CI failures are detected, proceed with
     the gh-fix-ci workflow to diagnose and fix.

## Command snippets (bash)

```bash
head=$(git rev-parse --abbrev-ref HEAD)
base=develop
GH_PR_SKILL_DIR="${GH_PR_SKILL_DIR:-$HOME/.codex/skills/gh-pr}"
PR_BODY_TEMPLATE="${GH_PR_SKILL_DIR}/references/pr-body-template.md"

if [ ! -f "$PR_BODY_TEMPLATE" ]; then
  echo "PR template not found: $PR_BODY_TEMPLATE" >&2
  exit 1
fi

# Preflight: local working tree state
status_lines=$(git status --porcelain)
if [ -n "$status_lines" ] && [ "${ALLOW_DIRTY_WORKTREE:-0}" != "1" ]; then
  echo "Detected local uncommitted/untracked changes:" >&2
  echo "$status_lines" >&2
  echo "Choose one before continuing: continue as-is, abort, or manual cleanup then rerun." >&2
  echo "Set ALLOW_DIRTY_WORKTREE=1 only after explicit user confirmation to continue." >&2
  exit 1
fi

# Fetch latest remote state
git fetch origin

# Check existing PRs for the head branch
pr_json=$(gh pr list --head "$head" --state all --json number,state,mergedAt,mergeCommit)
pr_count=$(echo "$pr_json" | jq 'length')
unmerged_count=$(echo "$pr_json" | jq 'map(select(.mergedAt == null)) | length')

if [ "$pr_count" -eq 0 ]; then
  action=create
elif [ "$unmerged_count" -gt 0 ]; then
  action=push_only
else
  # All PRs are merged - check for post-merge commits
  merge_commit=$(echo "$pr_json" | jq -r 'sort_by(.mergedAt) | last | .mergeCommit.oid')

  if [ -n "$merge_commit" ] && [ "$merge_commit" != "null" ]; then
    new_commits=$(git rev-list --count "$merge_commit"..HEAD 2>/dev/null || echo "0")

    if [ "$new_commits" -gt 0 ]; then
      echo "Found $new_commits commit(s) after merge - creating new PR"
      action=create
    else
      echo "No new commits since merge - nothing to do"
      action=none
    fi
  else
    # Fallback: check against base branch
    new_commits=$(git rev-list --count "origin/$base"..HEAD 2>/dev/null || echo "0")

    if [ "$new_commits" -gt 0 ]; then
      action=create
    else
      action=none
    fi
  fi
fi

# Execute action
case "$action" in
  create)
    cp "$PR_BODY_TEMPLATE" /tmp/pr-body.md

    git push -u origin "$head"
    gh pr create -B "$base" -H "$head" --title "..." --body-file /tmp/pr-body.md
    ;;
  push_only)
    echo "Existing unmerged PR found - pushing changes only"
    git push
    gh pr list --head "$head" --state open --json url -q '.[0].url'
    ;;
  none)
    echo "No action needed - no new changes since last merge"
    ;;
esac
```

## References

- `${GH_PR_SKILL_DIR}/references/pr-body-template.md`: PR body template
