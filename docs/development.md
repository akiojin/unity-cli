# Development Guide

English | [日本語](#日本語)

This document covers internal development workflow for `unity-cli`.

## Core Stack

- CLI runtime: Rust (`src/`)
- Unity bridge package: `UnityCliBridge/Packages/unity-cli-bridge`
- Unity test project: `UnityCliBridge`
- C# LSP: `lsp/`
- Spec workflow: GitHub Issue-first (`gwt-spec`) only

## Prerequisites

| Tool                    | Version               | Purpose                                                  |
| ----------------------- | --------------------- | -------------------------------------------------------- |
| Rust toolchain (stable) | latest                | CLI build and test                                       |
| .NET SDK                | 10.0                  | LSP server build and test                                |
| Unity Editor            | 2022.3 LTS or Unity 6 | E2E tests (requires live connection)                     |
| Python + `tiktoken`     | 3.9+                  | LSP perf token measurement (`scripts/lsp-perf-check.sh`) |

### Installation

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 10
# https://dotnet.microsoft.com/download/dotnet/10.0
```

### Docker

Rust and .NET SDK 10 are included in the development Docker image.

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

| Env                             |          Default | Notes                                          |
| ------------------------------- | ---------------: | ---------------------------------------------- |
| `UNITY_PROJECT_ROOT`            |      auto-detect | Directory containing `Assets/` and `Packages/` |
| `UNITY_CLI_HOST`                |      `localhost` | Unity TCP listener host                        |
| `UNITY_CLI_PORT`                |           `6400` | Unity TCP listener port                        |
| `UNITY_CLI_TIMEOUT_MS`          |          `30000` | Command timeout (ms)                           |
| `UNITY_CLI_LSP_MODE`            |            `off` | `off`, `auto`, `required`                      |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` |            `600` | Daemon idle timeout (seconds)                  |
| `UNITY_CLI_TOOLS_ROOT`          | platform default | Root directory for downloaded tools            |

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

For Docker, WSL2, and multi-instance examples, see
[`docs/configuration.md`](configuration.md).

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only.
`UNITY_CLI_UNITYD` has been removed; unityd is always auto-managed.

Remote typed, raw and batch commands start unityd on demand when its IPC endpoint
is unavailable, including after the idle timeout (600 seconds by default;
`UNITY_CLI_UNITYD_IDLE_TIMEOUT` overrides it). Automatic startup uses the invoking
CLI binary and serializes concurrent starts. `unityd stop` stops the current
process; the next remote operation starts it again. `unityd status` does not
start it. Host/port resolution is unchanged, and connections are pooled by both.
If startup fails, stderr explains the failure and direct TCP fallback.
Use `-v` to inspect `route`, `startup_ms` and `operation_ms` separately on stderr;
JSON results stay on stdout. `unityd status` reports the PID and connection count.

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
- `unity-cli tool schema <tool_name> --output json`
- `unity-cli tool call <tool_name> --json '{...}'`
- `unity-cli --dry-run tool call <tool_name> --json '{...}'`
- `unity-cli unityd start` / `stop` / `status`
- `unity-cli batch --json '[{"tool":"ping","params":{}},{"tool":"get_editor_state","params":{}}]'`

Raw example:

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

Parameter validation is strict by default for tools with explicit schemas.
Unknown keys, missing required fields, and type mismatches are rejected before execution.
`oneOf` / `anyOf` constraints are also enforced (e.g. `load_scene`, `delete_gameobject`, `input_keyboard`).
For action-based tools, required fields are validated per action variant (e.g. `package_manager` search requires `keyword`, `manage_layers` add requires `layerName`).

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

- Rust catalog: `src/tooling/tool_catalog.rs`
- Local tool implementation: `src/tooling/local_tools.rs`
- Snapshot list: `docs/tools.md` (`Tool Catalog`)

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
./scripts/switch-unity-manifest.sh 6
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode-unity6.xml

./scripts/switch-unity-manifest.sh 2022
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode-unity2022.xml
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

## Local Unity E2E

Unity E2E is not executed in CI. Use it only for local validation against a running Unity Editor with the TCP listener active.

### Preparation

1. Open `UnityCliBridge` in Unity Editor.
2. Ensure the Unity CLI Bridge package is loaded.
3. Confirm the listener is active on `UNITY_CLI_HOST` / `UNITY_CLI_PORT` (default `127.0.0.1:6400`).

### Execution

```bash
# Build
cargo build --release

# Smoke E2E
scripts/e2e-test.sh

# Reference fetch regression (live Editor; isolated cache; downloads UnityCsReference)
# Run cargo build first, or set UNITY_CLI_BIN to the binary under test.
scripts/e2e-reference-fetch.sh --port 6400

# Player build: relevant EditMode tests, real build/report, polling, failures,
# restart recovery, and headless launch (isolated project copy on macOS)
python3 scripts/e2e-player-build.py --launch --editmode

# Deterministic input simulation E2E
scripts/e2e-input-tools.sh

# C# eval E2E against an existing listener (see editor-eval.md)
scripts/e2e-eval.sh --unity-cli "$PWD/target/debug/unity-cli"

# C# eval with an isolated batch host (build the debug CLI first)
scripts/e2e-input-batch-host.sh --suite eval --port 6402 --unity-cli "$PWD/target/debug/unity-cli"

# Timeline editing, persistence and Animator evaluation (real Editor)
cargo build --bin unity-cli
scripts/e2e-timeline-batch-host.sh --port 6474

# Isolated project without com.unity.timeline: compile/start and error contract
scripts/e2e-timeline-batch-host.sh --port 6475 --without-timeline

# Test-result counting regression (7 EditMode + 2 PlayMode tests)
# Requires the project's default DisableDomainReload setting and a running listener.
scripts/e2e-test-results.sh

# Headless batch-host input simulation E2E
scripts/e2e-input-batch-host.sh

# Recommended local path when no Unity GUI listener is already running
scripts/e2e-input-batch-host.sh --port 6402

# The batch-host helper uses the editor version in ProjectVersion.txt by default
# and only needs UNITY_PATH when that editor is not installed locally.

# Full local E2E sweep
scripts/e2e-all-tools.sh

# Full local E2E sweep with custom host/port
scripts/e2e-all-tools.sh --host 192.168.1.10 --port 9090

# Media / Profiler benchmark artifacts under UnityCliBridge/.unity/perf-media/
scripts/perf-media-benchmark.sh

# Recording formats: MP4, WebM and PNG sequence (graphics-enabled Editor in Play mode)
scripts/e2e-video-formats.sh --port 6400
```

### Scene Layout Policy

Timeline E2E uses `Assets/Scenes/Generated/E2E/Timeline/` for its fixture
scene and assets. The suite checks clip timing, binding, asset unload/reimport
and scene reload, numeric Animator evaluation, edits/removal, and rejected
operations. The batch helper preserves Editor and test logs in the printed
`/tmp/unity-cli-timeline-e2e.*` directory. The package-absent variant copies
the Bridge into a temporary project; it never removes the working project's
Timeline dependency. Recorder is also excluded from the isolated project because
it depends on Timeline; the runner asserts neither package is in the resolved
package lock. Set `UNITY_PATH` or `UNITY_CLI` to override binaries.
Against an existing listener, run `scripts/e2e-timeline.sh --port <port>`;
save unrelated dirty scenes first because the fixture opens its own scene.

- Stable tracked scenes stay in `UnityCliBridge/Assets/Scenes/` (`SampleScene` only).
- Local E2E-generated scenes go under `UnityCliBridge/Assets/Scenes/Generated/E2E/` and must not be committed.
- UI manual test scenes continue to use `UnityCliBridge/Assets/Scenes/Generated/UI/`.

### Media Perf Benchmark

`scripts/e2e-video-formats.sh` checks actual file headers and session cleanup. It controls Play mode and restores its initial state; use a dedicated local Editor with a camera scene loaded. For a batch host, omit `-nographics` so Game View can render. Video start returns the planned output path; successful stop returns an existing nonempty file. For `png_sequence`, this is the first PNG in a unique session directory.

- `scripts/lsp-perf-check.sh` is still the canonical benchmark for LSP/search/index performance. `scripts/perf-media-benchmark.sh` is a separate runtime benchmark for screenshot/video/profiler flows.
- `scripts/perf-media-benchmark.sh` generates `Assets/Scenes/Generated/E2E/Performance/UnityCli_PerfBenchmark.unity` via `Tools/Unity CLI/Performance/Generate Media Perf Scene`.
- Benchmark outputs are written under `UnityCliBridge/.unity/perf-media/<timestamp>/`.
- The script captures:
  - `get_command_stats`
  - `profiler_start` / `profiler_stop`
  - `capture_screenshot`
  - `capture_video_start` / `capture_video_status` / `capture_video_stop`
- Review `summary.md` and `result.json` in the artifact directory for CLI latency, bridge timing deltas, and profiler metrics.

## Troubleshooting

### Quick Checks

1. Unity Editor is running.
2. `Unity CLI Bridge` package is installed.
3. Unity TCP listener is active (default `6400`).
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` points to that listener.

### Connection Issues

| Symptom              | Cause                            | Fix                                                |
| -------------------- | -------------------------------- | -------------------------------------------------- |
| `Connection timeout` | Unity not running                | Start Unity Editor                                 |
| `ECONNREFUSED`       | Listener not active / wrong port | Reopen Unity project settings and restart listener |
| `invalid response`   | Protocol mismatch or stale build | Reimport package and restart Unity                 |

### LSP Issues

| Symptom                      | Fix                                        |
| ---------------------------- | ------------------------------------------ |
| LSP command not found        | Run `unity-cli lsp install` and retry      |
| LSP timeout                  | Increase `UNITY_CLI_TIMEOUT_MS` and retry  |
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

| Job                            | Trigger                 | Description                                                                           |
| ------------------------------ | ----------------------- | ------------------------------------------------------------------------------------- |
| Skill Contract Lint (required) | push / PR               | `cargo run -- skills lint --severity error` (Skill Contract v1; SPEC #160)            |
| Rust Tests (required)          | push / PR               | `cargo test`                                                                          |
| LSP Tests (required)           | push / PR               | `dotnet test lsp/Server.Tests.csproj`                                                 |
| LSP Performance (required)     | push / PR               | `scripts/lsp-perf-check.sh` (full cases + history artifact)                           |
| Skill Routing Eval             | daily schedule / manual | `scripts/skill-eval/llm-routing-eval.sh` (`.github/workflows/skill-routing-eval.yml`) |

Skill Contract Check, Rust Tests, LSP Tests, and LSP Performance are required checks for PR merges.

## Capability Catalog

The full current capability list (typed command groups + Unity Tool APIs) is maintained in `docs/tools.md` under:

- `Tool Catalog`

Regenerate command examples:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```

## Benchmark Policy

### Baseline Targets (Reference)

These are guidance values and vary by host:

| Scenario                             | Mean (target) | Notes                            |
| ------------------------------------ | ------------- | -------------------------------- |
| `unity-cli --help`                   | ~2-5 ms       | Local startup only               |
| `unity-cli tool list`                | ~2-5 ms       | Local list generation            |
| `unity-cli system ping`              | ~10-50 ms     | Requires running Unity Editor    |
| `unity-cli system ping` (via unityd) | ~5-20 ms      | Daemon keeps TCP connection open |
| `unity-cli batch` (5 commands)       | ~25-100 ms    | Single IPC round-trip via daemon |

### Run

```bash
# human-readable
./scripts/benchmark.sh

# JSON for CI/storage
./scripts/benchmark.sh --json

# LSP perf measurement with thresholds + size/token metrics
./scripts/lsp-perf-check.sh

# Media capture / profiler benchmark against a connected Unity Editor
# This is separate from lsp-perf-check.sh and does not append to lsp-history.jsonl.
./scripts/perf-media-benchmark.sh

# Stored history file
cat .unity/perf/lsp-history.jsonl | tail -n 5
```

Regression policy:

1. Track JSON outputs over time.
2. Keep `.unity/perf/lsp-history.jsonl` as append-only history.
3. Use recorded trends as baseline comparison input.
4. Exclude `system ping` from strict regression gate (depends on Unity availability and machine/network state).
5. Use `scripts/perf-media-benchmark.sh` only for runtime screenshot / video / profiler evidence alongside `get_command_stats`; it is not part of the LSP history pipeline.

## Skill Accuracy Evaluation

Benchmark and history files:

- `tests/fixtures/skill-routing/benchmark.jsonl` (routing benchmark: 127 cases)
- `.unity/skill-eval/skill-routing-history.jsonl` (append-only eval history)
- `.unity/skill-eval/skill-static-report.json` (latest static contract report)

Run static validation (required in PR CI):

```bash
cargo run -- skills lint --severity error
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

Run a local Codex-based routing check:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model codex-local \
  --runner-cmd 'python3 scripts/skill-eval/run-codex-routing.py'
```

This local runner requires `codex login` to be configured on the machine.

Current thresholds:

- `top1 >= 0.90`
- `top2 >= 0.98`
- `tool_correct >= 0.92`
- `payload_valid >= 0.95`

## Release Flow

1. Run `./scripts/publish.sh <major|minor|patch>`
2. Confirm the new `vX.Y.Z` tag was pushed
3. Verify `.github/workflows/release.yml` uploaded the release binaries
4. Verify the crate is available on crates.io

Detailed steps: `RELEASE.md`.

## Documentation Consistency Checks

Periodically verify that docs and issue-first workflow references match the current implementation.

### Check Targets

| Directory / File                                        | Contents                           |
| ------------------------------------------------------- | ---------------------------------- |
| `docs/architecture.md`                                  | Architecture overview              |
| `docs/migration-notes.md`                               | Migration and deprecation notes    |
| `docs/`                                                 | Development guide and constitution |
| `README.md`                                             | Project overview                   |
| `UnityCliBridge/Packages/unity-cli-bridge/README.md`    | UPM package docs (EN)              |
| `UnityCliBridge/Packages/unity-cli-bridge/README.ja.md` | UPM package docs (JA)              |

### Check Procedure

1. **Legacy name residuals**: Search for unintentional `MCP` or old project name references.

   ```bash
   grep -rni "mcp" docs/ README.md | grep -v migration-notes.md
   ```

   - `docs/migration-notes.md` intentionally contains old names for migration/deprecation documentation.

2. **Environment variable consistency**: Compare the variables listed in this document with `src/core/config.rs`.

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/core/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/development.md | sort -u
   ```

3. **Source file structure**: Verify `docs/architecture.md` file list matches actual sources.

   ```bash
   ls src/*.rs
   ```

4. **Command list**: Check `README.md` Command Overview against `src/cli.rs` subcommand definitions.

### When to Check

- Before merging PRs that add features or change design
- After changes to environment variables or command structure
- As a release checklist item

## Baseline Policy

The Unity-side codebase uses `unity-mcp-server` as its base copy. Differences are limited to changes required for the MCP → CLI migration. For migration policy and diff notes, see [`docs/migration-notes.md`](./migration-notes.md).

---

## 日本語

このドキュメントは `unity-cli` の内部開発フローをまとめたものです。

## コア構成

- CLI本体: Rust (`src/`)
- Unity連携パッケージ: `UnityCliBridge/Packages/unity-cli-bridge`
- Unityテストプロジェクト: `UnityCliBridge`
- C# LSP: `lsp/`
- Specワークフロー: GitHub Issue-first（`gwt-spec`）のみ

## 前提条件

| ツール                  | バージョン                | 用途                                                        |
| ----------------------- | ------------------------- | ----------------------------------------------------------- |
| Rust toolchain (stable) | latest                    | CLI 本体のビルド・テスト                                    |
| .NET SDK                | 10.0                      | LSP サーバーのビルド・テスト                                |
| Unity Editor            | 2022.3 LTS または Unity 6 | ローカル Unity listener / ローカル E2E 検証                 |
| Python + `tiktoken`     | 3.9+                      | LSP 性能計測時のトークン算出（`scripts/lsp-perf-check.sh`） |

### インストール

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# .NET SDK 10
# https://dotnet.microsoft.com/download/dotnet/10.0 からダウンロード
```

### Docker を使う場合

Rust と .NET SDK 10 が同梱された開発用 Docker イメージを利用できます。

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

| 環境変数                        |  デフォルト | 補足                                        |
| ------------------------------- | ----------: | ------------------------------------------- |
| `UNITY_PROJECT_ROOT`            |    自動検出 | `Assets/` と `Packages/` を含むディレクトリ |
| `UNITY_CLI_HOST`                | `localhost` | Unity TCP リスナーのホスト                  |
| `UNITY_CLI_PORT`                |      `6400` | Unity TCP リスナーのポート                  |
| `UNITY_CLI_TIMEOUT_MS`          |     `30000` | コマンドタイムアウト (ms)                   |
| `UNITY_CLI_LSP_MODE`            |       `off` | `off`, `auto`, `required`                   |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` |       `600` | デーモンアイドルタイムアウト（秒）          |
| `UNITY_CLI_TOOLS_ROOT`          |  OS依存既定 | ツール配置ルート                            |

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

Docker、WSL2、複数インスタンスの設定例は
[`docs/configuration.md`](configuration.md) を参照してください。

旧 MCP プレフィックス環境変数は未サポートです。`UNITY_CLI_*` のみ使用してください。
`UNITY_CLI_UNITYD` は廃止済みで、unityd は常時自動管理です。

リモートの typed / raw / batch 操作は、IPC 接続先がない場合に unityd を自動起動します。
idle 終了（既定 600 秒、`UNITY_CLI_UNITYD_IDLE_TIMEOUT` で変更可能）後も同様です。
自動起動には実行中の CLI と同じバイナリを使い、同時起動を排他制御します。
`unityd stop` 後の次のリモート操作では再起動し、`unityd status` 単独では起動しません。
host/port の解決方法は維持し、接続を両方の組み合わせごとに再利用します。
起動失敗の理由と直接 TCP 接続への fallback は stderr に表示します。
`-v` で `route` / `startup_ms` / `operation_ms` を stderr に分けて記録し、
stdout は JSON 結果用に維持します。PID と接続数は `unityd status` で確認できます。

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
- `unity-cli tool schema <tool_name> --output json`
- `unity-cli tool call <tool_name> --json '{...}'`
- `unity-cli --dry-run tool call <tool_name> --json '{...}'`
- `unity-cli unityd start` / `stop` / `status`
- `unity-cli batch --json '[{"tool":"ping","params":{}},{"tool":"get_editor_state","params":{}}]'`

raw 例:

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

明示スキーマを持つツールはデフォルトで厳格バリデーションされます。
未知キー、必須不足、型不一致は実行前にエラーになります。
`oneOf` / `anyOf` 制約（例: `load_scene`, `delete_gameobject`, `input_keyboard`）も実行前に検証されます。
action 付きツールは action ごとの必須項目も実行前に検証されます（例: `package_manager` の search では `keyword` 必須、`manage_layers` の add では `layerName` 必須）。

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

- Rustツールカタログ: `src/tooling/tool_catalog.rs`
- ローカルツール実装: `src/tooling/local_tools.rs`
- スナップショット一覧: `docs/tools.md`（`Tool Catalog`）

## ローカル実行コマンド

```bash
# Rust
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo test --all-targets

# C# LSP
dotnet test lsp/Server.Tests.csproj

# Unity（EditModeテスト）
./scripts/switch-unity-manifest.sh 6
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode-unity6.xml

./scripts/switch-unity-manifest.sh 2022
unity -batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform editmode -testResults test-results/editmode-unity2022.xml
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

## ローカル Unity E2E

Unity E2E は CI では実行しません。Unity Editor が起動しているローカル環境でのみ実行します。

### 準備

1. `UnityCliBridge` プロジェクトを Unity Editor で開く
2. Unity CLI Bridge パッケージが読み込まれていることを確認する
3. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` の listener が起動していることを確認する

### 実行

```bash
# ビルド
cargo build --release

# スモークE2E
scripts/e2e-test.sh

# 参照ソース取得の回帰検証（起動済み Editor・隔離キャッシュ・UnityCsReference を取得）
# 先に cargo build、または UNITY_CLI_BIN で検証バイナリを指定する。
scripts/e2e-reference-fetch.sh --port 6400

# Player build: 隔離コピーで EditMode・実ビルド・状態取得・失敗・再起動・画面なし起動
python3 scripts/e2e-player-build.py --launch --editmode

# 入力シミュレーション決定的 E2E
scripts/e2e-input-tools.sh

# テスト件数の回帰検証（EditMode 7件 + PlayMode 2件）
# プロジェクト既定の DisableDomainReload 設定と起動済み listener が必要です。
scripts/e2e-test-results.sh

# headless batch host 入力 E2E
scripts/e2e-input-batch-host.sh

# unityd 自動起動・idle 復帰・同時操作 E2E（専用 batch host を起動・終了）
cargo build
python3 scripts/e2e-unityd.py --port 6453
# 結果・cold/warm 時間・Editor log: UnityCliBridge/.unity/unityd-<timestamp>/

# Unity GUI listener が無い場合の推奨経路
scripts/e2e-input-batch-host.sh --port 6402

# 既定では ProjectVersion.txt の Unity を使う。
# その editor が未インストールのときだけ UNITY_PATH を上書きする。

# フルローカルE2E
scripts/e2e-all-tools.sh

# ホスト・ポート指定
scripts/e2e-all-tools.sh --host 192.168.1.10 --port 9090

# media capture / profiler ベンチマーク
scripts/perf-media-benchmark.sh

# 録画形式: MP4 / WebM / PNG連番（描画可能なEditor）
scripts/e2e-video-formats.sh --port 6400
```

### タップ・スワイプの検証

入力の `tap` / `swipe` は開始受付後すぐ応答し、自然な Input System 更新で
複数フレームにわたって実行します。`holdSeconds` / `duration` は実時間で計測し、
swipe は途中の位置と終点をゲームの Update に公開してから解除します。
同じ `touchId` は受付順に実行し、異なる ID は並行して実行します。
CLI の `touchId` はゼロ始まりで、Input System の `touchId` はその値 + 1 です。
`touches` 配列内の物理スロットは Unity が割り当てます。
batch / sequence の応答もジェスチャー終了待ちではなく受付結果です。

`TouchGesturePlayModeTests` は実 Touchscreen を MonoBehaviour.Update から観測し、
30fps・120fps・無制限での保持と移動、既定 tap、同一/別 ID、デバイス削除を検証します。
実 Editor の PlayMode テストランナーでこのクラスを指定して実行してください。

### シーン配置ポリシー

- `UnityCliBridge/Assets/Scenes/` 直下は固定で追跡するシーン（`SampleScene` のみ）を配置する
- ローカル E2E 生成シーンは `UnityCliBridge/Assets/Scenes/Generated/E2E/` に作成し、コミットしない
- UI 手動検証シーン（UGUI/UITK/IMGUI）は `Tools/Unity CLI/UI Tests/*` で必要時に `UnityCliBridge/Assets/Scenes/Generated/UI/` へ生成する

### Media Perf Benchmark

`scripts/e2e-video-formats.sh` は実ファイルのヘッダーとセッション終了を検証します。Play状態を切り替えて元に戻すため、カメラのあるシーンを開いた専用Editorで実行します。batch-host では Game View を描画できるよう `-nographics` を外してください。録画開始時は予定パス、停止成功時は存在する空でないファイルのパスを返します。`png_sequence` の停止結果はセッション固有ディレクトリ内の最初のPNGです。

- `scripts/lsp-perf-check.sh` は引き続き LSP / search / index 性能の正規ベンチマークで、`scripts/perf-media-benchmark.sh` とは別物。
- `scripts/perf-media-benchmark.sh` は `Tools/Unity CLI/Performance/Generate Media Perf Scene` 経由で `Assets/Scenes/Generated/E2E/Performance/UnityCli_PerfBenchmark.unity` を生成する。
- 出力 artifact は `UnityCliBridge/.unity/perf-media/<timestamp>/` に保存する。
- 保存対象は `get_command_stats`、`profiler_start` / `profiler_stop`、`capture_screenshot`、`capture_video_start` / `capture_video_status` / `capture_video_stop`。
- 確認対象は `summary.md` と `result.json`。LSP の履歴ファイル `.unity/perf/lsp-history.jsonl` には追記しない。

## トラブルシューティング

### まず確認

1. Unity Editor が起動していること
2. `Unity CLI Bridge` パッケージが導入されていること
3. Unity TCP リスナーが起動していること（デフォルト `6400`）
4. `UNITY_CLI_HOST` / `UNITY_CLI_PORT` が一致していること

### 接続エラー

| 症状                 | 原因                          | 対処                              |
| -------------------- | ----------------------------- | --------------------------------- |
| `Connection timeout` | Unity未起動                   | Unity Editorを起動                |
| `ECONNREFUSED`       | リスナー未起動 / ポート不一致 | Project Settingsで再起動          |
| `invalid response`   | プロトコル不一致 / 古いビルド | パッケージ再import後にUnity再起動 |

### LSP関連

| 症状                          | 対処                                              |
| ----------------------------- | ------------------------------------------------- |
| LSP実行ファイルが見つからない | `unity-cli lsp install` を実行して再試行          |
| LSPタイムアウト               | `UNITY_CLI_TIMEOUT_MS` を延長                     |
| 必須LSPモードで失敗           | セットアップ中は `UNITY_CLI_LSP_MODE=auto` を利用 |

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

| ジョブ                         | トリガー                | 内容                                                                                   |
| ------------------------------ | ----------------------- | -------------------------------------------------------------------------------------- |
| Skill Contract Lint (required) | push / PR               | `cargo run -- skills lint --severity error` (Skill Contract v1; SPEC #160)             |
| Rust Tests (required)          | push / PR               | `cargo test`                                                                           |
| LSP Tests (required)           | push / PR               | `dotnet test lsp/Server.Tests.csproj`                                                  |
| LSP Performance (required)     | push / PR               | `scripts/lsp-perf-check.sh`（全ケース実行 + 履歴artifact）                             |
| Skill Routing Eval             | 毎日スケジュール / 手動 | `scripts/skill-eval/llm-routing-eval.sh`（`.github/workflows/skill-routing-eval.yml`） |

Skill Contract Check / Rust Tests / LSP Tests / LSP Performance は PR マージの必須チェックです。

## 機能カタログ

最新の全機能一覧（typed コマンド群 + Unity Tool API 一覧）は `docs/tools.md` の以下を正本とします。

- `Tool Catalog`

再生成コマンド例:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.[]'
```

## ベンチマーク方針

### 目安値（参考）

環境依存ですが、目安は次のとおりです。

| シナリオ                             | 平均（目安） | 備考                                  |
| ------------------------------------ | ------------ | ------------------------------------- |
| `unity-cli --help`                   | ~2-5 ms      | ローカル起動時間のみ                  |
| `unity-cli tool list`                | ~2-5 ms      | ローカル一覧生成                      |
| `unity-cli system ping`              | ~10-50 ms    | Unity Editor 起動時のみ               |
| `unity-cli system ping` (unityd経由) | ~5-20 ms     | デーモンがTCP接続を保持               |
| `unity-cli batch` (5コマンド)        | ~25-100 ms   | デーモン経由の単一IPCラウンドトリップ |

### 実行

```bash
# 人間向け
./scripts/benchmark.sh

# CI・保存向けJSON
./scripts/benchmark.sh --json

# LSP性能計測 + 閾値チェック + サイズ/トークン計測
./scripts/lsp-perf-check.sh

# 接続済み Unity Editor に対する media capture / profiler ベンチマーク
# これは lsp-perf-check.sh とは別系統で、lsp-history.jsonl には追記しない
./scripts/perf-media-benchmark.sh

# 保存済み履歴の確認
cat .unity/perf/lsp-history.jsonl | tail -n 5
```

回帰判定方針:

1. JSON 結果を継続保存する
2. `.unity/perf/lsp-history.jsonl` を追記履歴として維持する
3. 履歴トレンドをベースライン比較に利用する
4. `system ping` は Unity の可用性に依存するため厳密ゲートには含めない
5. スクリーンショット / 動画 / Profiler の回帰確認には `scripts/perf-media-benchmark.sh` を使い、`get_command_stats` の結果も合わせて保存する。ただし LSP 履歴パイプラインには含めない

## スキル精度評価

ベンチマーク・履歴ファイル:

- `tests/fixtures/skill-routing/benchmark.jsonl`（ルーティング評価ベンチマーク: 127ケース）
- `.unity/skill-eval/skill-routing-history.jsonl`（追記専用の評価履歴）
- `.unity/skill-eval/skill-static-report.json`（最新の静的契約チェック結果）

静的検証（PR CI 必須）:

```bash
cargo run -- skills lint --severity error
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

ローカルの Codex runner を使ったルーティング評価:

```bash
./scripts/skill-eval/llm-routing-eval.sh \
  --model codex-local \
  --runner-cmd 'python3 scripts/skill-eval/run-codex-routing.py'
```

この local runner を使うには、事前に `codex login` が通っている必要があります。

現在の閾値:

- `top1 >= 0.90`
- `top2 >= 0.98`
- `tool_correct >= 0.92`
- `payload_valid >= 0.95`

## リリースフロー

1. `./scripts/publish.sh <major|minor|patch>` を実行
2. 新しい `vX.Y.Z` タグが push されたことを確認
3. `.github/workflows/release.yml` でバイナリが公開されたことを確認
4. crates.io で crate が公開されたことを確認

詳細は `RELEASE.md` を参照してください。

## ドキュメント整合チェック

ドキュメントと Issue-first 運用が現行の実装と矛盾していないことを定期的に確認します。

### チェック対象

| ディレクトリ / ファイル                                 | 内容                                 |
| ------------------------------------------------------- | ------------------------------------ |
| `docs/architecture.md`                                  | アーキテクチャ概要                   |
| `docs/migration-notes.md`                               | 移行と廃止の記録                     |
| `docs/`                                                 | 開発ガイドと憲章                     |
| `README.md`                                             | プロジェクト概要                     |
| `UnityCliBridge/Packages/unity-cli-bridge/README.md`    | UPM パッケージドキュメント（英語）   |
| `UnityCliBridge/Packages/unity-cli-bridge/README.ja.md` | UPM パッケージドキュメント（日本語） |

### チェック手順

1. **旧名称の残留確認**: 以下のコマンドで `MCP` や旧プロジェクト名の残留をチェックします。

   ```bash
   grep -rni "mcp" docs/ README.md | grep -v migration-notes.md
   ```

2. **環境変数の整合確認**: 本ドキュメントの変数一覧と `src/core/config.rs` の実装を比較します。

   ```bash
   grep -oE 'UNITY_CLI_[A-Z_]+' src/core/config.rs | sort -u
   grep -oE 'UNITY_CLI_[A-Z_]+' docs/development.md | sort -u
   ```

3. **ソースファイル構成の確認**: `docs/architecture.md` のソースファイル一覧と実際のファイルを比較します。

   ```bash
   ls src/*.rs
   ```

4. **コマンド一覧の確認**: `README.md` の Command Overview セクションが `src/cli.rs` のサブコマンド定義と一致しているか確認します。

### チェックのタイミング

- 新機能追加・設計変更を含む PR をマージする前
- 環境変数やコマンド体系に変更があった場合
- リリース前のチェックリストの一項目として

## ベースライン方針

Unity 側コードベースは `unity-mcp-server` をベースコピーとし、差分は MCP→CLI 移行に必要な変更に限定します。方針と差分の記録は [`docs/migration-notes.md`](./migration-notes.md) を参照してください。
