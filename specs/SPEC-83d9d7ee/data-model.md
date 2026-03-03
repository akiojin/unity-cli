# Data Model: SPEC-83d9d7ee

## RuntimeConfig

| フィールド | 型 | 説明 |
|---|---|---|
| `host` | `String` | Unity接続先ホスト |
| `port` | `u16` | Unity接続先ポート |
| `timeout` | `Duration` | 接続/送受信タイムアウト |

## UnityRequest

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | `String` | コマンドID |
| `type` | `String` | Unity command type (`ping`, `create_scene`, etc.) |
| `params` | `Object` | コマンド引数JSON |

## UnityResponse (正規化前)

| フィールド | 型 | 説明 |
|---|---|---|
| `status` | `String?` | `success` / `error` |
| `success` | `Bool?` | 互換用成功フラグ |
| `result` | `Value?` | 正常系ペイロード |
| `data` | `Value?` | 互換ペイロード |
| `error` | `String?` | エラーメッセージ |
| `code` | `String?` | エラーコード |

## InstanceRecord

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | `String` | `host:port` 形式 |
| `host` | `String` | 接続先ホスト |
| `port` | `u16` | 接続先ポート |

## Registry

| フィールド | 型 | 説明 |
|---|---|---|
| `active_id` | `Option<String>` | 現在アクティブなID |
| `entries` | `Vec<InstanceRecord>` | 既知インスタンス一覧 |

## InstanceStatus (出力)

| フィールド | 型 | 説明 |
|---|---|---|
| `id` | `String` | `host:port` |
| `status` | `String` | `up` / `down` |
| `last_checked_at` | `String` | Unix秒文字列 |
| `active` | `bool` | アクティブ判定 |

## SetActiveResult (出力)

| フィールド | 型 | 説明 |
|---|---|---|
| `active_id` | `String` | 更新後アクティブID |
| `previous_id` | `Option<String>` | 更新前アクティブID |
