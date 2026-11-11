# Embed Search Playbook

Semantic な近傍検索で UnityCsReference キャッシュからシンボル候補を引き出すための手順。Phase 4-E (`unity-cli reference embed-build` / `embed-search`) の運用ガイド。

## When to use

- 「Animator まわりで使えるコールバック型を知りたい」など、シンボル名が分からない状態で関連 API を探したい。
- LLM が自然言語の query を投げてきた場合に、Phase 2 の `find-symbol` ではヒットしない曖昧な検索を補完したい。
- `grep` の正規表現でも当てられない概念（例: 「reactive update」「coroutine cancellation」）を探したい。

## Prerequisites

1. 対象 Unity バージョンが既に fetch 済みであること:

   ```bash
   unity-cli reference status --output json
   ```

   なければ `unity-cli reference fetch --version <v> --accept-license` で取得する。

2. ONNX model `BAAI/bge-small-en-v1.5` (~130MB) を取得するためのネットワーク接続。初回のみ自動 download される (`fastembed-rs` がプラットフォーム標準のキャッシュ位置 (`~/.cache/fastembed` 等) に保存)。

3. Intel macOS では ONNX Runtime の共有ライブラリが必要。`brew install onnxruntime`
   で導入するか、互換ライブラリ `libonnxruntime.dylib` のパスを `ORT_DYLIB_PATH`
   に設定してから `embed-build` / `embed-search` を実行する。unity-cli の Homebrew
   formula は依存として導入し、Apple Silicon リリース版は runtime を同梱する。

## Build

```bash
unity-cli reference embed-build --version 2023.2.20f1
```

- 全 symbol を `{namespace}.{name} ({kind})\n{view 抜粋 20 行}` という text に整形 → BGE-small-en で embedding。
- 結果は `~/.unity/cache/UnityCsReference/<version>/.unity-cli-index/embeddings.bin` に bincode 形式で保存。
- 数千シンボル規模で数十秒〜数分。CPU bound。
- 同 version で 2 回目以降の `embed-build` は再生成（incremental は未実装）。`find-symbol` の index 更新と分離されているので、symbol index を rebuild した後にも明示的に `embed-build` し直す必要がある。

## Search

```bash
unity-cli reference embed-search --query "animator state callback" \
  --version 2023.2.20f1 \
  --top-k 10
```

出力例:

```json
{
  "ok": true,
  "version": "2023.2.20f1",
  "query": "animator state callback",
  "modelId": "BGESmallENV15",
  "hits": [
    {
      "symbol": "UnityEngine.AnimatorStateInfo",
      "kind": "struct",
      "path": "Runtime/Export/Animation/Animator.cs",
      "line": 123,
      "score": 0.874
    }
  ]
}
```

- `score` は cosine 類似度 (-1.0〜1.0)。`0.7+` 程度なら関連性が高い、`0.5` 以下は noisy。
- 結果は score 降順。同点の場合は元の出現順。
- `--top-k` を指定しない場合は 10 件。

## Recovery

- `embed-search` で「embedding index missing」エラー → 先に `embed-build --version <v>` を実行。
- 結果の質が低い → query の表現を変える（英語の方が精度が高い、`bge-small-en` は英語 model）。
- model download に失敗 → ネットワーク確認、`HF_HUB_OFFLINE` 等の env が設定されていないか確認。

## Anti-patterns

- 数秒間隔で `embed-build` を繰り返す。1 回 build したら `embeddings.bin` を再利用する。
- `embed-search` の結果を絶対的な相関と扱う。あくまで semantic な top-k 候補で、最終確認には `reference view` で抜粋を読む。
- 日本語 query で精度を期待する。MVP の `bge-small-en` は英語専用。多言語サポートは Phase 4 の継続改善余地。

## Future improvements (umbrella SPEC #191 で継続管理)

- 多言語 model (multilingual-e5-large) を `--model` オプションで選択
- HNSW / IVF ベースの近似最近傍検索（現状は線形 scan）
- file-chunk 単位の埋め込み（現状は symbol 単位のみ）
- score の re-ranking / 正規化
