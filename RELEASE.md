# unity-cli Release Guide

## Quick Start

Run the publish script from the repository root:

```bash
./scripts/publish.sh patch
```

`scripts/publish.sh` is the release entrypoint. It:

1. Verifies the working tree is clean
2. Bumps the workspace version with `npm version <major|minor|patch>`
3. Syncs the Cargo crate, Unity package, and LSP versions
4. Runs `cargo test`, `dotnet test lsp/Server.Tests.csproj` on .NET 10, and `cargo publish --dry-run`
5. Commits the version sync, creates `vX.Y.Z`, and runs `cargo publish`
6. Pushes the release commit and tag

After the push, GitHub Actions `release.yml` builds the release binaries, generates managed-binary manifests, and uploads the GitHub Release assets.

## Tag Convention

All release tags use the format `vX.Y.Z` (for example `v0.2.3`).

## Release Workflow Details

### `scripts/publish.sh`

Usage:

```bash
./scripts/publish.sh <major|minor|patch> [--tags-only|--no-push] [--remote <name>]
```

Checks performed before publish:

| Step | Description                                                   |
| ---- | ------------------------------------------------------------- |
| 1    | Git working tree is clean (no uncommitted or untracked files) |
| 2    | `node` is available for the version bump                      |
| 3    | `cargo test` passes                                           |
| 4    | `dotnet test lsp/Server.Tests.csproj` passes                  |
| 5    | `cargo publish --dry-run` succeeds                            |
| 6    | `cargo publish` succeeds                                      |

The script then creates the annotated tag `vX.Y.Z` and pushes the commit and tag to the selected remote.

### `.github/workflows/release.yml`

Triggered by:

- Pushes to `main` whose commit message contains `chore(release):`
- Manual dispatch from the GitHub Actions UI

Jobs:

| Job                            | Description                                                                                                   |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `create-tag`                   | Creates the release tag if it does not exist yet                                                              |
| `build`                        | Matrix build for `unity-cli` release binaries                                                                 |
| `build-lsp`                    | Matrix build for the bundled C# LSP binaries                                                                  |
| `upload-release` manifest step | Generates `unity-cli-manifest.json` and `csharp-lsp-manifest.json` with RID-specific URLs and SHA-256 digests |
| `upload-release`               | Uploads all built artifacts to the GitHub Release                                                             |

Release artifacts include:

- `unity-cli-linux-x64`
- `unity-cli-linux-arm64`
- `unity-cli-osx-arm64`
- `unity-cli-win-x64`
- `unity-cli-manifest.json`
- `csharp-lsp-linux-x64`
- `csharp-lsp-linux-arm64`
- `csharp-lsp-osx-arm64`
- `csharp-lsp-win-x64`
- `csharp-lsp-manifest.json`

## Step-by-Step Release Checklist

1. Ensure the release changes are merged and the working tree is clean
2. Run:

   ```bash
   ./scripts/publish.sh patch
   ```

3. Verify the [Release workflow](../../actions/workflows/release.yml) succeeds
4. Verify the crate appears on crates.io
5. Verify the [GitHub Release](../../releases) page contains the expected assets

## Troubleshooting

### `publish.sh` fails with "working tree is not clean"

Commit or remove the pending changes, then rerun:

```bash
git status --short
./scripts/publish.sh patch
```

### `publish.sh` fails with `dotnet test`

Install the required .NET SDK and rerun:

```bash
dotnet --info
./scripts/publish.sh patch
```

### `cargo publish` fails with authentication error

Log in to crates.io on the release machine:

```bash
cargo login
./scripts/publish.sh patch
```

### GitHub Actions release workflow fails

- Check the [Actions tab](../../actions/workflows/release.yml) for logs
- Re-run the failed workflow from GitHub Actions after fixing the issue
- If assets are missing, verify the release tag exists on the remote

### Tag already exists

If the release tag was created locally but not pushed successfully:

```bash
git tag -d vX.Y.Z
./scripts/publish.sh patch
```

If the crate was already published to crates.io, you must bump the version before retrying.
