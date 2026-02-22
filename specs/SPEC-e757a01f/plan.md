# 実装計画: C# LSP統合機能（コードインデックス）

**機能ID**: `SPEC-e757a01f` | **更新日**: 2025-11-26 | **ステータス**: 完了

**統合SPEC**:
- SPEC-aa705b2b: コードインデックスビルドのバックグラウンド実行（実装完了）
- SPEC-eb99c755: Prebuilt better-sqlite3 配布（実装完了）

## 概要

自己完結型のC# Language Server (LSP) を使用した、シンボル検索・参照検索・構造化編集・軽量スニペット編集・リネーム・インデックス管理・バックグラウンドビルド・初回起動高速化機能。

## 実装状況

### 実装完了（全9ユーザーストーリー）

- ✅ US-1: シンボル検索
- ✅ US-2: 参照検索
- ✅ US-3: 構造化編集（メソッド本体置換、メンバー挿入）
- ✅ US-4: リネーム
- ✅ US-5: シンボル定義取得
- ✅ US-6: コードインデックス管理
- ✅ US-7: 軽量スニペット編集
- ✅ US-8: バックグラウンドインデックスビルド
- ✅ US-9: 初回起動の高速化（Prebuilt better-sqlite3 配布）
  - TDDテスト: 11ユニット + 5統合テスト
  - CIワークフロー: prebuild.yml
  - ドキュメント: README.mdにトラブルシューティング追加

## 軽量スニペット編集の技術設計

### 背景

既存の `edit_structured` はメソッド全体の置換やクラス単位の追加に最適化されており、1-2行規模の局所編集には過剰です。過去に行ベース編集を許容した際には括弧整合が破綻し、最終的に `edit_structured` のみを許可する方針へ転換した経緯があります。本設計では、その制約を維持しつつ「小さな断片編集を安全に適用する」ための新ツール `edit_snippet` を設計します。

### アーキテクチャ

#### 1. 指示バンドル設計

1回のリクエストで最大10件の編集を受け取るJSONスキーマ:

```json
{
  "filePath": "Assets/Scripts/Example.cs",
  "edits": [
    {
      "operation": "delete|replace|insert",
      "anchor": {
        "before": "前後2-3行のコンテキスト",
        "target": "編集対象の文字列",
        "after": "前後2-3行のコンテキスト"
      },
      "newText": "置換/挿入の場合の新テキスト（80文字以内）"
    }
  ],
  "preview": false
}
```

#### 2. アンカー解決エンジン

- LSPの `mcp/findText` または既存 `search` 結果を活用してアンカー候補を特定
- 候補が複数ある場合はエラーで返し、曖昧さを許容しない
- 実際の編集適用前に該当範囲を再抽出し、diff長を80文字以内に収める

#### 3. 括弧・構文検証パイプライン

- 編集後に対象ファイルを一時バッファへ適用
- LSPへシンタックスチェックを要求（`textDocument/diagnostics`）
- diagnosticsに括弧不整合・構文エラーが含まれる場合は全編集をロールバック
- 必要であれば `mcp/formatDocument` をpreflightで呼び出し、フォーマット破綻も検出

#### 4. 適用パイプライン

- **Previewモード**: アンカー結果と推定diff、構文検証結果を返し書き込みは行わない
- **Applyモード**: 全編集をまとめて `WorkspaceEdit` としてLSPへ送信し、ハッシュ値（before/after）をレスポンスに含めて二重適用防止を支援

#### 5. フォールバック設計

- アンカーが解決できない場合や差分が80文字超の場合は早期に失敗
- `edit_structured` へのフォールバック案内を含む
- エージェント指示を更新し、用途の棲み分けを明確化

### 技術コンテキスト

- **言語/ランタイム**: Node.js 20.x (既存MCPサーバ)、ECMAScriptモジュール
- **主要依存**: LspRpcClient（内製C# LSPラッパー）、Roslynベースの構文解析（LSPサーバ側）
- **対象ファイル**: `Assets/` および `Packages/` 以下のC#スクリプト
- **テスト**: `unity-cli/tests/unit/handlers/script/*.test.js`（mocha + chai）
- **制約**: LSPから取得できる構文情報とテキスト編集APIで括弧整合を検証、Node側のみで完結

### リサーチ項目（Phase 0）

- [ ] Roslyn LSP側で任意テキスト編集後に即時構文診断を得る最適なリクエストを特定
- [ ] LspRpcClientに複数テキストEditを適用するAPIの存在確認と拡張方法検討
- [ ] 既存 `search` のレスポンス構造確認、アンカー解像度への再利用可能性検証
- [ ] フォーマッタ（dotnet-format等）呼び出しの必要性評価

## バックグラウンドインデックスビルドの技術設計（実装完了）

### アーキテクチャ

#### 1. JobManagerクラス

メモリ内Mapでジョブを管理するシングルトン:

- `create(jobId, jobFn)`: ジョブ作成＆バックグラウンド実行
- `get(jobId)`: ジョブ状態取得
- `cleanup(jobId, retentionMs)`: 自動削除（5分後）

#### 2. バックグラウンド実行パターン

- Promise非同期実行（Worker Threads不要）
- オブジェクト参照による進捗共有（EventEmitter不要）
- 1ビルドジョブのみ許可（リソース競合防止）

#### 3. IndexWatcher統合

- JobManagerで実行中ジョブをチェック
- 手動ビルド実行中は自動ビルドをスキップ
- ジョブID命名規則: `build-xxx`（手動）、`watcher-xxx`（自動）

### 実装ファイル

- `unity-cli/src/core/jobManager.js`: JobManagerクラス
- `unity-cli/src/handlers/script/CodeIndexBuildToolHandler.js`: バックグラウンド化
- `unity-cli/src/handlers/script/CodeIndexStatusToolHandler.js`: buildJob拡張
- `unity-cli/src/core/indexWatcher.js`: JobManager統合

---

## Prebuilt better-sqlite3 配布の技術設計（未実装）

### アーキテクチャ

#### 1. Prebuiltバイナリ同梱

- npm パッケージに主要プラットフォーム向けバイナリを同梱
- 対象: linux/darwin/win32 × x64/arm64 × Node18/20/22

#### 2. postinstallスクリプト

- `ensure-better-sqlite3.mjs`: バイナリ検知とフォールバック
- 同梱バイナリを優先展開（ソースビルドなし）
- 未対応プラットフォームはWASMフォールバック

#### 3. 環境変数制御

- `UNITY_CLI_SKIP_NATIVE_BUILD=1`: ネイティブビルドスキップ
- `UNITY_CLI_FORCE_NATIVE=1`: ソースビルド強制

### 実装予定ファイル

- `unity-cli/prebuilt/better-sqlite3/`: プラットフォーム別バイナリ
- `unity-cli/scripts/ensure-better-sqlite3.mjs`: postinstallスクリプト
- `.github/workflows/prebuild.yml`: CI用ビルドワークフロー

---

## 参考実装

実装詳細については `spec.md` の「参考実装」セクションを参照してください。

---
*本ドキュメントは2025-10-24に作成され、2025-11-26にSPEC統合に伴い更新されました*
