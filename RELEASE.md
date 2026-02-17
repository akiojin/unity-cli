# unity-cli Release Guide

## 1. Preflight

```bash
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

## 2. Version Sync

Update both versions (`package.json` + Unity UPM package):

```bash
node scripts/release/update-versions.mjs <X.Y.Z>
```

or use the helper:

```bash
./scripts/publish.sh patch
```

## 3. Tag and Push

```bash
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin develop --tags
```

## 4. GitHub Release

Workflow: `.github/workflows/unity-cli-release.yml`

- Trigger: `v*` tag push or manual dispatch
- Outputs:
  - `unity-cli-linux-x64`
  - `unity-cli-macos-arm64`
  - `unity-cli-windows-x64.exe`

## 5. Cargo Publish

```bash
cargo publish
```

After publish, users can install with:

```bash
cargo install unity-cli
```
