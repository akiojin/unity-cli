# Development Guide

English | [日本語](#日本語)

This document covers internal development workflow for `unity-cli`.

## Core Stack

- CLI runtime: Rust (`src/`)
- Unity bridge package: `UnityCliBridge/Packages/unity-cli-bridge`
- Unity test project: `UnityCliBridge`
- C# LSP: `lsp/`
- Spec workflow: `.specify/` + `specs/`

## Prerequisites

| Tool | Version | Purpose |
| ------ | --------- | -------- |
| Rust toolchain (stable) | latest | CLI build and test |
| .NET SDK | 9.0 | LSP server build and test |
| Unity Editor | 2022.3+ | E2E tests (requires live connection) |
| Python + `tiktoken` | 3.9+ | LSP perf token measurement (`scripts/lsp-perf-check.sh`) |

### Installation

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 9
# https://dotnet.microsoft.com/download/dotnet/9.0
```

### Docker

Rust and .NET SDK 9 are included in the development Docker image.

```bash
# Build image
docker build -t unity-cli-dev .

# Run all tests (default)
docker run --rm unity-cli-dev

# Run individual tests
docker run --rm unity-cli-dev cargo test
docker run --rm unity-cli-dev dotnet test lsp/Server.Tests.csproj
```

## Configuration

`unity-cli` works with defaults, but these variables are recommended for CI and multi-instance setups.

| Env | Default | Notes |
| --- | ---: | --- |
| `UNITY_PROJECT_ROOT` | auto-detect | Directory containing `Assets/` and `Packages/` |
| `UNITY_CLI_HOST` | `localhost` | Unity TCP listener host |
| `UNITY_CLI_PORT` | `6400` | Unity TCP listener port |
| `UNITY_CLI_TIMEOUT_MS` | `30000` | Command timeout (ms) |
| `UNITY_CLI_LSP_MODE` | `off` | `off`, `auto`, `required` |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` | `600` | Daemon idle timeout (seconds) |
| `UNITY_CLI_TOOLS_ROOT` | platform default | Root directory for downloaded tools |

Minimal setup:

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=localhost
export UNITY_CLI_PORT=6400
```

Unity setting path: `Edit -> Project Settings -> Unity CLI Bridge`

- `Host`: bind/listen host
- `Port`: TCP port (must match `UNITY_CLI_PORT`)
- `Apply & Restart`: restarts Unity listener

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only.
`UNITY_CLI_UNITYD` has been removed; unityd is always auto-managed.

## Tool Invocation & Discovery

Use one of these paths:

1. Typed subcommands (common operations)
2. Raw tool calls (full coverage)

Typed examples:

- `unity-cli system ping`
- `unity-cli scene create MainScene`
- `unity-cli instances list`
- `unity-cli instances set-active --name "<instance>"`
- `unity-cli tool list`
- `unity-cli tool call <tool_name> --json '{...}'`
- `unity-cli unityd start` / `stop` / `status`
- `unity-cli batch --json '[{"tool":"ping","params":{}},{"tool":"get_editor_state","params":{}}]'`

Raw example:

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

Local (Rust-side) tools that do not require Unity TCP roundtrip:

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

Index workflow example:

```bash
unity-cli tool call build_index --json '{}'
unity-cli tool call find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
unity-cli tool call find_refs --json '{"name":"MyClass","pageSize":20}'
```

Tool catalog sources:

- Rust catalog: `src/tool_catalog.rs`
- Local tool implementation: `src/local_tools.rs`
- Snapshot list: `README.md` (`Full Capability Catalog (Snapshot)`)

## Local Commands

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets
cargo llvm-cov --all-targets --summary-only --fail-under-lines 90

# C# LSP
dotnet test lsp/Server.Tests.csproj
dotnet test lsp/Server.Tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:Threshold=90 /p:ThresholdType=line /p:ThresholdStat=total

# Unity (EditMode tests)
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

Rust coverage gate is enforced at repository level (all Rust targets, line >= 90%).

### Pre-push Hook

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

`git push` will automatically run `cargo test` and `dotnet test`.

## TDD Flow

1. Write failing tests (RED)
2. Implement minimum change (GREEN)
3. Refactor while tests stay green

Keep test-first commit order whenever possible.

## E2E Tests

E2E tests require a running Unity Editor with TCP server active.

### Preparation

1. Open the Unity project in Unity Editor
2. Ensure the UnityCliBridge package is installed
3. Enter Play mode and verify the TCP server is running

### Execution

```bash
# Build
cargo build --release

# Smoke E2E (default 127.0.0.1:6400)
scripts/e2e-test.sh

# Full coverage E2E (all tools + LSP perf check)
scripts/e2e-all-tools.sh

# Full coverage E2E with custom host/port
scripts/e2e-all-tools.sh --host 192.168.1.10 --port 9090

# LSP perf check only (full case set + size/token metrics + history append)
scripts/lsp-perf-check.sh
```

### Test Scenarios

| Scenario | Command | Verification |
| ---------- | --------- | ------------- |
| system ping | `unity-cli system ping` | TCP connectivity check |
| raw create_scene | `unity-cli raw create_scene --json '{"sceneName":"E2ETest_YYYYmmdd-HHMMSS","path":"Assets/Scenes/Generated/E2E/"}'` | Scene creation |
| tool list | `unity-cli tool list` | Tool listing |

Generated E2E scenes are created under `UnityCliBridge/Assets/Scenes/Generated/E2E/` and are ignored by Git.

Logs on failure are saved to `/tmp/unity-cli-e2e-*.log`.

### Scene Layout Policy

- Stable tracked scenes stay in `UnityCliBridge/Assets/Scenes/` (`SampleScene` only).
- Generated E2E scenes are created in `UnityCliBridge/Assets/Scenes/Generated/E2E/` and must not be committed.
- UI manual test scenes (UGUI/UITK/IMGUI) are generated on demand via `Tools/Unity CLI/UI Tests/*` into `UnityCliBridge/Assets/Scenes/Generated/UI/`.

## Troubleshooting

### Quick Checks

1. Unity Editor is running.
2. `Unity CLI Bridge` package is installed.
3. Unity TCP listener is active (default `6400`).
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` points to that listener.

### Connection Issues

| Symptom | Cause | Fix |
| --- | --- | --- |
| `Connection timeout` | Unity not running | Start Unity Editor |
| `ECONNREFUSED` | Listener not active / wrong port | Reopen Unity project settings and restart listener |
| `invalid response` | Protocol mismatch or stale build | Reimport package and restart Unity |

### LSP Issues

| Symptom | Fix |
| --- | --- |
| LSP command not found | Run `unity-cli lsp install` and retry |
| LSP timeout | Increase `UNITY_CLI_TIMEOUT_MS` and retry |
| LSP required but unavailable | Use `UNITY_CLI_LSP_MODE=auto` during setup |

### WSL2/Docker -> Windows Unity

```bash
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
export UNITY_PROJECT_ROOT=/absolute/path/to/UnityCliBridge
```

### `Capabilities: none`

`unity-cli` is a CLI, not an MCP server.  
If a client still expects MCP capabilities directly, remove legacy MCP launch settings and configure command execution to call `unity-cli`.

Verification:

```bash
unity-cli --output json system ping
echo "$UNITY_CLI_HOST:$UNITY_CLI_PORT"
```

## CI Overview

CI is defined in `.github/workflows/lint.yml`, `.github/workflows/test.yml`, and `.github/workflows/skill-routing-eval.yml`.

| Job | Trigger | Description |
| ----- | --------- | ------------- |
| Skill Contract Check (required) | push / PR | `scripts/skill-eval/static-skill-contract-check.sh` |
| Rust Tests (required) | push / PR | `cargo test` |
| LSP Tests (required) | push / PR | `dotnet test lsp/Server.Tests.csproj` |
| LSP Performance (required) | push / PR | `scripts/lsp-perf-check.sh` (full cases + history artifact) |
| Skill Routing Eval | daily schedule / manual | `scripts/skill-eval/llm-routing-eval.sh` (`.github/workflows/skill-routing-eval.yml`) |
| Unity E2E Tests | manual (`workflow_dispatch`) | E2E test script |

Skill Contract Check, Rust Tests, LSP Tests, and LSP Performance are required checks for PR merges.
E2E Tests are manual-only and require a runner with Unity Editor.

## Capability Catalog

The full current capability list (typed command groups + Unity Tool APIs) is maintained in `README.md` under:

- `Full Capability Catalog (Snapshot)`

Regenerate command examples:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```

## Benchmark Policy

### Baseline Targets (Reference)

These are guidance values and vary by host:

| Scenario | Mean (target) | Notes |
| --- | --- | --- |
| `unity-cli --help` | ~2-5 ms | Local startup only |
| `unity-cli tool list` | ~2-5 ms | Local list generation |
| `unity-cli system ping` | ~10-50 ms | Requires running Unity Editor |
| `unity-cli system ping` (via unityd) | ~5-20 ms | Daemon keeps TCP connection open |
| `unity-cli batch` (5 commands) | ~25-100 ms | Single IPC round-trip via daemon |

### Run

```bash
# human-readable
./scripts/benchmark.sh

# JSON for CI/storage
./scripts/benchmark.sh --json

# LSP perf measurement with thresholds + size/token metrics
./scripts/lsp-perf-check.sh

# Stored history file
cat specs/perf/lsp-history.jsonl | tail -n 5
```

Regression policy:

1. Track JSON outputs over time.
2. Keep `specs/perf/lsp-history.jsonl` as append-only history.
3. Use recorded trends as baseline comparison input.
4. Exclude `system ping` from strict regression gate (depends on Unity availability and machine/network state).

## Skill Accuracy Evaluation

Benchmark and history files:

- `specs/perf/skill-routing-benchmark.jsonl` (routing benchmark: 120 cases)
- `specs/perf/skill-routing-history.jsonl` (append-only eval history)
- `specs/perf/skill-static-report.json` (latest static contract report)

Run static validation (required in PR CI):

```bash
./scripts/skill-eval/static-skill-contract-check.sh
```

Run routing eval with predictions:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model local-debug \
  --predictions /path/to/predictions.jsonl
```

Run routing eval with an external runner command:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model nightly \
  --runner-cmd '<your-runner-command>'
```

Current thresholds:

- `top1 >= 0.90`
- `top2 >= 0.98`
- `tool_correct >= 0.92`
- `payload_valid >= 0.95`

## Speckit Upgrade Runbook

When upgrading GitHub Spec Kit project files:

1. Fetch upstream snapshot:

```bash
bash scripts/upgrade-speckit.sh --tag <TAG>
```

1. Compare and selectively apply:

```bash
git diff --no-index .specify/templates .specify/upstream/spec-kit-<TAG>/templates
git diff --no-index .specify/scripts/bash .specify/upstream/spec-kit-<TAG>/scripts/bash
```

1. Preserve repository invariants:

- Japanese localization for `.claude/commands/speckit.*.md` and `.specify/templates/*`
- No branch/worktree creation in Speckit flow
- Spec ID format: `specs/SPEC-[UUID8]`
- `specs/specs.md` generation remains enabled

1. Validate:

```bash
bash -n .specify/scripts/bash/*.sh
bash .specify/scripts/bash/update-specs-readme.sh --quiet
bash .specify/scripts/checks/check-speckit-commands.sh
bash scripts/sync-constitution.sh --check
```

## Release Flow

1. Update versions: `node scripts/release/update-versions.mjs <X.Y.Z>`
2. Tag: `vX.Y.Z`
3. Push tag and run `.github/workflows/unity-cli-release.yml`
4. Publish crate: `cargo publish`

Detailed steps: `RELEASE.md`.

## Spec Kit

- Source of truth: `docs/constitution.md`
- Mirror for Spec Kit: `.specify/memory/constitution.md`
- Spec generation:
  - `/speckit.specify`
  - `/speckit.plan`
  - `/speckit.tasks`

## Documentation Consistency Checks

Periodically verify that specs and docs match the current implementation.

### Check Targets

| Directory / File | Contents |
| ------------------ | ---------- |
| `specs/` | Design specs (architecture, migration notes) |
| `docs/` | Development guide and constitution |
| `README.md` | Project overview |
| `UnityCliBridge/Packages/unity-cli-bridge/README.md` | UPM package docs (EN) |
| `UnityCliBridge/Packages/unity-cli-bridge/README.ja.md` | UPM package docs (JA) |

### Check Procedure

1. **Legacy name residuals**: Search for unintentional `MCP` or old project name references.

   ```bash
   grep -rni "mcp" specs/ docs/ README.md | grep -v migration-notes.md
   ```

   - `specs/migration-notes.md` intentionally contains old names for migration/deprecation documentation.

2. **Environment variable consistency**: Compare the variables listed in this document with `src/config.rs`.

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/development.md | sort -u
   ```

3. **Source file structure**: Verify `specs/architecture.md` file list matches actual sources.

   ```bash
   ls src/*.rs
   ```

4. **Command list**: Check `README.md` Command Overview against `src/cli.rs` subcommand definitions.

### When to Check

- Before merging PRs that add features or change design
- After changes to environment variables or command structure
- As a release checklist item

## Baseline Policy

The Unity-side codebase uses `unity-mcp-server` as its base copy. Differences are limited to changes required for the MCP → CLI migration. For the full policy and diff inventory, see [`specs/SPEC-83d9d7ee/baseline-policy.md`](../specs/SPEC-83d9d7ee/baseline-policy.md).

---

## 日本語

このドキュメントは `unity-cli` の内部開発フローをまとめたものです。

## コア構成

- CLI本体: Rust (`src/`)
- Unity連携パッケージ: `UnityCliBridge/Packages/unity-cli-bridge`
- Unityテストプロジェクト: `UnityCliBridge`
- C# LSP: `lsp/`
- Specワークフロー: `.specify/` + `specs/`

## 前提条件

| ツール | バージョン | 用途 |
| -------- | ----------- | ------ |
| Rust toolchain (stable) | latest | CLI 本体のビルド・テスト |
| .NET SDK | 9.0 | LSP サーバーのビルド・テスト |
| Unity Editor | 2022.3+ | E2E テスト (実機接続が必要) |
| Python + `tiktoken` | 3.9+ | LSP 性能計測時のトークン算出（`scripts/lsp-perf-check.sh`） |

### インストール

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 9
# https://dotnet.microsoft.com/download/dotnet/9.0 からダウンロード
```

### Docker を使う場合

Rust と .NET SDK 9 が同梱された開発用 Docker イメージを利用できます。

```bash
# イメージをビルド
docker build -t unity-cli-dev .

# 全テストを実行 (デフォルト)
docker run --rm unity-cli-dev

# 個別テスト
docker run --rm unity-cli-dev cargo test
docker run --rm unity-cli-dev dotnet test lsp/Server.Tests.csproj
```

## 設定

`unity-cli` はデフォルトでも動作しますが、CI や複数インスタンス運用では以下の環境変数の利用を推奨します。

| 環境変数 | デフォルト | 補足 |
| --- | ---: | --- |
| `UNITY_PROJECT_ROOT` | 自動検出 | `Assets/` と `Packages/` を含むディレクトリ |
| `UNITY_CLI_HOST` | `localhost` | Unity TCP リスナーのホスト |
| `UNITY_CLI_PORT` | `6400` | Unity TCP リスナーのポート |
| `UNITY_CLI_TIMEOUT_MS` | `30000` | コマンドタイムアウト (ms) |
| `UNITY_CLI_LSP_MODE` | `off` | `off`, `auto`, `required` |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` | `600` | デーモンアイドルタイムアウト（秒） |
| `UNITY_CLI_TOOLS_ROOT` | OS依存既定 | ツール配置ルート |

最小設定:

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=localhost
export UNITY_CLI_PORT=6400
```

Unity 側設定: `Edit -> Project Settings -> Unity CLI Bridge`

- `Host`: 待受ホスト
- `Port`: TCP ポート（`UNITY_CLI_PORT` と一致させる）
- `Apply & Restart`: Unity 側リスナー再起動

旧 MCP プレフィックス環境変数は未サポートです。`UNITY_CLI_*` のみ使用してください。
`UNITY_CLI_UNITYD` は廃止済みで、unityd は常時自動管理です。

## ツール呼び出しと探索

呼び出し経路は2種類です。

1. typed サブコマンド（主要操作）
2. raw 呼び出し（全コマンドカバー）

typed 例:

- `unity-cli system ping`
- `unity-cli scene create MainScene`
- `unity-cli instances list`
- `unity-cli instances set-active --name "<instance>"`
- `unity-cli tool list`
- `unity-cli tool call <tool_name> --json '{...}'`
- `unity-cli unityd start` / `stop` / `status`
- `unity-cli batch --json '[{"tool":"ping","params":{}},{"tool":"get_editor_state","params":{}}]'`

raw 例:

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

Unity TCP を介さずローカル実行される Rust 側ツール:

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

インデックス運用例:

```bash
unity-cli tool call build_index --json '{}'
unity-cli tool call find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
unity-cli tool call find_refs --json '{"name":"MyClass","pageSize":20}'
```

参照先:

- Rustツールカタログ: `src/tool_catalog.rs`
- ローカルツール実装: `src/local_tools.rs`
- スナップショット一覧: `README.md`（`Full Capability Catalog (Snapshot)`）

## ローカル実行コマンド

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity（EditModeテスト）
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode.xml -quit
```

### プッシュ前フック

```bash
chmod +x .husky/pre-push
git config core.hooksPath .husky
```

`git push` 時に自動で `cargo test` と `dotnet test` が実行されます。

## TDDフロー

1. 失敗するテストを先に作成（RED）
2. 最小実装で通す（GREEN）
3. テストを維持したまま整理（REFACTOR）

## E2E テスト

E2E テストは Unity Editor が起動している環境で実行します。

### 準備

1. Unity Editor でプロジェクトを開く
2. UnityCliBridge パッケージが導入されていることを確認する
3. Play モードに入り、TCP サーバーが起動していることを確認する

### 実行

```bash
# ビルド
cargo build --release

# スモークE2E（デフォルト 127.0.0.1:6400）
scripts/e2e-test.sh

# 全機能E2E（全ツール + LSP性能チェック）
scripts/e2e-all-tools.sh

# 全機能E2E（ホスト・ポート指定）
scripts/e2e-all-tools.sh --host 192.168.1.10 --port 9090

# LSP性能チェックのみ（全ケース + サイズ/トークン計測 + 履歴追記）
scripts/lsp-perf-check.sh
```

### テストシナリオ

| シナリオ | コマンド | 確認内容 |
| ---------- | --------- | --------- |
| system ping | `unity-cli system ping` | Unity Editor との疎通確認 |
| raw create_scene | `unity-cli raw create_scene --json '{"sceneName":"E2ETest_YYYYmmdd-HHMMSS","path":"Assets/Scenes/Generated/E2E/"}'` | シーン作成の実行確認 |
| tool list | `unity-cli tool list` | ツール一覧の取得確認 |

E2E で生成されるシーンは `UnityCliBridge/Assets/Scenes/Generated/E2E/` 配下に作成され、Git 追跡対象外です。

失敗時はログが `/tmp/unity-cli-e2e-*.log` に保存されます。

### シーン配置ポリシー

- `UnityCliBridge/Assets/Scenes/` 直下は固定で追跡するシーン（`SampleScene` のみ）を配置する
- E2E 生成シーンは `UnityCliBridge/Assets/Scenes/Generated/E2E/` に作成し、コミットしない
- UI 手動検証シーン（UGUI/UITK/IMGUI）は `Tools/Unity CLI/UI Tests/*` で必要時に `UnityCliBridge/Assets/Scenes/Generated/UI/` へ生成する

## トラブルシューティング

### まず確認

1. Unity Editor が起動していること
2. `Unity CLI Bridge` パッケージが導入されていること
3. Unity TCP リスナーが起動していること（デフォルト `6400`）
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` が一致していること

### 接続エラー

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| `Connection timeout` | Unity未起動 | Unity Editorを起動 |
| `ECONNREFUSED` | リスナー未起動 / ポート不一致 | Project Settingsで再起動 |
| `invalid response` | プロトコル不一致 / 古いビルド | パッケージ再import後にUnity再起動 |

### LSP関連

| 症状 | 対処 |
| --- | --- |
| LSP実行ファイルが見つからない | `unity-cli lsp install` を実行して再試行 |
| LSPタイムアウト | `UNITY_CLI_TIMEOUT_MS` を延長 |
| 必須LSPモードで失敗 | セットアップ中は `UNITY_CLI_LSP_MODE=auto` を利用 |

### WSL2/Docker -> Windows Unity

```bash
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
export UNITY_PROJECT_ROOT=/absolute/path/to/UnityCliBridge
```

### `Capabilities: none`

`unity-cli` は MCP サーバーではなく CLI です。  
クライアントが MCP capabilities を直接期待している場合は、旧 MCP 起動設定を削除し、コマンド実行先を `unity-cli` に切り替えてください。

確認:

```bash
unity-cli --output json system ping
echo "$UNITY_CLI_HOST:$UNITY_CLI_PORT"
```

## CI の概要

CI は `.github/workflows/lint.yml` / `.github/workflows/test.yml` / `.github/workflows/skill-routing-eval.yml` で定義されています。

| ジョブ | トリガー | 内容 |
| -------- | --------- | ------ |
| Skill Contract Check (required) | push / PR | `scripts/skill-eval/static-skill-contract-check.sh` |
| Rust Tests (required) | push / PR | `cargo test` |
| LSP Tests (required) | push / PR | `dotnet test lsp/Server.Tests.csproj` |
| LSP Performance (required) | push / PR | `scripts/lsp-perf-check.sh`（全ケース実行 + 履歴artifact） |
| Skill Routing Eval | 毎日スケジュール / 手動 | `scripts/skill-eval/llm-routing-eval.sh`（`.github/workflows/skill-routing-eval.yml`） |
| Unity E2E Tests | 手動 (`workflow_dispatch`) | E2E テストスクリプトの実行 |

Skill Contract Check / Rust Tests / LSP Tests / LSP Performance は PR マージの必須チェックです。
E2E Tests は手動トリガーのみで、Unity Editor が起動しているランナーが必要です。

## 機能カタログ

最新の全機能一覧（typed コマンド群 + Unity Tool API 一覧）は `README.md` の以下を正本とします。

- `Full Capability Catalog (Snapshot)`

再生成コマンド例:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```

## ベンチマーク方針

### 目安値（参考）

環境依存ですが、目安は次のとおりです。

| シナリオ | 平均（目安） | 備考 |
| --- | --- | --- |
| `unity-cli --help` | ~2-5 ms | ローカル起動時間のみ |
| `unity-cli tool list` | ~2-5 ms | ローカル一覧生成 |
| `unity-cli system ping` | ~10-50 ms | Unity Editor 起動時のみ |
| `unity-cli system ping` (unityd経由) | ~5-20 ms | デーモンがTCP接続を保持 |
| `unity-cli batch` (5コマンド) | ~25-100 ms | デーモン経由の単一IPCラウンドトリップ |

### 実行

```bash
# 人間向け
./scripts/benchmark.sh

# CI・保存向けJSON
./scripts/benchmark.sh --json

# LSP性能計測 + 閾値チェック + サイズ/トークン計測
./scripts/lsp-perf-check.sh

# 保存済み履歴の確認
cat specs/perf/lsp-history.jsonl | tail -n 5
```

回帰判定方針:

1. JSON 結果を継続保存する
2. `specs/perf/lsp-history.jsonl` を追記履歴として維持する
3. 履歴トレンドをベースライン比較に利用する
4. `system ping` は Unity の可用性に依存するため厳密ゲートには含めない

## スキル精度評価

ベンチマーク・履歴ファイル:

- `specs/perf/skill-routing-benchmark.jsonl`（ルーティング評価ベンチマーク: 120ケース）
- `specs/perf/skill-routing-history.jsonl`（追記専用の評価履歴）
- `specs/perf/skill-static-report.json`（最新の静的契約チェック結果）

静的検証（PR CI 必須）:

```bash
./scripts/skill-eval/static-skill-contract-check.sh
```

予測JSONを使ったルーティング評価:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model local-debug \
  --predictions /path/to/predictions.jsonl
```

外部ランナーコマンドを使ったルーティング評価:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model nightly \
  --runner-cmd '<your-runner-command>'
```

現在の閾値:

- `top1 >= 0.90`
- `top2 >= 0.98`
- `tool_correct >= 0.92`
- `payload_valid >= 0.95`

## Speckit 更新手順（要約）

GitHub Spec Kit の上流更新を取り込むときは次を実施します。

1. 上流スナップショット取得:

```bash
bash scripts/upgrade-speckit.sh --tag <TAG>
```

1. 差分確認:

```bash
git diff --no-index .specify/templates .specify/upstream/spec-kit-<TAG>/templates
git diff --no-index .specify/scripts/bash .specify/upstream/spec-kit-<TAG>/scripts/bash
```

1. 不変条件を維持:

- `.claude/commands/speckit.*.md` と `.specify/templates/*` の日本語維持
- Speckit フローでブランチ/worktree を作成しない
- Spec ID 形式: `specs/SPEC-[UUID8]`
- `specs/specs.md` 自動生成を維持

1. 検証:

```bash
bash -n .specify/scripts/bash/*.sh
bash .specify/scripts/bash/update-specs-readme.sh --quiet
bash .specify/scripts/checks/check-speckit-commands.sh
bash scripts/sync-constitution.sh --check
```

## リリースフロー

1. `node scripts/release/update-versions.mjs <X.Y.Z>` でバージョン同期
2. `vX.Y.Z` タグ作成
3. `.github/workflows/unity-cli-release.yml` でバイナリ公開
4. `cargo publish` で crates.io 公開

詳細は `RELEASE.md` を参照してください。

## ドキュメント整合チェック

仕様書・ドキュメントが現行の実装と矛盾していないことを定期的に確認します。

### チェック対象

| ディレクトリ / ファイル | 内容 |
| ------------------------ | ------ |
| `specs/` | 設計仕様書（アーキテクチャ、移行記録） |
| `docs/` | 開発ガイドと憲章 |
| `README.md` | プロジェクト概要 |
| `UnityCliBridge/Packages/unity-cli-bridge/README.md` | UPM パッケージドキュメント（英語） |
| `UnityCliBridge/Packages/unity-cli-bridge/README.ja.md` | UPM パッケージドキュメント（日本語） |

### チェック手順

1. **旧名称の残留確認**: 以下のコマンドで `MCP` や旧プロジェクト名の残留をチェックします。

   ```bash
   grep -rni "mcp" specs/ docs/ README.md | grep -v migration-notes.md
   ```

2. **環境変数の整合確認**: 本ドキュメントの変数一覧と `src/config.rs` の実装を比較します。

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/development.md | sort -u
   ```

3. **ソースファイル構成の確認**: `specs/architecture.md` のソースファイル一覧と実際のファイルを比較します。

   ```bash
   ls src/*.rs
   ```

4. **コマンド一覧の確認**: `README.md` の Command Overview セクションが `src/cli.rs` のサブコマンド定義と一致しているか確認します。

### チェックのタイミング

- 新機能追加・設計変更を含む PR をマージする前
- 環境変数やコマンド体系に変更があった場合
- リリース前のチェックリストの一項目として

## ベースライン方針

Unity 側コードベースは `unity-mcp-server` をベースコピーとし、差分は MCP→CLI 移行に必要な変更に限定します。方針の全文と差分一覧は [`specs/SPEC-83d9d7ee/baseline-policy.md`](../specs/SPEC-83d9d7ee/baseline-policy.md) を参照してください。
