---
name: gh-fix-issue
description: Analyze a GitHub Issue to extract error context, stack traces, file references, and cross-references. Classify the issue, search the codebase for relevant files, produce a structured Issue Analysis Report, and propose a concrete fix plan. Post progress updates to the issue.
metadata:
  short-description: Analyze GitHub Issues and propose fix plans
---

# Gh Fix Issue

## Overview

Use gh to inspect a GitHub Issue and:

- Fetch issue metadata (title, body, state, labels, assignees)
- Fetch all comments
- Fetch linked PRs via timeline events
- Extract error messages, stack traces, file references, code blocks, and cross-references
- Classify issue type (BUG / FEATURE / ENHANCEMENT / DOCUMENTATION / QUESTION)
- Search the codebase for relevant files
- Propose a fix plan with confidence levels

Then produce a structured Issue Analysis Report, post progress to the issue, and implement fixes after explicit approval.

- Depends on the `plan` skill for drafting and approving the fix plan.

Prereq: ensure `gh` is authenticated (for example, run `gh auth login` once), then run `gh auth status` with escalated permissions (include repo scope) so `gh` commands succeed.

## Analysis Report Anti-Patterns

### Prohibited Language

| Prohibited | Required Alternative |
|---|---|
| "We should look into..." | "Edit `path/file.ts:42` to..." |
| "There seem to be some issues" | "3 actionable items detected" |
| "This might be causing..." | "Root cause: `<error from issue>`" |
| "Consider fixing..." / "It looks like..." | "Action: Fix `<what>` in `<where>`" |
| "Various errors are reported" | "2 error messages extracted: `<msg1>`, `<msg2>`" |
| "Some files are involved" | "3 file references: `src/a.ts:42`, `src/b.rs:10`, `src/c.py`" |
| "I'll try to fix this" | "Action: \<specific fix\>" |

### Structural Prohibitions

- Prose paragraphs for reporting — use A1/I1 item format exclusively.
- Omitting the Evidence field in any ACTIONABLE item.
- Combining multiple independent problems into a single item.
- Omitting file paths or line numbers when the script output contains them.

## Issue/PR Comment Formatting (must follow)

- Final comment text must not contain escaped newline literals such as `\n`.
- Use real line breaks in comment bodies. Do not rely on escaped sequences for formatting.
- Before posting (`gh issue comment`), verify the final body does not accidentally include escaped control sequences (`\n`, `\t`).
- If a raw escape sequence must be shown for explanation, include it only inside a fenced code block and clarify it is intentional.

## Issue Progress Comment Template (required)

When posting progress updates to the issue, use this template:

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

## Inputs

- `repo`: path inside the repo (default `.`)
- `issue`: Issue number or URL (required)
- `focus`: codebase search narrowing (optional; e.g., `src/lib/components`)
- `max-comment-length`: max characters per comment body (0 = unlimited)
- `gh` authentication for the repo host

## Quick start

```bash
# Inspect issue (text output)
python3 "${CLAUDE_PLUGIN_ROOT}/github/skills/gh-fix-issue/scripts/inspect_issue.py" --repo "." --issue "<number>"

# Inspect issue by URL
python3 "${CLAUDE_PLUGIN_ROOT}/github/skills/gh-fix-issue/scripts/inspect_issue.py" --repo "." --issue "https://github.com/org/repo/issues/123"

# With focus area
python3 "${CLAUDE_PLUGIN_ROOT}/github/skills/gh-fix-issue/scripts/inspect_issue.py" --repo "." --issue "<number>" --focus "src/lib"

# JSON output
python3 "${CLAUDE_PLUGIN_ROOT}/github/skills/gh-fix-issue/scripts/inspect_issue.py" --repo "." --issue "<number>" --json

# Limit comment length
python3 "${CLAUDE_PLUGIN_ROOT}/github/skills/gh-fix-issue/scripts/inspect_issue.py" --repo "." --issue "<number>" --max-comment-length 500
```

## Workflow

1. **Verify gh authentication.**
   - Run `gh auth status` in the repo with escalated scopes (repo).
   - If unauthenticated, ask the user to log in before proceeding.

2. **Resolve the Issue.**
   - Accept issue number or full URL.
   - Validate that the issue exists and is accessible.

3. **Run inspect_issue.py to fetch and parse data.**
   - Fetch issue metadata, comments, and linked PRs.
   - Parse body and comments for error messages, stack traces, file references, code blocks, sections, and cross-references.
   - Classify the issue type.
   - Check file existence for extracted file references.

4. **Classify the Issue.**
   - Use labels (highest priority) and body/title heuristics.
   - Classification: BUG, FEATURE, ENHANCEMENT, DOCUMENTATION, QUESTION, UNCLASSIFIED.
   - BUG issues proceed to fix planning; FEATURE/ENHANCEMENT proceed to implementation planning.

5. **Search the codebase for relevant context.**
   - Use extracted file references as starting points.
   - Search for error messages and symbols mentioned in the issue.
   - If `--focus` is provided, restrict search to that area.
   - Use Grep/Glob to find related files and definitions.

6. **Produce Issue Analysis Report (mandatory format).**

   Output MUST use this exact structure:

   ```text
   ## Issue Analysis Report: #<number>

   **Issue Type:** BUG | FEATURE | ENHANCEMENT | DOCUMENTATION | QUESTION | UNCLASSIFIED
   **Title:** <issue title>
   **State:** OPEN | CLOSED
   **Labels:** <label1>, <label2>, ...
   **Assignees:** <assignee1>, <assignee2>, ...
   Actionable items: <N>

   ---

   ### EXTRACTED CONTEXT

   #### Error Messages
   - `<error message 1>`
   - `<error message 2>`

   #### Stack Traces
   ```
   <stack trace>
   ```

   #### File References
   - `path/to/file.ext:42` [EXISTS]
   - `path/to/other.ext:10` [NOT FOUND]

   #### Repro Steps
   <extracted Steps to Reproduce section>

   #### Expected vs Actual
   - **Expected:** <extracted expected behavior>
   - **Actual:** <extracted actual behavior>

   ---

   ### CODEBASE MATCHES

   #### M1. <file or symbol>
   - **Path:** `path/to/file.ext:line`
   - **Relevance:** Why this file matters

   #### M2. <file or symbol>
   ...

   ---

   ### ACTIONABLE

   #### A1. [CATEGORY] <1-line title>
   - **What:** Factual statement (no speculation)
   - **Where:** file_path:line_number
   - **Evidence:** Verbatim quote from issue or codebase
   - **Action:** Specific fix (file path, command, or code change)
   - **Confidence:** High | Medium | Low

   #### A2. [CATEGORY] <1-line title>
   ...

   ---

   ### INFORMATIONAL

   #### I1. [CATEGORY] <1-line title>
   - **What / Note**

   ---

   ### LINKED CONTEXT

   #### Linked PRs
   - PR #<number>: <title> [<state>]

   #### Cross-references
   - #<number>
   - org/repo#<number>

   #### Comments Summary
   - <N> comments from <M> authors
   - Key points: ...

   ---

   **Summary:** <N> actionable items, <M> informational items, <K> codebase matches.
   ```

   **Category labels:** `CODE-FIX`, `CONFIG-FIX`, `TEST-FIX`, `DEPENDENCY`, `DOCUMENTATION`, `DESIGN-DECISION`, `INVESTIGATION`

   **Confidence judgment:**

   - **High** — Clear error with obvious fix location, stack trace points to exact line
   - **Medium** — Error pattern matches but fix location needs investigation
   - **Low** — Requires design decision or the root cause is unclear

7. **Decide execution path based on Confidence.**
   - If ALL actionable items have Confidence: High → display report, propose plan, proceed after approval.
   - If ANY actionable item has Confidence: Low → request user guidance for low-confidence items before planning.
   - For FEATURE/ENHANCEMENT types → always create a plan and request approval.

8. **Post progress comment to the Issue.**
   - Use the Issue Progress Comment Template.
   - Include analysis summary and planned actions.
   - Command: `gh issue comment <number> -b "<body>"`

9. **Implement fixes after approval.**
   - Apply the approved fixes, summarize diffs/tests.
   - After applying fixes, commit changes and push.
   - Create or update a PR linking to the issue (e.g., `Fixes #<number>`).
   - Post a final progress comment to the issue.

## Bundled Resources

### scripts/inspect_issue.py

GitHub Issue inspection and analysis tool. Fetches issue data, parses error context, classifies the issue, and checks file existence.

**Arguments:**

| Argument | Default | Description |
|----------|---------|-------------|
| `--repo` | `.` | Path inside the target Git repository |
| `--issue` | (required) | Issue number or URL |
| `--focus` | (none) | Codebase search narrowing area |
| `--max-comment-length` | 0 (unlimited) | Max characters per comment body |
| `--json` | false | Emit JSON output |

**Exit codes:**

- `0`: Success
- `1`: Error occurred

## Features

### Issue Data Fetching

Fetches comprehensive issue data:

- Title, body, state, labels, assignees, author
- All comments (with optional length truncation)
- Linked PRs via GraphQL timeline events (CrossReferencedEvent, ConnectedEvent)

### Error Context Extraction

Parses issue body and comments for:

- Error messages (`Error:`, `TypeError:`, `panicked at`, etc.)
- Stack traces (`at `, `Traceback`, `thread '...' panicked`, etc.)
- File path references (`path/to/file.ext:123` format)
- Fenced code blocks
- Well-known sections (Steps to Reproduce, Expected Behavior, Actual Behavior)
- Cross-references (`#123`, `org/repo#123`)

### Issue Classification

Classifies based on labels (highest priority) and body/title heuristics:

- **BUG**: `bug`, `defect`, `regression`, `crash`, `error` labels or error indicators in body
- **FEATURE**: `feature`, `feature-request` labels or feature request language
- **ENHANCEMENT**: `enhancement`, `improvement` labels
- **DOCUMENTATION**: `documentation`, `docs` labels
- **QUESTION**: `question`, `help`, `support` labels or question language
- **UNCLASSIFIED**: No matching signals

### File Existence Check

Validates extracted file references against the repository:

- Checks if files exist at the referenced paths
- Reports `[EXISTS]` or `[NOT FOUND]` status

## Output Examples

### Text Output

```text
Issue #42: TypeError when clicking save button
============================================================
State: OPEN
Type: BUG
Labels: bug, ui
Assignees: developer1
Author: @reporter1
URL: https://github.com/org/repo/issues/42

BODY
------------------------------------------------------------
When I click the save button on the settings page, I get this error:

```
TypeError: Cannot read properties of undefined (reading 'name')
    at SaveHandler (src/components/Settings.tsx:42)
    at onClick (src/components/Button.tsx:15)
```

### Steps to Reproduce
1. Open Settings page
2. Change any setting
3. Click Save

### Expected Behavior
Settings should be saved successfully.

### Actual Behavior
TypeError is thrown and settings are not saved.

EXTRACTED SECTIONS
------------------------------------------------------------

[Steps To Reproduce]
1. Open Settings page
2. Change any setting
3. Click Save

[Expected]
Settings should be saved successfully.

[Actual]
TypeError is thrown and settings are not saved.

ERROR MESSAGES (1)
------------------------------------------------------------
  [1] TypeError: Cannot read properties of undefined (reading 'name')

STACK TRACES (1)
------------------------------------------------------------
  [1]
    at SaveHandler (src/components/Settings.tsx:42)
    at onClick (src/components/Button.tsx:15)

FILE REFERENCES (2)
------------------------------------------------------------
  src/components/Settings.tsx:42 [EXISTS]
  src/components/Button.tsx:15 [EXISTS]

COMMENTS (1)
------------------------------------------------------------
@maintainer1 (2025-01-20):
  This might be related to the recent refactor in #38.

LINKED PULL REQUESTS (1)
------------------------------------------------------------
  PR #45: Fix settings save handler [OPEN]
    https://github.com/org/repo/pull/45
============================================================
```

### JSON Output

```json
{
  "issue": {
    "number": 42,
    "title": "TypeError when clicking save button",
    "body": "...",
    "state": "OPEN",
    "labels": [{"name": "bug"}, {"name": "ui"}],
    "assignees": [{"login": "developer1"}],
    "author": {"login": "reporter1"},
    "url": "https://github.com/org/repo/issues/42"
  },
  "issueType": "BUG",
  "comments": [
    {
      "id": 123456,
      "author": "maintainer1",
      "body": "This might be related to the recent refactor in #38.",
      "createdAt": "2025-01-20T12:00:00Z"
    }
  ],
  "linkedPRs": [
    {
      "number": 45,
      "title": "Fix settings save handler",
      "state": "OPEN",
      "url": "https://github.com/org/repo/pull/45"
    }
  ],
  "parsed": {
    "errorMessages": [
      "TypeError: Cannot read properties of undefined (reading 'name')"
    ],
    "stackTraces": [
      "at SaveHandler (src/components/Settings.tsx:42)\n    at onClick (src/components/Button.tsx:15)"
    ],
    "fileReferences": [
      "src/components/Settings.tsx:42",
      "src/components/Button.tsx:15"
    ],
    "codeBlocks": ["TypeError: Cannot read properties of undefined..."],
    "sections": {
      "steps_to_reproduce": "1. Open Settings page\n2. Change any setting\n3. Click Save",
      "expected": "Settings should be saved successfully.",
      "actual": "TypeError is thrown and settings are not saved."
    },
    "crossReferences": [
      {"repo": "", "number": 38, "ref": "#38"}
    ]
  },
  "fileChecks": [
    {"reference": "src/components/Settings.tsx:42", "path": "src/components/Settings.tsx", "exists": true},
    {"reference": "src/components/Button.tsx:15", "path": "src/components/Button.tsx", "exists": true}
  ]
}
```
