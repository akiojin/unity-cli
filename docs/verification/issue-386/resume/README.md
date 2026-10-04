# #386 継続検証の記録

2026-10-04 JST。Windows 11 x64 / Unity 6000.4.4f1 / CLI・Bridge 0.18.1。
成功した独立検証と、Issue 全体の未達条件を区別する。

**Overall: FAIL — Issue 全体は未完了、PR 未作成。**

## 実行した検証

| 対象 | 実測 | 証跡 |
| --- | --- | --- |
| Windows native Rust | 610 PASS / 0 FAIL（558 unit +52 integration） | [全件ログ](cargo-all-final.txt)、[全610テスト名](cargo-test-inventory.json)、[コマンド・exit・時刻](final-checks.json) |
| fmt / clippy / skills lint | PASS、23 skills / 0 violations | [fmt](fmt.txt)、[clippy](clippy.txt)、[skills](skills-lint-final.txt) |
| all-features clippy / rustdoc / Markdown | warningsをエラーとして全件成功 | [コマンド・exit・時刻](final-doc-checks.json) |
| Python の PID 生存確認 | Windows4件、Linux3件成功・Windows専用1件skip | [Windows](python-windows.txt)、[Linux](python-linux.txt)、[最終Windows4件](process-liveness-final.txt) |
| 実 Editor の公開23操作 | 23/23、各1回の機能確認 | [操作と結果](staff-final-operations.json)、[条件・バイナリ](staff-final-summary.json) |
| Windows 公式 CLI 比較・プロセス方式 | 作成・階層・evalを両CLIで各100回、前面・最小化の2条件。3 warmup | [前面](official-windows-process-frontmost.json)、[最小化](official-windows-process-background.json)、[公式binary検証](official-windows-binary.json) |
| 実 Editor モーダル / Windows GDI fallback | PNG生成、無効時 TIMEOUT | [結果](modal-summary.json) |
| Prefab | 45 runtime +22 EditMode、失敗0 | [結果](prefab-results.json)、[実行ログ](prefab.txt) |
| Timeline | 40 PASS / 0 FAIL | [実行ログ](timeline.txt) |
| C# eval | 18 PASS / 0 FAIL、1000連続実行 | [実行ログ](eval.txt)。追加assembly4、管理memory増加2,658,304 bytes |
| Domain Reload | 有効2件・無効2件とも成功、設定復元 | [全テスト・実行条件](domain-reload.json) |
| 実 Hot Reload | Windows x64 / FSR1.8.0、25 PASS / 0 FAIL | [実行ログ](hot-reload-results.txt)、[条件・終了処理](hot-reload-summary.json) |
| InputActions の保存・再起動 | 50 PASS / 0 FAIL、Editor2回起動 | [実行ログ](input-actions-persistence.txt) |
| InputActions の型 / 通知 | 型12件、通知4件とも成功 | [型](InputActionsHandlerTests.xml)、[通知](InputActionNotificationTests.xml) |
| Animation Curves | 数値・reimport等25項目、EditMode26件成功 | [runtime](animation-curves.txt)、[EditMode](AnimationCurveHandlerTests.xml) |
| 動画形式 | mp4 / webm / PNG sequence / 再実行・拒否を含む6項目成功 | [結果](video-summary.json)、[実行ログ](video-formats.txt) |
| C# reference | 実fetch5項目 + ref選択/status/grep/cache4項目成功 | [fetch](reference-fetch-summary.json)、[解決](reference-resolution-summary.json) |
| Bake | 4 backend、101チェック成功。保存・再読込・実NavMesh問い合わせを確認 | [実行ログ](bake-headed-final.txt)、[操作条件](bake-headed-result.json)、[実ファイルSHA256](bake-artifact-metadata.json) |
| Bake / Scene の回帰テスト | 15件 /11件成功 | [Bake](BakeHandlerTests.xml)、[Scene](SceneHandlerTests.xml) |
| Windows Player | 実ビルド、exe / Data / BuildReport一致、headless起動5秒、GUI描画 | [ビルド・起動](windows-player-result.json)、[640×360の描画観測](windows-player-graphical.json)。ゲーム入力は未確認 |
| Gamepad Stick / Touch | 2件 /15件成功 | [Gamepad](GamepadStickInputPlayModeTests.xml)、[Touch](TouchGesturePlayModeTests-fresh.xml) |
| 複合入力・カウンター読取 | Windows headlessで11/11成功。元の7件と改行回帰4件 | [RED: 11件中3失敗](InputSimulationAutomationPlayModeTests-counter-red.xml)、[GREEN: 11件成功](InputSimulationAutomationPlayModeTests-counter-green.xml)、[コマンド・exit](input-counter-green.json) |
| Linux X11 | import / scrot / maim、12チェック成功 | [実行ログ](linux-x11-platform.txt) |
| Linux Wayland | GNOME / sway(grim) / KWin(spectacle) の実PNG生成成功 | [GNOME](linux-gnome-fallback-result.json)、[grim](linux-wayland-first-results.json)、[spectacle](linux-wayland-extra-results.json) |
| Linux 実 Unity | 起動exit198、認証未完了 | [匿名化した結果](linux-license.json)。上記captureはsilent TCP fixtureであり実Editorの証明ではない |
| winget初回manifest | validate PASS | [結果](winget-manifest-validate.txt)。公開sourceからのinstall / pingは未実施 |

## 確認して修正した原因

- Git smart HTTP に `Authorization: token` を渡すと有効なGitHub tokenでも失敗した。
  同じtokenのBasic形式で成功を確認し、HTTP fixtureの回帰テストをRED→GREENにした。
  [RED](reference-auth-red.txt)、[GREEN](reference-auth-green.txt)、[値を含まない実transport検証](reference-auth-probe.json)。
- Windowsの深いC# referenceキャッシュでcheckoutが`Filename too long`になった。
  Gitの子プロセスだけに`core.longpaths=true`を指定し、設定falseの環境でも実checkoutできる
  [RED](reference-longpath-red.txt)→[GREEN](reference-longpath-green.txt)を確認した。
- batch失敗時に配列から診断を読めず`UNKNOWN_ERROR`になった。
  最初の失敗項目からmessage/codeを読み、全結果の配列を保持する。
  [RED](batch-message-red.txt)、[GREEN](batch-message-green.txt)。
- 保存シーン1つでも内部Preview SceneがあるとBakeを拒否していた。
  対象シーンのPreview判定と通常シーン数を確認するよう修正した。
  [RED](bake-preview-red.xml)、[GREEN](bake-preview-green.xml)。追加ロードした通常シーンの拒否も15件のsuiteで確認した。
- offlineのMCP / warningテストが実Editorを自動発見していた。
  期待値のCLIも同じ未接続port1を指定し、実Editor起動中に検証した。
- Touchの端末除去テストは通常のCLI接続ログを予期しないログと扱っていた。
  loopbackからの接続を示す既知のLogだけを期待値として登録する。
  Warning / Error / Exceptionの検出を保ったまま15件すべて成功した。

Bakeのheaded検証では、Unityが生成シーンの外部変更確認を5回表示した。
自分が起動したEditorのPID、ダイアログ名、生成シーン1つだけのパスを照合し、
`Ignore`を選択してtracked sceneを維持した。操作履歴を上表の条件JSONに残している。
UI操作なしの最初の実行はTIMEOUTだった。無人実行での成功とは扱わない。

複合入力の初回[7件中2失敗](InputSimulationAutomationPlayModeTests.xml)は、テストがWindowsの
CRLF表示からカウンターを読み取れず0にしたことが原因だった。数値読取の`$`の前に任意の
`\r`を認め、CRLF / LF / 末尾改行なし / キー欠落の4ケースを追加した。
旧正規表現では新CRLFケースと複合2件が失敗し、修正後は元の入力シナリオを含む11件が成功した。
入力runtime、50msのhold、ミリ秒単位のswipe20ms、元のアサーションは変更していない。
調査用のruntime変更や一時fixtureは製品差分から除外した。

## 証跡の帰属と残る条件

[ソース・最新Windows CLI・公開Linux CLIのSHA256](source-snapshot.json)を保存した。
公開Linux CLIのSHA256はRelease 0.18.1の`SHA256SUMS`と一致する。
先に実行した23操作・Prefab等には、その時点のバイナリSHA256を別のJSONに保持している。
全結果を同一バイナリの実行と扱わない。

[スタッフ範囲の再構成](staff-scope-reconstruction.md)は#243–260 / #390–395からの推論であり、
未取得の`pm-scratch/staff-report.md`そのものではない。元の全行・印・正のシナリオとの一致は未確認。
Windowsでの公式CLIプロセス比較は上表の2条件で計測済み、常駐23操作は計測中である。
同じUnity6000.4.4f1のプロジェクトにBridge0.18.1とPipeline0.8.0-exp.1を入れ、
両CLIの応答からプロジェクトとUnityバージョンの一致を確認した。
背景条件は、Windowsが既存GWTウィンドウの前面化を拒否したため、自分のEditorだけを最小化した。
macOSの表示したままの背景条件とは異なる。ほかのプロジェクトの正式検証が稼働中のhostであり、
Windowsの性能予算や過去のbaselineを満たしたという結果ではない。
常駐方式の初回は、公式`find_assets`の文字列`search_in`に配列を渡して失敗した。
[失敗記録](official-windows-resident-frontmost-search-array-failure.json)を残し、
導入済みPipelineの実メソッド定義に合わせて文字列を渡すよう修正した。
[比較スクリプトの15件の回帰テスト](comparison-unit-linux.txt)も成功した。

正式な`verify.run`は共通host leaseを1500秒待って2回ともdeferred。
[1回目](canonical-verify.txt)、[2回目](canonical-verify-second.txt)。通常の直接実行とは区別する。
同一sessionのblocked復旧用deriveはbinary-only Cargoに`--lib`を生成し、
[実Cargoがno library targets foundで拒否する](derived-lib-error.txt)。PMに修復裁定を依頼済み。

User Verification Result: pending。Agentの描画観測を人の確認済みとは扱わない。
不足するLinux認証・スタッフ原資料・winget公開/Secretの操作経路・正式検証復旧が揃った後、
元資料と照合して必要なユーザー確認とReady PRへ進む。
