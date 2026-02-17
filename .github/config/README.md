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

適用には [gh-repo-config](https://github.com/twelvelabs/gh-repo-config) を使います。

```bash
gh extension install twelvelabs/gh-repo-config
gh repo-config apply
```
