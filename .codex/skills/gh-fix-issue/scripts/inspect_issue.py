#!/usr/bin/env python3
"""
inspect_issue.py - GitHub Issue inspection and analysis tool

Fetches Issue data (title, body, state, labels, assignees, comments),
parses error messages, stack traces, file references, code blocks,
and cross-references. Classifies the issue type and checks file existence.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Sequence


# =============================================================================
# Constants
# =============================================================================

ISSUE_TYPES = ("BUG", "FEATURE", "ENHANCEMENT", "DOCUMENTATION", "QUESTION", "UNCLASSIFIED")

BUG_LABELS = {"bug", "defect", "regression", "crash", "error"}
FEATURE_LABELS = {"feature", "feature-request", "enhancement", "improvement"}
DOCUMENTATION_LABELS = {"documentation", "docs", "doc"}
QUESTION_LABELS = {"question", "help", "support"}

ERROR_PATTERNS = (
    re.compile(r"(?:^|\s)(Error:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(TypeError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(ReferenceError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(SyntaxError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(RuntimeError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(ValueError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(KeyError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(AttributeError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(ImportError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(ModuleNotFoundError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(IOError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(OSError:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(panicked at\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(thread '.+' panicked at .+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(FATAL:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(FAILED:\s*.+)", re.MULTILINE),
    re.compile(r"(?:^|\s)(error\[E\d+\]:\s*.+)", re.MULTILINE),
)

STACK_TRACE_PATTERNS = (
    re.compile(r"(^\s+at\s+.+$)", re.MULTILINE),
    re.compile(r"(^Traceback \(most recent call last\):.*?)(?=^\S|\Z)", re.MULTILINE | re.DOTALL),
    re.compile(r"(thread '.+' panicked at .+[\s\S]*?stack backtrace:[\s\S]*?)(?=\n\n|\Z)", re.MULTILINE),
    re.compile(r"(^\s*\d+:\s+0x[0-9a-f]+\s+-\s+.+$)", re.MULTILINE),
)

FILE_PATH_PATTERN = re.compile(
    r"(?:^|[\s`\"'(])([a-zA-Z0-9_./-]+\.[a-zA-Z0-9]+(?::\d+)?(?::\d+)?)(?:[\s`\"'),]|$)",
    re.MULTILINE,
)

CROSS_REF_PATTERN = re.compile(
    r"(?:^|[\s(])(?:(?:([a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+))?#(\d+))(?:[\s),.]|$)",
    re.MULTILINE,
)

SECTION_PATTERNS = {
    "steps_to_reproduce": re.compile(
        r"#+\s*(?:Steps?\s+to\s+Reproduce|Reproduction\s+Steps?|How\s+to\s+Reproduce|STR)\s*\n(.*?)(?=\n#+\s|\Z)",
        re.IGNORECASE | re.DOTALL,
    ),
    "expected": re.compile(
        r"#+\s*(?:Expected\s+(?:Behavior|Result|Outcome|Output))\s*\n(.*?)(?=\n#+\s|\Z)",
        re.IGNORECASE | re.DOTALL,
    ),
    "actual": re.compile(
        r"#+\s*(?:Actual\s+(?:Behavior|Result|Outcome|Output)|What\s+(?:Happened|Occurs))\s*\n(.*?)(?=\n#+\s|\Z)",
        re.IGNORECASE | re.DOTALL,
    ),
}

CODE_BLOCK_PATTERN = re.compile(r"```[\w]*\n(.*?)```", re.DOTALL)

# File extensions likely to be source code paths
SOURCE_EXTENSIONS = {
    ".rs", ".py", ".ts", ".tsx", ".js", ".jsx", ".svelte", ".vue",
    ".go", ".java", ".kt", ".c", ".cpp", ".h", ".hpp", ".cs",
    ".rb", ".php", ".swift", ".sh", ".bash", ".zsh",
    ".toml", ".yaml", ".yml", ".json", ".xml", ".html", ".css", ".scss",
    ".md", ".txt", ".cfg", ".ini", ".env",
}


class GhResult:
    def __init__(self, returncode: int, stdout: str, stderr: str):
        self.returncode = returncode
        self.stdout = stdout
        self.stderr = stderr


# =============================================================================
# Git and GH utilities
# =============================================================================

def run_gh_command(args: Sequence[str], cwd: Path) -> GhResult:
    process = subprocess.run(
        ["gh", *args],
        cwd=cwd,
        text=True,
        capture_output=True,
    )
    return GhResult(process.returncode, process.stdout, process.stderr)


def find_git_root(start: Path) -> Path | None:
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=start,
        text=True,
        capture_output=True,
    )
    if result.returncode != 0:
        return None
    return Path(result.stdout.strip())


def ensure_gh_available(repo_root: Path) -> bool:
    result = run_gh_command(["auth", "status"], cwd=repo_root)
    if result.returncode == 0:
        return True
    message = (result.stderr or result.stdout or "").strip()
    print(message or "Error: gh not authenticated.", file=sys.stderr)
    return False


def fetch_repo_slug(repo_root: Path) -> str | None:
    result = run_gh_command(["repo", "view", "--json", "nameWithOwner"], cwd=repo_root)
    if result.returncode != 0:
        return None
    try:
        data = json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        return None
    name_with_owner = data.get("nameWithOwner")
    if not name_with_owner:
        return None
    return str(name_with_owner)


def parse_repo_owner_name(repo_slug: str) -> tuple[str, str] | None:
    """Parse 'owner/repo' into (owner, repo)."""
    parts = repo_slug.split("/")
    if len(parts) != 2:
        return None
    return parts[0], parts[1]


# =============================================================================
# Issue resolution
# =============================================================================

def extract_issue_number(issue_value: str) -> str | None:
    """Extract issue number from a number or URL."""
    if issue_value.isdigit():
        return issue_value
    match = re.search(r"/issues/(\d+)", issue_value)
    if match:
        return match.group(1)
    return None


def resolve_issue(issue_value: str, repo_root: Path) -> str | None:
    """Resolve an issue number from user input."""
    extracted = extract_issue_number(issue_value)
    if extracted:
        return extracted
    result = run_gh_command(
        ["issue", "view", issue_value, "--json", "number"],
        cwd=repo_root,
    )
    if result.returncode != 0:
        message = (result.stderr or result.stdout or "").strip()
        print(message or "Error: unable to resolve issue.", file=sys.stderr)
        return None
    try:
        data = json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        print("Error: unable to parse issue JSON.", file=sys.stderr)
        return None
    number = data.get("number")
    if not number:
        print("Error: no issue number found.", file=sys.stderr)
        return None
    return str(number)


# =============================================================================
# Issue data fetching
# =============================================================================

def fetch_issue_data(issue_number: str, repo_root: Path) -> dict[str, Any] | None:
    """Fetch issue metadata via gh issue view."""
    fields = "number,title,body,state,labels,assignees,author,createdAt,updatedAt,url"
    result = run_gh_command(
        ["issue", "view", issue_number, "--json", fields],
        cwd=repo_root,
    )
    if result.returncode != 0:
        message = (result.stderr or result.stdout or "").strip()
        print(message or f"Error: failed to fetch issue #{issue_number}.", file=sys.stderr)
        return None
    try:
        data = json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        print("Error: unable to parse issue data.", file=sys.stderr)
        return None
    return data


def fetch_issue_comments(
    issue_number: str,
    repo_root: Path,
    max_comment_length: int = 0,
) -> list[dict[str, Any]]:
    """Fetch all comments on an issue."""
    repo_slug = fetch_repo_slug(repo_root)
    if not repo_slug:
        return []

    result = run_gh_command(
        ["api", f"repos/{repo_slug}/issues/{issue_number}/comments?per_page=100"],
        cwd=repo_root,
    )
    if result.returncode != 0:
        return []

    try:
        comments = json.loads(result.stdout or "[]")
    except json.JSONDecodeError:
        return []

    if not isinstance(comments, list):
        return []

    formatted: list[dict[str, Any]] = []
    for comment in comments:
        if not isinstance(comment, dict):
            continue
        body = (comment.get("body") or "").strip()
        if max_comment_length > 0 and len(body) > max_comment_length:
            body = body[:max_comment_length] + "..."
        formatted.append({
            "id": comment.get("id"),
            "author": (comment.get("user") or {}).get("login", "unknown"),
            "body": body,
            "createdAt": comment.get("created_at", ""),
            "htmlUrl": comment.get("html_url", ""),
        })
    return formatted


# =============================================================================
# Timeline events (linked PRs)
# =============================================================================

def fetch_timeline_linked_prs(
    issue_number: str,
    repo_root: Path,
) -> list[dict[str, Any]]:
    """Fetch linked PRs via GraphQL timeline events."""
    repo_slug = fetch_repo_slug(repo_root)
    if not repo_slug:
        return []

    parsed = parse_repo_owner_name(repo_slug)
    if not parsed:
        return []

    owner, repo = parsed

    query = """
    query($owner: String!, $repo: String!, $number: Int!) {
      repository(owner: $owner, name: $repo) {
        issue(number: $number) {
          timelineItems(first: 100, itemTypes: [CROSS_REFERENCED_EVENT, CONNECTED_EVENT]) {
            nodes {
              __typename
              ... on CrossReferencedEvent {
                source {
                  __typename
                  ... on PullRequest {
                    number
                    title
                    state
                    url
                  }
                }
              }
              ... on ConnectedEvent {
                subject {
                  __typename
                  ... on PullRequest {
                    number
                    title
                    state
                    url
                  }
                }
              }
            }
          }
        }
      }
    }
    """

    result = run_gh_command(
        [
            "api", "graphql",
            "-f", f"query={query}",
            "-f", f"owner={owner}",
            "-f", f"repo={repo}",
            "-F", f"number={issue_number}",
        ],
        cwd=repo_root,
    )

    if result.returncode != 0:
        return []

    try:
        data = json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        return []

    nodes = (
        data.get("data", {})
        .get("repository", {})
        .get("issue", {})
        .get("timelineItems", {})
        .get("nodes", [])
    )

    seen: set[int] = set()
    linked_prs: list[dict[str, Any]] = []

    for node in nodes:
        typename = node.get("__typename", "")
        pr_data: dict[str, Any] | None = None

        if typename == "CrossReferencedEvent":
            source = node.get("source") or {}
            if source.get("__typename") == "PullRequest":
                pr_data = source
        elif typename == "ConnectedEvent":
            subject = node.get("subject") or {}
            if subject.get("__typename") == "PullRequest":
                pr_data = subject

        if pr_data and pr_data.get("number") and pr_data["number"] not in seen:
            seen.add(pr_data["number"])
            linked_prs.append({
                "number": pr_data["number"],
                "title": pr_data.get("title", ""),
                "state": pr_data.get("state", ""),
                "url": pr_data.get("url", ""),
            })

    return linked_prs


# =============================================================================
# Body / comment parsing
# =============================================================================

def extract_error_messages(text: str) -> list[str]:
    """Extract error messages from text."""
    errors: list[str] = []
    seen: set[str] = set()
    for pattern in ERROR_PATTERNS:
        for match in pattern.finditer(text):
            msg = match.group(1).strip()
            if msg not in seen:
                seen.add(msg)
                errors.append(msg)
    return errors


def extract_stack_traces(text: str) -> list[str]:
    """Extract stack traces from text."""
    traces: list[str] = []
    seen: set[str] = set()
    for pattern in STACK_TRACE_PATTERNS:
        for match in pattern.finditer(text):
            trace = match.group(1).strip()
            if trace and trace not in seen:
                seen.add(trace)
                traces.append(trace)
    return traces


def extract_file_references(text: str) -> list[str]:
    """Extract file path references (path/to/file.ext:line) from text."""
    refs: list[str] = []
    seen: set[str] = set()
    for match in FILE_PATH_PATTERN.finditer(text):
        ref = match.group(1).strip()
        # Filter: must contain a plausible file extension
        base = ref.split(":")[0]
        ext = os.path.splitext(base)[1].lower()
        if ext not in SOURCE_EXTENSIONS:
            continue
        # Skip URLs and very short matches
        if ref.startswith("http") or len(base) < 3:
            continue
        if ref not in seen:
            seen.add(ref)
            refs.append(ref)
    return refs


def extract_code_blocks(text: str) -> list[str]:
    """Extract fenced code blocks from text."""
    blocks: list[str] = []
    for match in CODE_BLOCK_PATTERN.finditer(text):
        block = match.group(1).strip()
        if block:
            blocks.append(block)
    return blocks


def extract_sections(text: str) -> dict[str, str]:
    """Extract well-known sections (Steps to Reproduce, Expected, Actual)."""
    sections: dict[str, str] = {}
    for key, pattern in SECTION_PATTERNS.items():
        match = pattern.search(text)
        if match:
            sections[key] = match.group(1).strip()
    return sections


def extract_cross_references(text: str) -> list[dict[str, Any]]:
    """Extract cross-references (#123, org/repo#123) from text."""
    refs: list[dict[str, Any]] = []
    seen: set[str] = set()
    for match in CROSS_REF_PATTERN.finditer(text):
        repo_ref = match.group(1) or ""
        number = match.group(2)
        key = f"{repo_ref}#{number}"
        if key not in seen:
            seen.add(key)
            refs.append({
                "repo": repo_ref,
                "number": int(number),
                "ref": key,
            })
    return refs


def parse_all_text(body: str, comments: list[dict[str, Any]]) -> dict[str, Any]:
    """Parse issue body and all comments, aggregating extracted data."""
    all_text = body or ""
    for comment in comments:
        comment_body = comment.get("body", "")
        if comment_body:
            all_text += "\n\n" + comment_body

    return {
        "errorMessages": extract_error_messages(all_text),
        "stackTraces": extract_stack_traces(all_text),
        "fileReferences": extract_file_references(all_text),
        "codeBlocks": extract_code_blocks(all_text),
        "sections": extract_sections(body or ""),
        "crossReferences": extract_cross_references(all_text),
    }


# =============================================================================
# Issue classification
# =============================================================================

def classify_issue(
    labels: list[str],
    body: str,
    title: str,
) -> str:
    """Classify issue type based on labels and body heuristics."""
    labels_lower = {lbl.lower() for lbl in labels}

    # Label-based classification (highest priority)
    if labels_lower & BUG_LABELS:
        return "BUG"
    if labels_lower & FEATURE_LABELS:
        return "FEATURE" if "feature" in labels_lower or "feature-request" in labels_lower else "ENHANCEMENT"
    if labels_lower & DOCUMENTATION_LABELS:
        return "DOCUMENTATION"
    if labels_lower & QUESTION_LABELS:
        return "QUESTION"

    # Body/title heuristic
    combined = (title + " " + (body or "")).lower()

    bug_indicators = (
        "error", "bug", "crash", "fail", "broken", "regression",
        "panicked", "traceback", "exception", "unexpected",
    )
    feature_indicators = (
        "feature request", "would be nice", "please add", "suggestion",
        "propose", "enhancement", "new feature",
    )
    question_indicators = (
        "how do i", "how to", "is it possible", "question",
        "help", "what is the",
    )

    if any(ind in combined for ind in bug_indicators):
        return "BUG"
    if any(ind in combined for ind in feature_indicators):
        return "FEATURE"
    if any(ind in combined for ind in question_indicators):
        return "QUESTION"

    return "UNCLASSIFIED"


# =============================================================================
# File existence check
# =============================================================================

def check_file_existence(
    file_refs: list[str],
    repo_root: Path,
) -> list[dict[str, Any]]:
    """Check whether referenced files exist in the repository."""
    results: list[dict[str, Any]] = []
    for ref in file_refs:
        path_str = ref.split(":")[0]
        full_path = repo_root / path_str
        exists = full_path.exists()
        results.append({
            "reference": ref,
            "path": path_str,
            "exists": exists,
        })
    return results


# =============================================================================
# Output rendering
# =============================================================================

def render_text_output(results: dict[str, Any]) -> None:
    """Render results in human-readable text format."""
    issue = results.get("issue", {})
    issue_number = issue.get("number", "?")
    title = issue.get("title", "")
    state = issue.get("state", "")
    issue_type = results.get("issueType", "UNCLASSIFIED")

    print(f"Issue #{issue_number}: {title}")
    print("=" * 60)
    print(f"State: {state}")
    print(f"Type: {issue_type}")

    labels = issue.get("labels", [])
    if labels:
        label_names = [lbl.get("name", "") if isinstance(lbl, dict) else str(lbl) for lbl in labels]
        print(f"Labels: {', '.join(label_names)}")

    assignees = issue.get("assignees", [])
    if assignees:
        assignee_names = [a.get("login", "") if isinstance(a, dict) else str(a) for a in assignees]
        print(f"Assignees: {', '.join(assignee_names)}")

    author = issue.get("author", {})
    if isinstance(author, dict) and author.get("login"):
        print(f"Author: @{author['login']}")

    print(f"URL: {issue.get('url', '')}")

    # Body
    body = issue.get("body", "")
    if body:
        print(f"\nBODY")
        print("-" * 60)
        print(body)

    # Extracted context
    parsed = results.get("parsed", {})

    sections = parsed.get("sections", {})
    if sections:
        print(f"\nEXTRACTED SECTIONS")
        print("-" * 60)
        for key, value in sections.items():
            label = key.replace("_", " ").title()
            print(f"\n[{label}]")
            print(value)

    errors = parsed.get("errorMessages", [])
    if errors:
        print(f"\nERROR MESSAGES ({len(errors)})")
        print("-" * 60)
        for i, err in enumerate(errors, 1):
            print(f"  [{i}] {err}")

    traces = parsed.get("stackTraces", [])
    if traces:
        print(f"\nSTACK TRACES ({len(traces)})")
        print("-" * 60)
        for i, trace in enumerate(traces, 1):
            print(f"  [{i}]")
            for line in trace.splitlines():
                print(f"    {line}")

    file_refs = parsed.get("fileReferences", [])
    if file_refs:
        print(f"\nFILE REFERENCES ({len(file_refs)})")
        print("-" * 60)
        file_checks = results.get("fileChecks", [])
        check_map = {fc["reference"]: fc["exists"] for fc in file_checks}
        for ref in file_refs:
            exists = check_map.get(ref)
            marker = " [EXISTS]" if exists else " [NOT FOUND]" if exists is not None else ""
            print(f"  {ref}{marker}")

    code_blocks = parsed.get("codeBlocks", [])
    if code_blocks:
        print(f"\nCODE BLOCKS ({len(code_blocks)})")
        print("-" * 60)
        for i, block in enumerate(code_blocks, 1):
            print(f"  [{i}]")
            for line in block.splitlines():
                print(f"    {line}")

    cross_refs = parsed.get("crossReferences", [])
    if cross_refs:
        print(f"\nCROSS-REFERENCES ({len(cross_refs)})")
        print("-" * 60)
        for ref in cross_refs:
            print(f"  {ref['ref']}")

    # Comments
    comments = results.get("comments", [])
    if comments:
        print(f"\nCOMMENTS ({len(comments)})")
        print("-" * 60)
        for comment in comments:
            author_name = comment.get("author", "unknown")
            created = comment.get("createdAt", "")[:10] if comment.get("createdAt") else ""
            body_text = comment.get("body", "")
            print(f"@{author_name} ({created}):")
            if body_text:
                for line in body_text.splitlines():
                    print(f"  {line}")
            else:
                print("  (empty)")
            print()

    # Linked PRs
    linked_prs = results.get("linkedPRs", [])
    if linked_prs:
        print(f"\nLINKED PULL REQUESTS ({len(linked_prs)})")
        print("-" * 60)
        for pr in linked_prs:
            state_str = pr.get("state", "")
            print(f"  PR #{pr['number']}: {pr.get('title', '')} [{state_str}]")
            if pr.get("url"):
                print(f"    {pr['url']}")

    print("=" * 60)


# =============================================================================
# Argument parsing
# =============================================================================

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Inspect a GitHub Issue: fetch data, parse error context, "
            "extract file references, classify type, and check file existence."
        ),
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--repo", default=".", help="Path inside the target Git repository.")
    parser.add_argument(
        "--issue",
        required=True,
        help="Issue number or URL (required).",
    )
    parser.add_argument(
        "--focus",
        default=None,
        help="Focus area for codebase search narrowing (e.g., 'src/lib/components').",
    )
    parser.add_argument(
        "--max-comment-length",
        type=int,
        default=0,
        help="Max characters per comment body (0 = unlimited).",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit JSON instead of text output.",
    )
    return parser.parse_args()


# =============================================================================
# Main
# =============================================================================

def main() -> int:
    args = parse_args()
    repo_root = find_git_root(Path(args.repo))
    if repo_root is None:
        print("Error: not inside a Git repository.", file=sys.stderr)
        return 1

    if not ensure_gh_available(repo_root):
        return 1

    issue_number = resolve_issue(args.issue, repo_root)
    if issue_number is None:
        return 1

    # Fetch issue data
    issue_data = fetch_issue_data(issue_number, repo_root)
    if issue_data is None:
        return 1

    # Fetch comments
    comments = fetch_issue_comments(
        issue_number,
        repo_root,
        max_comment_length=args.max_comment_length,
    )

    # Fetch linked PRs
    linked_prs = fetch_timeline_linked_prs(issue_number, repo_root)

    # Parse body + comments
    body = issue_data.get("body") or ""
    parsed = parse_all_text(body, comments)

    # Classify issue
    labels_raw = issue_data.get("labels") or []
    label_names = [
        lbl.get("name", "") if isinstance(lbl, dict) else str(lbl)
        for lbl in labels_raw
    ]
    issue_type = classify_issue(label_names, body, issue_data.get("title", ""))

    # Check file existence
    file_checks = check_file_existence(parsed["fileReferences"], repo_root)

    # Build results
    results: dict[str, Any] = {
        "issue": issue_data,
        "issueType": issue_type,
        "comments": comments,
        "linkedPRs": linked_prs,
        "parsed": parsed,
        "fileChecks": file_checks,
    }

    if args.focus:
        results["focus"] = args.focus

    # Output
    if args.json:
        print(json.dumps(results, indent=2))
    else:
        render_text_output(results)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
