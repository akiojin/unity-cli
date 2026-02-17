# ベンチマーク結果（unity-cli）

本リポジトリでは、`unity-mcp-server` 時代の Node ベース計測値は現行値として扱いません。

現在の性能検証ポリシー:

- Rust CLI の回帰は PR 単位で再現可能な計測を添付して確認する
- LSP は `dotnet test lsp/Server.Tests.csproj` で挙動を検証する
- 移行時の性能主張（改善/劣化）は、同一条件の測定結果を必須とする

新しいベンチマークを追加する場合は `docs/benchmarks/` に配置し、以下を明記してください。

1. 計測環境（OS / CPU / Unityバージョン）
2. 実行コマンド
3. before/after の数値
4. 生ログまたは計測スクリプト
