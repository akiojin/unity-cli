# リサーチ結果: Unity Addressablesコマンドサポート

**機能ID**: `SPEC-a108b8a3` | **日付**: 2025-11-07

## 技術決定

### 1. ハンドラー分割戦略: 3つのハンドラー

**決定**: Node側で3つの独立したハンドラーを実装
- `AddressablesManageToolHandler.js` - アセット・グループ管理
- `AddressablesBuildToolHandler.js` - ビルド操作
- `AddressablesAnalyzeToolHandler.js` - 依存関係分析

**理由**:
- **責任分離**: 各ハンドラーが明確な責任を持つ（管理/ビルド/分析）
- **独立テスト**: 各ハンドラーを独立してテスト可能
- **保守性**: 小さなファイルで管理が容易
- **既存パターン準拠**: 既存の`Asset*ToolHandler`パターンと一貫性

**検討した代替案**:
1. **単一`AddressablesToolHandler`**
   - 却下理由: 15+ actionsを持つ大きなファイルになる。保守性が低下
2. **5つ以上のハンドラー（操作ごと）**
   - 却下理由: 過度な分割、ファイル数の増加

### 2. Unity側の実装: 単一Handler

**決定**: `AddressablesHandler.cs` 一つで全操作を実装

**理由**:
- **既存パターン準拠**: `AssetDatabaseHandler.cs`, `PackageManagerHandler.cs`等と同じパターン
- **Unity側は実装詳細**: Node側が責任分離を担い、Unity側は実行のみ
- **シンプルさ**: `HandleCommand(action, parameters)`パターンで統一
- **Addressables APIの凝集性**: すべて`UnityEditor.AddressableAssets`名前空間内

**検討した代替案**:
1. **Unity側も3つのハンドラーに分割**
   - 却下理由: 不要な複雑化、Unity側は既存パターンで単一ファイル

### 3. JSON-RPCコマンド形式

**決定**: 以下の3つのコマンド
- `addressables_manage`
- `addressables_build`
- `addressables_analyze`

**理由**:
- **既存命名規則準拠**: `manage_asset_database`, `package_managerr`, `load_scene`等と一貫性
- **スネークケース**: MCPサーバーの標準
- **動詞_名詞パターン**: 操作が明確

**検討した代替案**:
1. `addressables:manage` (コロン区切り)
   - 却下理由: 既存プロトコルと非一貫
2. `addressables/manage` (スラッシュ区切り)
   - 却下理由: コマンド名ではなくパスのように見える

### 4. エラーハンドリング戦略

**決定**: 構造化エラーレスポンス
```json
{
  "error": true,
  "message": "指定されたパスのアセットが見つかりません",
  "solution": "Assets/Prefabs/Player.prefab が存在するか確認してください",
  "assetPath": "Assets/Prefabs/Player.prefab"
}
```

**理由**:
- **LLM最適化**: エージェントが自己修復可能な詳細情報
- **憲章VI準拠**: エラーメッセージは解決策を明示
- **デバッグ容易性**: コンテキスト情報を含む

**検討した代替案**:
1. **エラーコードのみ**
   - 却下理由: LLMが理解しにくい、憲章違反
2. **例外スローのみ**
   - 却下理由: Unity側でクラッシュする可能性

### 5. レスポンスページング

**決定**: 一覧取得系APIに`pageSize`, `offset`パラメータを追加

**理由**:
- **トークン節約**: 大規模プロジェクトで1000+ アセットがある場合
- **憲章VI準拠**: "大きな出力は常にページング可能"
- **デフォルト制限**: `pageSize=20` (憲章推奨値)

**実装対象**:
- `list_entries` (アセット一覧)
- `list_groups` (グループ一覧)
- `analyze_duplicates` (重複アセット一覧)
- `analyze_unused` (未使用アセット一覧)

**検討した代替案**:
1. **すべて一括返却**
   - 却下理由: 大規模プロジェクトでトークン浪費、憲章違反

## Unity Addressables API調査

### 使用するAPIクラス

| クラス | 用途 | 主要メソッド |
|--------|------|-------------|
| `AddressableAssetSettings` | 設定取得 | `GetDefault()`, `CreateGroup()`, `CreateOrMoveEntry()` |
| `AddressableAssetGroup` | グループ管理 | `AddAssetEntry()`, `RemoveAssetEntry()` |
| `AddressableAssetEntry` | エントリ管理 | `SetAddress()`, `SetLabel()` |
| `AddressableAssetSettingsDefaultObject` | デフォルト設定 | `Settings` (singleton) |
| `AddressablesPlayerBuildResult` | ビルド結果 | `Error`, `Duration`, `OutputPath` |

### API制約

1. **Unity Editorのみ**: ランタイムでは使用不可
2. **PlayMode制限**: 一部の操作はPlayMode中に制限される可能性
3. **非同期操作**: ビルドは非同期で実行される（進捗監視が必要）

## テストフレームワーク

### Node側: Jest

**選定理由**:
- 既存テストで使用済み
- 非同期テスト、モック、スナップショットテストをサポート

### Unity側: Unity Test Framework

**選定理由**:
- Unity公式テストフレームワーク
- EditModeテストでAddressables APIをテスト可能

## 依存関係

| 依存 | バージョン | 必須/任意 | 備考 |
|------|-----------|---------|------|
| `com.unity.addressables` | 2.7.4 | 必須 | Unity Package Manager経由でインストール済み |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 | 必須 | JSON処理（既存依存）|
| `@modelcontextprotocol/sdk` | latest | 必須 | MCPサーバーSDK（既存依存）|
| `jest` | latest | 必須 | テストフレームワーク（既存依存）|

## パフォーマンス検証計画

| 操作 | 目標 | 測定方法 |
|------|------|---------|
| アセット登録 | <5秒 | Unity Profiler + Node側タイムスタンプ |
| グループ一覧取得(100個) | <3秒 | Node側レスポンスタイム測定 |
| 依存関係分析(1000個) | <30秒 | Unity Addressables AnalyzeRule実行時間 |

## リスクと緩和策

| リスク | 影響 | 緩和策 |
|--------|------|--------|
| Addressablesパッケージ未インストール | 高 | 初回コマンド実行時に明確なエラーメッセージ |
| PlayMode中の操作制限 | 中 | PlayMode検出とエラーメッセージ |
| ビルドの長時間実行 | 中 | タイムアウト設定（デフォルト30秒→ビルドは5分） |
| 大量アセットでのメモリ不足 | 低 | ページング実装で緩和 |

## 参考資料

- **Unity Addressables 公式ドキュメント**: <https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/>
- **AddressableAssetSettings API**: <https://docs.unity3d.com/Packages/com.unity.addressables@2.7/api/UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.html>
- **既存MCPハンドラー実装**: `unity-cli/src/handlers/asset/AssetDatabaseManageToolHandler.js`
- **既存Unity Handler実装**: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AssetDatabaseHandler.cs`
