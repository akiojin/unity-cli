# タスク: SerializeField 値更新ツール

**入力**: `/specs/SPEC-0b6b106c/` の設計ドキュメント
**前提条件**: plan.md (完了), research.md/data-model.md/contracts/quickstart.md (これから作成)

## 実行フロー (main)
```
1. plan.md を開き技術スタックと対象パスを確認
2. research.md / data-model.md / contracts/ / quickstart.md を順次作成
3. Setup → Tests(RED) → 実装 → Integration → Polish の順序でタスクを実行
4. テストが失敗した状態 (RED) を確認してから実装に進む
5. すべてのタスクで TDD / 小刻みコミット / ログ更新を徹底
```

## Phase 3.1: セットアップ / ドキュメント
- [ ] **T001** `/specs/SPEC-0b6b106c/research.md` に SerializedPropertyパス表記・Prefab保存方針・PlayMode制約の調査結果を記載
- [P] **T002** `/specs/SPEC-0b6b106c/data-model.md` で `ComponentFieldUpdateRequest` と `ComponentFieldUpdateResult` の属性・検証ルールを定義
- [P] **T003** `/specs/SPEC-0b6b106c/contracts/set_component_field.request.json` と `.response.json` を作成し、MCP↔Unity間Payload契約を記述
- [P] **T004** `/specs/SPEC-0b6b106c/quickstart.md` に LLM利用者向けのドライラン→本適用→保存確認の手順を書く
- [ ] **T005** `.claude/settings` 経由の `scripts/update-agent-context.sh claude` を実行し、新ツールの利用方針/TDD必須をエージェント文脈に追記

## Phase 3.2: テストファースト (RED)
- [ ] **T006** `unity-cli/tests/unit/handlers/component/ComponentFieldSetToolHandler.test.js` に入力バリデーション・dry-run・Prefab適用・エラー分岐を網羅するREDテストを追加
- [ ] **T007** `UnityCliBridge/Packages/unity-cli-bridge/Tests/Editor/Handlers/ComponentHandlerTests.cs` にシーン/Prefab/dry-run/PlayModeブロックのEditModeテスト (失敗状態) を追加
- [ ] **T008** `tests/test-mcp-component-tools.md` に `set_component_field` 用のチェック項目 (U30-04) を追加し、差分比較ステップを記述

## Phase 3.3: コア実装 (GREEN化)
- [ ] **T009** `unity-cli/src/handlers/component/ComponentFieldSetToolHandler.js` を実装し、フィールドパス正規化・value型推論・dry-run処理を追加
- [ ] **T010** `unity-cli/src/handlers/index.js` と `README.md` にツール登録と説明を追記
- [ ] **T011** `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ComponentHandler.cs` に SerializedProperty更新ロジック/Prefab保存/PlayModeガードを実装 (tests T007 をGREEN化)
- [ ] **T012** `UnityCliBridge/Packages/unity-cli-bridge/Editor/Core/UnityCliBridge.cs` に `set_component_field` ルーティングを追加し、ログを出力
- [ ] **T013** `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/ComponentHandler.cs` へ構造化ログ/Undo/notes収集を追加

## Phase 3.4: 統合・ドキュメント
- [ ] **T014** `unity-cli/tests/unit/handlers/component/ComponentFieldSetToolHandler.test.js` をGREENにし、`npm run test:unit -- tests/unit/handlers/component/ComponentFieldSetToolHandler.test.js` を記録
- [ ] **T015** Unity EditModeテストを `Unity -runTests -testPlatform editmode ...` で実行し、Prefab/シーンテスト結果を共有
- [P] **T016** `tests/test-mcp-component-tools.md` のU30-04シナリオを実走し、Before/Afterスナップショット手順を更新
- [ ] **T017** `specs/SPEC-0b6b106c/quickstart.md` と `README.md` に dry-run→適用→保存のワークフローを反映

## Phase 3.5: 仕上げ / リリース準備
- [ ] **T018** `unity-cli/src/core/config.js` 周辺で `set_component_field` のログ/メトリクスカテゴリを登録し、loggerテストを追加
- [ ] **T019** `unity-cli/package.json` と Unityパッケージ `package.json` にバージョン・エクスポート項目を更新 (必要なら)
- [ ] **T020** `CHANGELOG.md` / `README.ja.md` / `CLAUDE.md` に新フロー・注意事項を追記 (TDD必須を再強調)
- [ ] **T021** 手動検証: シーン/Prefab/dry-run/エラー系のCLI操作ログを `docs/manual-tests/component-field-set.md` (新規) に残す

## 依存関係
- T001→T002-T004 (仕様が無いと設計不可)
- T006/T007/T008 (RED) が T009〜T013 (実装) より先
- T009完了後でなければ T014 (GREEN確認) に進まない
- T011完了後にのみ T012〜T015 実行可
- ドキュメント更新 (T017, T020) は実装/テストを依存

## 並列実行例
```
# ドキュメント整備後
並列1: [P] T002 data-model.md 作成
並列2: [P] T003 contracts JSON Schema
並列3: [P] T004 quickstart 手順

# 実装フェーズ
並列1: Node側 T009
並列2: Unity側 T011 (ただし共通仕様に従い連携)
```

## 注意事項
- すべての [P] タスクは異なるファイルを対象とし依存関係が無い場合のみ並列実行する
- コミットは RED→GREEN→リファクタ単位で細かく行い、各コミットに対応するテスト結果を記載
- `set_component_field` は高リスク操作のため dry-run サポートを常に確認 (T007/T017)
