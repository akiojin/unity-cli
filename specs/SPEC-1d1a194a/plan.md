# 実装計画: MCP Capabilities正常認識

**機能ID**: `SPEC-1d1a194a` | **日付**: 2025-11-18 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-1d1a194a/spec.md`の機能仕様

## 概要

Claude CodeなどMCPクライアントで「Capabilities: none」と表示される問題を修正し、加えてツール数上限（100件）クライアントでも接続可能にする。MCP SDK v0.6.1の仕様準拠（未サポートcapabilityの省略）に加え、カテゴリ指定による公開ツール制御を提供する。

**主要変更箇所**:
- `unity-cli/src/core/server.js`: capabilities宣言（2箇所）とハンドラー登録（2箇所削除）
- `unity-cli/src/core/toolCategoryFilter.js`: カテゴリ正規化・ツール分類・公開判定
- `unity-cli/src/core/config.js`: カテゴリ指定環境変数読み込み
- `unity-cli/src/core/server.js`: `tools/list`/`tools/call`へのカテゴリ公開ポリシー適用
- `docs/configuration.md`, `docs/tools.md`, `README.md`, `README.ja.md`: ツール数上限クライアント向け設定追記

**成功基準**:
- Claude Codeで「Capabilities: tools」と表示される
- カテゴリ指定で`tools/list`を縮小でき、`tools/call`と整合する
- 既存テストと追加テストが成功する

## 追補 (2026-02-14): Issue #381 対応

### 実装方針

- デフォルト（カテゴリ指定なし）は従来互換で全ツール公開
- `UNITY_MCP_TOOL_INCLUDE_CATEGORIES` と `UNITY_MCP_TOOL_EXCLUDE_CATEGORIES` を追加
- includeで候補を絞り、excludeで最終除外する
- 非公開ツール名の直接実行を`tools/call`で拒否し、`tools/list`の公開範囲と一致させる

### TDD適用方針

1. RED: カテゴリフィルタの単体テストと`startServer`の公開範囲テストを先行追加  
2. GREEN: `toolCategoryFilter`・`config`・`server`を実装してテスト合格  
3. REFACTOR: 仕様説明と運用ドキュメント（設定例）を更新

### 追加テスト対象

- `unity-cli/tests/unit/core/toolCategoryFilter.test.js`
- `unity-cli/tests/unit/core/startServer.test.js`
- `unity-cli/tests/unit/core/config.test.js`

## 技術コンテキスト

**言語/バージョン**: Node.js 18.0.0以降（ES Modules）
**主要依存関係**: @modelcontextprotocol/sdk v0.6.1, better-sqlite3 v9.4.3
**ストレージ**: N/A（MCPサーバーはステートレス）
**テスト**: Node.js標準test runner（`node --test`）
**対象プラットフォーム**: Node.js 18〜22（`engines`フィールド指定）
**プロジェクトタイプ**: single（`unity-cli/`単一プロジェクト）
**パフォーマンス目標**: MCP接続時100ms以内にcapability宣言完了
**制約**:
- MCP SDK v0.6.1の`assertRequestHandlerCapability`チェックに準拠
- npm packageとして配布（`unity-cli/`ディレクトリのみ）
- semantic-release自動バージョニング（手動version変更禁止）
**スケール/スコープ**: 107個のツール定義、68個のテストファイル

## 憲章チェック

*ゲート: Phase 0 research前に合格必須。Phase 1 design後に再チェック。*

**シンプルさ**:
- プロジェクト数: 1 (unity-cli/) ✅
- フレームワークを直接使用? ✅（MCP SDKを直接使用、ラッパーなし）
- 単一データモデル? ✅（ToolDefinition、ToolResponse）
- パターン回避? ✅（BaseToolHandlerのみ、不要な抽象化なし）

**アーキテクチャ**:
- すべての機能をライブラリとして? ✅（`src/core/`, `src/handlers/`）
- ライブラリリスト:
  - **core**: MCPサーバー本体、UnityConnection、config
  - **handlers**: 107個のツールハンドラー（BaseToolHandler継承）
  - **lsp**: C# LSP統合（コードインデックス、シンボル検索）
- ライブラリごとのCLI: ✅ `unity-cli --help/--version`
- ライブラリドキュメント: ✅ README.md + CLAUDE.md

**テスト (妥協不可)**:
- RED-GREEN-Refactorサイクルを強制? ✅（今回のTDD実装で厳守）
- Gitコミットはテストが実装より先に表示? ✅（テストコミット→実装コミット順）
- 順序: Contract→Integration→E2E→Unitを厳密に遵守? ✅
  1. Contract test: MCP capabilities宣言テスト
  2. Integration test: MCP SDK接続テスト
  3. Unit test: server.js capabilities検証テスト
- 実依存関係を使用? ✅（MCP SDK実インスタンス、モックなし）
- Integration testの対象: ✅ 新しいcapability宣言ロジック
- 禁止: テスト前の実装、REDフェーズのスキップ ✅

**可観測性**:
- 構造化ロギング含む? ✅（logger.info/debug/error）
- フロントエンドログ → バックエンド? N/A（サーバーサイドのみ）
- エラーコンテキスト十分? ✅（エラーメッセージにcode、details含む）

**バージョニング**:
- バージョン番号割り当て済み? ✅（semantic-releaseが自動決定: v2.40.3予定）
- 変更ごとにBUILDインクリメント? ✅（`fix:`コミットでpatch up）
- 破壊的変更を処理? N/A（破壊的変更なし）

## プロジェクト構造

### ドキュメント (この機能)
```
specs/SPEC-1d1a194a/
├── spec.md              # 機能仕様 (/speckit.specify 出力)
├── plan.md              # このファイル (/speckit.plan 出力)
├── research.md          # Phase 0 出力（本計画で作成）
├── data-model.md        # Phase 1 出力（本計画で作成）
├── contracts/           # Phase 1 出力（本計画で作成）
│   └── mcp-capabilities.schema.json  # MCP capabilities schema
├── quickstart.md        # Phase 1 出力（本計画で作成）
└── tasks.md             # Phase 2 出力 (/speckit.tasks で作成)
```

### ソースコード (リポジトリルート)
```
unity-cli/
├── src/
│   ├── core/
│   │   ├── server.js        # ✏️ 修正対象: capabilities宣言 + ハンドラー登録
│   │   ├── config.js
│   │   └── unityConnection.js
│   └── handlers/
│       └── index.js         # 107個のハンドラー登録
├── tests/
│   ├── contract/            # ✨ 新規: MCP capabilities contract test
│   ├── integration/         # ✨ 新規: MCP SDK integration test
│   └── unit/
│       └── core/
│           └── server.test.js  # ✨ 新規: server.js capabilities test
├── README.md                # ✏️ 修正対象: トラブルシューティング追加
└── package.json             # semantic-releaseが自動更新
```

**構造決定**: オプション1（単一プロジェクト） - MCPサーバーのみ

## Phase 0: アウトライン＆リサーチ

### リサーチ項目

1. **MCP SDK v0.6.1 capabilities仕様**:
   - **質問**: 未サポートcapabilityは空オブジェクト`{}`と省略のどちらが正しい?
   - **調査方法**: @modelcontextprotocol/sdk v0.6.1のTypeScript定義確認
   - **期待される結果**: ZodOptional型定義を確認、省略が正しいことを検証

2. **MCP SDK v0.6.1 assertRequestHandlerCapability動作**:
   - **質問**: capability未宣言でハンドラー登録するとエラーが発生するか?
   - **調査方法**: MCP SDKソースコード`server/index.js`の`assertRequestHandlerCapability`実装確認
   - **期待される結果**: capability未宣言のハンドラー登録は実行時エラーを発生させることを確認

3. **Claude Code MCP client capabilities解釈**:
   - **質問**: なぜ空オブジェクト`{}`で「Capabilities: none」と表示されるのか?
   - **調査方法**: Claude Code側の実装推測（MCP仕様の厳密解釈）
   - **期待される結果**: 空オブジェクト = 「機能なし」と解釈される理由を文書化

**出力**: `research.md`（リサーチ結果を次のセクションで作成）

## Phase 1: 設計＆契約

### 1. データモデル

**エンティティ**: MCPサーバーcapabilities宣言

```typescript
// MCP SDK v0.6.1準拠の型定義
interface ServerCapabilities {
  tools?: {
    listChanged?: boolean;
  };
  resources?: {
    subscribe?: boolean;
    listChanged?: boolean;
  };
  prompts?: {
    listChanged?: boolean;
  };
}
```

**変更前**:
```javascript
capabilities: {
  tools: { listChanged: true },
  resources: {},  // ❌ 空オブジェクト
  prompts: {}     // ❌ 空オブジェクト
}
```

**変更後**:
```javascript
capabilities: {
  tools: { listChanged: true }
  // resources, promptsは省略
}
```

### 2. API契約

**MCP JSON-RPC契約**:

```json
// tools/list リクエスト
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list"
}

// tools/list レスポンス
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "ping",
        "description": "Test connection to Unity Editor",
        "inputSchema": { "type": "object", "properties": {} }
      }
      // ... 106個のツール定義
    ]
  }
}
```

**契約ファイル**: `contracts/mcp-capabilities.schema.json`（次のセクションで作成）

### 3. 契約テスト

**Contract Test 1**: MCP capabilities宣言が正しい形式であることを検証

```javascript
// tests/contract/mcp-capabilities.test.js
test('Server capabilities should only declare supported features', async () => {
  const { server } = await createServer();
  const capabilities = server.getCapabilities();

  // tools は宣言されている
  assert.ok(capabilities.tools);
  assert.strictEqual(capabilities.tools.listChanged, true);

  // resources と prompts は省略されている（undefined）
  assert.strictEqual(capabilities.resources, undefined);
  assert.strictEqual(capabilities.prompts, undefined);
});
```

**Contract Test 2**: 未サポートcapabilityのハンドラーが登録されていないことを検証

```javascript
// tests/contract/mcp-handler-registration.test.js
test('Server should not register handlers for unsupported capabilities', () => {
  // ListResourcesRequestSchema ハンドラーが登録されていないことを確認
  // ListPromptsRequestSchema ハンドラーが登録されていないことを確認
});
```

### 4. Integration Test

**Integration Test**: MCP SDK経由でtools/listリクエストを送信し、107個のツール定義が返却されることを検証

```javascript
// tests/integration/mcp-tools-list.test.js
test('tools/list should return all 107 tool definitions', async () => {
  const client = new MCPClient();
  await client.connect(server);

  const response = await client.request('tools/list');

  assert.strictEqual(response.tools.length, 107);
  assert.ok(response.tools.find(t => t.name === 'ping'));
});
```

### 5. クイックスタート

`quickstart.md`に以下の検証手順を記載:

1. unity-cliをインストール
2. Claude Codeで接続
3. 「Capabilities: tools」と表示されることを確認
4. `ping`ツールを実行して接続確認

（詳細は次のセクションで作成）

### 6. エージェント固有ファイル

**CLAUDE.md更新不要**: 既存のCLAUDE.mdに「MCP SDK v0.6.1仕様準拠」の記載なし
→ 今回の変更は技術的詳細のため、CLAUDE.md更新はスコープ外

## Phase 2: タスク計画アプローチ

**タスク生成戦略**:

1. **Setup** (並列実行可):
   - [P] `research.md`作成（MCP SDK仕様調査）
   - [P] `contracts/mcp-capabilities.schema.json`作成
   - [P] `data-model.md`作成
   - [P] `quickstart.md`作成

2. **Contract Tests** (並列実行可):
   - [P] `tests/contract/mcp-capabilities.test.js`作成（RED: テスト失敗）
   - [P] `tests/contract/mcp-handler-registration.test.js`作成（RED: テスト失敗）

3. **Integration Tests**:
   - `tests/integration/mcp-tools-list.test.js`作成（RED: テスト失敗）

4. **Unit Tests**:
   - `tests/unit/core/server-capabilities.test.js`作成（RED: テスト失敗）

5. **Core Implementation** (順次実行):
   - `unity-cli/src/core/server.js`修正:
     - Step 1: capabilities宣言から`resources: {}, prompts: {}`削除
     - Step 2: ListResourcesRequestSchemaハンドラー削除
     - Step 3: ListPromptsRequestSchemaハンドラー削除
     - Step 4: import文からListResourcesRequestSchema, ListPromptsRequestSchema削除
   - テスト実行（GREEN: すべてのテスト合格）

6. **Documentation**:
   - `unity-cli/README.md`にトラブルシューティングセクション追加

7. **Integration & Polish**:
   - 既存68個のテストすべて実行（リグレッション確認）
   - ESLint/Prettier実行
   - コミットメッセージ作成（Conventional Commits準拠）

**推定タスク数**: 14タスク

**並列実行マーカー**: Setup（4タスク）、Contract Tests（2タスク）を`[P]`マーク

## 複雑さトラッキング

*憲章チェックに正当化が必要な違反はありません。*

## 進捗トラッキング

**フェーズステータス**:
- [x] Phase 0: Research完了 (/speckit.plan コマンド) - ✅ research.md作成済み
- [x] Phase 1: Design完了 (/speckit.plan コマンド) - ✅ data-model.md, contracts/, quickstart.md作成済み
- [x] Phase 2: Task planning完了 (/speckit.plan コマンド - アプローチ記述済み)
- [ ] Phase 3: Tasks生成済み (/speckit.tasks コマンド)
- [ ] Phase 4: 実装完了
- [ ] Phase 5: 検証合格

**ゲートステータス**:
- [x] 初期憲章チェック: 合格
- [x] 設計後憲章チェック: 合格（Phase 1完了後に再評価）
- [x] すべての要明確化解決済み（research.mdで解決済み）
- [x] 複雑さの逸脱を文書化済み（逸脱なし）

---

## 実装計画完了

**生成されたアーティファクト**:
- ✅ `plan.md`: この実装計画ドキュメント
- ✅ `research.md`: MCP SDK v0.6.1仕様調査結果
- ✅ `data-model.md`: ServerCapabilities, ToolDefinition, RequestHandlerエンティティ定義
- ✅ `contracts/mcp-capabilities.schema.json`: MCP capabilities JSON Schema
- ✅ `quickstart.md`: ユーザーストーリー検証手順

**次のステップ**: `/speckit.tasks` コマンドで`tasks.md`を生成してください。
