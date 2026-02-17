# unity-cli Release Guide

This document defines the release path while `unity-cli` still lives under this monorepo.

## 1. Export `unity-cli` to dedicated repository

Use subtree split/push helper:

```bash
./scripts/export-unity-cli-subtree.sh <remote-or-url> [dest-branch] [release-tag]
```

Examples:

```bash
./scripts/export-unity-cli-subtree.sh git@github.com:akiojin/unity-cli.git main
./scripts/export-unity-cli-subtree.sh git@github.com:akiojin/unity-cli.git main unity-cli-v0.1.0
```

Notes:

- Requires clean git working tree.
- Exports only `unity-cli/` history.
- If `release-tag` is passed, the same tag is pushed to destination repository.

## 2. Build + release binaries

Workflow file:

`/.github/workflows/unity-cli-release.yml`

Trigger options:

- Push tag: `unity-cli-v*`
- Manual dispatch with `release_tag`

Artifacts:

- `unity-cli-linux-x64`
- `unity-cli-macos-arm64`
- `unity-cli-windows-x64.exe`

## 3. Cargo publish

To make `cargo install unity-cli` available, publish the crate:

```bash
cd unity-cli
cargo publish
```

## 4. Minimal release checklist

1. `cargo test --manifest-path unity-cli/Cargo.toml`
2. `dotnet test unity-cli/lsp/Server.Tests.csproj`
3. `cargo publish` (from `unity-cli/`)
4. `./scripts/export-unity-cli-subtree.sh ...`
5. Push `unity-cli-vX.Y.Z` tag to target repository
6. Confirm GitHub Release assets were published by workflow
