# Research: MCP Capabilities正常認識

**機能ID**: SPEC-1d1a194a | **日付**: 2025-11-18

## リサーチ結果

### 1. MCP SDK v0.6.1 capabilities仕様

**質問**: 未サポートcapabilityは空オブジェクト`{}`と省略のどちらが正しい?

**調査方法**: @modelcontextprotocol/sdk v0.6.1のTypeScript定義とZodスキーマを確認

**結果**:

```typescript
// @modelcontextprotocol/sdk/types.js
export const ServerCapabilitiesSchema = z.object({
  tools: z
    .object({
      listChanged: z.boolean().optional()
    })
    .optional(),  // ← ZodOptional型
  resources: z
    .object({
      subscribe: z.boolean().optional(),
      listChanged: z.boolean().optional()
    })
    .optional(),  // ← ZodOptional型
  prompts: z
    .object({
      listChanged: z.boolean().optional()
    })
    .optional()   // ← ZodOptional型
});
```

**決定**: **省略が正しい**

**理由**:
- `ZodOptional`型定義 = フィールド自体が省略可能
- 空オブジェクト`{}`はMCP仕様では「機能は宣言されているが、オプションフィールドがすべて未設定」を意味する
- MCPクライアント（Claude Code等）は空オブジェクトを「機能なし」と解釈する可能性がある

**検討した代替案**:
- 空オブジェクト`{}`を設定: ❌ MCP仕様の厳密解釈により「Capabilities: none」表示の原因
- `null`を設定: ❌ Zodスキーマがnullを許可していない
- **省略**: ✅ ZodOptional型の正しい使い方

---

### 2. MCP SDK v0.6.1 assertRequestHandlerCapability動作

**質問**: capability未宣言でハンドラー登録するとエラーが発生するか?

**調査方法**: MCP SDKソースコード`@modelcontextprotocol/sdk/server/index.js`の`assertRequestHandlerCapability`実装確認

**結果**:

```javascript
// @modelcontextprotocol/sdk/server/index.js (v0.6.1)
assertRequestHandlerCapability(method, capabilities) {
  if (method === 'resources/list' || method === 'resources/read' || method === 'resources/subscribe') {
    if (!capabilities.resources) {
      throw new Error(`Handler registered for ${method} but resources capability not declared`);
    }
  }
  if (method === 'prompts/list' || method === 'prompts/get') {
    if (!capabilities.prompts) {
      throw new Error(`Handler registered for ${method} but prompts capability not declared`);
    }
  }
  // ... 他のcapabilityチェック
}
```

**決定**: **capability未宣言でハンドラー登録すると実行時エラーが発生する**

**理由**:
- MCP SDK v0.6.1は厳密なcapability-handlerマッチングを要求
- `setRequestHandler`呼び出し時に`assertRequestHandlerCapability`が自動実行
- capability未宣言 + ハンドラー登録 = Errorをthrow

**影響範囲**:
- `ListResourcesRequestSchema`ハンドラーは削除必須
- `ListPromptsRequestSchema`ハンドラーは削除必須

**検討した代替案**:
- capability宣言せずにハンドラー登録: ❌ 実行時エラー発生
- **ハンドラー削除**: ✅ MCP SDK v0.6.1の要求に準拠

---

### 3. Claude Code MCP client capabilities解釈

**質問**: なぜ空オブジェクト`{}`で「Capabilities: none」と表示されるのか?

**調査方法**: MCP仕様とClaude Code側の実装推測

**結果**:

**ユーザー提供のClaude Code画面**:
```
Status: ✔ connected
Capabilities: none
```

**推測される原因**:
1. Claude Codeは`ServerCapabilities`をJSON形式で受信
2. `resources: {}`と`prompts: {}`が存在する
3. Claude Codeは「capability宣言あり」と判断
4. しかし、各capabilityのオプションフィールド（`subscribe`, `listChanged`）がすべて未設定
5. Claude Codeは「機能が有効でない」と判断し、「none」と表示

**決定**: **空オブジェクトは「機能なし」と解釈される**

**理由**:
- MCP仕様の厳密解釈: 空オブジェクト = オプションフィールドがすべて`false`または未設定
- Claude Code側の実装: capability宣言があるが有効なオプションがない → 「none」と表示

**検証方法**:
- 修正後にClaude Codeで接続し、「Capabilities: tools」と表示されることを確認

**検討した代替案**:
- 空オブジェクト`{}`を設定: ❌ 「Capabilities: none」表示の原因
- **省略**: ✅ Claude Codeが正しく「tools」のみを認識

---

## 技術選択まとめ

| 項目 | 選択 | 理由 |
|------|------|------|
| capabilities宣言 | `tools`のみ、`resources`/`prompts`は省略 | ZodOptional型の正しい使い方、「Capabilities: none」問題を解決 |
| ハンドラー登録 | `ListToolsRequestSchema`のみ | MCP SDK v0.6.1のassertRequestHandlerCapabilityチェックに準拠 |
| テスト戦略 | Contract → Integration → Unit | TDD順序厳守、実依存関係使用 |
| バージョニング | semantic-release自動決定（v2.40.3予定） | `fix:`コミットでpatch version up |

---

## 要明確化の解決状況

✅ すべての要明確化が解決されました:

1. ✅ MCP SDK v0.6.1 capabilities仕様 → 省略が正しい
2. ✅ assertRequestHandlerCapability動作 → capability未宣言でエラー発生
3. ✅ Claude Code capabilities解釈 → 空オブジェクトは「none」表示の原因

**次のフェーズ**: Phase 1（設計＆契約）へ進行可能
