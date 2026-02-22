# クイックスタート: MCP Capabilities正常認識

**機能ID**: SPEC-1d1a194a | **日付**: 2025-11-18

## 検証手順（ユーザーストーリー1-3に対応）

### ユーザーストーリー1: ツール一覧が正常に認識される

**手順**:

1. **unity-cliをインストール**:
   ```bash
   npm install -g @akiojin/unity-cli@latest
   ```

2. **Unity Editorを起動**:
   - Unity 2020.3 LTS以降を起動
   - Unity CLI Bridgeパッケージがインストールされていることを確認

3. **Claude Codeで接続**:
   - Claude Codeを起動
   - MCP設定画面でunity-cliを追加
   - 接続ステータスを確認

4. **期待される結果**:
   ```
   Status: ✔ connected
   Capabilities: tools  ← ✅ 「tools」と表示される
   ```

5. **ツール一覧を確認**:
   - Claude CodeのMCPツール一覧を表示
   - 107個のツール（ping, create_gameobject等）がすべて表示される

6. **pingツールを実行**:
   ```json
   {
     "tool": "ping"
   }
   ```
   - Unity Editorとの接続が確認できる
   - レスポンスにUnity Editorのバージョン情報が含まれる

**成功基準**:
- ✅ 「Capabilities: tools」と表示される
- ✅ 107個のツールすべてが認識される
- ✅ pingツールが正常に実行される

---

### ユーザーストーリー2: エラーなく接続が完了する

**手順**:

1. **unity-cliを起動**:
   ```bash
   unity-cli
   ```

2. **サーバーログを確認**:
   - コンソールに以下のログが表示される:
     ```
     [INFO] MCP server started successfully
     [INFO] 107/107 handlers initialized successfully
     ```

3. **Claude Codeで接続**:
   - 接続ステータスが「connected」になる
   - エラー/ワーニングが表示されない

4. **Unity Editorログを確認**:
   - Unity ConsoleでMCP関連のエラーがないことを確認

**成功基準**:
- ✅ サーバーログにエラー/ワーニングが出力されない
- ✅ Claude Codeのコンソールにエラー/ワーニングが表示されない
- ✅ Unity Consoleにエラーが表示されない

---

### ユーザーストーリー3: トラブルシューティングガイドが提供される

**手順**:

1. **README.mdのTroubleshootingセクションを開く**:
   ```bash
   cat unity-cli/README.md | grep -A 20 "## Troubleshooting"
   ```

2. **「Capabilities: none」セクションを確認**:
   - 症状の説明が記載されている
   - 原因の説明が記載されている
   - 解決策が記載されている

3. **最新版へのアップデート手順を実施**:
   ```bash
   npm install -g @akiojin/unity-cli@latest
   ```

4. **Claude Codeで再接続**:
   - 「Capabilities: tools」と表示される
   - 問題が解消される

**成功基準**:
- ✅ README.mdに「Capabilities: none」セクションが存在する
- ✅ 症状・原因・解決策が明確に記載されている
- ✅ 最新版へのアップデート手順が記載されている

---

## リグレッションテスト

**既存68個のテストをすべて実行**:

```bash
cd unity-cli
npm run test:ci
```

**期待される結果**:
```
✅ 68/68 tests passed
```

---

## 手動検証チェックリスト

- [ ] Claude Codeで「Capabilities: tools」と表示される
- [ ] 107個のツールすべてが認識される
- [ ] pingツールが正常に実行される
- [ ] サーバーログにエラー/ワーニングが出力されない
- [ ] Claude Codeのコンソールにエラー/ワーニングが表示されない
- [ ] Unity Consoleにエラーが表示されない
- [ ] README.mdに「Capabilities: none」セクションが存在する
- [ ] 既存68個のテストすべて成功

---

## トラブルシューティング

### 問題: 「Capabilities: none」と表示される

**症状**:
- Status: ✔ connected
- Capabilities: none

**原因**:
- 古いバージョン（v2.40.2以前）を使用している
- capabilities宣言に空オブジェクト`{}`が設定されている

**解決策**:
1. 最新版にアップデート:
   ```bash
   npm install -g @akiojin/unity-cli@latest
   ```
2. Claude Codeを再起動
3. unity-cliに再接続

### 問題: ツール一覧が空

**症状**:
- Capabilities: tools と表示される
- ツール一覧が空（0個）

**原因**:
- Unity Editorが起動していない
- Unity CLI Bridgeパッケージがインストールされていない

**解決策**:
1. Unity Editorを起動
2. Unity Package ManagerからUnity CLI Bridgeパッケージをインストール
3. unity-cliを再起動

### 問題: assertRequestHandlerCapabilityエラー

**症状**:
- サーバー起動時にエラーが発生:
  ```
  Error: Handler registered for resources/list but resources capability not declared
  ```

**原因**:
- capability未宣言なのにハンドラーを登録している

**解決策**:
- この問題はv2.40.3で修正済み
- 最新版にアップデートしてください
