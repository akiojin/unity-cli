# GitHub Actions

`unity-cli` keeps CI intentionally small and deterministic.

## Active Workflows

- `lint.yml`
  - `cargo fmt --check`
  - `cargo clippy -- -D warnings`
  - markdownlint + commitlint
- `test.yml`
  - `cargo test`
  - `dotnet test lsp/Server.Tests.csproj`
  - `cargo llvm-cov` (Rust coverage gate, line >= 90%)
  - `dotnet test ... /p:CollectCoverage=true` (LSP coverage gate, line >= 90%)
- `release.yml`
  - runs after `chore(release):` pushes to `main` or manual dispatch
  - creates the release tag, builds release binaries for Linux/macOS/Windows
  - publishes GitHub Release assets
- `main-pr-policy.yml`
  - branch policy for PRs into `main`
- `auto-merge.yml`
  - enables GitHub auto-merge for non-draft same-repository PRs into `develop`
    and for the `develop` -> `main` release PR
  - merges happen only after the branch-protection required checks pass
