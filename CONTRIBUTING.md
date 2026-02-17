# Contributing to unity-cli

English | [日本語](#日本語)

Thanks for contributing to `unity-cli`.

## Prerequisites

- Rust stable
- .NET SDK 9.0+ (for `lsp/` tests)
- Node.js 20+ + pnpm (for markdown/commit tooling)
- Unity 6+ (when validating Unity package behavior)

## Development Setup

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
pnpm install --frozen-lockfile
```

### Docker (Optional)

You can use Docker without installing Rust / .NET locally.

```bash
docker build -t unity-cli-dev .
docker run --rm unity-cli-dev
```

## Validation Commands

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

### Running Tests

#### Rust

```bash
cargo test
```

#### LSP (C# / .NET 9)

```bash
dotnet test lsp/Server.Tests.csproj
```

.NET SDK 9 が必要です。`dotnet --version` で 9.x を確認してください。

### Pre-push Hook

This repository includes a `.husky/pre-push` hook. To enable it:

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

The hook automatically runs `cargo test` and `dotnet test` before `git push`.
If any test fails, the push is aborted.

## CI

To merge a Pull Request into main, the following CI checks must pass:

- **Rust Tests (required)** — `cargo test`
- **LSP Tests (required)** — `dotnet test lsp/Server.Tests.csproj`

PRs with failing tests cannot be merged.

## E2E Tests

Unity E2E tests run via manual trigger (`workflow_dispatch`).
See `docs/development.md` for instructions on running them locally.

## Branch Policy

- Default target branch: `develop`
- `main` accepts only release PRs from `develop` or release automation branches

## Commit Style

Use Conventional Commits:

- `feat: ...`
- `fix: ...`
- `chore: ...`
- `docs: ...`
- `test: ...`

## TDD

Follow RED -> GREEN -> REFACTOR.
Add/adjust tests in the same change set as implementation.

## Spec-Driven Development

For feature work, keep specs updated in `specs/SPEC-xxxxxxxx/`:

1. `/speckit.specify`
2. `/speckit.plan`
3. `/speckit.tasks`

## License and Attribution

`unity-cli` is MIT licensed. MIT requires preserving the copyright + permission notice.

If you ship an app built with `unity-cli`, please include attribution in one of:

- app credits
- about screen
- repository README

Recommended text:

`This product uses unity-cli (https://github.com/akiojin/unity-cli), licensed under MIT.`

---

## 日本語

`unity-cli` へのコントリビュートありがとうございます。

## 前提ツール

- Rust stable
- .NET SDK 9.0+（`lsp/` テスト用）
- Node.js 20+ と pnpm（ドキュメント/コミット系ツール用）
- Unity 6+（Unityパッケージ挙動確認時）

## セットアップ

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
pnpm install --frozen-lockfile
```

### Docker（任意）

ローカルに Rust / .NET をインストールせずに Docker で検証できます。

```bash
docker build -t unity-cli-dev .
docker run --rm unity-cli-dev
```

## 検証コマンド

```bash
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
dotnet test lsp/Server.Tests.csproj
```

### プッシュ前フック

このリポジトリには `.husky/pre-push` フックが含まれています。有効にするには:

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

フックは `git push` の前に自動的に `cargo test` と `dotnet test` を実行します。
テストが失敗した場合、push は中断されます。

## CI / 継続的インテグレーション

Pull Request を main にマージするには、以下の CI チェックがすべて成功する必要があります:

- **Rust Tests (required)** — `cargo test`
- **LSP Tests (required)** — `dotnet test lsp/Server.Tests.csproj`

テストが失敗している PR はマージできません。

## E2E テスト

Unity 実機 E2E テストは手動トリガー (`workflow_dispatch`) で実行されます。
ローカルでの実行方法は `docs/development.md` を参照してください。

## ブランチ運用

- 通常のPR先は `develop`
- `main` へのPRはリリース系のみ

## コミット規約

Conventional Commits を使用してください（`feat:`, `fix:`, `chore:`, `docs:`, `test:` など）。

## TDD

RED -> GREEN -> REFACTOR を前提に進めてください。実装変更には対応テストを含めます。

## Spec駆動開発

機能開発時は `specs/SPEC-xxxxxxxx/` を更新してください。

1. `/speckit.specify`
2. `/speckit.plan`
3. `/speckit.tasks`

## ライセンスと表記

`unity-cli` は MIT ライセンスです。MIT条項に従い、著作権表示と許諾表示を保持してください。

`unity-cli` を利用したアプリ配布時は、次のいずれかへの表記を推奨します。

- クレジット
- About画面
- README

推奨表記:

`This product uses unity-cli (https://github.com/akiojin/unity-cli), licensed under MIT.`
