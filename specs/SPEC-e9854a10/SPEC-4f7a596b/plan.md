# 実装計画: 軽量スニペット編集ツール

**機能ID**: `SPEC-4f7a596b` | **日付**: 2025-10-23 | **仕様**: `specs/SPEC-4f7a596b/spec.md`
**入力**: script編集ワークフロー向け軽量ツールの機能仕様

## 概要
edit_structuredによるメソッド全体置換へ依存した結果、1〜2行規模の修正で余計な差分や括弧崩れが頻発した。過去に行ベース編集を許容した際には括弧整合が破綻し、最終的にedit_structuredのみを許可する方針へ転換した経緯がある。本計画では、その制約を維持しつつ「小さな断片編集を安全に適用する」ための新ツール（仮称 `edit_snippet`）を設計・実装する。

## 現状課題の整理
- **括弧整合問題**: 以前の行編集は`{}`バランス崩壊を多発させ、ビルド不能に。再導入時は検証ステップが必須。
- **ツール粒度のミスマッチ**: edit_structuredはメソッド単位編集が前提で、LLMが求める1〜2行の条件削除には過剰。
- **多箇所編集の非効率**: 同一ファイル内で複数ガードを同時に削除する際に複数リクエストが必要でトークン・時間コストが高い。
- **安全性の担保**: 行指向編集を復活させる場合でも、誤適用やアンカー衝突時の安全なフェイルが必須。

## 技術コンテキスト
- **言語/ランタイム**: Node.js 20.x (既存MCPサーバ), ECMAScriptモジュール
- **主要依存**: LspRpcClient（内製C# LSPラッパー）, Roslynベースの構文解析 (LSPサーバ側)
- **対象ファイル**: Unityプロジェクトの`Assets/`および`Packages/`以下のC#スクリプト
- **テスト**: `unity-cli/tests/unit/handlers/script/*.test.js`（mocha + chai）, 追加でエンドツーエンド検証用の`tests/test-mcp-script-tools.md`
- **制約**: LSPから取得できる構文情報とテキスト編集APIで括弧整合を検証すること。Node側のみで完結させ、Unity Editor起動を不要とする。

## アプローチ概要
1. **指示バンドル設計**  
   - 1回のリクエストで最大10件の編集を受け取るJSONスキーマを定義。  
   - 各編集は`operation (delete|replace|insert)`・`anchor`（textスニペット or symbol path）・`payload`（削除の場合なし、置換/挿入の場合は新テキスト）を必須化。  
   - アンカーは前後2〜3行のコンテキストまたは構造化シンボルのいずれかを選択可能にする。

2. **アンカー解決エンジン**  
   - LSPの`mcp/findText`や既存`search`結果を活用してアンカー候補を特定。  
   - 候補が複数ある場合はエラーで返し、曖昧さを許容しない。  
   - 実際の編集適用前に該当範囲を再抽出し、`diff`長を80文字以内に収める制御を実装。

3. **括弧・構文検証**  
   - 編集後に対象ファイルを一時バッファへ適用し、LSPへシンタックスチェックを要求。  
   - diagnosticsに括弧不整合・構文エラーが含まれる場合は全編集をロールバックし、失敗理由を返す。  
   - 必要であれば`mcp/formatDocument`をpreflightで呼び出し、フォーマット破綻も検出。

4. **適用パイプライン**  
   - previewモードではアンカー結果と推定diff、構文検証結果を返し書き込みは行わない。  
   - applyモードでは全編集をまとめて`WorkspaceEdit`としてLSPへ送信し、ハッシュ値（before/after）をレスポンスに含めて二重適用防止を支援。

5. **フォールバック設計**  
   - アンカーが解決できない場合や差分が80文字超の場合は早期に失敗し、edit_structuredへのフォールバック案内を含む。  
   - 既存ドキュメントとエージェント指示を更新し、用途の棲み分けを明確化する。

## リサーチ & 未確定事項 (Phase 0)
- [ ] Roslyn LSP側で任意テキスト編集後に即時構文診断を得る最適なリクエスト（`textDocument/diagnostics` 等）を特定する。
- [ ] LspRpcClientに複数テキストEditを適用するAPIが既に存在するか調査し、必要なら拡張方法を検討する。
- [ ] 既存`search`のレスポンス構造を確認し、アンカー解像度に再利用できるか検証する。
- [ ] フォーマッタ（dotnet-format等）を呼ぶ必要があるか、LSPの`formatting`機能で十分か評価する。

## 設計 (Phase 1) のアウトライン
1. **データモデル**  
   - `SnippetEditInstruction`（operation, anchor, newText, metadata）  
   - `SnippetAnchor`（type=text|symbol, before, target, after, symbolPath）  
   - `SnippetEditResult`（status, reason, beforeSnippet, afterSnippet, hashBefore, hashAfter）
2. **API 契約**  
   - MCPツール名: `edit_snippet`（仮）  
   - 入力JSON Schemaと出力Schemaを定義し、`specs/.../contracts/snippet-edit.json` に格納。  
   - エラーコード体系（例: `anchor_not_found`, `ambiguous_anchor`, `syntax_error`）。
3. **Quickstart/プレイブック**  
   - 典型例: nullチェック削除、条件式置換、return直前ログ挿入。  
   - 既存プレイブックとの棲み分け説明をREADME/CLAUDE.mdへ反映。

## 実装フェーズのロードマップ
- **Phase 0 (Research)**  
  - LSP APIと既存インフラ調査を完了し、`research.md`へ記録。  
  - 行編集禁止の再開理由（括弧崩れ事例）を事後分析し、検証手段に反映。
- **Phase 1 (Design)**  
  - data-model.md, contracts/, quickstart.md を更新。  
  - 検証フロー（pre-apply diagnostics）のシーケンス図を作成し、Team内レビューを実施。
- **Phase 2 (Implementation)**  
  - ユニットテスト（RED）→ Node側Handler実装（GREEN）→ リファクタリングの順で進める。  
  - LSP通信層のヘルパーを追加し、複数編集のWorkspaceEditサポートを実装。  
  - テキストdiffとレスポンス整形を実装し、既存`ScriptEditStructuredToolHandler`に影響がないことを確認。
- **Phase 3 (Integration & Polish)**  
  - README/CLAUDE.md/エージェントガイド更新。  
  - 既存テストシナリオ(`tests/test-mcp-script-tools.md`)へ新ツールの手順を追記。  
  - edit_structuredとの併用ガイダンスをドキュメント化し、括弧崩れ防止策を強調。

## リスクと対策
- **アンカー一致の誤検出**: テキストアンカーにNFAマッチングを使うと誤爆の恐れ → コンテキスト長と一意性チェックを必須にする。  
- **括弧整合検査のコスト**: 毎回全文診断を取ると遅くなる → 対象ファイルのみを診断し、結果キャッシュを活用。  
- **LSP API不足**: WorkspaceEditの同時送信が未サポートなら、段階的に`applyEdit`をチェインし、失敗時にロールバック計画。  
- **既存LLMフローとの不整合**: CLAUDE.mdやREADMEの手順を更新し、edit_structured優先指示を調整。

## 次アクション
1. Phase 0リサーチ項目の調査結果を`specs/SPEC-4f7a596b/research.md`へ記録する。
2. データモデル・契約スキーマの詳細化（Phase 1）とレビュー準備。
3. TDDタスク分解（tasks.md）に従い、REDテスト追加から着手できる体制を整える。
