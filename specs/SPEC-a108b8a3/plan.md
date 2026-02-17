# 実装計画: Unity Addressablesコマンドサポート

**機能ID**: `SPEC-a108b8a3` | **日付**: 2025-11-07 | **仕様**: [../SPEC-a108b8a3/spec.md](../SPEC-a108b8a3/spec.md)
**入力**: `/specs/SPEC-a108b8a3/spec.md`の機能仕様

## 実行フロー (/speckit.plan コマンドのスコープ)
```
1. 入力パスから機能仕様を読み込み ✅
2. 技術コンテキストを記入 ✅
3. 憲章チェックセクションを評価 → 進行中
4. Phase 0 を実行 → research.md → 進行中
5. Phase 1 を実行 → contracts, data-model.md, quickstart.md → 進行中
6. 憲章チェックセクションを再評価
7. Phase 2 を計画 → タスク生成アプローチを記述
8. 停止 - /speckit.tasks コマンドの準備完了
```

## 概要

Unity AddressablesのフルサポートをMCPツールとして実装。LLMエージェントがUnity Editor経由でAddressables操作を完全に制御可能にする。以下の4つのユーザーストーリーを優先度順に実装：

1. **P1: アセット登録管理** - 登録、削除、アドレス名変更、ラベル付与、一覧取得
2. **P2: グループ管理** - グループ作成、削除、アセット移動、一覧取得
3. **P3: ビルド自動化** - ビルド実行、キャッシュクリア、エラー報告
4. **P4: 依存関係分析** - 重複アセット検出、依存関係解析、未使用アセット検出

技術アプローチ: 既存のMCPハンドラーアーキテクチャ（Node.js + Unity Editor C#）を拡張し、Unity Addressables APIをラップして公開。

## 技術コンテキスト

**言語/バージョン**: JavaScript (Node.js v18+) + C# (Unity 2021.3+)
**主要依存関係**:
- Node側: 既存MCPサーバーフレームワーク (`BaseToolHandler.js`)
- Unity側: Unity Addressablesパッケージ (`com.unity.addressables v2.7.4`)、Newtonsoft.Json
**ストレージ**: N/A (Addressablesの設定はUnityが管理)
**テスト**: Jest (Node側 integration tests) + Unity Test Framework (Unity側 contract tests)
**対象プラットフォーム**: Unity Editor (Windows/macOS/Linux)
**プロジェクトタイプ**: single (MCPサーバー拡張)
**パフォーマンス目標**:
- アセット登録: <5秒
- グループ一覧取得(100個): <3秒
- 依存関係分析(1000個): <30秒
**制約**:
- Unity Editorが起動している必要がある
- Addressablesパッケージがインストール済み
- TCP JSON-RPC通信経由でのみ操作可能
**スケール/スコープ**:
- 単一Unityプロジェクト内のAddressables操作
- 数千個のアセット管理をサポート
- LLMエージェント向けに最適化されたレスポンス

## 憲章チェック
*ゲート: Phase 0 research前に合格必須。Phase 1 design後に再チェック。*

**シンプルさ**:
- プロジェクト数: 1 (MCPサーバー拡張) ✅
- フレームワークを直接使用? **Yes** - 既存のBaseToolHandler、Unity Addressables APIを直接使用 ✅
- 単一データモデル? **Yes** - エンティティはJSON-RPCレスポンス形式のみ（DTOなし）✅
- パターン回避? **Yes** - Repository/UoW不使用、ハンドラーがAddressables APIを直接呼び出し ✅

**アーキテクチャ**:
- すべての機能をライブラリとして? **N/A** - これはMCPツール（ハンドラー）であり、ライブラリではない
- ライブラリリスト: N/A
- ライブラリごとのCLI: N/A
- ライブラリドキュメント: N/A

**テスト (妥協不可)**:
- RED-GREEN-Refactorサイクルを強制? **Yes** - TDD厳守、テストが実装より先 ✅
- Gitコミットはテストが実装より先に表示? **Yes** - コミット順序: テスト → 実装 ✅
- 順序: Contract→Integration→E2E→Unitを厳密に遵守? **Yes** ✅
  - Contract tests: Node-Unity間のJSON-RPCインターフェース
  - Integration tests: 実Unity Editor接続
  - Unit tests: Node側のvalidation/formatting
- 実依存関係を使用? **Yes** - Integration testsは実Unity Editor接続 ✅
- Integration testの対象: 新しいAddressablesハンドラー（新ライブラリ相当）✅
- 禁止: テスト前の実装、REDフェーズのスキップ ✅

**可観測性**:
- 構造化ロギング含む? **Yes** - Node側はlogger経由、Unity側はDebug.Log経由 ✅
- フロントエンドログ → バックエンド? **N/A** - これはUnity Editor拡張
- エラーコンテキスト十分? **Yes** - エラー時に詳細メッセージ、推奨解決策を返す ✅

**バージョニング**:
- バージョン番号割り当て済み? **Yes** - `package.json`で管理（semantic-release自動化）✅
- 変更ごとにBUILDインクリメント? **Yes** - semantic-releaseが自動実行 ✅
- 破壊的変更を処理? **Yes** - 新規ツール追加のため破壊的変更なし ✅

## プロジェクト構造

### ドキュメント (この機能)
```
specs/SPEC-a108b8a3/
├── spec.md              # 機能仕様書 (完了)
├── plan.md              # このファイル (/speckit.plan コマンド出力)
├── research.md          # Phase 0 出力 (/speckit.plan コマンド) → 作成予定
├── data-model.md        # Phase 1 出力 (/speckit.plan コマンド) → 作成予定
├── quickstart.md        # Phase 1 出力 (/speckit.plan コマンド) → 作成予定
├── contracts/           # Phase 1 出力 (/speckit.plan コマンド) → 作成予定
│   ├── addressables-manage.json  # アセット・グループ管理コマンド契約
│   ├── addressables-build.json   # ビルド関連コマンド契約
│   └── addressables-analyze.json # 依存関係分析コマンド契約
└── tasks.md             # Phase 2 出力 (/speckit.tasks コマンド - /speckit.planでは作成しない)
```

### ソースコード (リポジトリルート)
```
# Node.js MCPサーバー側
unity-cli/src/handlers/addressables/
├── AddressablesManageToolHandler.js    # アセット・グループ管理ツール (新規)
├── AddressablesBuildToolHandler.js     # ビルド実行ツール (新規)
└── AddressablesAnalyzeToolHandler.js   # 依存関係分析ツール (新規)

unity-cli/src/handlers/index.js        # ハンドラーエクスポート (更新)

# Unity Editor C#側
UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/
└── AddressablesHandler.cs              # Addressables操作の実装 (新規)

# テスト
tests/integration/addressables/
├── addressables-manage.test.js         # アセット・グループ管理テスト (新規)
├── addressables-build.test.js          # ビルドテスト (新規)
└── addressables-analyze.test.js        # 分析テスト (新規)
```

**構造決定**: 単一プロジェクト構造 (MCPサーバー拡張)

## Phase 0: アウトライン＆リサーチ

### リサーチタスク

すべての技術コンテキストは既知であり、事前調査により以下が確認済み：

1. **Unity Addressables API**: v2.7.4がインストール済み、AddressableAssetSettings APIが利用可能
2. **既存ハンドラーパターン**: BaseToolHandler、JSON-RPCプロトコル、Unity CommandHandler統合
3. **テストフレームワーク**: Jest (Node側)、Unity Test Framework (Unity側)

### リサーチ結果

**技術選択の理由**:

1. **ハンドラー分割戦略: 3つのハンドラー**
   - **決定**: `AddressablesManageToolHandler`, `AddressablesBuildToolHandler`, `AddressablesAnalyzeToolHandler`
   - **理由**: 責任分離 - 管理操作、ビルド操作、分析操作は異なるユースケース。各ハンドラーが独立してテスト可能
   - **代替案**: 単一の`AddressablesToolHandler` - 却下理由: 単一ハンドラーが大きくなりすぎる（15+ actions）

2. **Unity側の実装: 単一Handler**
   - **決定**: `AddressablesHandler.cs` 一つで全操作を実装
   - **理由**: Unity側は実装詳細、Node側が責任分離を担う。Unity側は`HandleCommand(action, parameters)`パターンで統一
   - **代替案**: Unity側も分割 - 却下理由: 不要な複雑化、既存パターン（AssetDatabaseHandler等）は単一ファイル

3. **JSON-RPCコマンド形式**
   - **決定**: `addressables_manage`, `addressables_build`, `addressables_analyze`
   - **理由**: 既存のコマンド命名規則（`manage_asset_database`, `package_managerr`）に準拠
   - **代替案**: `addressables:manage`, `addressables/manage` - 却下理由: 既存プロトコルとの一貫性

4. **エラーハンドリング戦略**
   - **決定**: Unity側でtry-catchし、`{error, message, solution}`形式で返す
   - **理由**: LLMエージェントが理解しやすく、自己修復可能な形式
   - **代替案**: エラーコードのみ - 却下理由: LLM最適化の原則に反する

5. **レスポンスページング**
   - **決定**: 一覧取得系は`pageSize`, `offset`パラメータをサポート
   - **理由**: 大規模プロジェクト（1000+ アセット）でのトークン節約
   - **代替案**: すべて一括返却 - 却下理由: LLM最適化の原則（憲章VI）に反する

**出力**: research.md (下記で作成)

## Phase 1: 設計＆契約

### 1. データモデル

主要エンティティ（仕様書から抽出）:

- **AddressableEntry**: Addressablesに登録されたアセット
  - フィールド: `guid`, `assetPath`, `address`, `labels[]`, `groupName`
  - 検証: `assetPath`は存在する必要がある、`address`は一意

- **AddressablesGroup**: アセットをまとめる論理的なグループ
  - フィールド: `groupName`, `buildPath`, `loadPath`, `entriesCount`
  - 検証: `groupName`は一意

- **BuildResult**: Addressablesビルドの実行結果
  - フィールド: `success`, `duration`, `outputPath`, `errors[]`
  - 状態遷移: N/A（ステートレス）

- **AnalysisReport**: 依存関係分析の結果
  - フィールド: `duplicates[]`, `unused[]`, `dependencies{}`
  - 検証: N/A

詳細は`data-model.md`に記述（下記で作成）。

### 2. API契約

機能要件（FR-001〜FR-015）から以下のJSON-RPCコマンドを生成：

#### `addressables_manage` コマンド

| Action | 説明 | FR |
|--------|------|-----|
| `add_entry` | アセットをAddressableとして登録 | FR-001 |
| `remove_entry` | Addressableエントリを削除 | FR-004 |
| `set_address` | アドレス名を変更 | FR-002 |
| `add_label` / `remove_label` | ラベル追加/削除 | FR-003 |
| `list_entries` | 登録済みアセット一覧取得 | FR-005 |
| `list_groups` | グループ一覧取得 | FR-008 |
| `create_group` | 新規グループ作成 | FR-006 |
| `remove_group` | グループ削除 | FR-006 |
| `move_entry` | エントリを別グループに移動 | FR-007 |

#### `addressables_build` コマンド

| Action | 説明 | FR |
|--------|------|-----|
| `build` | Addressablesビルド実行 | FR-009 |
| `clean_build` | ビルドキャッシュクリア | FR-010 |

#### `addressables_analyze` コマンド

| Action | 説明 | FR |
|--------|------|-----|
| `analyze_duplicates` | 重複アセット検出 | FR-012 |
| `analyze_dependencies` | 依存関係解析 | FR-011 |
| `analyze_unused` | 未使用アセット検出 | FR-013 |

OpenAPI相当のスキーマは`/contracts/`に出力（下記で作成）。

### 3. 契約テスト生成

各コマンドに対して契約テストを作成：

- `tests/integration/addressables/addressables-manage.test.js`
- `tests/integration/addressables/addressables-build.test.js`
- `tests/integration/addressables/addressables-analyze.test.js`

**重要**: これらのテストは最初に失敗する必要がある（RED）。

### 4. テストシナリオ抽出

ユーザーストーリーから統合テストシナリオを生成：

- **P1 (アセット登録)**: "プレハブをAddressableとして登録 → アドレス名変更 → ラベル追加 → 一覧取得で確認"
- **P2 (グループ管理)**: "新規グループ作成 → アセット移動 → グループ一覧確認 → グループ削除"
- **P3 (ビルド)**: "アセット登録 → ビルド実行 → 成功確認 → キャッシュクリア"
- **P4 (分析)**: "重複アセット作成 → 分析実行 → 重複検出確認"

詳細は`quickstart.md`に記述（下記で作成）。

### 5. エージェントファイル更新

該当なし（本プロジェクトではCLAUDE.mdが既に存在し、十分に更新されている）。

**出力**: data-model.md, /contracts/*, 失敗するテスト, quickstart.md

## Phase 2: タスク計画アプローチ

*このセクションは/speckit.tasksコマンドが実行することを記述 - /speckit.plan中は実行しない*

**タスク生成戦略**:

1. **Setup Tasks** (環境準備):
   - Addressablesパッケージインストール確認
   - テスト用Unityプロジェクトセットアップ
   - CI環境でのUnity Editor起動確認

2. **Contract Test Tasks** (TDD: RED):
   - `addressables-manage.test.js` 作成 [P]
   - `addressables-build.test.js` 作成 [P]
   - `addressables-analyze.test.js` 作成 [P]
   - テスト実行 → 失敗確認

3. **Core Implementation Tasks** (TDD: GREEN):
   - Unity側: `AddressablesHandler.cs` 実装
     - `add_entry`, `remove_entry`, `set_address` 実装
     - `add_label`, `remove_label`, `list_entries` 実装
     - `list_groups`, `create_group`, `remove_group` 実装
     - `move_entry` 実装
     - `build`, `clean_build` 実装
     - `analyze_duplicates`, `analyze_dependencies`, `analyze_unused` 実装
   - Node側: Handler実装
     - `AddressablesManageToolHandler.js` 実装 [P]
     - `AddressablesBuildToolHandler.js` 実装 [P]
     - `AddressablesAnalyzeToolHandler.js` 実装 [P]
   - `unity-cli/src/handlers/index.js` 更新 (ハンドラーエクスポート)

4. **Integration Test Tasks** (TDD: GREEN):
   - Contract tests実行 → 合格確認
   - 各ユーザーストーリーのE2Eシナリオ実行

5. **Polish Tasks**:
   - エラーメッセージ改善
   - レスポンスページング実装
   - ドキュメント更新 (README.md, README.ja.md)

**順序戦略**:
- TDD順序: Contract test → Unity実装 → Node実装 → Integration test
- 依存関係順序: Unity側実装が完了してからNode側実装
- 並列実行: Node側の3つのハンドラーは並列実装可能 [P]

**推定出力**: tasks.mdに35-40個の番号付き、順序付きタスク

**重要**: このフェーズは/speckit.tasksコマンドで実行、/speckit.planではない

## Phase 3+: 今後の実装

*これらのフェーズは/planコマンドのスコープ外*

**Phase 3**: タスク実行 (/speckit.tasksコマンドがtasks.mdを作成)
**Phase 4**: 実装 (憲章原則に従ってtasks.mdを実行)
**Phase 5**: 検証 (テスト実行、quickstart.md実行、パフォーマンス検証)

## 複雑さトラッキング

*憲章チェックに正当化が必要な違反がある場合のみ記入*

| 違反 | 必要な理由 | より単純な代替案が却下された理由 |
|------|-----------|--------------------------------|
| なし | - | - |

すべての憲章要件を満たしており、複雑さの逸脱はありません。

## 進捗トラッキング

*このチェックリストは実行フロー中に更新される*

**フェーズステータス**:
- [x] Phase 0: Research完了 (/speckit.plan コマンド)
- [x] Phase 1: Design完了 (/speckit.plan コマンド)
- [x] Phase 2: Task planning完了 (/speckit.plan コマンド - アプローチのみ記述)
- [ ] Phase 3: Tasks生成済み (/speckit.tasks コマンド)
- [ ] Phase 4: 実装完了
- [ ] Phase 5: 検証合格

**ゲートステータス**:
- [x] 初期憲章チェック: 合格
- [x] 設計後憲章チェック: 合格
- [x] すべての要明確化解決済み
- [x] 複雑さの逸脱を文書化済み (逸脱なし)

---
*憲章 v1.0.0 に基づく - `docs/constitution.md` 参照*
