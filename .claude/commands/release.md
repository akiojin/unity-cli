---
description: unity-cli のリリース補助。バージョン更新、タグ作成、PR作成手順を案内します。
---

# /release コマンド

`unity-cli` のリリースを `develop` ブランチで進めるためのガイドです。

## 前提チェック

```bash
git branch --show-current
git status --porcelain
git fetch --tags origin
```

- ブランチは `develop`
- 作業ツリーはクリーン

## 1. 変更分析

```bash
PREV_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
git log "${PREV_TAG}"..HEAD --oneline
git diff "${PREV_TAG}"..HEAD --stat
```

## 2. バージョン決定

- `feat!:` / `BREAKING CHANGE:` -> major
- `feat:` -> minor
- `fix:` / `perf:` -> patch

現在のバージョンは `package.json` から読み取ります。

## 3. バージョン更新

```bash
node scripts/release/update-versions.mjs <X.Y.Z>
```

または:

```bash
./scripts/publish.sh patch
```

## 4. コミットとタグ

```bash
git add package.json UnityCliBridge/Packages/unity-cli-bridge/package.json lsp/Directory.Build.props CHANGELOG.md
git commit -m "chore(release): vX.Y.Z"
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin develop --tags
```

## 5. main へのPR

```bash
gh pr create --base main --head develop --title "chore(release): vX.Y.Z"
```

## 6. リリース確認

- `main` へマージ後、`.github/workflows/unity-cli-release.yml` が実行される
- 必要に応じて `cargo publish` を実行する
