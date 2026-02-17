# CLI Contracts: SPEC-83d9d7ee

## Global options

| Option | Type | Contract |
|---|---|---|
| `--output` | `text\|json` | JSON選択時は有効なJSONをstdout出力する |
| `--host` | string | Unity接続ホストを上書きする |
| `--port` | u16 | Unity接続ポートを上書きする |
| `--timeout-ms` | u64 | 接続/通信タイムアウトを上書きする |

## `raw <tool_name>`

### Input contract
- `tool_name` は空でない文字列。
- `--json` は JSON object のみ許可。
- `--json` と `--params-file` は同時指定不可。

### Output contract
- 成功時: Unity `result` または `data` を表示。
- 失敗時: エラー終了コード `1` で終了。

## `tool list`

### Input contract
- 追加引数不要。

### Output contract
- 108ツール名を表示。
- `--output json` では `string[]` を返す。

## `tool <tool_name>`

### Input contract
- `tool_name` は catalog に存在する名前のみ許可。
- パラメータは `--json` または `--params-file` を受理。

### Output contract
- `raw` と同等の呼び出し結果を返す。
- 不明ツール名は終了コード `1` で失敗。

## `system ping`

### Input contract
- `--message` は任意文字列。

### Output contract
- Unity `ping` 応答を表示。

## `scene create <scene_name>`

### Input contract
- `scene_name` 必須。
- `--load-scene`, `--add-to-build-settings` は bool。

### Output contract
- `create_scene` の結果を返す。

## `instances list`

### Input contract
- `--ports` はカンマ区切り整数（重複は排除）。

### Output contract
- 各インスタンスに `status=up|down` を付与。
- JSONモードでは `InstanceStatus[]`。

## `instances set-active <id>`

### Input contract
- `id` は `host:port` 形式。

### Output contract
- 到達可能なら `active_id` を更新。
- 到達不能なら `unreachable` エラーで失敗。

## Local script/index tools (`tool <name>`)

### 対象ツール
- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

### Input contract
- `path` 系は `Assets/` or `Packages/` 起点を要求（`Library/PackageCache/` は index/search 系で許可）。
- `find_symbol` / `find_refs` は `name` 必須。
- `update_index` は `paths` 非空配列必須。

### Output contract
- `build_index` は `.unity/cache/unity-cli/symbol-index.json` を更新し件数を返す。
- `find_symbol` は `results[]` (path単位グループ) と `total` を返す。
- `find_refs` は `pageSize` / `maxBytes` / `startAfter` に応じて `truncated` と `cursor` を返す。
- index未構築時は `success=false` + `index_not_ready` を返す。
- `UNITY_CLI_LSP_MODE=auto|required` の場合、可能なら LSP backend を使用し、レスポンスに `backend: "lsp"` を含める。
