# Fetch and Cache

Operational guide for the `unity-cli reference` cache commands.

## Cache Layout

```
~/.unity/cache/UnityCsReference/
  <unity-version>/
    .git/
    Runtime/
    Editor/
    Modules/
    .unity-cli-meta.json   # Stored provenance; field names use snake_case
```

Override the base path with `UNITY_CLI_CACHE_ROOT` (defaults to `~/.unity/cache`). Each Unity version lives in its own directory and is independent of `~/.unity/tools/` (which stores managed binaries).

## Public Ref Resolution

The requested Unity version comes from `--version`, or from `ProjectSettings/ProjectVersion.txt` when omitted. An explicit version works outside a Unity project. Git discovers published heads and tags with `ls-remote`; the CLI selects in this order:

1. The explicit `--branch` ref, if supplied (a short name or full `refs/heads/...` / `refs/tags/...` name).
2. A published tag or head named exactly for the requested version.
3. A published head for the same major.minor, named `<major.minor>` or `<major.minor>/staging`.

Multiple candidates at the selected priority produce an ambiguity error listing candidates, even if they share a SHA. Choose one using its full ref name. If nothing matches, the error lists available branches; inspect those before passing an explicit ref. There is no unconditional staging fallback.

Only the tag `refs/tags/<requested-version>` sets `exactMatch: true`. Branches, including version-named heads, report `false` because their content can change. A same-minor branch does not guarantee a patch-version match.

## Commands

### Fetch

```bash
# Auto-detect Unity version from the current project
unity-cli reference fetch --accept-license

# Explicit version, without a Unity project
unity-cli reference fetch --version 6000.4.12f1 --accept-license

# Explicit published branch; use a full ref to disambiguate short names
unity-cli reference fetch --version 6000.4.12f1 --branch refs/heads/6000.4 --force --accept-license

# Refetch an existing snapshot
unity-cli reference fetch --version 6000.4.12f1 --force --accept-license
```

Fetch uses the selected commit SHA with a shallow Git fetch and detached checkout, then verifies `HEAD` against that SHA. The snapshot stays fixed even if the remote branch moves after discovery. `GITHUB_TOKEN` / `GH_TOKEN`, when set, supplies authentication. If Git is absent, the official repository uses paginated GitHub API discovery and a ZIP archive addressed by the selected SHA. A Git transport failure is reported directly; it never silently selects another ref.

An existing version directory is skipped unless `--force` is set. The response returns its stored provenance, even when a different branch was requested; it does not relabel the cached source. Missing legacy metadata fields are returned as `null`. Use `--force` to fetch the newly requested ref. A replacement is prepared before switching directories, so a failed resolution or download preserves the previous cache.

### Status

```bash
unity-cli reference status --output json
```

Returns `{ ok: true, versions: [ ... ] }`. Each entry includes `version`, `branch`, `sourceRef`, `commitSha`, `exactMatch`, `selectionReason`, `sourceUrl`, `fetchedAt`, `sizeBytes`, and `path`. Fetch returns the same provenance fields. `branch` is the selected ref name without the `refs/heads/` or `refs/tags/` prefix; `sourceRef` retains the full name.

The on-disk `.unity-cli-meta.json` uses `source_ref`, `commit_sha`, `exact_match`, `selection_reason`, `source_url`, and `fetched_at` alongside `version` and `branch`. Read `exactMatch` and `selectionReason` before treating the source as an exact editor-version match; use `commitSha` to identify the actual snapshot.

### Clean

```bash
# Show what would be removed (LRU by mtime)
unity-cli reference clean --keep 1 --dry-run

# Actually remove old snapshots
unity-cli reference clean --keep 1
```

`clean` retains the newest `--keep` snapshots (mtime descending) and removes the rest. The CLI prints the removed paths so they can be re-cached on demand.

## Troubleshooting

- `git binary not found`: install `git` or point `PATH` at a working installation.
- `Unity Companion License`: pass `--accept-license` or export `UNITY_CLI_ACCEPT_LICENSE=1`.
- `ambiguous published refs`: choose one listed full ref with `--branch refs/heads/...` or `--branch refs/tags/...`.
- `no published ref matches`: inspect the listed branches and explicitly select a suitable ref. Its `exactMatch` may be `false`.
