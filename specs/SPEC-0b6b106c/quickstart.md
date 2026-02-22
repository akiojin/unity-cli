# Quickstart: SerializeField 値更新ツール

1. **ターゲットを特定する**
   - `unity-cli raw get_hierarchy` で GameObjectパスを取得。
   - `unity-cli raw list_components` または `get_component_values` でコンポーネント名とフィールドを確認。
   - Prefab資産を編集する場合は `Assets/.../Foo.prefab` のフルパスを控える。

2. **dry-runで検証する**
   ```json
   {
     "gameObjectPath": "/LevelRoot/EnemySpawner",
     "componentType": "EnemySpawner",
     "fieldPath": "_spawnInterval",
     "value": 2.0,
     "valueType": "float",
     "dryRun": true
   }
   ```
   - レスポンスの `dryRun: true` と `previewValue` を確認し、`notes` に保存要否/Play Mode警告が出ていないかを読む。

3. **本適用を行う**
   - 同じPayloadで `dryRun` を `false` にして再送信。
   - Prefab資産を編集する際は以下を追加:
     ```json
     {
       "prefabAssetPath": "Assets/Prefabs/Enemy.prefab",
       "prefabObjectPath": "/EnemyBoss",
       "applyPrefabChanges": true
     }
     ```
   - 結果の `requiresSave` が `true` の場合は Unity Editor で該当シーン/Prefabを保存。

4. **Play Mode中の扱い**
   - Play Modeで値を触る場合は `"runtime": true` を追加し、構造変更は禁止。
   - `runtime` を付けないままPlay Modeで送信すると、ツールは安全のためエラーを返す。

5. **参照型フィールド**
   - 参照を差し替える場合は `objectReference` ブロックを利用:
     ```json
     {
       "objectReference": {
         "guid": "0123456789abcdef0123456789abcdef"
       },
       "valueType": "objectReference"
     }
     ```

6. **エラー時のトラブルシュート**
   - `notes` にフィールド未発見/型不一致の理由が記載される。
   - それでも解決しない場合は `unity-cli raw get_component_values` でフィールド名を再確認。

7. **監査ログ**
   - すべての更新は `set_component_field` カテゴリで構造化ログが残る。dry-runも記録されるため、セッション後に差分を追跡できる。
