# Repository Configuration

このディレクトリは GitHub リポジトリ設定をコード管理する場所です。

```
.github/config/
├── README.md
├── repo.json                  # リポジトリ設定
└── branch-protection/
    ├── main.json              # main ブランチ保護
    └── develop.json           # develop ブランチ保護
```

## 自動適用

[gh-repo-config](https://github.com/twelvelabs/gh-repo-config) を使った一括適用:

```bash
gh extension install twelvelabs/gh-repo-config
gh repo-config apply
```

## 手動適用 (`gh api`)

### リポジトリ設定

```bash
gh api repos/akiojin/unity-cli -X PATCH \
  -F allow_auto_merge=true \
  -F delete_branch_on_merge=true
```

### main ブランチ保護

```bash
gh api repos/akiojin/unity-cli/branches/main/protection -X PUT \
  --input .github/config/branch-protection/main.json
```

### develop ブランチ保護

```bash
gh api repos/akiojin/unity-cli/branches/develop/protection -X PUT \
  --input .github/config/branch-protection/develop.json
```

## チェック名とワークフローの対応

| ブランチ | 必須チェック名 | ワークフロー |
|---------|--------------|-------------|
| main | `Main PR Policy` | `main-pr-policy.yml` |
| develop | `Rust Format & Lint` | `lint.yml` |
| develop | `Markdown & Commitlint` | `lint.yml` |
| develop | `Rust Tests (required)` | `test.yml` |
| develop | `LSP Tests (required)` | `test.yml` |
| develop | `Verify specs/specs.md is up-to-date` | `specs-readme.yml` |

## 適用確認

```bash
# リポジトリ設定
gh api repos/akiojin/unity-cli | jq '{allow_auto_merge, delete_branch_on_merge}'

# main ブランチ保護
gh api repos/akiojin/unity-cli/branches/main/protection | jq '.required_status_checks.checks'

# develop ブランチ保護
gh api repos/akiojin/unity-cli/branches/develop/protection | jq '.required_status_checks.checks'
```
