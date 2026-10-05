# unity-cli vs the official Unity CLI

[English](#english) | [日本語](#日本語) | [Results / 実測結果](#results) | [Reproduce / 再計測](#reproduce)

## English

Unity's official toolchain combines the **Unity Plugin for Claude Code**
(`unity@unity-agent-plugin`), **Unity CLI** (`unity`), and the Editor-side
**Pipeline** package (`com.unity.pipeline`). This comparison covers
`unity-cli` **0.17.0 development build** (including Bridge authentication), official CLI **1.0.0-beta.11**, and Pipeline
**0.8.0-exp.1**, checked on **2026-10-02**. It is a versioned comparison,
not a claim about future beta releases.

The published timings identify their source commit and binary hashes under
[Results](#results). Rebuilding a newer checkout produces a new measurement.
The feature links refer to the implementation shipped with this page.

### Sources

The official documentation below is pinned to plugin commit
[`9c01e8d`](https://github.com/Unity-Technologies/unity-agent-plugin/tree/9c01e8d21cfefa28bc0a18f92ec910a738a1b1bc).
Each table row links to its evidence:

- [Official skill][official-skill]: command groups, requirements, environment variables.
- [Official integration guide][official-integration]: Pipeline, transport, command discovery, NDJSON shell.
- [Official diagnostics guide][official-diagnostics]: analytics, crash reports, invocation telemetry.
- [Plugin license][official-license]: Unity Companion License.
- [Recorded command catalog and package metadata][catalog]: facts read from the installed Pipeline's `package.json` / `LICENSE.md` and both CLIs' command listings; includes package version, minimum Unity version and license URL.

### Feature comparison

| Topic                      | unity-cli                                                                                                                                                                                                                                                  | Official Unity CLI + Pipeline                                                                                                                                                                                                                                                                            |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Unity versions             | 2022.3+. [Package requirement](../UnityCliBridge/Packages/unity-cli-bridge/package.json), [tested macOS matrix](editor-compatibility.md).                                                                                                                  | Unity 6.0+ for Editor control. [Official skill][official-skill], [installed package metadata][catalog].                                                                                                                                                                                                  |
| Account / sign-in          | No CLI account or login command. The Editor still uses normal Unity licensing. [Configuration](configuration.md), [command implementation](../src/cli.rs).                                                                                                 | Benchmark Editor commands ran without a CLI login step. Authentication workflows exist for cloud, licenses and Editor installation. [Official skill][official-skill], [benchmark script](../scripts/bench-compare-official.py).                                                                          |
| Telemetry                  | No analytics or usage reporting in the CLI. [Dependencies](../Cargo.toml), [source](../src).                                                                                                                                                               | Opt-in analytics, separate anonymous Sentry crash/error reports, and a per-invocation usage ping regardless of analytics consent. `UNITY_NO_CRASH_REPORT` and `UNITY_NO_CLI_INVOKED_TELEMETRY=1` disable the latter two. [Official diagnostics][official-diagnostics], [official skill][official-skill]. |
| License                    | MIT. [LICENSE](../LICENSE).                                                                                                                                                                                                                                | Plugin: Unity Companion License. Pipeline: Unity Terms of Service. [Plugin license][official-license], [package metadata][catalog], [Unity terms](https://unity.com/legal/terms-of-service).                                                                                                             |
| Transport / resident mode  | TCP Bridge with an Editor-local authentication token; `unityd` retains connections. Eval deliberately uses direct TCP; local C# reads do not contact the Editor. [Architecture](architecture.md), [eval](editor-eval.md), [routing](../src/app/runner.rs). | Pipeline HTTP with an instance token discovered from a lockfile; `unity shell --protocol ndjson` keeps the CLI process warm. [Official integration guide][official-integration].                                                                                                                         |
| Command surface            | 153 entries, including local tools, in `unity-cli tool list --output json`. [Tool docs](tools.md), [recorded listing][catalog].                                                                                                                            | 160 Pipeline commands, plus CLI management commands. These counts cover different scopes. [Recorded listing][catalog], [official command discovery][official-integration].                                                                                                                               |
| C# evaluation              | `editor eval`; caches compilation references and identical snippets while executing every call. [Eval behavior](editor-eval.md).                                                                                                                           | `unity command eval`; statements such as `return 1+2;`. [Official skill][official-skill], [integration guide][official-integration].                                                                                                                                                                     |
| Play-mode input            | Keyboard, mouse, gamepad and touch. [Tool reference](tools.md#input-system).                                                                                                                                                                               | `simulate_key` and `simulate_pointer` are in the observed catalog. [Catalog][catalog], [official discovery][official-integration].                                                                                                                                                                       |
| C# code intelligence       | Bundled LSP supports symbol search, references, rename and structured edits without an Editor connection. [LSP source](../lsp), [tool reference](tools.md).                                                                                                | File read/write and eval; the documented CLI command groups do not provide a C# LSP. [Official skill][official-skill], [catalog][catalog].                                                                                                                                                               |
| Editor / project lifecycle | Bridge setup and installed Editor open/close/headless lifecycle; use Unity Hub or the official CLI for Editor installation and licenses. [Setup](../src/app/setup.rs), [lifecycle implementation](../src/app/editor.rs).                                   | Editor/module install, licensing, project creation/opening, builds, tests and version control. [Official skill][official-skill].                                                                                                                                                                         |
| Agent integration          | Claude Code plugin and Codex skills. [Skill contract](skills.md).                                                                                                                                                                                          | Claude Code / Codex plugin, task skills and `unity mcp`. [Official skill][official-skill], [integration guide][official-integration].                                                                                                                                                                    |

Windows validation was added separately in [Issue #386 evidence](verification/issue-386/README.md):
CLI / Bridge 0.18.1 with Unity 6000.4.4f1 verified real setup, lockfile discovery,
OS screenshot fallback during an Editor modal, and all 23 listed operations with
DisableDomainReload / PlayUnfocused. A separate Windows comparison with official CLI
1.0.0-beta.11 and Pipeline 0.8.0-exp.1 measured 100 process samples per operation
with the Editor frontmost and minimized. Resident measurements covered all 23
operations with 100 samples per tool and condition. [Windows timings](verification/issue-386/resume/windows-comparison.md)
record the debug build, a contended host, and the minimized background condition.
The timings below remain macOS measurements. [Additional functional evidence](verification/issue-386/resume/README.md)
includes successful Test Runner result collection with Domain Reload enabled and disabled,
Prefab, Timeline, InputActions persistence, animation, video, reference fetching,
baking, real Hot Reload, and a built Windows Player. Complex input passed 11 checks
after correcting the test's CRLF counter parsing.
Linux screenshot tools produced real images using TCP fixtures; a licensed Linux
Unity Editor, winget installation, and comparison with the original staff report
remain unverified.

### Coexistence

Both packages were loaded into **one real Unity 6000.3.25f1 Editor project** on
Apple Silicon macOS. Pipeline listened on port **7800**, Bridge on **6471**.
`unity command editor_status` reported that project as ready; `unity-cli system ping`
returned `pong` with matching CLI/Bridge version **0.17.0**. The [results](#results)
exercise both connections against the same scene and assets. Both servers see the
same Unity objects; their connection endpoints and protocols are separate.

The benchmark uses these manifest entries (the local Bridge is copied from the
source commit recorded in each JSON):

```json
"com.unity.pipeline": "0.8.0-exp.1",
"com.akiojin.unity-cli-bridge": "file:unity-cli-bridge"
```

For a normal project, follow [the setup guide](../README.md#getting-started).
The current Bridge declares its uGUI dependency; the old Empty-template workaround
reported in [#373](https://github.com/akiojin/unity-cli/issues/373) is no longer needed.
Keep Input System support enabled: `unity-cli setup` configures Both while the
Editor is closed. A modal dialog or compilation error prevents a meaningful
benchmark of either tool. See [setup behavior](../src/core/bridge.rs).

### What is measured

- **Process mode:** empty GameObject creation, hierarchy retrieval and `1+2`,
  **100 samples per tool per operation**, after 3 warmups. A sample spans one
  CLI spawn to exit. `unityd` is already warm for applicable calls; eval uses
  direct TCP. Objects are removed outside the timed span, keeping the hierarchy
  the same size throughout.
- **Resident mode:** the **23 operations** from the staff investigation, each
  with **100 samples per tool**, after 3 complete warmup cycles. `unity-cli`
  still starts a small CLI process per call, using a warm `unityd`; the official
  tool uses one persistent **NDJSON shell**. The shell and daemon startup are
  excluded. Samples include request/response handling; Play and Stop include
  polling until the requested state is reached and readiness flags clear:
  Bridge `isCompiling` / `isUpdating`, Pipeline `compiling` / `domainReloadInProgress`.
  This is a comparison of the supported resident
  configurations, not raw TCP versus raw HTTP.
- Both modes alternate tool order, use one machine/project/Editor, and record
  **frontmost and background** runs separately. Complete resident cycles balance
  create/delete, add/remove, asset copy/move/delete and Play/Stop.
- JSON includes all raw samples, nearest-rank p50/p95, tool versions, source
  commit, binary/script hashes, focus, Play Mode settings, host load and top CPU
  process names. Failed responses, wrong request IDs, timeouts, daemon restarts
  and daemon-to-direct fallback abort the run. Focus-disturbed pairs/cycles are
  discarded and remeasured, with a bounded retry count.

### Interpreting the numbers

The [Issue's staff investigation](https://github.com/akiojin/unity-cli/issues/371)
reported roughly **20–60 ms** for unity-cli and **510–630 ms** for the official
resident configuration. The earlier [2026-09-30 foreground process run][old-front]
reported about **50 ms** for the official CLI. Those are observations from different
runs, not interchangeable baselines or a promised 10× improvement from a shell.
The new tables show both invocation modes explicitly. We do not assign the
staff report's ~550 ms to process startup or Editor throttling without a trace
that isolates that cost.

In the new **frontmost hierarchy** measurements, process-mode p50 was
**7.91 ms / 20.40 ms** (unity-cli / official), while resident-mode p50 was
**8.74 ms / 22.17 ms**. The official shell did not reproduce ~550 ms for this
operation, and keeping that process resident did not reduce its observed p50
in these runs. These are warmed runs with different invocation paths and
operation sequences; their difference does not isolate pure startup cost.
Conversely, **Play until ready** in the frontmost resident run favored the
official tool: **552.16 ms / 266.35 ms**. The tables retain that result too.

The historical v0.15.3 [foreground][old-front] / [background][old-back] results
are retained for provenance. They precede compilation caching and other performance
fixes and must not be described as current unity-cli latency. Repeating `1+2`
measures a warm snippet; distinct C# snippets can still require compilation.

Keep the semantic differences in mind:

- Hierarchy and Transform responses contain different fields. These are comparable
  user tasks, not byte-identical payloads.
- Both screenshot operations write a 1280×720 game-camera PNG, with no base64
  payload. Their capture implementations differ. A background screenshot starts
  with another app frontmost and may activate the target Game View; focus is
  restored before the next operation.
- C# read uses the same small `Assets/HotReloadProbe.cs`. unity-cli reads it
  locally; the official command goes through the Editor. Pipeline's authoring
  root does not permit the Bridge source under `Packages/`.
- Both resident cycles assign the same position and material color repeatedly.
  Play Mode uses **DisableDomainReload** and **PlayUnfocused**. Domain Reload,
  larger scenes, scripts, graphics setup and unrelated host work can change results.
- These are measurements on one machine. No claim is made that every command,
  project, focus condition or percentile is faster. Directory listing submissions
  remain an owner decision and are outside this change.
- Host load varies during a run. Each run met the stated start conditions;
  the tables and JSON retain both start and end load rather than assuming the
  entire interval was idle.

## 日本語

Unity 公式は **Unity Plugin for Claude Code**、**Unity CLI**（`unity`）、
Editor 側の **Pipeline**（`com.unity.pipeline`）を提供しています。
このページは **2026-10-02** 時点の `unity-cli` **0.17.0 開発ビルド**（Bridge認証対応込み）、公式 CLI
**1.0.0-beta.11**、Pipeline **0.8.0-exp.1** の比較です。将来の beta 版に
同じ結果を保証するものではありません。公式資料は上記のコミットに固定しています。
計測時のソースコミットとバイナリのハッシュは [Results](#results) に記録しています。
新しい checkout をビルドすれば新しい測定になります。機能表はこのページと同時に
提供する実装をリンク先として参照します。

### 機能比較

| 項目                     | unity-cli                                                                                                                                                                                                     | 公式 Unity CLI + Pipeline                                                                                                                                                                                |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 対応 Unity               | 2022.3 以降。[package.json](../UnityCliBridge/Packages/unity-cli-bridge/package.json)、[macOS 実機マトリクス](editor-compatibility.md)。                                                                      | Editor 操作は 6.0 以降。[公式スキル][official-skill]、[パッケージ記録][catalog]。                                                                                                                        |
| アカウント               | CLI 独自のログインなし。Editor の通常ライセンスは別です。[設定](configuration.md)、[コマンド実装](../src/cli.rs)。                                                                                            | 今回の Editor 操作は CLI ログイン手順なしで実行。クラウド・ライセンス・Editor 導入には認証のワークフローがあります。[公式資料][official-skill]、[計測スクリプト](../scripts/bench-compare-official.py)。 |
| テレメトリ               | CLI に分析・利用状況送信なし。[依存](../Cargo.toml)、[ソース](../src)。                                                                                                                                       | オプトインの分析、匿名のクラッシュ報告、分析同意と独立した毎回の利用 ping。後者2つは `UNITY_NO_CRASH_REPORT` と `UNITY_NO_CLI_INVOKED_TELEMETRY=1` で無効化。[公式診断ガイド][official-diagnostics]。    |
| ライセンス               | MIT。[LICENSE](../LICENSE)。                                                                                                                                                                                  | プラグインは Unity Companion License、Pipeline は Unity Terms of Service。[プラグインライセンス][official-license]、[パッケージ記録][catalog]、[Unity 規約](https://unity.com/legal/terms-of-service)。  |
| 通信・常駐方式           | Editor ごとの認証トークンを使う TCP Bridge と接続を保持する `unityd`。eval は直接TCP、ローカル読取は Editor 接続なし。[構成](architecture.md)、[eval](editor-eval.md)、[ルーティング](../src/app/runner.rs)。 | lockfile と接続用トークンを使う HTTP。`unity shell --protocol ndjson` は CLI プロセスを保持。[公式連携ガイド][official-integration]。                                                                    |
| コマンド範囲             | ローカル処理を含め153件。[ツール一覧](tools.md)、[実測カタログ][catalog]。                                                                                                                                    | Pipeline が160件、ほかに管理用 CLI コマンド。件数は集計範囲が異なります。[実測カタログ][catalog]、[公式資料][official-integration]。                                                                     |
| C# 評価                  | `editor eval`。参照情報と同一コードのコンパイル結果をキャッシュし、処理は毎回実行。[eval](editor-eval.md)。                                                                                                   | `unity command eval`。例は `return 1+2;`。[公式資料][official-skill]、[連携ガイド][official-integration]。                                                                                               |
| Play中の入力             | キーボード・マウス・ゲームパッド・タッチ。[ツール](tools.md#input-system)。                                                                                                                                   | `simulate_key` / `simulate_pointer` を確認。[カタログ][catalog]、[公式資料][official-integration]。                                                                                                      |
| C# コード解析            | Editor なしで LSP のシンボル検索・参照検索・リネーム・構造編集。[LSP](../lsp)、[ツール](tools.md)。                                                                                                           | ファイル読書きと eval。公開 CLI のコマンド群に C# LSP はありません。[公式資料][official-skill]、[カタログ][catalog]。                                                                                    |
| Editor・プロジェクト管理 | Bridge 導入、導入済み Editor の起動・終了・headless運用。Editor インストールやライセンスは Hub / 公式 CLI を利用。[setup](../src/app/setup.rs)、[ライフサイクル](../src/app/editor.rs)。                      | Editor / モジュール導入、ライセンス、プロジェクト作成・起動、ビルド、テスト、VCS。[公式資料][official-skill]。                                                                                           |
| エージェント連携         | Claude Code プラグイン / Codex スキル。[契約](skills.md)。                                                                                                                                                    | Claude Code / Codex プラグイン、タスクスキル、`unity mcp`。[公式スキル][official-skill]、[連携ガイド][official-integration]。                                                                            |

Windows の実機確認は [Issue #386 の証跡](verification/issue-386/README.md) を追加しました。
CLI / Bridge 0.18.1、Unity 6000.4.4f1 で実 setup、lockfile 発見、Editor モーダル中の OS
画面取得、および DisableDomainReload / PlayUnfocused 条件で公開23操作を確認しています。
公式 CLI 1.0.0-beta.11 / Pipeline 0.8.0-exp.1 の Windows 比較では、プロセス方式を
各操作100回、Editor 前面・最小化の2条件で計測しました。常駐方式も23操作を各100回完了しました。
[Windows計測値](verification/issue-386/resume/windows-comparison.md)に、debugビルド・他の処理が稼働中のhost・最小化した背景条件を記録しています。
下の速度比較は macOS の計測です。
[追加の機能検証](verification/issue-386/resume/README.md)では、Domain Reload 有効・無効でのTest Runnerの結果取得、
Prefab、Timeline、InputActions 保存、Animation、Video、Reference、Bake、実 Hot Reload、実 Windows Player が成功しました。
複合入力はテストのCRLFカウンター読取を修正して11件成功しました。Linux の画面取得ツールは TCP fixture で実画像を生成しましたが、
認証済み Linux Unity Editor、winget 導入、スタッフ原資料との網羅性確認は未検証です。

### 同一プロジェクトでの併用

Apple Silicon macOS の **同じ Unity 6000.3.25f1 プロジェクト**に両パッケージを
導入しました。Pipeline はポート **7800**、Bridge は **6471** で応答し、
公式 `editor_status` の ready と `unity-cli system ping` の pong / バージョン一致
**0.17.0** を確認しました。2つの接続先は別ですが、操作するシーンやアセットは共通です。
manifest の例と出典は英語節を参照してください。

現在の Bridge は uGUI を依存に含むため、旧バージョンの Empty テンプレート用回避策は
不要です。Input System は有効にします。`unity-cli setup` は Editor を閉じているときに
Both を設定します。モーダルダイアログやコンパイルエラーがある状態では両ツールとも
正常に計測できません。[導入手順](../README.ja.md)、[実装](../src/core/bridge.rs)。

### 計測方式と結果の読み方

- **プロセス起動方式:** 空の GameObject 作成、hierarchy、`1+2` を各ツール
  **100回**、ウォームアップ3回後に測ります。CLI起動から終了までの時間です。
  適用対象の呼出しでは `unityd` が起動済み、eval は直接TCPです。作成したオブジェクトは
  計測時間外で削除し、シーンの大きさを一定にします。
- **常駐方式:** スタッフ調査の **23操作を各100回**。`unity-cli` は CLI 起動＋
  常駐 `unityd`、公式は1つの **NDJSON shell** を使い続けます。常駐プロセスの初期起動は
  測定外です。Play / Stop は状態遷移完了に加え、Bridge の `isCompiling` / `isUpdating`、
  Pipeline の `compiling` / `domainReloadInProgress` が解除されるまでのポーリングを含めます。
- 同一マシン・同一プロジェクトでツール順を交互にし、最前面とバックグラウンドを別に
  計測します。常駐モードは23操作を一巡して作成・削除や Play / Stop の状態を戻します。
  JSON に全サンプル、nearest-rank の p50/p95、バージョン、ソース・バイナリの識別情報、
  フォーカス、Play設定、ホスト負荷を記録します。失敗や通信経路のfallbackは中止し、
  意図しないフォーカス変化があった組・一巡は計測し直します。

[スタッフ調査](https://github.com/akiojin/unity-cli/issues/371)の常駐構成では
unity-cli 約 **20〜60ms** / 公式 約 **510〜630ms**、旧プロセス計測では
公式 約 **50ms** でした。異なる実行の数値を同じ基準として扱わず、今回の結果は方式別に
載せます。約550msの原因が起動コストなのか、Editorの間引きなのかを切り分ける記録は
ないため、原因は断定しません。

今回の**最前面での階層取得**は、プロセス方式の p50 が **7.91 / 20.40ms**
（unity-cli / 公式）、常駐方式が **8.74 / 22.17ms** でした。この操作では公式の
約550msを再現せず、常駐化による p50 の短縮も見られませんでした。ウォームアップ後の
別経路・別操作順の実測であり、差分だけを純粋な起動コストとは扱いません。
一方、常駐・最前面の **Play完了待ち**は **552.16 / 266.35ms** で公式が速く、
この結果も省略せず掲載しています。

旧 v0.15.3 の [最前面][old-front] / [背面][old-back] 結果は履歴として保持します。
コンパイルキャッシュなどの改善前であり、現在の性能値ではありません。同じ `1+2` の
反復はキャッシュ済みコードの計測です。異なるコードではコンパイルが必要になり得ます。

返却フィールドや実装も完全には同一ではありません。hierarchy / Transform の情報量、
1280×720 PNG のキャプチャ方法、ローカル読取と Editor 経由の読取には差があります。
C# は両者が読める同じ小さな `Assets/HotReloadProbe.cs` を使います。スクリーンショットは
背面から開始しても Game View が最前面になる場合があり、次の操作前に背面へ戻します。
位置とマテリアル色は同じ値を繰り返し設定します。Play は **DisableDomainReload** /
**PlayUnfocused** です。大きなシーン、別のスクリプト、Domain Reload やホストの負荷で
結果は変わります。すべての操作・条件・百分位で速いという主張ではありません。
ディレクトリへの掲載申請はオーナー判断のため、この変更には含みません。
各実行は開始条件を満たしていますが、実行中のホスト負荷は変動します。
全区間が無負荷だったとは仮定せず、表とJSONに開始・終了両方の負荷を残しています。

## Results

同じ表を英日共通で使います。All values below are milliseconds (ms).

<!-- comparison-results:start -->

Machine / マシン: **Apple M5 Max, 128 GiB, macOS 26.5 (arm64)**. Unity **6000.3.25f1**, one shared isolated project / 両ツール共通の隔離プロジェクト。

Build / ビルド: unity-cli **0.17.0 development build / 開発ビルド** (Bridge authentication enabled / 認証有効), official **1.0.0-beta.11**, Pipeline **0.8.0-exp.1**. 100 measured samples per cell after 3 warmups / 各セル100サンプル、事前ウォームアップ3回。

Source commit / ソース: `6ca7f4542aad491a427cb6147b947680fbcc531f`. Binary and script SHA-256 values are recorded in every JSON / 各JSONにバイナリ・スクリプトのSHA-256を記録。

<!-- process-frontmost -->

### Process / プロセス起動 — frontmost / 最前面

[Raw samples and conditions / 生データと条件](benchmarks/official-comparison-2026-10-02-process-frontmost.json). Load1 at start/end / 開始・終了load1: **8.42 / 8.88**.

| Operation / 操作                                            | unity-cli p50 | unity-cli p95 | official p50 | official p95 |
| ----------------------------------------------------------- | ------------: | ------------: | -----------: | -----------: |
| `create_gameobject` — Empty GameObject / 空オブジェクト作成 |          8.04 |         10.57 |        21.57 |        28.32 |
| `hierarchy` — Hierarchy / 階層取得                          |          7.91 |          9.99 |        20.40 |        25.09 |
| `eval` — eval 1+2                                           |          8.80 |         10.58 |        25.89 |        29.03 |

<!-- process-background -->

### Process / プロセス起動 — background / 背面

[Raw samples and conditions / 生データと条件](benchmarks/official-comparison-2026-10-02-process-background.json). Load1 at start/end / 開始・終了load1: **7.58 / 14.56**.

| Operation / 操作                                            | unity-cli p50 | unity-cli p95 | official p50 | official p95 |
| ----------------------------------------------------------- | ------------: | ------------: | -----------: | -----------: |
| `create_gameobject` — Empty GameObject / 空オブジェクト作成 |         66.65 |         83.76 |        72.36 |       127.15 |
| `hierarchy` — Hierarchy / 階層取得                          |         60.99 |         88.78 |        63.10 |       135.79 |
| `eval` — eval 1+2                                           |         61.78 |         72.47 |        72.06 |        80.23 |

<!-- resident-frontmost -->

### Resident / 常駐 — frontmost / 最前面

[Raw samples and conditions / 生データと条件](benchmarks/official-comparison-2026-10-02-resident-frontmost.json). Load1 at start/end / 開始・終了load1: **5.51 / 7.88**.

| Operation / 操作                                           | unity-cli p50 | unity-cli p95 | official p50 | official p95 |
| ---------------------------------------------------------- | ------------: | ------------: | -----------: | -----------: |
| `editor_state` — Editor state / Editor状態                 |          8.93 |         14.47 |        24.07 |        44.80 |
| `hierarchy` — Hierarchy / 階層取得                         |          8.74 |         13.86 |        22.17 |        37.89 |
| `find_gameobject` — Find GameObject / 検索                 |          8.16 |         12.50 |        21.81 |        37.45 |
| `transform` — Transform / 取得                             |          9.00 |         15.77 |        77.69 |       125.28 |
| `position` — Position / 位置変更                           |          8.90 |         14.11 |        21.90 |        38.04 |
| `create_cube` — Create Cube / Cube作成                     |          8.34 |         13.80 |        21.94 |        36.94 |
| `add_component` — Add component / 追加                     |          8.32 |         13.22 |        77.28 |       121.55 |
| `remove_component` — Remove component / 削除               |          8.34 |         14.11 |        76.60 |       116.29 |
| `delete_gameobject` — Delete GameObject / オブジェクト削除 |          8.17 |         13.91 |        21.34 |        37.10 |
| `scene_info` — Scene info / シーン情報                     |          8.00 |         13.19 |        21.09 |        36.57 |
| `scene_save` — Save scene / シーン保存                     |         51.91 |         79.06 |       265.93 |       498.61 |
| `console` — Console / Console取得                          |          8.53 |         14.12 |        25.81 |        40.04 |
| `screenshot` — Screenshot / PNG保存                        |         33.22 |         49.34 |        49.03 |        67.74 |
| `material_search` — Find Material / Material検索           |          9.44 |         14.81 |        28.51 |        41.60 |
| `asset_copy` — Copy asset / コピー                         |         33.50 |         57.91 |        55.88 |        78.70 |
| `asset_move` — Move asset / 移動                           |         32.58 |         57.37 |        53.23 |        76.25 |
| `asset_delete` — Delete asset / アセット削除               |         30.78 |         48.14 |        49.28 |        77.60 |
| `import_settings` — Import settings / Import設定           |          8.21 |         13.76 |        24.24 |        37.12 |
| `material_modify` — Modify Material / Material変更         |         18.99 |         37.55 |        34.18 |        62.31 |
| `time_settings` — Time settings / Time設定                 |          8.14 |         13.40 |        23.69 |        36.54 |
| `read_csharp` — Read C# / C#読取                           |          7.64 |         14.07 |        23.15 |        36.67 |
| `play_until_ready` — Play until ready / Play到達           |        552.16 |        650.36 |       266.35 |       388.92 |
| `stop_until_ready` — Stop until ready / Stop到達           |        285.91 |        413.76 |       310.80 |       452.95 |

<!-- resident-background -->

### Resident / 常駐 — background / 背面

[Raw samples and conditions / 生データと条件](benchmarks/official-comparison-2026-10-02-resident-background.json). Load1 at start/end / 開始・終了load1: **6.25 / 10.92**.

| Operation / 操作                                           | unity-cli p50 | unity-cli p95 | official p50 | official p95 |
| ---------------------------------------------------------- | ------------: | ------------: | -----------: | -----------: |
| `editor_state` — Editor state / Editor状態                 |         62.65 |         72.13 |        64.72 |        75.18 |
| `hierarchy` — Hierarchy / 階層取得                         |         64.09 |         73.63 |        64.75 |        75.18 |
| `find_gameobject` — Find GameObject / 検索                 |         63.37 |         72.56 |        64.36 |        75.48 |
| `transform` — Transform / 取得                             |         65.04 |         74.08 |       124.13 |       145.92 |
| `position` — Position / 位置変更                           |         62.98 |         72.82 |        87.68 |       119.33 |
| `create_cube` — Create Cube / Cube作成                     |         64.18 |         74.62 |        64.19 |        75.59 |
| `add_component` — Add component / 追加                     |         65.41 |         75.03 |       124.48 |       143.12 |
| `remove_component` — Remove component / 削除               |         65.23 |         74.54 |       159.54 |       172.14 |
| `delete_gameobject` — Delete GameObject / オブジェクト削除 |         65.52 |         73.77 |        88.82 |       117.78 |
| `scene_info` — Scene info / シーン情報                     |         65.10 |         74.89 |        65.62 |        75.17 |
| `scene_save` — Save scene / シーン保存                     |        112.63 |        144.45 |       372.79 |       581.19 |
| `console` — Console / Console取得                          |         30.70 |         99.17 |        26.12 |        38.12 |
| `screenshot` — Screenshot / PNG保存                        |         93.31 |        114.41 |       112.63 |       157.66 |
| `material_search` — Find Material / Material検索           |         59.16 |        102.21 |        50.41 |       129.84 |
| `asset_copy` — Copy asset / コピー                         |         91.56 |        115.23 |        90.42 |       162.61 |
| `asset_move` — Move asset / 移動                           |         63.77 |        145.57 |       126.94 |       154.97 |
| `asset_delete` — Delete asset / アセット削除               |         62.62 |        142.08 |        70.84 |       155.12 |
| `import_settings` — Import settings / Import設定           |         37.92 |        104.56 |        48.12 |       124.02 |
| `material_modify` — Modify Material / Material変更         |         77.11 |         86.32 |        76.86 |        93.98 |
| `time_settings` — Time settings / Time設定                 |         44.99 |         62.09 |        52.20 |       125.93 |
| `read_csharp` — Read C# / C#読取                           |          8.13 |         11.52 |        62.39 |        74.64 |
| `play_until_ready` — Play until ready / Play到達           |        775.58 |        909.15 |       402.96 |       470.26 |
| `stop_until_ready` — Stop until ready / Stop到達           |        452.43 |        545.13 |       442.72 |       570.69 |

<!-- comparison-results:end -->

## Reproduce

Use a disposable project. The following commands run from this repository's root
on macOS with Unity **6000.3.25f1** installed through Unity Hub. They copy a fixture;
no existing project is modified. Python 3.9+ and a release Rust build are required.

既存プロジェクトを使わず、専用の一時プロジェクトを作ります。以下は macOS / Unity Hub
の **6000.3.25f1** 用で、リポジトリのルートから実行します。Python 3.9 以降が必要です。
公式バイナリは専用ディレクトリに取得し、公開ハッシュを検証してから使います。

```bash
cargo build --release --bin unity-cli
mkdir -p .cache/official-comparison
curl -fLsS https://public-cdn.cloud.unity3d.com/hub/prod/cli/1.0.0-beta.11/unity-darwin-arm64 \
  -o .cache/official-comparison/unity
curl -fLsS https://public-cdn.cloud.unity3d.com/hub/prod/cli/1.0.0-beta.11/latest.json \
  -o .cache/official-comparison/manifest.json
python3 - <<'PY'
import hashlib, importlib.util, json
from pathlib import Path
root = Path('.cache/official-comparison').resolve()
binary = root / 'unity'
manifest = json.loads((root / 'manifest.json').read_text())
assert hashlib.sha256(binary.read_bytes()).hexdigest() == manifest['binaries']['darwin-arm64']['sha256']
binary.chmod(0o755)
spec = importlib.util.spec_from_file_location('matrix', 'scripts/e2e-matrix.py')
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)
editor = Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity')
_, project, manifest = matrix.prepare(editor, root)  # refuses an existing project directory
matrix.prepare_perf_fixture(project)
manifest['dependencies']['com.unity.pipeline'] = '0.8.0-exp.1'
(project / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
PY
env -u UNITY_CLI_ALLOW_UNAUTHENTICATED UNITY_CLI_PORT=6471 /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -projectPath "$PWD/.cache/official-comparison/project" \
  -logFile "$PWD/.cache/official-comparison/editor.log" &
```

Wait for the Editor to finish importing and compiling, then verify both endpoints:

Editor のインポートとコンパイルが終了したら、両接続を確認します。

```bash
UNITY_CLI_NO_AUTO_UPDATE=1 target/release/unity-cli --port 6471 system ping --output json
UNITY_NO_CLI_INVOKED_TELEMETRY=1 UNITY_NO_CRASH_REPORT=1 \
  .cache/official-comparison/unity command editor_status \
  --project-path "$PWD/.cache/official-comparison/project" --format json
```

Run all four combinations sequentially. Keep other Editors, builds and tests idle.
For the published runs, start only when load1 is below 9 and no other project's
compiler/test/Editor exceeds 50% CPU. Do not switch apps during a run: the script
establishes and checks the focus condition. Allow macOS automation permission
for controlling the disposable Editor's focus if this machine requires it.

4通りを順に実行します。他の Editor・ビルド・テストは停止中の時間を使います。
公開値は load1 < 9、他プロジェクトのコンパイラ・テスト・Editor が CPU 50% 以下で開始。
実行中のアプリ切り替えは避けてください。スクリプトがフォーカスを設定・確認します。
macOS で必要なら、専用 Editor のフォーカス制御に対するオートメーション権限を許可します。

```bash
for mode in process resident; do
  for focus in frontmost background; do
    python3 scripts/bench-compare-official.py \
      --project-path .cache/official-comparison/project \
      --unity-cli target/release/unity-cli \
      --official .cache/official-comparison/unity --port 6471 \
      --mode "$mode" --focus "$focus" --iterations 100 --warmup 3 \
      --out ".cache/official-comparison/${mode}-${focus}.json"
  done
done
python3 -m unittest discover -s tests/scripts -p test_compare_official.py -v
```

The script removes inherited authentication bypass/token-file overrides and uses
the project's Editor token discovery. It disables both CLIs' update checks and the official CLI's invocation
ping/crash reports for these runs. The official analytics preference is not
changed. Its `HOME` and shell configuration are not rewritten. `unityd` uses a
project-local tools root so another project's daemon cannot affect the result.
The generated scenes/assets are under `Assets/Scenes/Generated/E2E/Performance`.
Close the disposable Editor after measuring. Identical eval snippets reuse compiled
code, so the 128 distinct-compilation limit does not require a restart between
these `1+2` runs. See [eval caching](editor-eval.md#performance-and-caching).

この計測では認証回避・別トークンファイルの環境変数を引き継がず、対象 Editor の認証を使います。
両 CLI の更新確認と公式 CLI の利用 ping / クラッシュ報告を無効にします。
公式 analytics の同意設定は変更しません。`HOME` やシェル設定も書き換えません。
`unityd` はプロジェクト内の専用 tools root を使います。生成物は
`Assets/Scenes/Generated/E2E/Performance` 内です。計測後は専用 Editor を閉じます。
同一 eval コードは再利用されるため、この `1+2` の計測間に128アセンブリ制限のための
再起動は不要です。[eval キャッシュ](editor-eval.md#performance-and-caching)を参照してください。

[official-skill]: https://github.com/Unity-Technologies/unity-agent-plugin/blob/9c01e8d21cfefa28bc0a18f92ec910a738a1b1bc/skills/unity-cli/SKILL.md
[official-integration]: https://github.com/Unity-Technologies/unity-agent-plugin/blob/9c01e8d21cfefa28bc0a18f92ec910a738a1b1bc/skills/unity-cli/references/integration-advanced.md
[official-diagnostics]: https://github.com/Unity-Technologies/unity-agent-plugin/blob/9c01e8d21cfefa28bc0a18f92ec910a738a1b1bc/skills/unity-cli/references/diagnostics-maintenance.md
[official-license]: https://github.com/Unity-Technologies/unity-agent-plugin/blob/9c01e8d21cfefa28bc0a18f92ec910a738a1b1bc/LICENSE.md
[catalog]: benchmarks/official-capabilities-2026-10-02.json
[old-front]: benchmarks/official-comparison-2026-09-30-foreground.json
[old-back]: benchmarks/official-comparison-2026-09-30-background.json
