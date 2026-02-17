# データモデル: Unity Addressablesコマンドサポート

**機能ID**: `SPEC-a108b8a3` | **日付**: 2025-11-07

## エンティティ定義

すべてのエンティティはJSON-RPCレスポンスとして返却される。DTOクラスは作成せず、JSON形式で直接返却。

### 1. AddressableEntry

Addressablesに登録されたアセットを表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 | 制約 |
|-----------|-----|------|------|------|
| `guid` | string | ✅ | アセットのGUID | Unity内で一意 |
| `assetPath` | string | ✅ | Assets/から始まるパス | 実在するアセット |
| `address` | string | ✅ | Addressable名 | プロジェクト内で一意 |
| `labels` | string[] | ✅ | ラベル一覧 | 空配列可 |
| `groupName` | string | ✅ | 所属グループ名 | 既存グループ |

**例**:
```json
{
  "guid": "a1b2c3d4e5f6",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "address": "Prefabs/Player",
  "labels": ["Essential", "Character"],
  "groupName": "Default Local Group"
}
```

**検証ルール**:
- `assetPath`は`AssetDatabase.AssetPathToGUID()`で解決可能
- `address`はプロジェクト内で一意（重複時は警告）
- `groupName`は既存のグループに対応

**状態遷移**: N/A（ステートレス）

---

### 2. AddressablesGroup

アセットをまとめる論理的なグループを表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 | 制約 |
|-----------|-----|------|------|------|
| `groupName` | string | ✅ | グループ名 | プロジェクト内で一意 |
| `buildPath` | string | ✅ | ビルド出力パス | 相対パス |
| `loadPath` | string | ✅ | ロードパス | URL形式またはローカルパス |
| `entriesCount` | number | ✅ | 含まれるエントリ数 | >= 0 |

**例**:
```json
{
  "groupName": "UI Assets",
  "buildPath": "ServerData/[BuildTarget]",
  "loadPath": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]",
  "entriesCount": 42
}
```

**検証ルール**:
- `groupName`はプロジェクト内で一意
- `buildPath`, `loadPath`はAddressables設定で有効なパス

**状態遷移**: N/A（ステートレス）

---

### 3. BuildResult

Addressablesビルドの実行結果を表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 | 制約 |
|-----------|-----|------|------|------|
| `success` | boolean | ✅ | ビルド成功/失敗 | true/false |
| `duration` | number | ✅ | ビルド時間（秒） | >= 0 |
| `outputPath` | string | ✅ | 出力ディレクトリ | 絶対パス |
| `errors` | string[] | ✅ | エラーメッセージ一覧 | 空配列可 |

**例（成功時）**:
```json
{
  "success": true,
  "duration": 12.34,
  "outputPath": "/path/to/ServerData/StandaloneWindows64",
  "errors": []
}
```

**例（失敗時）**:
```json
{
  "success": false,
  "duration": 5.67,
  "outputPath": "",
  "errors": [
    "Missing asset reference in group 'UI Assets'",
    "Build script execution failed"
  ]
}
```

**検証ルール**: なし（読み取り専用）

**状態遷移**: N/A（ステートレス、ビルドごとに新規作成）

---

### 4. AnalysisReport

依存関係分析の結果を表す。

**フィールド**:

| フィールド | 型 | 必須 | 説明 | 制約 |
|-----------|-----|------|------|------|
| `duplicates` | DuplicateEntry[] | ✅ | 重複アセット一覧 | 空配列可 |
| `unused` | string[] | ✅ | 未使用アセット一覧 | 空配列可 |
| `dependencies` | object | ✅ | 依存関係マップ | key=assetPath, value=依存先paths[] |

**サブエンティティ: DuplicateEntry**:

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| `assetPath` | string | ✅ | アセットパス |
| `groups` | string[] | ✅ | 含まれるグループ名一覧 |

**例**:
```json
{
  "duplicates": [
    {
      "assetPath": "Assets/Textures/Icon.png",
      "groups": ["UI Assets", "Character Assets"]
    }
  ],
  "unused": [
    "Assets/Prefabs/Deprecated/OldPlayer.prefab"
  ],
  "dependencies": {
    "Assets/Prefabs/Player.prefab": [
      "Assets/Materials/PlayerMaterial.mat",
      "Assets/Textures/PlayerTexture.png"
    ]
  }
}
```

**検証ルール**: なし（読み取り専用）

**状態遷移**: N/A（ステートレス）

---

## エンティティ間の関係

```
AddressablesGroup (1) ---< (*) AddressableEntry
```

- 1つのGroupは複数のEntryを持つ
- 1つのEntryは1つのGroupに所属

```
AddressableEntry ---depends-on---> * Assets (依存関係)
```

- 1つのEntryは複数のアセットに依存可能（AnalysisReportで解析）

---

## JSON-RPCレスポンス形式

### 成功レスポンス

```json
{
  "success": true,
  "data": <Entity or Entity[]>,
  "message": "Operation completed successfully"
}
```

### エラーレスポンス

```json
{
  "success": false,
  "error": "指定されたパスのアセットが見つかりません",
  "solution": "Assets/Prefabs/Player.prefab が存在するか確認してください",
  "context": {
    "assetPath": "Assets/Prefabs/Player.prefab",
    "action": "add_entry"
  }
}
```

### ページング対応レスポンス

```json
{
  "success": true,
  "data": <Entity[]>,
  "pagination": {
    "offset": 0,
    "pageSize": 20,
    "total": 150,
    "hasMore": true
  }
}
```

---

## バリデーションルール

### 共通ルール

1. **必須フィールド**: すべての必須フィールドは`null`または空文字列を許可しない
2. **文字列長**: アセットパス、アドレス名、グループ名は1文字以上
3. **一意性**: GUID、address、groupNameはプロジェクト内で一意

### アセットパス検証

```csharp
bool IsValidAssetPath(string assetPath)
{
    return assetPath.StartsWith("Assets/")
        && AssetDatabase.AssetPathToGUID(assetPath) != "";
}
```

### アドレス名検証

```csharp
bool IsValidAddress(string address)
{
    return !string.IsNullOrWhiteSpace(address)
        && !address.Contains(" ") // スペース禁止
        && address.Length <= 256;
}
```

### グループ名検証

```csharp
bool IsValidGroupName(string groupName)
{
    return !string.IsNullOrWhiteSpace(groupName)
        && groupName.Length <= 128;
}
```

---

## LLM最適化

### トークン節約

- **ページング**: デフォルト`pageSize=20`
- **省略可能フィールド**: `buildPath`, `loadPath`はverbose=trueの時のみ返却
- **要約モード**: 100件以上のエントリは自動的に要約

### エラーメッセージ

- **詳細レベル**: 常に`error`, `solution`, `context`を含む
- **多言語**: 日本語優先、英語フォールバック
- **LLM理解可能**: "次に〜してください"形式

---

## 実装ノート

- **シリアライゼーション**: `Newtonsoft.Json`を使用（Unity標準）
- **null許容**: C#側では`null`チェック必須、JSON側では`null`を許可しない
- **enum**: Unity側のenumはJSON側では文字列に変換
