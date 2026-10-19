# GitHub Actions

`unity-cli` keeps CI intentionally small and deterministic.

## Active Workflows

- `lint.yml`
  - `cargo fmt --check`
  - `cargo clippy -- -D warnings`
  - markdownlint + commitlint
- `test.yml`
  - `cargo test`
  - Windows (`windows-latest`): `cargo test`, then the built binary runs
    `tests/scripts/platform-checks.py` (lockfile Editor discovery,
    `setup --dry-run`, OS screenshot fallback) and is installed through
    `scripts/install.ps1`
  - Linux: the same `platform-checks.py` under Xvfb with the X11 screenshot
    tools, then its screenshot check again with `grim` under a headless `sway`
    (Wayland); none of these checks need a Unity Editor
  - `dotnet test lsp/Server.Tests.csproj`
  - `cargo llvm-cov` (Rust coverage gate, line >= 90%)
  - `dotnet test ... /p:CollectCoverage=true` (LSP coverage gate, line >= 90%)
- `release.yml`
  - runs after `chore(release):` pushes to `main` or manual dispatch
  - creates the release tag, builds release binaries for Linux/macOS/Windows
  - publishes GitHub Release assets
- `published-install.yml`
  - installs the latest published release on `windows-latest` with
    `irm .../main/scripts/install.ps1 | iex` and checks `unity-cli --version`
  - runs after the `Release` workflow, weekly, on manual dispatch, and for PRs
    that change the check itself (not on every PR: it depends on the network
    and on the published release)
- `main-pr-policy.yml`
  - branch policy for PRs into `main`
- `auto-merge.yml`
  - enables GitHub auto-merge for non-draft same-repository PRs authored by
    `akiojin` or `dependabot[bot]` into `develop`
    and for the `develop` -> `main` release PR
  - merges happen only after the branch-protection required checks pass
  - external (fork) PRs into `develop` merge only after a threat review:
    1. a reviewer reads the full diff and confirms nothing in it is a threat
    2. a user with write or higher permission (or `akiojin`) adds the
       `reviewed-safe` label to the non-draft PR
    3. the workflow enables auto-merge with
       `gh pr merge --auto --merge --match-head-commit <head SHA at label time>`,
       so commits pushed after the review are never merged
  - a new push to a labeled fork PR (`synchronize`) removes `reviewed-safe` and
    disables auto-merge; the PR needs a new review and a new label
  - runs under `pull_request_target` but never checks out or runs fork code;
    every step only calls the `gh` API
