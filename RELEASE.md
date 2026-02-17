# unity-cli Release Guide

## CI Release Workflow

Workflow file:

- `.github/workflows/release.yml`

Trigger options:

- Push tag: `unity-cli-v*`
- Manual dispatch with `release_tag`

Artifacts:

- `unity-cli-linux-x64`
- `unity-cli-macos-arm64`
- `unity-cli-windows-x64.exe`

## Minimal checklist

1. `cargo test`
2. Create and push a tag (example: `unity-cli-v0.1.0`)
3. Confirm GitHub Release assets are uploaded
