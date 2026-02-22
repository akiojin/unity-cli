# Research: SPEC-83d9d7ee

## 1. Unity TCPプロトコル互換

### 決定
- Rust 側でも「4byte big-endian length + JSON payload」を標準フレームとして採用する。
- 例外的に unframed JSON が返るケースに備え、受信時は fallback パースを持つ。

### 理由
- 既存 Unity 側実装は framed JSON を前提としているため、互換維持が最小リスク。
- 一部テスト/デバッグ経路では unframed が混在するため、防御的パースが必要。

### 代替案
- 完全 framed 強制: テスト互換性を落とすため却下。

## 2. 設定キー移行

### 決定
- 正規キーを `UNITY_CLI_*` とし、`UNITY_CLI_*` を後方互換として読み込む。

### 理由
- CLI 名称変更に合わせた明示的な設定体系が必要。
- 既存環境変数を維持することで移行コストを下げられる。

## 3. インスタンス管理保存先

### 決定
- OS標準 config ディレクトリ配下 `unity-cli/instances.json` を使用する。
- テストでは `UNITY_CLI_REGISTRY_PATH` で保存先を上書き可能にする。

### 理由
- ホーム直下への散在を防ぎ、CLI 設定の一貫性を保つ。
- テストで実ユーザー環境を汚さないため。

## 4. TDD対象

### 決定
- まず以下3領域を自動テスト対象に固定する。
  - パラメータ検証（JSON object / ports）
  - 通信処理（成功/失敗応答）
  - インスタンス切替（到達性判定・失敗系）

### 理由
- 置換初期で回帰しやすい核を先に固定し、以後の機能追加の安全性を担保するため。

## 5. LSP同梱方針

### 決定
- LSP 実装は `unity-cli/lsp` として同梱し、Rust 側では `UNITY_CLI_LSP_MODE` で有効化を制御する。
- 既定値は `off` とし、性能劣化を避けるため Rust ローカル実装をデフォルト経路にする。

### 理由
- ユーザー要求の「LSP同梱移行」を満たしつつ、`cargo test`/ローカル実行の安定性を維持するため。
- LSP 実行環境（dotnet/配布バイナリ）がない環境でも CLI の基本機能を維持するため。

## 6. Unity UPM統合

### 決定
- Unity 側は `UnityCliBridge/Packages/unity-cli-bridge` に移設し、パッケージ名を `com.akiojin.unity-cli-bridge` とする。

### 理由
- 今後の保守対象を `unity-cli` のみに一本化するという運用方針に合わせるため。
