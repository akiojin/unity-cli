# タスク: 軽量スニペット編集ツール

**機能ID**: `SPEC-4f7a596b`
**前提条件**: `spec.md`, `plan.md`

## カテゴリ別タスク一覧

### Setup
- [ ] T001 `specs/SPEC-4f7a596b/research.md` に LSP診断APIとWorkspaceEdit可否の調査結果を記録
- [ ] T002 既存括弧崩れ事例を `specs/SPEC-4f7a596b/research.md` へ抜粋し、検証要件に紐付け

### Tests (RED先行)
- [ ] T003 `unity-cli/tests/unit/handlers/script/ScriptEditSnippetToolHandler.test.js` にアンカー解決/80文字上限/previewフローのユニットテストを追加
- [ ] T004 `unity-cli/tests/unit/handlers/script/ScriptEditSnippetToolHandler.test.js` に括弧不整合時のロールバックテストを追加
- [ ] T005 `tests/test-mcp-script-tools.md` に新ツールの操作シナリオ（nullチェック削除）を追記

### Core Implementation
- [ ] T006 `unity-cli/src/handlers/script/ScriptEditSnippetToolHandler.js` を実装（検証・diff整形・レスポンスハッシュ）
- [ ] T007 `unity-cli/src/lsp/LspRpcClient.js` に複数テキスト編集サポートと診断呼び出しヘルパーを追加
- [ ] T008 `unity-cli/src/handlers/index.js` と 初期化コードへ `edit_snippet` の登録を追加

### Integration
- [ ] T009 `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ToolManagementHandler.cs` などツール公開リストを更新し、新ツールをエクスポート
- [ ] T010 `docs/constitution.md`・`CLAUDE.md`・`README.ja.md`・`README.md` に利用ガイドと棲み分けを反映

### Polish
- [ ] T011 `tests/unit` 実行と既存script編集テストの回帰確認
- [ ] T012 リリースノート/CHANGELOG作成、成功指標の初期モニタリング項目を追記
