# Issue #391 — eval_csharp の高速化（参照とコンパイル結果のキャッシュ）

## 変更

- `EvalCompiler`（Bridge）
  - コンパイル参照（`MetadataReference`）を Editor ドメイン内で 1 回だけ作り、再利用する。
    ファイルを持つアセンブリが新しくロードされたら（`AppDomain.AssemblyLoad`）、次の呼び出しで参照集合を作り直す。
    ファイルごとの `MetadataReference` は使い回すので、作り直しても Roslyn が読み込んだメタデータは残る。
  - 同じ `code` + `mode` は、最初にコンパイルしたアセンブリを再利用して再実行する（コードは毎回実行する。
    `requestId` による結果の再利用とは別）。参照集合が変わったら、このキャッシュも破棄する。
  - ドメインロード後、Editor がアイドルになった時点でバックグラウンドスレッドから Roslyn をウォームアップする。
    `EditorApplication.delayCall` は Editor がバックグラウンドのときに実行されなかったため、`EditorApplication.update` から起動する。
- `EvalHandler`（Bridge）
  - 新ツール `get_eval_stats`（CLI: `unity-cli editor eval-stats [--collect]`）。ドメイン内の評価回数、
    Emit 済みアセンブリ数、キャッシュ件数とヒット数、参照数、ロード済みアセンブリ数、マネージドメモリと増加量を返す。
  - 128 アセンブリの上限は「このドメインで未コンパイルのソース」だけに適用する。コンパイル済みのソースは上限到達後も実行できる。
  - 結果キャッシュ（256 件）は、満杯になったら最も古い ID を捨てる（後述の判断を参照）。
- CLI: ツール総数 148 → 149。`get_eval_stats` は読み取り専用。
- `scripts/bench-eval.py`: #371 の `bench-compare-official.py` と同じ方式（1 呼び出し = 1 プロセスの実時間、
  ウォームアップ 3 回 + 計測 100 回、nearest-rank の p50/p95、失敗が 1 件でもあれば中断）で eval を計測し、
  `perf-budgets.json` の予算を超えたら終了コード 1 を返す。
- `perf-budgets.json`: `editor_eval`（Editor 最前面、p50 ≤ 50 ms、p95 ≤ 100 ms）を登録した。

## 判断と前提（自律実行のため記録）

- **結果キャッシュを「満杯で新規拒否」から「最古を破棄」に変えた。** 従来は 256 件で `reload_required` を返していた。
  CLI は呼び出しごとに `requestId` を生成するので、この仕様のままでは AC-4（1,000 回連続実行）を満たせない。
  直近 256 件の ID は引き続き照会でき、それより古い ID は `unknown` になる。docs とスキルに明記した。
- **公式 CLI との同時計測は行っていない。** このマシンには公式 CLI（`~/.unity/bin/unity`）が入っていなかった。
  AC-1 は「公式 CLI の同条件値を下回る、または p50 ≤ 50 ms・p95 ≤ 100 ms」なので、絶対値の条件と、
  #371 が記録した公式 CLI の値（最前面 p50 52.4 / p95 104.0 ms）との比較で判定した。
- **`perf-budgets.json` は本 Issue で新規作成した。** #394（ci(perf)）は未着手で、登録先のファイルがまだ無い。
  #394 が同じファイルに他の操作を追加できる形（`budgets.<key>.p50_ms` / `p95_ms`）にし、#394 にコメントで伝えた。

## 検証（実 Unity Editor、macOS 26.5、Apple M5 Max、2026-09-30）

他エージェントの Editor やビルドが同時に動いているマシン（load average 15〜32）で計測した。

### AC-1 / AC-2: レイテンシ（Editor 最前面、`unity-cli editor eval '1+2'`、100 回）

```bash
cargo build --release
python3 scripts/bench-eval.py --port <port> --require-frontmost --activate --budget editor_eval --out <file>
```

各実行の前にドメインをリロードし、ウォームアップの完了（`eval-stats` の `warmedUp: true`）を確認した。
`--require-frontmost` は、計測対象の Editor（ポートの listener の PID）が各サンプルの前後で最前面だったことを検査する。

| Unity       | 状態          | 初回呼び出し |      p50 |      p95 | 最前面だったサンプル | 判定     |
| ----------- | ------------- | -----------: | -------: | -------: | -------------------: | -------- |
| 6000.3.25f1 | 変更前        |     646.4 ms | 170.5 ms | 268.2 ms |             89 / 100 | 予算超過 |
| 6000.3.25f1 | 変更後 1 回目 |      28.9 ms |  14.4 ms |  24.4 ms |            100 / 100 | PASS     |
| 6000.3.25f1 | 変更後 2 回目 |      47.8 ms |  17.1 ms |  81.0 ms |            100 / 100 | PASS     |
| 6000.3.25f1 | 変更後 3 回目 |     134.3 ms |  24.9 ms |  67.2 ms |            100 / 100 | PASS     |
| 2022.3.62f3 | 変更前        |     536.4 ms | 126.1 ms | 171.1 ms |             95 / 100 | 予算超過 |
| 2022.3.62f3 | 変更後 1 回目 |      26.7 ms |  14.2 ms |  17.5 ms |            100 / 100 | PASS     |
| 2022.3.62f3 | 変更後 2 回目 |      54.3 ms |  18.4 ms |  53.9 ms |            100 / 100 | PASS     |
| 2022.3.62f3 | 変更後 3 回目 |      86.9 ms |  22.4 ms |  81.5 ms |            100 / 100 | PASS     |

- 変更後は 6 回の実行すべてで p50 ≤ 50 ms・p95 ≤ 100 ms を満たし、#371 が記録した公式 CLI の値
  （p50 52.4 / p95 104.0 ms）も下回った。
- 変更前の計測は、フォーカスを奪われたサンプルを再計測する機能（`--activate`）を入れる前に行った。
  そのため他アプリが最前面だったサンプルが 5〜11 件含まれる。Editor 起動直後（ウォームアップなし）の
  初回呼び出しは、変更前に 6000.3.25f1 で 2,440 ms、2022.3.62f3 で 868 ms だった。
- 毎回違うソース（40 種類、最前面）の p50 / p95 は、6000.3.25f1 で 24.7 / 42.6 ms、2022.3.62f3 で 20.5 / 50.5 ms。
- 生データ: `docs/benchmarks/eval-391/`。

### AC-3 / AC-4: 実 Editor E2E（`scripts/e2e-eval.py` を拡張）

```bash
python3 scripts/e2e-eval.py --port <port> --unity-cli target/release/unity-cli
UNITY_CLI_NO_AUTO_UPDATE=1 scripts/e2e-input-batch-host.sh --suite eval --port 6493 --unity-cli "$PWD/target/release/unity-cli"
```

| Unity       | 起動方法   | 結果                | 1,000 回連続実行（4 種類のソース）                             |
| ----------- | ---------- | ------------------- | -------------------------------------------------------------- |
| 6000.3.25f1 | GUI        | 18 passed, 0 failed | Emit 4、ロード済みアセンブリ +4、メモリ +10.8 MB、保持結果 256 |
| 2022.3.62f3 | GUI        | 18 passed, 0 failed | Emit 4、ロード済みアセンブリ +4、メモリ +3.7 MB、保持結果 256  |
| 6000.4.11f1 | batch host | 18 passed, 0 failed | Emit 4、ロード済みアセンブリ +4、メモリ +1.9 MB、保持結果 256  |

追加した検査:

- 同じソースを 3 回実行しても、コンパイルは再利用される。
- 新しいアセンブリ（E2E が Roslyn で作って `Assembly.LoadFrom` する DLL）の型は、ロード前は `compile_error`、
  ロード後は同じソースで解決できる。
- `EditorUtility.RequestScriptReload()` によるドメインリロードの前後で、プロジェクトの型と Unity の型を解決できる。
  リロード前のドメインにだけあった DLL の型は、リロード後は `compile_error` になる（古い参照が残っていない）。
- 1,000 回連続実行の上限: Emit 済みアセンブリ ≤ 8、ロード済みアセンブリの増加 ≤ 16、GC 後のマネージドメモリ増加 ≤ 64 MiB。

### 単体テスト・静的検査

- EditMode `EvalHandlerTests`: 6000.3.25f1 と 2022.3.62f3 でそれぞれ 30 passed, 0 failed
  （実装前は 14 件失敗。新規テストは 8 件）。`BridgeCommandRouterTests`: 6000.3.25f1 で 4 passed。
  EditMode の全件実行は行っていない。
- `cargo fmt --all -- --check` / `cargo clippy --all-targets -- -D warnings`: 問題なし。
- `cargo test --all-targets -- --test-threads=1`（`HOME` を一時ディレクトリに分離）: 555 passed, 0 failed。
  `HOME` を分離しないと、このマシンで動いている共有の unityd に batch が届き、
  `run_with_cli_exercises_remote_command_error_paths` の 1 件が失敗する（本変更の差分はこの経路に触れていない）。
- `cargo run -- skills lint --severity error`: 16 skills checked, 0 violations。
- `dotnet test lsp/Server.Tests.csproj`: 未実行（`lsp/` に変更なし）。

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)
