# Data Model: SerializeField 値更新ツール

## ComponentFieldUpdateRequest
| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| scope | enum(`auto`,`scene`,`prefabStage`,`prefabAsset`) | 任意 (default auto) | 操作対象の明示スコープ。autoはパスに応じて自動判定。 |
| gameObjectPath | string | scopeがscene/prefabStage/autoでPrefabAsset未指定時は必須 | 先頭`/`付きヒエラルリーパス。Prefab Stageでも同形式。 |
| prefabAssetPath | string | scope=prefabAsset または Prefab資産編集時 | `Assets/... .prefab` のパス。 |
| prefabObjectPath | string | 任意 | Prefab資産内で操作する相対パス (省略時はPrefab root)。 |
| componentType | string | 必須 | MonoBehaviour/コンポーネントの短名または完全修飾名。 |
| componentIndex | int >=0 | 任意 (default 0) | 同一タイプ複数存在時のインデックス。 |
| fieldPath | string | 必須 | SerializeFieldパス。`foo.bar[0].baz` 形式で受け付ける。 |
| serializedPropertyPath | string | 任意 | 既に正規化された `.Array.data[n]` パスを指定する場合に使用。 |
| value | any | dryRun=false の場合必須 | 設定したい値 (JSONオブジェクト/配列/スカラー)。 |
| valueType | enum | 任意 | `auto`/`bool`/`int`/`float`/`string`/`enum`/`objectReference`/`json` ... 入力ヒント。 |
| enumValue | string | 任意 | enumを識別する文字列 (valueが数値のときに補助)。 |
| objectReference | object | 任意 | `assetPath`/`guid` を含む参照指定。 |
| runtime | bool | 任意 (default false) | Play Modeで実行する際に true 指定。 |
| dryRun | bool | 任意 (default false) | trueの場合は検証のみを実施。 |
| applyPrefabChanges | bool | 任意 (default true for prefabAsset) | Prefab資産編集時に保存するか。 |
| createUndo | bool | 任意 (default true) | シーン/Prefab StageでUndo履歴を残すか。 |
| markSceneDirty | bool | 任意 (default true) | シーンへの変更をDirty扱いにするか。 |

**検証ルール**
- `gameObjectPath` と `prefabAssetPath` は少なくとも一方が必要。
- scope=`prefabAsset` の場合 `prefabAssetPath` 必須 / `gameObjectPath` 任意。
- `value` が省略されるのは `dryRun=true` または `valueType='null'` の場合のみ。
- `objectReference` 指定時は `assetPath` か `guid` のどちらかが必須。

## ComponentFieldUpdateResult
| フィールド | 型 | 説明 |
| --- | --- | --- |
| scope | enum | 実際に処理したスコープ (`scene`,`prefabStage`,`prefabAsset`). |
| gameObjectPath | string | 処理対象GameObjectの絶対パス。Prefab資産の場合はPrefab内のパス。 |
| prefabAssetPath | string? | Prefab資産編集時に設定。 |
| serializedPropertyPath | string | Unity互換の正規化パス。 |
| previousValue | any | 変更前の値 (dryRun時も含む)。 |
| appliedValue | any | 適用後の値。dryRun時は preview 値。 |
| requiresSave | bool | 追加でPrefab保存/シーン保存が必要か。 |
| dryRun | bool | dry-run実行かどうか。 |
| notes | string[] | Prefab Stage警告やPlay Mode制約などの追加情報。 |
| error | string? | 失敗時のみ設定。 |

## Related Entities
- **TargetObjectContext**: `isPrefabStage`, `isSceneObject`, `isPrefabAsset`, `scenePath`, `prefabRoot`, `playMode`。
- **ValidationError**: `code`, `message`, `details`。NodeハンドラはこれをMCPエラーとして返す。
