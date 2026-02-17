# リサーチノート: 軽量スニペット編集ツール

## 2025-10-23 調査メモ

### 1. LSP 側の診断／複数編集サポート状況
- `lsp/Program.cs` を精査したところ、実装済みのメソッドは `mcp/replaceSymbolBody` / `mcp/insertBeforeSymbol` / `mcp/insertAfterSymbol` / `mcp/removeSymbol` といったシンボル単位操作のみであり、**任意テキスト差分をまとめて適用する汎用 WorkspaceEdit API は未提供**。
- Roslyn `SyntaxTree.ParseText` を都度生成し、編集適用後は `File.WriteAllTextAsync` による全書き換えでコミットしている。→ 新ツールで複数指示を扱う場合、Node 側でまとめて適用した後 1 回だけ書き戻す or LSP 側に複数編集 API を新設する必要がある。
- LSP 実装内に `textDocument/publishDiagnostics` や `textDocument/diagnostic` などの**診断エンドポイントは存在しない**。構文検証を行うには、(a) Roslyn で再パースし SyntaxTree を検査するメソッドを追加する、もしくは (b) Node 側で `CSharpSyntaxTree.ParseText` 相当の処理を行うヘルパーを実装する必要がある。
  - 既存 `ParseBlock` 実装はメソッドボディのみ対象のため、今回の要件（複数ステートメントの整合性確認）には流用できない。

### 2. 行ベース編集禁止の背景復元
- `README.ja.md` (`36行目`) および `docs/constitution.md` (`IV. C# LSP統合`) にて、**危険な行単位置換／パターン置換を廃止し、構造化操作のみ許可している旨が明示**されている。  
  → 以前の CLI では行単位パッチを許容していたが、波括弧 `{}` の崩壊によるビルド失敗が頻発したため `edit_structured` 1本へ移行した経緯と合致。
- 実際、`README.md` (`36行目`) でも同趣旨の注意書きを確認。  
  → 新ツールを導入する際も、このリスク認識を共有し括弧整合チェックを必須要件とする根拠になる。

### 3. 構文検証の実装オプション
1. **LSP 拡張案**  
   - `mcp/validateTextEdits` のような新RPCを追加し、編集後テキストから `SyntaxTree` を生成 → `GetDiagnostics()` で構文エラーを抽出 → 問題があれば詳細を返却。  
   - 利点: Roslyn を既に参照しているため実装が比較的容易。  
   - 懸念: 追加RPCのクライアント実装（LspRpcClient）変更が必要。
2. **Node 側ローカル検証案**  
   - 既存の `lsp` バイナリに依存せず、`dotnet-script` 等を用いずに `lsp` をアドホック起動して `CSharpSyntaxTree.ParseText` 相当を実行するのは現実的でない。  
   - Node 単体では C# 構文チェックが困難なため、LSP拡張案が優位。

### 4. 複数編集の適用戦略
- **事前適用シミュレーション**: Node 側で対象ファイルを読み込み、各指示を順に適用 → 結果テキストに対して LSP 検証 RPC を呼ぶ → 問題なしなら `apply=true` で書き戻し。  
- **ハッシュ管理**: 既存 `ScriptEditStructuredToolHandler` が preview結果に Diff を含めているが、ハッシュは返していない。新ツールでは `beforeHash`/`afterHash` を SHA-256 等で返す設計が妥当（FR-009の根拠）。

### 5. 整合性確保に向けた検証観点
- **括弧整合テスト**: `if (...) { ... }` を削除した際に余分な `}` が残るケースを再現し、ロールバック動作を確認するユニットテストが必要。  
- **アンカー曖昧性**: 前後コンテキストが一致しない場合に `ambiguous_anchor` エラーを返すテストケースを RED フェーズで作成予定。

### 6. 未解決事項
- LSP 側で複数ファイルを跨ぐ edits をサポートするか？ → 仕様では1ファイル想定だが、今後の拡張余地として要検討。
- フォーマッタ連携 (`textDocument/formatting`) をどのタイミングで呼ぶかは未決定。回避策として preview 応答にフォーマット差異を含めるだけでも良い可能性がある。

---

今後は上記調査結果を踏まえ、Phase 1 でデータモデルとRPC契約の詳細化を行う。特に LSP 側の新RPC追加有無を早期に判断し、工程のブレを防ぐ。
