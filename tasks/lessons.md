# tasks/lessons.md

このファイルは、ユーザーからの修正や失敗から得た再発防止ルールを記録する。

## Rules

- ルールは具体的に書く（「何を」「いつ」「どう防ぐか」）
- 再発した場合はルールを更新して、曖昧な文言を削る
- 直近の作業開始前に必ず読み返す

## Entries

### 2026-02-27

- Context: Markdown Lint を `npm run lint:md` で実行した際、ローカルに `markdownlint` が存在しなかった。
- Mistake: コマンド失敗後の代替手順を標準化していなかった。
- Rule: ツールが未インストールの環境では `npx --yes <tool>` で即時フォールバックする。
- Checkpoint: `npx --yes markdownlint-cli CLAUDE.md tasks/*.md --config .markdownlint.json --ignore-path .markdownlintignore`

### 2026-03-10

- Context: `gwt-spec` ラベル付き Issue #107 の実装後、Issue 本文の `Tasks` が未更新のまま「残り 2 点」と判断してしまった。
- Mistake: 完了判定の一次情報を Issue 本文ではなく、自分の縮約した内部チェックリストに置いてしまった。
- Rule: `gwt-spec` Issue の完了判定は必ず Issue 本文の `Tasks` / 受け入れ基準 / PR本文 / 作業ツリー状態を同期させた上で行う。1つでも未同期なら「完了」と言わない。
- Checkpoint: 1. Issue 本文の `Tasks` を更新 2. 検証結果を Issue/PR に反映 3. `git status --short` を確認 4. ignore すべきローカル生成物が残っていれば `.gitignore` か cleanup を先に行う

### 2026-05-11 (SPEC #185 振り返り)

- Context: SPEC #185 (UnityCsReference 参照キャッシュ Phase 1) を実装した PR #186 が、CI で `Rust Format & Lint` 失敗を 3 回繰り返した。原因はローカル clippy が 0.1.94、CI clippy が 1.95.0 で、新規 lint (`collapsible_match`、`unnecessary_sort_by`、`await_holding_lock`) と `unused_variables` を見逃していた。
- Mistake: ローカルでの `cargo clippy --all-targets -- -D warnings` が clean だったため push 前検証を完了と見なした。
- Rule: 大きな PR を出す前に、CI と同じ rust toolchain で clippy を回す。`rustup show active-toolchain` を CI の `dtolnay/rust-toolchain@stable` 指定と突き合わせ、ずれていたら `rustup update stable` してから再検証。最低限 `cargo +stable clippy --all-targets -- -D warnings` を必ず通す。
- Checkpoint: 1. `rustup show active-toolchain` を実行 2. CI の rust-toolchain 指定と一致するか確認 3. 不一致なら `rustup update` 4. clippy 再実行

### 2026-05-11 (coverage gate)

- Context: SPEC #185 で新規 module `src/reference/*` を ~1,000 行追加した結果、Phase 1 第 1 commit 時点の line coverage が 89.15% まで下がり、CI `Rust Coverage >= 90% (required)` が 2 連続で fail した。
- Mistake: 新規 module の test を「主要パスを cover していれば十分」と簡略化したため、dispatcher / wiring / error paths が未 cover で全体閾値を割った。
- Rule: 大規模な新規 module を追加する PR では、追加直後に `cargo llvm-cov --all-targets --summary-only -- --test-threads=1` をローカルで 1 回実行して 90% を確認する。reference / dispatcher / CLI 配線 / error path も TDD の中で test を書く。
- Checkpoint: 1. 新規 module 着手時に test stub を先に書く (RED) 2. 実装ごとに対応する test を増やす 3. PR push 前にローカル llvm-cov で全体 90% を確認 4. 90% 未達なら不足 module を識別し、small test を集中投入

### 2026-05-11 (SPEC section markers)

- Context: SPEC #185 を `gwtd issue spec create --title ... -f <body>` で plain markdown 本文として作成したため、`<!-- sections: -->` コメントが付かず、`gwtd issue spec 185 --section spec` / `--edit tasks -f <file>` などの section 操作が `section 'spec' not found` で失敗した。
- Mistake: section 構造が必要であることを起票時に意識せず、`gwt-build-spec` の completion gate で tasks セクション更新ができなくなった。
- Rule: SPEC を起票するときは body 内に `## Spec` / `## Plan` / `## Tasks` / `## TDD` の見出しを揃え、必要なら `gwtd issue spec --edit <section>` を初回投入時から使う。`--edit` 経由なら gwtd 側が `<!-- sections: ... -->` コメントを管理してくれる。
- Checkpoint: 1. spec create 直後に `gwtd issue spec <n>` を実行して `<!-- sections: -->` が埋まっているか確認 2. 空ならその場で `--edit spec` / `--edit plan` / `--edit tasks` / `--edit tdd` で投入し直す

### 2026-05-12 (子 SPEC 増殖の防止)

- Context: Phase 4 umbrella SPEC #191 を起票した直後、最優先サブタスクを「子 SPEC #192」として切り出してしまい、user から「基本的には子 SPEC は作らないでください」と明示指示を受けた。
- Mistake: ralph loop / autonomous 進行時に、umbrella SPEC があってもサブタスクごとに新 SPEC を生やす癖が出た。Phase 1-3 の「Phase ごとに 1 SPEC」パターンを引きずって過剰に細分化した。
- Rule: umbrella SPEC が存在する Phase では、サブタスクで独立した SPEC を新設しない。実装は umbrella SPEC を `Refs` する commit / PR で進め、umbrella SPEC 本文の Tasks セクションをチェックボックスで更新する。新 SPEC を起こすのは umbrella の意図と明確に外れる別軸の作業に限定する。
- Checkpoint: 1. 新規 SPEC 起票前に「既存の umbrella SPEC で受け止められないか」を 1 度自問する 2. 起票する場合は umbrella との関係（吸収 / 並列 / 独立）を本文の `Related` で明示する

### 2026-05-12 (正式な gwt-spec を close しない)

- Context: Phase 4 umbrella SPEC #191 の 5 サブタスク (A-E) がすべて develop に MERGED になった時点で「completed として close」してしまい、誤起票 #192 も同時に close した。user から「そもそも、正式な gwt-spec はクローズしません」と訂正を受けた。Phase 1-3 (#185 / #187 / #188) は実装 MERGED 後も OPEN 維持しているのが既存運用パターン (確認済み)。
- Mistake: GitHub Issue の標準的な「実装が終わったら close」感覚で gwt-spec を扱った。gwt-spec が "living documentation" として運用されている前提を見落とし、Tasks セクションに「整理タスク: umbrella を close するかどうか user 判断」と書いた時点で誤誘導が始まっていた。
- Rule: gwt-spec ラベル付き Issue は実装完了後も close せず OPEN 維持する。Phase 完了 / PR 全 MERGED / Tasks 全 `[x]` であっても `gh issue close` / `gwtd issue close` を呼ばない。Out of Scope の継続改善余地や後続 Phase の起点として残す。Tasks セクションに「整理タスク: umbrella を close する判断」のような close 誘導項目を作らない。重複 / 誤起票で close 候補となる場合でも、自律的に close せず必ず user 判断を仰ぐ。ただし、別の canonical な gwt-spec に統合されて canonical 性が新 SPEC に移行した旧 SPEC は、集約先 SPEC 番号を明記した最終コメントを残した上で close してよい (例: Phase 単位の SPEC をドメイン単位の SPEC に統合した 2026-05-12 のケース、#185 / #187 / #188 / #191 / #192 → #204)。重複 / 誤起票による単独 close と違い「canonical な後継 SPEC が存在する」ことが集約 close の必要条件。
- Checkpoint: 1. SPEC 完走報告は最終 comment + Workspace 更新 + Tasks `[x]` で足りる 2. close 操作の代わりに「OPEN 維持運用方針」を Tasks / 運用方針セクションに明示する 3. 既存 SPEC の状態確認時に `[CLOSED]` を見かけたら「意図的な close か」を user に確認、意図せず close されていたら reopen 相談 4. close する場合は「集約 close (canonical 後継 SPEC あり)」「単独 close (重複 / 誤起票で user 承認済み)」のどちらかを判別し、集約 close なら最終コメントに集約先 SPEC 番号を必ず明記する

### 2026-05-13 (cargo test の並列実行は env race で flaky)

- Context: PR #205 で CI `Rust Tests` が `daemon::unityd::tests::write_pid_and_cleanup_use_temp_home` 等の race condition で flakily に fail。ローカル `cargo test --all-targets` でも `run_with_cli_set_active_text_output_when_reachable` / `execution_context_uses_defaults` が intermittent に fail (registry file の `EOF while parsing` / `trailing characters`)。
- Mistake: 過去 PR で `cargo llvm-cov ... -- --test-threads=1` を採用していたのに、`cargo test` 側 (CI workflow / pre-push hook) を並列のまま残し、env race を本質的に解消する手当てをしていなかった。`crate::test_env::env_lock()` で test code 内の env 操作は serialize できているが、Rust の `std::env::set_var` は process-global で thread-unsafe、library 内部の getenv 読み出しと衝突するため並列実行は本質的に安全でない。
- Rule: `cargo test` / `cargo llvm-cov` の呼び出しは CI / hook / ローカル品質ゲート全てで `-- --test-threads=1` を必須にする。新規 test を追加する際も、env を mutate する場合は `crate::test_env::env_lock()` を取得した上で、それでも並列実行に頼らない前提を守る。env を触る test を追加する際は「並列でも壊れない設計か」を考慮し、可能なら env 依存自体を public API への引数注入で除去する方向を優先する。
- Checkpoint: 1. CI workflow (`.github/workflows/test.yml`) の `cargo test` / `cargo llvm-cov` 行に `-- --test-threads=1` があるか確認 2. CLAUDE.md の品質ゲートも `-- --test-threads=1` 表記で揃っているか確認 3. 新規 test で `std::env::set_var` を使う場合は `env_lock` を取得し、PR description に「env を mutate するため並列実行禁止」と明記する
