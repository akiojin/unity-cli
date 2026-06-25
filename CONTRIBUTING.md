# Contributing to unity-cli

English | [日本語](#日本語)

Thanks for contributing to `unity-cli`.

## Prerequisites

- Rust stable
- .NET SDK 10.0+ (for `lsp/` tests)
- Node.js 20+ + pnpm (for markdown/commit tooling)
- Unity 2022.3 LTS or Unity 6 (when validating Unity package behavior)

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
cargo test -- --test-threads=1
dotnet test lsp/Server.Tests.csproj
cargo run -- skills lint --severity error
```

### Running Tests

#### Rust

```bash
cargo test
```

#### LSP (C# / .NET 10)

```bash
dotnet test lsp/Server.Tests.csproj
```

.NET SDK 10 が必要です。`dotnet --version` で 10.x を確認してください。

### Pre-push Hook

This repository includes a `.husky/pre-push` hook. To enable it:

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

The hook automatically runs `cargo test` and `dotnet test` before `git push`.
If any test fails, the push is aborted.

## CI

Pull Requests targeting `develop` run the normal required checks:

- **Rust Format & Lint** — `cargo fmt --all -- --check` and `cargo clippy --all-targets -- -D warnings`
- **Markdown & Commitlint** — markdownlint plus Conventional Commit validation
- **Rust Tests (required)** — `cargo test`
- **LSP Tests (required)** — `dotnet test lsp/Server.Tests.csproj`
- **Rust Coverage >= 90% (required)** — `cargo llvm-cov`
- **LSP Coverage >= 90% (required)** — `dotnet test` with coverage threshold

Docs-only PRs still run the docs and commit checks. Changes under
`.claude-plugin/plugins/unity-cli/skills/` also run Skill Contract Lint, so run
`cargo run -- skills lint --severity error` before pushing skill changes.

PRs with failing required checks cannot be merged.

## Local Unity E2E

Unity E2E is not part of CI. Run it locally only when you need live Unity listener validation.

- Smoke: `scripts/e2e-test.sh`
- Full local sweep: `scripts/e2e-all-tools.sh`

## Branch Policy

- Default target branch for external contributions: `develop`
- `main` accepts only release/sync PRs from the base repository, normally from `develop`
- Fork PRs to `main` are rejected by `main-pr-policy.yml`; retarget them to `develop`

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

For feature work, use Issue-first spec management:

1. Create or update a GitHub Issue with label `gwt-spec`
2. Keep `## Spec`, `## Plan`, `## Tasks`, and `## TDD` sections current
3. Use the Issue number as SPEC ID and avoid local spec directories

## Skill Documentation Changes

The canonical skill source is `.claude-plugin/plugins/unity-cli/skills/`.
`.claude/skills/` and `.agents/skills/` are symlinks into that directory, so
edit the canonical files only.

Before opening a PR that changes skills:

```bash
cargo run -- skills lint --severity error
```

If you add a new skill, update both symlinks as described in
[docs/skills.md](docs/skills.md).

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
- .NET SDK 10.0+（`lsp/` テスト用）
- Node.js 20+ と pnpm（ドキュメント/コミット系ツール用）
- Unity 2022.3 LTS または Unity 6（Unityパッケージ挙動確認時）

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
cargo test -- --test-threads=1
dotnet test lsp/Server.Tests.csproj
cargo run -- skills lint --severity error
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

`develop` 宛ての Pull Request では、通常の必須チェックが実行されます:

- **Rust Format & Lint** — `cargo fmt --all -- --check` と `cargo clippy --all-targets -- -D warnings`
- **Markdown & Commitlint** — markdownlint と Conventional Commit 検証
- **Rust Tests (required)** — `cargo test`
- **LSP Tests (required)** — `dotnet test lsp/Server.Tests.csproj`
- **Rust Coverage >= 90% (required)** — `cargo llvm-cov`
- **LSP Coverage >= 90% (required)** — coverage 閾値付きの `dotnet test`

docs-only PR でも docs / commit チェックは実行されます。
`.claude-plugin/plugins/unity-cli/skills/` 配下を変更する場合は Skill Contract Lint も対象になるため、push 前に `cargo run -- skills lint --severity error` を実行してください。

必須チェックが失敗している PR はマージできません。

## ローカル Unity E2E

Unity E2E は CI では実行しません。live Unity listener の確認が必要なときだけローカルで実行してください。

- スモーク: `scripts/e2e-test.sh`
- フルローカル確認: `scripts/e2e-all-tools.sh`

## ブランチ運用

- 外部 contributor の通常の PR 先は `develop`
- `main` は base repository からの release / sync PR のみを受け付けます。通常は `develop` からの PR です
- fork から `main` への PR は `main-pr-policy.yml` で拒否されます。`develop` 宛てに変更してください

## コミット規約

Conventional Commits を使用してください（`feat:`, `fix:`, `chore:`, `docs:`, `test:` など）。

## TDD（日本語）

RED -> GREEN -> REFACTOR を前提に進めてください。実装変更には対応テストを含めます。

## Spec駆動開発

機能開発は Issue-first で管理してください。

1. `gwt-spec` ラベル付き GitHub Issue を作成または更新する
2. Issue 本文の `## Spec` / `## Plan` / `## Tasks` / `## TDD` を更新する
3. SPEC ID は Issue 番号を使い、ローカル spec ディレクトリは作成しない

## Skill ドキュメント変更

skill の正本は `.claude-plugin/plugins/unity-cli/skills/` です。
`.claude/skills/` と `.agents/skills/` はこの正本への symlink なので、編集は正本だけに行ってください。

skill を変更する PR を開く前に、次を実行してください:

```bash
cargo run -- skills lint --severity error
```

新しい skill を追加する場合は、[docs/skills.md](docs/skills.md) の手順に従って両方の symlink も更新してください。

## ライセンスと表記

`unity-cli` は MIT ライセンスです。MIT条項に従い、著作権表示と許諾表示を保持してください。

`unity-cli` を利用したアプリ配布時は、次のいずれかへの表記を推奨します。

- クレジット
- About画面
- README

推奨表記:

`This product uses unity-cli (https://github.com/akiojin/unity-cli), licensed under MIT.`
