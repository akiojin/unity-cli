# Data Model: MCP Capabilities正常認識

**機能ID**: SPEC-1d1a194a | **日付**: 2025-11-18

## エンティティ

### 1. ServerCapabilities

**説明**: MCPサーバーが提供する機能の宣言。MCP SDK v0.6.1のZodスキーマに準拠。

**型定義**:
```typescript
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

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `tools` | `object \| undefined` | No | ツール機能の宣言。`undefined`は未サポート。 |
| `tools.listChanged` | `boolean \| undefined` | No | ツール一覧変更通知のサポート。 |
| `resources` | `object \| undefined` | No | リソース機能の宣言。`undefined`は未サポート。 |
| `resources.subscribe` | `boolean \| undefined` | No | リソース購読機能のサポート。 |
| `resources.listChanged` | `boolean \| undefined` | No | リソース一覧変更通知のサポート。 |
| `prompts` | `object \| undefined` | No | プロンプト機能の宣言。`undefined`は未サポート。 |
| `prompts.listChanged` | `boolean \| undefined` | No | プロンプト一覧変更通知のサポート。 |

**検証ルール**:
- サポートするcapabilityのみ宣言する（空オブジェクト`{}`は禁止）
- 未サポートのcapabilityは`undefined`（フィールド自体を省略）
- `listChanged`が`true`の場合、変更通知を送信する能力を持つ

**状態遷移**: N/A（静的宣言）

---

### 2. ToolDefinition

**説明**: MCPツールの定義。107個のツール（ping, create_gameobject等）が該当。

**型定義**:
```typescript
interface ToolDefinition {
  name: string;
  description: string;
  inputSchema: {
    type: 'object';
    properties?: Record<string, unknown>;
    required?: string[];
  };
}
```

**フィールド**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `name` | `string` | Yes | ツール名（例: `ping`） |
| `description` | `string` | Yes | ツールの説明 |
| `inputSchema` | `object` | Yes | JSON Schemaによる入力パラメータ定義 |
| `inputSchema.type` | `'object'` | Yes | 常に`'object'` |
| `inputSchema.properties` | `object \| undefined` | No | パラメータプロパティ定義 |
| `inputSchema.required` | `string[] \| undefined` | No | 必須パラメータ一覧 |

**検証ルール**:
- `name`は一意（107個のツールで重複なし）
- `inputSchema.type`は必ず`'object'`（MCP仕様）

---

### 3. RequestHandler

**説明**: MCPリクエストを処理するハンドラー関数。capability宣言との整合性が必要。

**型定義**:
```typescript
type RequestHandler<Request, Response> = (request: Request) => Promise<Response>;
```

**登録済みハンドラー**:

| Schema | Capability要件 | 状態 |
|--------|---------------|------|
| `ListToolsRequestSchema` | `capabilities.tools`宣言 | ✅ 登録済み |
| `CallToolRequestSchema` | `capabilities.tools`宣言 | ✅ 登録済み |
| `ListResourcesRequestSchema` | `capabilities.resources`宣言 | ❌ 削除（capability未宣言） |
| `ListPromptsRequestSchema` | `capabilities.prompts`宣言 | ❌ 削除（capability未宣言） |

**検証ルール**:
- ハンドラー登録時に`assertRequestHandlerCapability`チェックが実行される
- capability未宣言でハンドラー登録 → Errorをthrow

---

## エンティティ関係

```
ServerCapabilities (1)
  ├─ tools (0..1)
  │   └─ listChanged (0..1)
  ├─ resources (0..1) ← 未サポート（省略）
  └─ prompts (0..1)   ← 未サポート（省略）

ToolDefinition (107)
  ├─ name (1)
  ├─ description (1)
  └─ inputSchema (1)

RequestHandler (2)
  ├─ ListToolsRequestSchema (1)
  └─ CallToolRequestSchema (1)
```

**関係性**:
- `ServerCapabilities.tools`が宣言されている → `ListToolsRequestSchema`ハンドラーを登録可能
- `ServerCapabilities.resources`が未宣言 → `ListResourcesRequestSchema`ハンドラーを登録不可
- `ServerCapabilities.prompts`が未宣言 → `ListPromptsRequestSchema`ハンドラーを登録不可

---

## 変更内容

### 変更前（v2.40.2）

```javascript
capabilities: {
  tools: { listChanged: true },
  resources: {},  // ❌ 空オブジェクト → 「Capabilities: none」の原因
  prompts: {}     // ❌ 空オブジェクト → 「Capabilities: none」の原因
}

// ❌ capability未宣言なのにハンドラー登録 → assertRequestHandlerCapability エラー
server.setRequestHandler(ListResourcesRequestSchema, async () => {
  return { resources: [] };
});

server.setRequestHandler(ListPromptsRequestSchema, async () => {
  return { prompts: [] };
});
```

### 変更後（v2.40.3予定）

```javascript
capabilities: {
  tools: { listChanged: true }
  // resources, prompts は省略（undefined）
}

// ✅ ハンドラー削除 → capability未宣言のため登録不可
// ListResourcesRequestSchema ハンドラーなし
// ListPromptsRequestSchema ハンドラーなし
```

---

## テスト可能性

**Contract Test**:
- `ServerCapabilities`が正しい形式であることを検証
- `resources`/`prompts`が`undefined`であることを検証

**Integration Test**:
- MCP SDK経由で`tools/list`リクエストを送信
- 107個の`ToolDefinition`が返却されることを検証

**Unit Test**:
- `server.js`の`capabilities`宣言が正しい形式であることを検証
- `ListResourcesRequestSchema`/`ListPromptsRequestSchema`ハンドラーが登録されていないことを検証
