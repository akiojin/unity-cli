# Issue #470: Windows の実 setup と Editor 自動発見

担当: ルカ・モリ（Codex）。自律起動で、Windows 実機の AC-1 / AC-2 と結果記録の AC-6 を検証する。
Issue 全体は未完了。Linux 実 Editor、スタッフ原資料、winget の条件はこの結果に含めない。

## 基準と範囲

fetch 後の `HEAD` / `origin/work/issue-470` / `origin/develop` は
`1d021ffb23d6960f9eae501d54a23e32057f6b81` で一致し、未配送ソースはなかった。
既存の Windows 修正・証跡は PR #471 で配送済みである。
今回はそのソースを Windows native でビルドし、CLI / Bridge 0.19.0 と
実 Unity 6000.4.4f1 の新しい実行結果を収録する。製品コードの変更はない。

PM default ruling (override available): 独立して配送できる Windows 検証だけを進める。
成功した項目だけを更新し、Issue を閉じない。資格情報・外部提出や原資料の代替を推測で補わない。

## 受け入れ基準

| 条件 | 判定 | 根拠・残る条件 |
| --- | --- | --- |
| AC-1: 実 Bridge の lockfile で Windows 自動発見 | PASS | [lockfile](lockfile.json) の PID / project / port と [発見結果](discovery.json) が一致。source は `lockfile`、status は `up`。[ping](ping.json) は明示 port なしで同じ project に成功。 |
| AC-2: dry-run ではない実 setup | PASS | [setup](setup.json) は Bridge のない manifest を変更し、OpenUPM の 0.19.0 を導入、実 Editor を起動して `projectMatches: true` と version match を確認。 |
| AC-3: Linux の実 Editor モーダルと OS fallback | 未達 | [既存証跡](../issue-386/resume/README.md) は capture fixture の成功と、実 Linux Editor の license exit 198 を区別している。本起動では Linux 認証を実施していない。 |
| AC-4: スタッフ原資料の全行と比較表 | 未達 | オーナー提供の `pm-scratch/staff-report.md` が未提供。公開シナリオからの再構成を原資料そのものとは扱わない。 |
| AC-5: winget の公開 install と実 ping | 未達 | 今回の `winget show --id akiojin.unity-cli --exact --source winget` は package not found。repository secret の名前一覧に `WINGET_TOKEN` はない。初回提出・secret 登録・winget install は未実施。 |
| AC-6: PR または Issue コメントへの記録 | 本資料と Issue コメントで記録 | 成功・未達・試行の限界を分けて残す。AC-3/4/5 を成功と主張しない。 |

## 実行方法と隔離

```bash
cargo build --bin unity-cli
python docs/verification/issue-470/check-evidence.py
```

上の checker は収録した実測の検査であり、Unity を再実行しない。
実機の成功結果は UTC 2026-10-24 18:47:05 開始の実行で取得した。
Unity 自身の `-batchmode -nographics -createProject <project> -quit` で新規 project を作り、
Bridge のない状態から `setup --launch-editor --wait-secs 600` を実行した。
setup は 69.169 秒で成功した。実際の引数と CLI SHA256 は [summary](summary.json) に残している。

実機試行は専用ディレクトリ、空の instance registry、専用 `UNITY_CLI_EDITORS_DIR`、
専用 tools root、未使用の loopback port を使う。`setup` 以外のコマンドには port / ports を渡さない。
実 Bridge が作成した lockfile を読み、公開する JSON から `authToken` を除去する。
インストールされた全 C# ファイルを checkout と比較する（CRLF / LF だけ正規化）。
終了時は検証用 Editor 自身に終了させ、PID の終了と lockfile の削除を確認した。
全144ファイルの [比較時 SHA256](bridge-source.json) を checker が現在の checkout とも照合する。

## 試行の限界

最初の手製の最小 project は初回 package import 中に 600 秒で timeout した。
その Editor は所有 PID と project path を照合して終了した。これを製品不具合の証明とは扱わない。
Unity 自身が作成した project では setup / 発見 / ping が成功したが、検証コードが
ping に存在しない `unityVersion` を参照して失敗したため、実際の `projectPath` / `pong` /
`bridgeVersion` を個別に確認した。成功した CLI 呼び出しと、その後の検証コードの例外を混同しない。
この実機 probe 全体の終了コードは **1** であり、完全成功した E2E run とは主張しない。
project 新規作成の再試行は 300 秒で timeout し、設定だけコピーした新規 project での別試行も
lockfile が ready になった後に応答が止まった。後者は所有 process tree だけを照合して終了した。
停滞の根本原因は未確定。連続起動の安定性や、どの条件でも setup が成功することは未証明である。
試行用 harness と失敗記録は `.cache/issue-470` に保持し、今回の PR には検証記録だけを含める。

## Verification Report

Mode: full（今回追加した証跡と、その整合性 checker の範囲）。Launch mode: autonomous。
Acceptance Surface: Windows CLI と実 Editor の接続。表示の実装変更はない。

- Cargo: Windows CLI をビルドする。
- Python: 収録した実 setup / lockfile / discovery / ping / 全 C# ソース一致 / 所有 Editor の終了の記録を検査する。Unity の再実行ではない。
- Markdown: 本資料を lint する。
- Rust / LSP / Unity の製品コードは変更しないため、全件 unit suite と dotnet の再実行は対象外。
- 正式な `verify.plan` / `verify.run` の結果・record id は PR と Issue コメントに記録する。

[実行時間・バイナリ SHA256・テスト名別の結果](summary.json)を参照。
raw log と project は ignore 対象の `.cache/issue-470` に保持し、認証情報はコミットしない。

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)

次の配送対象はこの Windows 証跡のみ。Issue 全体の再開条件は、Linux 実 Editor の認証経路、
スタッフ原資料、winget 初回提出と `WINGET_TOKEN` の登録が揃うことである。
