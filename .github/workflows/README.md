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
- `unity-cli-release.yml`
  - triggers on `v*` tag
  - builds release binaries for Linux/macOS/Windows
  - publishes GitHub Release assets
- `specs-readme.yml`
  - verifies `specs/specs.md` is in sync with Spec Kit
- `main-pr-policy.yml` / `auto-merge.yml`
  - branch policy and PR automation
