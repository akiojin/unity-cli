---
name: gh-fix-ci
description: Backward-compatible wrapper skill. The current workflow is `gh-fix-pr`.
metadata:
  short-description: Compatibility wrapper for gh-fix-pr
  deprecated: true
---

# GitHub PR Fix Workflow (compatibility)

`gh-fix-ci` は既存利用者向けのエイリアスです。新規利用は `gh-fix-pr` を使用してください。

For execution, follow exactly the same procedure as:

- `github/skills/gh-fix-pr/SKILL.md`

This file intentionally keeps behavior stable by delegating to `gh-fix-pr` in the same
repo path.
