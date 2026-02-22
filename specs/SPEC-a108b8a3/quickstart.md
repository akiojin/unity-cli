# クイックスタート: Unity Addressablesコマンドサポート

**機能ID**: `SPEC-a108b8a3` | **日付**: 2025-11-07

## 前提条件

1. Unity Editor起動済み
2. Unity Addressablesパッケージ (`com.unity.addressables v2.7.4`) インストール済み
3. Unity CLI Bridge Server起動済み、Unity Editorと接続済み

## テストシナリオ（ユーザーストーリー検証）

### P1: アセット登録管理

**目的**: プレハブをAddressableとして登録し、アドレス名変更、ラベル追加、一覧取得を行う。

#### ステップ1: プレハブをAddressableとして登録

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "add_entry",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "address": "Prefabs/Player",
  "groupName": "Default Local Group"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "guid": "a1b2c3d4e5f6",
    "assetPath": "Assets/Prefabs/Player.prefab",
    "address": "Prefabs/Player",
    "labels": [],
    "groupName": "Default Local Group"
  }
}
```

**検証**: Unity Addressables Windowでエントリが表示される。

---

#### ステップ2: アドレス名を変更

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "set_address",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "newAddress": "Characters/Player"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "guid": "a1b2c3d4e5f6",
    "assetPath": "Assets/Prefabs/Player.prefab",
    "address": "Characters/Player",
    "labels": [],
    "groupName": "Default Local Group"
  }
}
```

**検証**: Addressables Windowでアドレス名が変更されている。

---

#### ステップ3: ラベルを追加

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "add_label",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "label": "Essential"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "guid": "a1b2c3d4e5f6",
    "assetPath": "Assets/Prefabs/Player.prefab",
    "address": "Characters/Player",
    "labels": ["Essential"],
    "groupName": "Default Local Group"
  }
}
```

**検証**: Addressables Windowでラベルが表示される。

---

#### ステップ4: 登録済みアセット一覧を取得

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "list_entries",
  "groupName": "Default Local Group",
  "pageSize": 20,
  "offset": 0
}
```

**期待結果**:
```json
{
  "success": true,
  "data": [
    {
      "guid": "a1b2c3d4e5f6",
      "assetPath": "Assets/Prefabs/Player.prefab",
      "address": "Characters/Player",
      "labels": ["Essential"],
      "groupName": "Default Local Group"
    }
  ],
  "pagination": {
    "offset": 0,
    "pageSize": 20,
    "total": 1,
    "hasMore": false
  }
}
```

**検証**: 登録したエントリが一覧に含まれる。

---

### P2: グループ管理

**目的**: 新規グループを作成し、アセットを移動、グループ一覧確認、グループ削除を行う。

#### ステップ1: 新規グループを作成

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "create_group",
  "groupName": "UI Assets"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "groupName": "UI Assets",
    "buildPath": "ServerData/[BuildTarget]",
    "loadPath": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]",
    "entriesCount": 0
  }
}
```

**検証**: Addressables Windowで新規グループが表示される。

---

#### ステップ2: アセットを別グループに移動

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "move_entry",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "targetGroupName": "UI Assets"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "guid": "a1b2c3d4e5f6",
    "assetPath": "Assets/Prefabs/Player.prefab",
    "address": "Characters/Player",
    "labels": ["Essential"],
    "groupName": "UI Assets"
  }
}
```

**検証**: Addressables Windowでアセットが"UI Assets"グループに移動している。

---

#### ステップ3: グループ一覧を取得

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "list_groups",
  "pageSize": 20,
  "offset": 0
}
```

**期待結果**:
```json
{
  "success": true,
  "data": [
    {
      "groupName": "Default Local Group",
      "buildPath": "ServerData/[BuildTarget]",
      "loadPath": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]",
      "entriesCount": 0
    },
    {
      "groupName": "UI Assets",
      "buildPath": "ServerData/[BuildTarget]",
      "loadPath": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]",
      "entriesCount": 1
    }
  ],
  "pagination": {
    "offset": 0,
    "pageSize": 20,
    "total": 2,
    "hasMore": false
  }
}
```

**検証**: 両方のグループが一覧に含まれる。

---

#### ステップ4: 空のグループを削除

**前提**: アセットを元のグループに戻す（またはクリーンアップ用の空グループを作成）

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "remove_group",
  "groupName": "UI Assets"
}
```

**期待結果**:
```json
{
  "success": true,
  "message": "グループ 'UI Assets' を削除しました"
}
```

**検証**: Addressables Windowでグループが削除されている。

---

### P3: ビルド自動化

**目的**: アセット登録後にAddressablesビルドを実行し、成功確認、キャッシュクリアを行う。

#### ステップ1: アセットを登録

（P1のステップ1と同じ）

---

#### ステップ2: Addressablesビルドを実行

**リクエスト**:
```json
{
  "command": "addressables_build",
  "action": "build"
}
```

**期待結果** (成功時):
```json
{
  "success": true,
  "data": {
    "success": true,
    "duration": 12.34,
    "outputPath": "/path/to/ServerData/StandaloneWindows64",
    "errors": []
  }
}
```

**検証**: `outputPath`ディレクトリにビルド成果物（.bundle, catalog.json等）が存在する。

---

#### ステップ3: ビルドキャッシュをクリア

**リクエスト**:
```json
{
  "command": "addressables_build",
  "action": "clean_build"
}
```

**期待結果**:
```json
{
  "success": true,
  "message": "ビルドキャッシュをクリアしました"
}
```

**検証**: `Library/com.unity.addressables/`ディレクトリが削除されている。

---

### P4: 依存関係分析

**目的**: 重複アセットを作成し、分析実行、重複検出を確認する。

#### ステップ1: 重複アセット状態を作成

**前提**: 同じアセットを2つの異なるグループに登録

1. `Assets/Textures/Icon.png`を"Default Local Group"に登録
2. `Assets/Textures/Icon.png`を"UI Assets"に登録

---

#### ステップ2: 重複アセットを検出

**リクエスト**:
```json
{
  "command": "addressables_analyze",
  "action": "analyze_duplicates"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "duplicates": [
      {
        "assetPath": "Assets/Textures/Icon.png",
        "groups": ["Default Local Group", "UI Assets"]
      }
    ],
    "unused": [],
    "dependencies": {}
  }
}
```

**検証**: 重複アセットが検出される。

---

#### ステップ3: 依存関係を解析

**リクエスト**:
```json
{
  "command": "addressables_analyze",
  "action": "analyze_dependencies",
  "assetPath": "Assets/Prefabs/Player.prefab"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "duplicates": [],
    "unused": [],
    "dependencies": {
      "Assets/Prefabs/Player.prefab": [
        "Assets/Materials/PlayerMaterial.mat",
        "Assets/Textures/PlayerTexture.png"
      ]
    }
  }
}
```

**検証**: プレハブが依存しているアセットが一覧表示される。

---

#### ステップ4: 未使用アセットを検出

**リクエスト**:
```json
{
  "command": "addressables_analyze",
  "action": "analyze_unused"
}
```

**期待結果**:
```json
{
  "success": true,
  "data": {
    "duplicates": [],
    "unused": [
      "Assets/Prefabs/Deprecated/OldPlayer.prefab"
    ],
    "dependencies": {}
  }
}
```

**検証**: どのAddressableエントリからも参照されていないアセットが検出される。

---

## エラーケーステスト

### 存在しないアセットパスを指定

**リクエスト**:
```json
{
  "command": "addressables_manage",
  "action": "add_entry",
  "assetPath": "Assets/Prefabs/NonExistent.prefab",
  "address": "Test/NonExistent",
  "groupName": "Default Local Group"
}
```

**期待結果**:
```json
{
  "success": false,
  "error": "指定されたパスのアセットが見つかりません",
  "solution": "Assets/Prefabs/NonExistent.prefab が存在するか確認してください",
  "context": {
    "assetPath": "Assets/Prefabs/NonExistent.prefab",
    "action": "add_entry"
  }
}
```

---

### Addressablesパッケージ未インストール

**期待結果**:
```json
{
  "success": false,
  "error": "Addressablesパッケージがインストールされていません",
  "solution": "Unity Package Managerから com.unity.addressables をインストールしてください",
  "context": {
    "action": "add_entry"
  }
}
```

---

## 自動テスト実行

```bash
# Integration testsを実行
cd unity-cli
npm test tests/integration/addressables/
```

**期待**: すべてのテストがパス。

---

## パフォーマンス検証

| 操作 | 目標 | 実測値 | 合格/不合格 |
|------|------|--------|----------|
| アセット登録 | <5秒 | （測定予定） | - |
| グループ一覧取得(100個) | <3秒 | （測定予定） | - |
| 依存関係分析(1000個) | <30秒 | （測定予定） | - |

---

## トラブルシューティング

### Q: Unity Editorが応答しない

**A**: Unity Editorを再起動し、MCPサーバーとの接続を再確立してください。

### Q: ビルドが失敗する

**A**: Unity Console Logを確認し、エラーメッセージを確認してください。ビルドエラーはレスポンスの`errors`配列に含まれます。

### Q: ページングが動作しない

**A**: `pageSize`, `offset`パラメータが正しく指定されているか確認してください。デフォルト値はそれぞれ20, 0です。
