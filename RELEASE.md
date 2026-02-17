# unity-cli Release Guide

## Quick Start

Run the publish script from the repository root:

```bash
./scripts/publish.sh 0.2.0
```

The script automates the full release pipeline:

1. Validates clean working tree and `main` branch
2. Verifies `Cargo.toml` version matches the target version
3. Runs `cargo test` and `dotnet test lsp/Server.Tests.csproj`
4. Performs `cargo publish --dry-run` to catch packaging issues
5. Prompts for confirmation
6. Publishes the crate to crates.io via `cargo publish`
7. Creates and pushes the git tag `vX.Y.Z`

Once the tag is pushed, **GitHub Actions** automatically:

1. Validates the tag and runs the test suite
2. Builds release binaries for linux-x64, macos-arm64, and windows-x64
3. Creates a GitHub Release with the built artifacts

## Tag Convention

All release tags use the format `vX.Y.Z` (e.g. `v0.1.0`, `v1.0.0`).

## Release Workflow Details

### `scripts/publish.sh`

Pre-publish validation script. Accepts a version argument without the `v` prefix:

```bash
./scripts/publish.sh <VERSION>
```

Checks performed before publishing:

| Step | Description |
|------|-------------|
| 1 | Git working tree is clean (no uncommitted or untracked files) |
| 2 | Current branch is `main` |
| 3 | `Cargo.toml` version matches the provided version |
| 4 | Tag `vX.Y.Z` does not already exist |
| 5 | `cargo test` passes |
| 6 | `dotnet test lsp/Server.Tests.csproj` passes |
| 7 | `cargo publish --dry-run` succeeds |

### `.github/workflows/release.yml`

Triggered by:

- **Tag push**: Pushing a `v*` tag (created by `publish.sh`)
- **Manual dispatch**: Enter the `release_tag` (e.g. `v0.2.0`) in the GitHub Actions UI

Jobs:

| Job | Description |
|-----|-------------|
| `validate` | Checks tag format, verifies Cargo.toml version, runs cargo test and dotnet test |
| `build` | Matrix build for linux-x64, macos-arm64, windows-x64 |
| `release` | Creates a GitHub Release and attaches the built binaries |

Release artifacts:

- `unity-cli-linux-x64`
- `unity-cli-macos-arm64`
- `unity-cli-windows-x64.exe`

## Step-by-Step Release Checklist

1. Ensure all changes are merged to `main`
2. Update `version` in `Cargo.toml` to the new version
3. Commit the version bump: `git commit -am "chore: bump version to X.Y.Z"`
4. Run the publish script:
   ```bash
   ./scripts/publish.sh X.Y.Z
   ```
5. Confirm the prompt to publish and push
6. Verify the GitHub Actions [Release workflow](../../actions/workflows/release.yml) completes
7. Verify the [GitHub Release](../../releases) page has the correct artifacts

## Troubleshooting

### `publish.sh` fails with "working tree is not clean"

Commit or stash all changes before running the script:

```bash
git stash        # or git commit
./scripts/publish.sh X.Y.Z
```

### `publish.sh` fails with "Must be on 'main' branch"

Switch to the main branch first:

```bash
git checkout main
git pull origin main
./scripts/publish.sh X.Y.Z
```

### `publish.sh` fails with "Cargo.toml version is '...' but release version is '...'"

Update `Cargo.toml` to match the version you want to release:

```bash
# Edit Cargo.toml, set version = "X.Y.Z"
git commit -am "chore: bump version to X.Y.Z"
./scripts/publish.sh X.Y.Z
```

### `cargo publish` fails with authentication error

Ensure you are logged in to crates.io:

```bash
cargo login
```

### GitHub Actions release workflow fails

- Check the [Actions tab](../../actions/workflows/release.yml) for detailed logs
- For manual re-trigger, use **workflow_dispatch** with the release tag
- If the tag was pushed but the workflow failed, fix the issue and re-run the workflow from the Actions UI

### Tag already exists

If you need to re-release the same version:

```bash
git tag -d vX.Y.Z              # delete local tag
git push origin :refs/tags/vX.Y.Z  # delete remote tag
./scripts/publish.sh X.Y.Z     # re-run
```

Note: You cannot re-publish the same version to crates.io. If the crate was already published, you must bump the version.
