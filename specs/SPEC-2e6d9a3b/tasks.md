# タスク: コンソール管理機能

**機能ID**: `SPEC-2e6d9a3b` | **ステータス**: 改善中

## 実装状況

本機能は既に実装済みですが、パフォーマンス改善のため追加タスクを実施中です。

### 完了済みタスク

- [x] 機能設計
- [x] コア実装
- [x] テスト実装
- [x] ドキュメント作成

### 改善タスク（2025-11-06）

#### 背景
ログのスタックトレース含有時にレスポンスサイズが5-20MBになる問題を改善。デフォルトでスタックトレースを含めないことで、約10-40倍のサイズ削減を実現。

#### タスク一覧

- [ ] **[TASK-001]** テストケース更新: includeStackTraceのデフォルト値をfalseに変更
  - ファイル: `unity-cli/tests/unit/handlers/console/ConsoleReadToolHandler.test.js`
  - 期待動作: デフォルトでincludeStackTrace=falseになることを確認

- [ ] **[TASK-002]** ハンドラ修正: includeStackTraceのデフォルト値をfalseに変更
  - ファイル: `unity-cli/src/handlers/console/ConsoleReadToolHandler.js`
  - 変更内容: スキーマ定義のデフォルト値を`true`から`false`に変更

- [ ] **[TASK-003]** ドキュメント更新: ツール説明文を更新
  - ファイル: `unity-cli/src/handlers/console/ConsoleReadToolHandler.js`
  - 内容: スタックトレースがデフォルトで含まれないことを明記

- [ ] **[TASK-004]** 動作確認とコミット
  - 実施内容: ユニットテストの実行、変更内容のコミット

## 参考

実装詳細については `spec.md` および `plan.md` を参照してください。

---
*本ドキュメントは実装完了後に作成されました（2025年10月）*
*改善タスクを追加しました（2025年11月）*
