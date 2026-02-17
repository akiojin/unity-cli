# Spec Kit（ローカル運用）

このリポジトリは、GitHub公式`spec-kit`のテンプレート/スクリプトをベースに、ローカル運用の制約を上書きしています。

## 上流

- upstream: `github/spec-kit`
- tag: `v0.0.95`
- commit: `76cca342932d07a59abf1b766732f8097def55e8`
- synced: 2026-02-14

## ローカル差分（重要）

- **日本語化**: `.specify/`配下のテンプレート/コマンド文面は日本語を前提
- **ブランチレス運用**: Gitブランチの作成/切替を行わず、`specs/SPEC-xxxxxxxx/`で要件を管理
- **対象SPECの選択**:
  - `SPECIFY_FEATURE=SPEC-xxxxxxxx`（環境変数）
  - `.specify/current-feature`（永続化ファイル）
- **要件一覧の自動生成**: `specs/specs.md`
  - bash: `.specify/scripts/bash/update-specs-readme.sh`（bash 3.2互換）
  - powershell: `.specify/scripts/powershell/create-new-feature.ps1`（内部で更新）
- **コマンド定義の所在**:
  - Claude Code向け: `.claude/commands/speckit.*.md`（このリポジトリで管理）
  - Spec Kit側テンプレ: `.specify/templates/commands/*.md`（上流同期の対象）
  - 不変条件チェック: `.specify/scripts/checks/check-speckit-commands.sh`
