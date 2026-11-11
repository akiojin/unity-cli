# Runtime Checklist

Run these checks before the first `reference fetch` in a new environment or worktree.

## Binary Selection

- Prefer an installed `unity-cli` binary on `PATH`; verify it with `unity-cli --version`.
- If it is missing, install the release binary first:
  - macOS / Linux: `curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh`; use `~/.local/bin/unity-cli` if needed.
  - Windows (PowerShell): `irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex`. The installer adds `%USERPROFILE%\.unity\tools\unity-cli\win-x64` to the user `PATH`; open a new shell to pick it up, or invoke `unity-cli.exe` in that directory.
- Only inside a unity-cli source checkout (its `Cargo.toml` declares the `unity-cli` package), `cargo run -- <args>` is a development alternative. A consuming Unity project is not that checkout; use the installer there.

## Prerequisites

- Prefer `git` on `PATH` (`git --version`). When Git is absent, the official repository uses paginated GitHub API discovery and commit-pinned ZIP downloads.
- The target project has `ProjectSettings/ProjectVersion.txt` so Unity version detection can run automatically. Without a project, pass `--version <X.Y.Zfn>`; `--branch` is optional.
- Network connectivity to `github.com` is available. For rate-limited environments, export `GITHUB_TOKEN` or `GH_TOKEN` before fetching.
- Disk budget: budget roughly 400-600 MB per cached Unity version. Use `unity-cli reference status --output json` to inspect current usage.

## License Acceptance

UnityCsReference is distributed under the Unity Companion License. The CLI refuses to fetch without explicit consent:

- Pass `--accept-license` to `reference fetch`, or
- Export `UNITY_CLI_ACCEPT_LICENSE=1` for non-interactive sessions.

Local caches are for personal reference only. Do not redistribute the cached source.

## First-Run Verification

```bash
unity-cli reference fetch --accept-license
unity-cli reference status --output json
```

Expected: `status` includes the requested version and its `branch`, `sourceRef`, `commitSha`, `exactMatch`, `selectionReason`, `sourceUrl`, `fetchedAt`, `sizeBytes`, and `path`. Other cached versions may also appear. Check the selection reason: only the matching version tag guarantees `exactMatch: true`; a same-minor branch reports `false`.

The CLI prefers an explicit ref, then a published ref named for the requested version, then a published same-minor head. Ambiguous candidates produce an error; choose a listed full ref with `--branch`.

Re-running `fetch` without `--force` skips and returns stored provenance, including when a different branch was requested. Legacy caches may have `null` provenance fields; use `--force` to populate them. On disk, `.unity-cli-meta.json` uses snake_case keys such as `source_ref`, `commit_sha`, `exact_match`, and `selection_reason`.

## Recovery

- Fetch failed half-way: fix the network/ref error and retry. With `--force`, the existing cache is preserved until the replacement snapshot and metadata are ready.
- Cache directory unreadable: remove `~/.unity/cache/UnityCsReference/<version>` and re-fetch.
- Disk pressure: `unity-cli reference clean --keep 1` keeps the newest snapshot and removes the rest.
