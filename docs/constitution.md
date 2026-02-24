# unity-cli 開発憲章

## 基本原則

### I. CLI中心設計
- 実行インターフェースは `unity-cli` を主とする
- Unity連携は `UnityCliBridge/Packages/unity-cli-bridge` を唯一の配布実体とする
- MCP専用実装への逆戻りは行わない

### II. Rust優先
- CLIコアは Rust で実装する
- 性能回帰を許容しない（速度・起動時間・メモリ）
- 設定は `UNITY_CLI_*` のみをサポートし、旧MCP系の環境変数は受け付けない

### III. TDD（妥協不可）
- RED -> GREEN -> REFACTOR を必須とする
- 実装先行コミットは禁止
- 変更対象には対応テストを追加/更新する

### IV. LSP統合
- C# の symbol/search/edit は LSP を中心に設計する
- LSPが利用不能な場合は明示的に失敗を返し、黙って劣化しない
- LSP本体は `lsp/` として同梱管理する

### V. シンプルさとDX
- 実装は最小限の複雑さで維持する
- CLIエラーは原因と対処を明示する
- ドキュメントはルート `README.md` と `docs/` に集約する

### VI. リリースと配布
- GitHub Releases で各OS向けバイナリを配布する
- crates.io で `cargo install unity-cli` を提供する
- Unity UPM パッケージとCLIのバージョン整合を維持する

## テスト要件

- Rust: `cargo test --all-targets`
- LSP: `dotnet test lsp/Server.Tests.csproj`
- 重要機能は回帰テストを必須とする

## Spec要件

- 新機能は `specs/SPEC-xxxxxxxx/` を作成・更新する
- `.specify/current-feature` を作業対象として運用する

## ライセンス要件

- MITライセンス条項（著作権表示と許諾表示）を保持する
- 利用アプリへの帰属表記を推奨する（README/クレジット/About）

## ガバナンス

- 憲章は実装より優先される
- 例外は仕様書に理由を明記して承認を得る

## 憲章更新チェックリスト

憲章を更新した場合は、以下を必ず同期する。

### 更新対象（必須）

- `docs/constitution.md`（正本）
- `.specify/memory/constitution.md`（Spec Kit用ミラー）
- `CLAUDE.md`（参照先・運用ルール）
- `README.md`（導線・配布説明）
- `templates/plan-template.md`
- `.specify/templates/plan-template.md`
- `.claude/commands/speckit.plan.md`
- `.claude/commands/speckit.analyze.md`
- `.claude/commands/speckit.constitution.md`

### 仕様テンプレに影響がある場合

- `.specify/templates/spec-template.md`
- `.specify/templates/tasks-template.md`
- `templates/spec-template.md`
- `templates/tasks-template.md`

### バリデーション

```bash
# 憲章ミラー同期
bash scripts/sync-constitution.sh
bash scripts/sync-constitution.sh --check

# 古い参照の確認
rg -n "(^|\\s)/memory/constitution\\.md" -S --hidden .
rg -n "(^|\\s)memory/constitution\\.md" -S --hidden .
```

更新時は、`docs/constitution.md` の version / 日付、および plan-template フッターの version を一致させる。

**バージョン**: 2.0.0  
**制定日**: 2025-10-17  
**最終改定**: 2026-02-17
