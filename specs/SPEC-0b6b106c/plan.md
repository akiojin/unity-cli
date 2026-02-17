# 実装計画: SerializeField 値更新ツール

**機能ID**: `SPEC-0b6b106c` | **日付**: 2025-11-07 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-0b6b106c/spec.md` の機能仕様

## 実行フロー (/speckit.plan コマンドのスコープ)
```
1. 入力仕様を読み込み ✅
2. 技術コンテキストを記入 ✅
3. 憲章チェック (初回) ✅
4. Phase 0: research.md で未確定事項を解消 → 実施予定
5. Phase 1: data-model.md / contracts/ / quickstart.md を作成 → 実施予定
6. 憲章チェック (設計後) → Phase1完了後に再実施
7. Phase 2: タスク生成方針を本計画に記述 ✅
8. /speckit.tasks 実行待ち
```

## 概要
シーン/Prefabに紐づくSerializeFieldをリモートから安全に更新できるMCPツールを追加する。ユーザーはGameObjectパスまたはPrefab資産パスを指定して単一フィールドを更新し、dry-run・旧値/新値・保存要否・エラー理由を受け取る。Play Mode中の制約、Prefab Stage、オブジェクト参照、配列/enumなどの特例を標準化し、Undo/ログポリシーを含めて自動操作の安全性を確保する。

## 技術コンテキスト
**言語/バージョン**: Node.js 18.x (ESM) / C# 9 + Unity 2021LTS〜2023LTS
**主要依存関係**: MCP SDK, better-sqlite3 (既存), UnityEditor API (SerializedObject/PrefabUtility)
**ストレージ**: 既存のMCP設定/ログ＋Unity项目ファイル (追加永続化なし)
**テスト**: Node.js `node --test`、Unity Editor Test Runner (EditMode)
**対象プラットフォーム**: デスクトップOS上のUnity Editor + Node CLI
**プロジェクトタイプ**: multi (unity-cli + Unityパッケージ)
**パフォーマンス目標**: 単一フィールド更新を1秒以内に完了、dry-runは500ms以内で応答
**制約**: Play Modeではruntime:true指定時のみ許可、Prefab編集時は保存手順を必ず提示
**スケール/スコープ**: 10^3 GameObjects / 10^2 Prefab資産の運用を想定

## 憲章チェック
**シンプルさ**:
- プロジェクト数: 2 (Nodeサーバー, Unityパッケージ) → 既存構造に追従 ✅
- フレームワーク直使用: NodeはMCP SDKを直接利用 / UnityはEditor API直接利用 ✅
- 単一データモデル: `ComponentFieldUpdateRequest` と `SerializeFieldResult` の2つに統一予定 ✅
- 余計なパターンなし (Repository/UoW等は導入しない) ✅

**アーキテクチャ**:
- 既存BaseToolHandler + ComponentHandlerへの拡張のみ。新規ライブラリは不要 ✅
- CLI/ツールドキュメント: README + spec/plan に追記しLLM向けクイックスタートで周知 ✅

**テスト (妥協不可)**:
- RED→GREEN→リファクタを厳守。まずNode側ハンドラテスト (RED)→実装→Unity側EditModeテストの順 ✅
- Contract→Integration→Unit順: Nodeハンドラはcontract的テスト、Unity側はintegrationテストでGameplay環境を模倣 ✅
- 実依存関係を使用（Unity Editor実オブジェクト、実Prefabロード） ✅
- テスト前の実装/REDスキップは禁止 (チェックリストに追記) ✅

**可観測性**:
- 既存loggerに`set_component_field`カテゴリーを追加し、対象パス/結果/エラーを構造化出力 ✅
- エラー時は旧値を返すことで診断性を確保 ✅

**バージョニング**:
- npmパッケージはMINORアップデート予定 (新機能)。Unityパッケージは `package.json` のpatchアップデート記録を追加 ✅
- 破壊的変更なし (既存ツールに影響しない) ✅

## プロジェクト構造
### ドキュメント
```
specs/SPEC-0b6b106c/
├── spec.md        # 機能仕様 (完了)
├── plan.md        # このファイル
├── research.md    # Phase 0: API/ガード条件の調査 (作成予定)
├── data-model.md  # Phase 1: リクエスト/レスポンス構造 (作成予定)
├── quickstart.md  # Phase 1: LLM向け手順 (作成予定)
├── contracts/     # Nodeハンドラインターフェイス契約 (作成予定)
└── tasks.md       # /speckit.tasks で生成予定
```

### ソースコード
```
unity-cli/
├── src/handlers/component/ComponentFieldSetToolHandler.js  # 新規ハンドラ
├── tests/unit/handlers/component/ComponentFieldSetToolHandler.test.js # RED→GREEN
└── README.md (ツール一覧更新)

UnityCliBridge/Packages/unity-cli-bridge/
├── Editor/Handlers/ComponentHandler.cs  # SerializedProperty更新ロジックを追加
├── Editor/Core/UnityCliBridge.cs        # 新コマンドルーティング
└── Tests/Editor/Handlers/ComponentHandlerTests.cs # Prefab/シーン/EditModeテスト
```

## Phase 0: アウトライン＆リサーチ
1. **未確定事項**
   - Prefab Stage vs Prefab資産 vs シーンの判定アルゴリズム
   - enum/配列/参照型のSerializeFieldパス表記 (dot, `Array.data[x]` 等)
   - Play Mode中の安全ガード (runtimeパラメータの意味)
   - dry-run結果フォーマット (previewValueの扱い)

2. **リサーチタスク (research.mdに記録)**
   - Unity Editor API: SerializedObject/SerializedPropertyの最新制約
   - PrefabUtility.SavePrefabAsset vs ApplyPrefabInstance のベストプラクティス
   - Undo/Redoとログ出力の連携 (Unity Guidelines)
   - オブジェクト参照をGUID/AssetPathで受け取る際のセキュリティ考慮

3. **リサーチ成果の書式**
   - 決定 / 理由 / 代替案を箇条書きで記載
   - すべての要明確化タグを解消してからPhase1へ進む

## Phase 1: 設計＆契約
1. **data-model.md**
   - `ComponentFieldUpdateRequest`
     - `scope`, `gameObjectPath`, `prefabAssetPath`, `componentType`, `fieldPath`, `valueType`, `value`, `enumValue`, `objectReference`, `dryRun`, `applyPrefabChanges`, `createUndo`, `markSceneDirty`
   - `ComponentFieldUpdateResult`
     - `scope`, `gameObjectPath`, `prefabAssetPath`, `serializedPropertyPath`, `previousValue`, `appliedValue`, `requiresSave`, `notes[]`, `dryRun`
   - 検証ルール (必須/相互排他/型制約)

2. **contracts/**
   - `set_component_field.request.json` / `set_component_field.response.json` (JSON Schema) を追加
   - NodeハンドラがUnityコマンドに渡すPayload契約を明示

3. **quickstart.md**
   - LLMがツールを呼び出すためのステップ: パス取得→dry-run→本適用→保存確認
   - 事前条件: GameObjectパス確認方法、Prefab Stageの確認手順

4. **Unity側設計**
   - ComponentHandler内部でSerializedObjectの共通ユーティリティを定義
   - Play Mode制限ロジックとPrefab保存ロジックを整理
   - Undo/EditorUtility.SetDirty/PrefabUtility.RecordPrefabInstancePropertyModificationsの使い分け

5. **Node側設計**
   - ハンドラの入力検証、フィールドパス正規化(`[x]`→`.Array.data[x]`)
   - dry-run/実行共通処理
   - 失敗時の例外をMCPレスポンスに整形

6. **エージェント文脈更新**
   - `.claude/` などのエージェント設定に「SerializeField更新ツール」「テストファースト」の方針を追記 (scripts/update-agent-context.shを使用)

## Phase 2: タスク計画アプローチ
- `/speckit.tasks` で以下カテゴリを生成:
  1. **Setup**: research.md・data-model.md・contracts/・quickstart.mdの作成
  2. **Test (RED)**: Nodeユニットテスト、Unity EditModeテスト（シーン/Prefab/dry-runケース）
  3. **Core実装**: Nodeハンドラ + Unity ComponentHandler拡張 + コマンド登録
  4. **Integration**: README更新、テストグリーン確認、ログ出力整備
  5. **Polish**: CLIヘルプ・ドキュメント、LLM向けquickstart調整
- 依存関係: `research -> data-model/contracts -> tests (RED) -> 実装 -> テストGREEN -> ドキュメント`
- 並列化: Node側テスト/実装とUnity側テスト/実装を[P]でマークし、別担当が並行可能
- すべてのタスクに「テストが先」のチェックボックスを付与し、コミット時にテスト結果を記録

---
この計画により、要件化→Research→設計→TDD実装の流れを明文化し、/speckit.tasks 実行および以降の実装フェーズに進む準備が整った。
