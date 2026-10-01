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
| `UNITY_CLI_HOST`                |      `127.0.0.1` | Unity TCP listener host                        |
| `UNITY_CLI_PORT`                |           `6400` | Unity TCP listener port                        |
| `UNITY_CLI_TIMEOUT_MS`          |          `30000` | Command timeout (ms)                           |
| `UNITY_CLI_LSP_MODE`            |            `off` | `off`, `auto`, `required`                      |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` |            `600` | Daemon idle timeout (seconds)                  |
| `UNITY_CLI_TOOLS_ROOT`          | platform default | Root directory for downloaded tools            |

Minimal setup:

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=127.0.0.1
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
Before reusing a pooled connection, unityd checks whether the Editor closed it
(Editor restart or bridge reload) and reconnects before sending, so the first call
after a restart does not fail with `early eof` / `Broken pipe`. A request that was
already sent is never resent, so mutating calls are not executed twice.
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

### CI test command (headless or existing Editor)

`unity-cli test` reuses the Editor for `--project-path` when it is already
running. Otherwise it starts that project's installed Editor in batch mode and
stops only the process it started, including on compilation errors and timeout.
The project must contain the Unity CLI Bridge and Unity Test Framework packages.
Editor selection uses `ProjectSettings/ProjectVersion.txt`; `UNITY_EDITOR_PATH`
can select an installed binary explicitly.

```bash
unity-cli test --project-path ./UnityCliBridge --mode editmode \
  --report junit --output ./artifacts/editmode.xml --timeout 600
unity-cli test --project-path ./UnityCliBridge --mode playmode \
  --filter MyPlayModeTests --report nunit --output ./artifacts/playmode.xml
# Optional: exercise Enter Play Mode without Domain Reload
unity-cli test --project-path ./UnityCliBridge --mode playmode \
  --disable-domain-reload --output ./artifacts/playmode-no-reload.xml
```

For this subcommand, `--output` is the report filename, relative to the current
directory; other commands retain their `--output text|json` presentation option.
`--filter` follows `run_tests`: a dotted name selects an exact test, otherwise it
selects a fixture. PlayMode preserves the project's Domain Reload configuration
unless `--disable-domain-reload` is supplied. Reconnection polls the same run ID
and never restarts the tests. NUnit reports use the NUnit 3 XML structure; JUnit
reports include failures, skipped cases, durations and captured test output.

Exit codes are `0` for success, `8` for failed tests, `6` for execution failures
(including compilation errors), and `7` when the Editor cannot be reached.
A test execution timeout after the run starts is `6`. The timeout covers startup
and the test run; terminating an owned process can take up to 10 additional
seconds. Failed tests still produce a report before exit `8`.
`--output-format github` emits GitHub error annotations; file and line metadata
are included when Unity's failure stack trace contains a source location.

Example GitHub Actions workflow for an already provisioned self-hosted macOS
Apple Silicon runner (this example is not run by this repository's CI):

```yaml
name: Unity tests
on: [push, pull_request]
permissions:
  contents: read
jobs:
  editmode:
    runs-on: [self-hosted, macOS, ARM64]
    steps:
      - uses: actions/checkout@v7
      - uses: dtolnay/rust-toolchain@stable
      - run: cargo build --release --bin unity-cli
      - name: Run Unity tests
        env:
          UNITY_CLI_NO_AUTO_UPDATE: '1'
        run: |
          target/release/unity-cli test --project-path UnityCliBridge \
            --mode editmode --report junit --output artifacts/editmode.xml \
            --timeout 600 --output-format github
      - name: Upload results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: unity-test-results
          path: artifacts/*.xml
```

Install the matching Editor and activate a valid Unity license for the runner's
OS account before running the job. This command does not install or activate
Unity. Configure license-server access or activation according to your Unity
subscription; keep any activation credentials in Actions secrets, not in YAML
or artifacts. A license failure is an infrastructure failure, not a failed test.
Use an isolated runner/project checkout so concurrent jobs cannot open the same
Unity project.

Local acceptance verification (requires `xmllint` and the Jenkins xUnit
[JUnit schema](https://github.com/jenkinsci/xunit-plugin/blob/master/src/main/resources/org/jenkinsci/plugins/xunit/types/model/xsd/junit-10.xsd)):

```bash
python3 scripts/e2e-ci-tests.py --version 6000.3.25f1 \
  --output /tmp/ci-tests-unity6 --port 6551 --junit-schema /path/to/junit-10.xsd
python3 scripts/e2e-ci-tests.py --version 2022.3.62f3 \
  --output /tmp/ci-tests-unity2022 --port 6552 --junit-schema /path/to/junit-10.xsd
```

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

# Custom attributes, live recompilation/discovery, CLI/Bridge argument rejection,
# Light creation, exception isolation, and focused EditMode collision tests.
# Each run creates its own project, credentials, port, logs and source hashes.
python3 scripts/e2e-custom-tools.py --version 6000.3.25f1 \
  --cli target/debug/unity-cli --output /tmp/custom-tools-6000
python3 scripts/e2e-custom-tools.py --version 2022.3.62f3 \
  --cli target/debug/unity-cli --output /tmp/custom-tools-2022

# TCP authentication: isolated real Editor, missing/wrong token rejection,
# eval side effects, CLI/unityd success, POSIX 0600, and focused EditMode tests.
# Repeat for 2022.3.62f3; use a fresh output directory for each run.
python3 scripts/e2e-bridge-auth.py --version 6000.3.25f1 \
  --cli target/debug/unity-cli --output /tmp/bridge-auth-6000 --editmode
# Temporary legacy opt-out and next-minor deprecation warning
python3 scripts/e2e-bridge-auth.py --version 6000.3.25f1 \
  --cli target/debug/unity-cli --output /tmp/bridge-auth-optout-6000 --opt-out

# AudioMixer hierarchy/exposed Volume and AudioImporter persistence (isolated Editor)
bash scripts/e2e-audio-batch-host.sh --unity-version 2022.3.62f3 --port 6481
bash scripts/e2e-audio-batch-host.sh --unity-version 6000.3.25f1 --port 6482

# Reference fetch regression (live Editor; isolated cache; downloads UnityCsReference)
# Run cargo build first, or set UNITY_CLI_BIN to the binary under test.
scripts/e2e-reference-fetch.sh --port 6400

# Automatic ref resolution + provenance regression (live Editor; separate temporary caches)
# Covers the real project, a 6000.4.12f1 fixture, and explicit public ref 6000.4.
scripts/e2e-reference-resolution.sh --port 6400 --project-root UnityCliBridge

# Player build: relevant EditMode tests, real build/report, polling, failures,
# restart recovery, and headless launch (isolated project copy on macOS)
python3 scripts/e2e-player-build.py --launch --editmode

# unityd reconnect after Editor restart (#384; defaults: 6000.3.25f1, port 6471, 5 cycles)
# Kill -> restart on the same port -> ping -> reads -> create_gameobject; the mutating
# call must succeed first time and create exactly one GameObject.
python3 scripts/e2e-unityd-restart.py --workdir /tmp/unityd-restart

# Deterministic input simulation E2E
scripts/e2e-input-tools.sh

# C# eval E2E against an existing listener (see editor-eval.md)
scripts/e2e-eval.sh --unity-cli "$PWD/target/debug/unity-cli"

# C# eval with an isolated batch host (build the debug CLI first)
scripts/e2e-input-batch-host.sh --suite eval --port 6402 --unity-cli "$PWD/target/debug/unity-cli"

# C# eval latency budget (perf-budgets.json: editor_eval; GUI Editor frontmost, release CLI)
python3 scripts/bench-eval.py --port 6400 --require-frontmost --activate --budget editor_eval

# Timeline editing, persistence and Animator evaluation (real Editor)
cargo build --bin unity-cli
scripts/e2e-timeline-batch-host.sh --port 6474

# Isolated project without com.unity.timeline: compile/start and error contract
scripts/e2e-timeline-batch-host.sh --port 6475 --without-timeline

# Fresh project with / without com.unity.ugui: compile/start and uGUI UI tools
# (run for each Unity version to verify, e.g. 6000.3.25f1 and 2022.3.62f3)
scripts/e2e-ugui-batch-host.sh --unity-version 6000.3.25f1 --port 6476 --without-ugui
scripts/e2e-ugui-batch-host.sh --unity-version 6000.3.25f1 --port 6477

# Test-result counting regression (7 EditMode + 2 PlayMode tests)
# Requires the project's default DisableDomainReload setting and a running listener.
scripts/e2e-test-results.sh

# Input Actions types (both creation routes; requires this checkout's listener)
scripts/e2e-input-actions.sh

# Headless batch-host input simulation E2E
scripts/e2e-input-batch-host.sh

# Real Lighting / legacy NavMesh / NavMeshSurface / Occlusion bake and reload
cargo build --bin unity-cli
scripts/e2e-bake-batch-host.sh --port 6477

# Isolated optional hot reload backend / real Editor checks
scripts/e2e-hot-reload-batch-host.sh --port 6484 --expect missing
# See docs/hot-reload.md for real method replacement runs (ARM64 / x64 under Rosetta).

# PlayMode result collection with Domain Reload enabled and disabled
python3 scripts/e2e-test-domain-reload.py --batch-host --port 6450

# Recommended local path when no Unity GUI listener is already running
scripts/e2e-input-batch-host.sh --port 6402

# Input Actions source JSON persistence, reimport, and Editor restart (two owned batch hosts)
cargo build --bin unity-cli
python3 scripts/e2e-input-actions-persistence.py --port 6428

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

The all-tools sweep checks the runtime catalog against its invocations and
`scripts/e2e-all-tools-exclusions.json`. Each exclusion explains the required
environment or separate suite; excluded tools are reported as skipped, not passed.
`cargo test all_tools_e2e -- --test-threads=1` detects catalog drift and verifies
that disconnected or reloading Editors are not treated as ready. For an isolated
Editor project, set `UNITY_PROJECT_ROOT=/absolute/project/path` and pass its port.
The sweep quits that Editor unless `--skip-quit` is specified.

### Scene Layout Policy

Baking E2E creates fixtures under `Assets/Scenes/Generated/E2E/Baking/` and
checks generated assets, saved scene references, and navigation queries after
scene reload. Its batch host enables graphics for actual lightmap generation;
do not add `-nographics`. Logs and pass/fail counts are kept in the printed
`/tmp/unity-cli-bake-e2e.*` directory. Against a running Editor, use
`scripts/e2e-bake.sh --port <port>`. Save unrelated scenes first. Use
`--targets lighting,navmesh-legacy,navmesh-surface,occlusion` to select targets;
acceptance verification requires the default full set.

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

### Editor version matrix

Run the existing acceptance suites in isolated projects for every supported
Editor. The source `UnityCliBridge` project is never opened by this runner.
Build the CLI, publish the LSP into a dedicated directory, and select an Editor:

```bash
cargo build --bin unity-cli
dotnet publish lsp/Server.csproj -c Release -r osx-arm64 --self-contained true \
  -o /tmp/unity-cli-matrix-tools/csharp-lsp/osx-arm64
scripts/e2e-matrix.sh \
  --editor /Applications/Unity/Hub/Editor/6000.7.0b2/Unity.app/Contents/MacOS/Unity \
  --lsp-root /tmp/unity-cli-matrix-tools
```

Omit `--editor` to run all eight versions listed in the
[dated compatibility matrix](editor-compatibility.md); repeat it to select several.
`--port` chooses an unused local listener port. `--output` names a new evidence
directory. `--suites compile,input,timeline,vfx` performs a focused diagnostic run;
its report explicitly records `full_matrix: false`. Acceptance uses the default
complete suite set. A missing Editor, compiler error, failed suite or timeout
fails the run. Inspect `matrix.json` and the linked per-suite logs before claiming PASS.

The default matrix also includes `bake`, `player-build`, `video-formats`,
`animation-curves`, `input-actions-persistence`, and `reference`. To reproduce
only this extension, pass
`--suites bake,player-build,video-formats,animation-curves,input-actions-persistence,reference`.
The Player suite checks macOS output and, when Windows Build Support is installed,
starts a second isolated Editor for Windows output (port + 2); Input Actions owns another project and two sequential
Editor starts (port + 1) to prove persistence across restart. Install Windows
Build Support in Unity Hub to exercise actual cross-building. Without it, the
runner asserts `BUILD_MODULE_MISSING` and records `UNSUPPORTED`, never `PASS`.
An all-supported run reports `PASS`; an otherwise successful run containing
explicit unsupported results reports `UNSUPPORTED` and exits zero. Unexpected
errors still fail. Video capture keeps graphics enabled. Reference suites use
isolated caches and download public UnityCsReference with license acceptance.

Graphics stay enabled for VFX/SDF. Only processes launched by the runner are
stopped. Project settings, generated assets and package imports remain in the
retained temporary fixture. Editor reloads and scripts may reset the listener;
readiness requires an idle compiler, zero compilation errors and the owned port.

Unity 6000.7 Editor tests run on Mono. CoreCLR is a Player backend and is outside
this Editor matrix; the compatibility page links the official runtime boundary.

### VFX Graph verification

The Bridge uses reflection for the optional `com.unity.visualeffectgraph`
package; installing the Bridge does not install VFX Graph. The development
project includes VFX Graph 17.4.0. Use Unity 6000.4.11f1 for this matrix.

検証済み: Unity 6000.4.11f1 / VFX Graph 17.4.0 (macOS).

```bash
cargo build --bin unity-cli
scripts/e2e-vfx-batch-host.sh --port 6476
scripts/e2e-vfx-batch-host.sh --port 6477 --without-vfx
```

The normal run requires graphics/compute shader support for SDF baking; do not
add `-nographics`. It creates a graph from a template, adds a context, block and
parameter, verifies describe/compile, drives a VisualEffect in Play Mode with
parameter writes and events (including `OnPlay`), and checks a baked Texture3D.
Generated assets belong under `Assets/Scenes/Generated/E2E/Vfx/`.
The package-absent run uses a temporary project and preserves the working
project's manifest. It verifies compilation and `VFX_PACKAGE_MISSING` responses
from all six VFX tools without Console errors.

Run the focused NUnit suites on the same integrated source:

```bash
UNITY_PATH=/Applications/Unity/Hub/Editor/6000.4.11f1/Unity.app/Contents/MacOS/Unity
"$UNITY_PATH" -batchmode -projectPath "$PWD/UnityCliBridge" -runTests \
  -testPlatform EditMode -testFilter UnityCliBridge.Tests.VfxGraphHandlerTests \
  -testResults /tmp/vfx-editmode.xml -logFile /tmp/vfx-editmode.log
"$UNITY_PATH" -projectPath "$PWD/UnityCliBridge" -runTests \
  -testPlatform PlayMode -testFilter VfxRuntimePlayModeTests \
  -testResults /tmp/vfx-playmode.xml -logFile /tmp/vfx-playmode.log
```

The PlayMode suite needs a visible Editor/Game view to exercise GPU particle
counts and output-event callbacks. Omit `-batchmode` for that suite: batch mode
can advance the managed test loop without the rendered frames these assertions
need. The command exits automatically when tests finish. EditMode tests and the
CLI E2E host can run in batch mode with graphics enabled.

Keep the XML results, Editor logs and E2E artifact paths in the integration PR.
Windows verification is tracked separately in Issue #308.

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

The CLI is the primary implementation. For MCP clients, use the thin
`unity-cli mcp` stdio adapter and `unity-cli mcp configure <client>`;
see [MCP configuration](./mcp.md). Legacy Node.js MCP launch settings are not supported.

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
| CLI Latency (required)         | push / PR               | `scripts/bench-cli-latency.py` (p50/p95 budgets + injected-delay evidence)            |
| Skill Routing Eval             | daily schedule / manual | `scripts/skill-eval/llm-routing-eval.sh` (`.github/workflows/skill-routing-eval.yml`) |

Skill Contract Check, Rust Tests, LSP Tests, LSP Performance, and CLI Latency are required checks for PR merges.

## Capability Catalog

The full current capability list (typed command groups + Unity Tool APIs) is maintained in `docs/tools.md` under:

- `Tool Catalog`

Regenerate command examples:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.data[].name'
```

## Benchmark Policy

### Budgets and gates

`perf-budgets.json` is the source of p50/p95 latency limits. A missing budget,
failed operation, absolute limit violation, or relative regression fails the run.
Percentiles use the nearest rank; failed operations never count as fast samples.

| Gate                   | Environment                                 | Samples per operation                                   | Evidence                                               |
| ---------------------- | ------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------------ |
| CLI Latency (required) | Linux CI, deterministic loopback TCP Bridge | 50 after 3 warmups                                      | `cli-latency` artifact: normal and injected-delay JSON |
| Editor `perf` suite    | macOS GUI, 6000.3.25f1 and 2022.3.62f3      | 30 after 3 complete warmup cycles, each focus condition | `matrix.json`, `perf.json`, `perf.log`                 |
| `editor_eval`          | macOS GUI, Editor frontmost                 | 100 after 3 warmups                                     | `bench-eval.py --budget editor_eval` JSON              |

CI measures startup, tool list, and ping/state/batch-of-five over both direct TCP
and a warm unityd connection. Direct mode exercises the existing daemon-unavailable
fallback using a private unusable runtime path; connection counts prove the route.
The job also injects 30 ms into every Bridge response and requires a measured
budget failure. No Unity installation is needed.

The real Editor suite measures the staff report's 23 operations (Issue #371 AC-6):
state, hierarchy, object search, Transform, position, Cube creation, component
add/remove, object deletion, scene info/save, console, screenshot, material search,
asset copy/move/delete, import settings, material edit, Time settings, C# read,
Play-until-ready and Stop-until-ready. The 22 remote operations use warm unityd; C# `read`
uses the current CLI's local reader and is explicitly labelled `local` in JSON.
Play/Stop include polling until the requested state is observed.

**Sampling:** each operation still launches a CLI process, so reported wall time
includes process spawn and exit. The Editor and unityd stay resident and warm;
their initial startup, fixture creation and focus changes are outside the timed
interval. This is not a persistent CLI shell measurement. Do not compare it with
a resident-shell benchmark without stating that difference.

**Focus:** frontmost and background have separate budgets. macOS frontmost PID
is checked before and after every sample; background runs activate Finder.
The owned Game View windows use `PlayUnfocused` during both conditions so entering
Play Mode does not bring a background Editor forward. Their previous settings are
restored afterwards; this window setting is also recorded in the history conditions.
The background screenshot starts with Finder frontmost and may activate the target
Editor: the existing capture handler calls `GameView.Focus()`. This exception is
recorded as `backgroundScreenshot` in JSON conditions. Its post-call PID must remain
Finder or become the target Editor; third-party activation is never accepted.
Other focus changes discard the entire balanced cycle and retry; ten discarded cycles
or an OS screenshot fallback fail the run. Only valid cycles supply samples. Use an idle host
without another agent changing Editor focus or importing another project.

Initial Editor budgets come from the four complete runs in
[`issue-394-baseline.json`](verification/issue-394-baseline.json). For each operation
and focus condition, take the larger measurement across the two Unity versions,
multiply p50 by 1.5 and p95 by 2, and round up to 5 ms. For background p50 only,
use the calibration p95 as a floor before rounding. Background update scheduling
can move request timing between fast and slow phases: the same Unity 6 binary's
material-search p50 changed from 74.6 to 154.3 ms while p95 stayed at 182.0/180.5 ms.
The original failing verification is preserved; the baseline JSON records the
observations and calibration rationale. Frontmost and all p95 budgets are unchanged.
This leaves initial host variance headroom while the separate history gate still rejects p50 degradation
greater than 20%. Existing `editor_eval` and mock CLI budgets are unchanged.

### Run

```bash
cargo build --release --bin unity-cli

# Unity-free CI gate (50 samples per operation/route)
python3 scripts/bench-cli-latency.py --out /tmp/cli-latency.json

# Must exit 1 with latency violations and all eight results present
python3 scripts/bench-cli-latency.py --delay-ms 30 --out /tmp/cli-latency-delayed.json

# Release prerequisite: both Editors, frontmost AND background, isolated GUI projects
UNITY_CLI_PERF_REGRESSION_PERCENT=20 python3 scripts/e2e-matrix.py --suites perf \
  --unity-cli "$PWD/target/release/unity-cli" \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --editor /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  --output /tmp/unity-cli-release-perf

# Repeat against an already-open project owned by the perf matrix
python3 scripts/bench-editor-ops.py --project /tmp/perf-fixture/project \
  --port 6508 --focus both --out /tmp/editor-ops.json

# Existing eval and LSP gates
python3 scripts/bench-eval.py --port 6400 --require-frontmost --activate --budget editor_eval
./scripts/lsp-perf-check.sh
```

Choose a fresh `--output` directory for every matrix run. The perf suite uses
GUI Editors; its isolated-project ownership marker prevents the standalone
benchmark from replacing a user's scene. Fixtures live only under
`Assets/Scenes/Generated/E2E/Performance/`. The LSP binary is not needed for
`--suites perf`.
The copied URP material-upgrade cache is regenerated for the selected Editor;
render pipeline settings are retained while avoiding a cross-version startup dialog.
The suite also runs the existing `editor_eval` gate for 100 frontmost samples,
saves `perf-eval.json`/`perf-eval.log`, and appends its results to the same history
under separate eval conditions. Eval uses its existing direct TCP route.

### History and recovery

`scripts/bench-editor-ops.py` appends all results to
`.unity/perf/editor-ops-history.jsonl`. Compare p50 against the median of the
last five complete measurement runs with the same host, OS, architecture, Unity version,
focus, Play Mode options, Game View focus mode and sampling method. Fail when degradation is strictly
greater than 20%. Fewer than five matching runs means absolute budgets still
apply and the relative baseline is not yet established. Completed measurements
count even when a budget failed; incomplete/error runs remain in the log but do
not supply a baseline. Absolute budgets always apply alongside the rolling median.

`UNITY_CLI_PERF_REGRESSION_PERCENT` (or `--regression-percent`) overrides the
relative threshold; it never disables absolute budgets. Diagnose a failure from
`violations`, sample counts and condition metadata, then repeat on an idle host.
Fix actual regressions. If a deliberate change justifies new limits, review the
fresh measurements and update only the affected keys in `perf-budgets.json`.
For an intentional environment/baseline reset, preserve the old JSONL and select
a new `--history` path; do not delete failing history or raise limits merely to
get a green run. Release verification uses the normal history and default 20%.

`/release` requires both versions and both focus conditions to PASS before the
release is prepared. Keep the command, version, pass/fail counts and log summary
in the PR. Re-run after the last product or benchmark code change; a historical
PASS is not evidence for changed code. `scripts/benchmark.sh` and
`scripts/perf-media-benchmark.sh` remain exploratory tools; they do not replace
the gates above. LSP history remains separate at `.unity/perf/lsp-history.jsonl`.

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

This local runner requires `codex login` and `unity-cli` on PATH. It reads
`unity-cli tool schema --output json` locally to supplement skill examples
with every available tool and its top-level payload keys; no Editor is needed.
For checkout-specific evaluation, build with `cargo build` and append
`--unity-cli ./target/debug/unity-cli` to the runner command. Schema discovery
errors fail the prediction rather than silently using an incomplete catalog.

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
| `UNITY_CLI_HOST`                | `127.0.0.1` | Unity TCP リスナーのホスト                  |
| `UNITY_CLI_PORT`                |      `6400` | Unity TCP リスナーのポート                  |
| `UNITY_CLI_TIMEOUT_MS`          |     `30000` | コマンドタイムアウト (ms)                   |
| `UNITY_CLI_LSP_MODE`            |       `off` | `off`, `auto`, `required`                   |
| `UNITY_CLI_UNITYD_IDLE_TIMEOUT` |       `600` | デーモンアイドルタイムアウト（秒）          |
| `UNITY_CLI_TOOLS_ROOT`          |  OS依存既定 | ツール配置ルート                            |

最小設定:

```bash
export UNITY_PROJECT_ROOT=./UnityCliBridge
export UNITY_CLI_HOST=127.0.0.1
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
再利用の前に Editor 側で接続が閉じられていないか（Editor の再起動や bridge の再読み込み）を確認し、
閉じられていれば送信前に接続し直します。そのため再起動直後の最初の呼び出しが
`early eof` / `Broken pipe` で失敗しません。送信済みの要求は再送しないため、
変更系の呼び出しが二重に実行されることはありません。
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

VFX Graph の通常・非導入 E2E と EditMode / PlayMode の実行手順は
[VFX Graph verification](#vfx-graph-verification) を参照してください。

Input Actions のソース JSON 保存・再import・Editor 再起動の回帰検証は、
`cargo build --bin unity-cli` の後に
`python3 scripts/e2e-input-actions-persistence.py --port 6428` を実行します。
専用 batch host を2回起動し、生成アセットを終了時に削除します。
Unity ログ・CLI 応答・集計 `summary.json` は出力された一時ディレクトリに残ります。

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

# 参照の自動解決・取得元情報の検証（起動済み Editor・ケース別の一時キャッシュ）
# 実プロジェクト、6000.4.12f1 fixture、公開 ref 6000.4 の明示指定を検証する。
scripts/e2e-reference-resolution.sh --port 6400 --project-root UnityCliBridge

# Player build: 隔離コピーで EditMode・実ビルド・状態取得・失敗・再起動・画面なし起動
python3 scripts/e2e-player-build.py --launch --editmode

# 入力シミュレーション決定的 E2E
scripts/e2e-input-tools.sh

# テスト件数の回帰検証（EditMode 7件 + PlayMode 2件）
# プロジェクト既定の DisableDomainReload 設定と起動済み listener が必要です。
scripts/e2e-test-results.sh

# Input Actions の型指定（両作成経路、この checkout の listener が必要）
scripts/e2e-input-actions.sh

# headless batch host 入力 E2E
scripts/e2e-input-batch-host.sh

# unityd 自動起動・idle 復帰・同時操作 E2E（専用 batch host を起動・終了）
cargo build
python3 scripts/e2e-unityd.py --port 6453
# 結果・cold/warm 時間・Editor log: UnityCliBridge/.unity/unityd-<timestamp>/

# Editor 再起動後の unityd 再接続 E2E（#384、既定 6000.3.25f1・port 6471・5 サイクル）
# Editor を終了 → 同じポートで再起動 → ping → 読み取り → create_gameobject を繰り返し、
# 変更系が 1 回で成功し、GameObject が 1 つだけできることを確認する
cargo build --release
python3 scripts/e2e-unityd-restart.py --workdir /tmp/unityd-restart
# 結果: <workdir>/run-<timestamp>/result.json（--workdir 指定時は Library を再利用）

# Domain Reload 有効／無効で PlayMode の完了・結果・export・設定復元を検証
python3 scripts/e2e-test-domain-reload.py --batch-host --port 6450

# Unity GUI listener が無い場合の推奨経路
scripts/e2e-input-batch-host.sh --port 6402

# com.unity.ugui 有無の新規プロジェクトでコンパイル・起動・uGUI UI ツールを検証
# （検証対象の Unity バージョンごとに実行。例: 6000.3.25f1 と 2022.3.62f3）
scripts/e2e-ugui-batch-host.sh --unity-version 2022.3.62f3 --port 6476 --without-ugui
scripts/e2e-ugui-batch-host.sh --unity-version 2022.3.62f3 --port 6477

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

全ツール E2E の除外理由は `scripts/e2e-all-tools-exclusions.json` で管理し、
除外ツールを PASS として数えません。`cargo test all_tools_e2e -- --test-threads=1`
でカタログとの差分と、接続失敗を準備完了と扱わないことを検証します。
隔離した Editor を使う場合は `UNITY_PROJECT_ROOT=/absolute/project/path` と
ポートを指定してください。`--skip-quit` を指定しない場合、その Editor を終了します。

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

CLI が本体です。MCP クライアントには薄い stdio アダプタ `unity-cli mcp` と
`unity-cli mcp configure <client>` を使用してください（[MCP 設定](./mcp.md)）。
旧 Node.js MCP サーバーの起動設定はサポートしません。

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
| CLI Latency (required)         | push / PR               | `scripts/bench-cli-latency.py`（p50/p95 予算 + 遅延注入の証跡）                        |
| Skill Routing Eval             | 毎日スケジュール / 手動 | `scripts/skill-eval/llm-routing-eval.sh`（`.github/workflows/skill-routing-eval.yml`） |

Skill Contract Check / Rust Tests / LSP Tests / LSP Performance / CLI Latency は PR マージの必須チェックです。

## 機能カタログ

最新の全機能一覧（typed コマンド群 + Unity Tool API 一覧）は `docs/tools.md` の以下を正本とします。

- `Tool Catalog`

再生成コマンド例:

```bash
unity-cli --help
unity-cli tool list --host 127.0.0.1 --port 6400 --output json | jq -r '.data[].name'
```

## ベンチマーク方針

`perf-budgets.json` の p50/p95 を予算として検査し、超過・予算欠落・操作失敗は
非ゼロで終了します。パーセンタイルは nearest-rank です。失敗を高速なサンプルとして扱いません。

- CI の **CLI Latency (required)** は Unity 不要のモック TCP Bridge に対して、
  起動、tool list、ping、Editor 状態、batch 5 件を各 50 回（warmup 3 回除外）計測します。
  リモート操作は直接 TCP と warm unityd の両方を測り、接続数で経路も確認します。
  応答に 30 ms の遅延を加えて失敗を確認した JSON も artifact に保存します。
- 実機の **perf スイート**はスタッフ調査の 23 操作（#371 AC-6）を各 30 回、
  warmup 3 サイクル後に測ります。6000.3.25f1 / 2022.3.62f3 の GUI Editor を
  最前面・背景の両条件で動かし、条件別予算を適用します。
- Editor と unityd は常駐・warm ですが、CLI は操作ごとに起動します。
  計測はプロセス起動から終了までで、Play/Stop は状態確認までを含みます。
  常駐 CLI shell の計測とは区別してください。22 操作は warm unityd を使い、C# ファイル読取だけは現行 CLI の
  ローカル処理であり、JSON の経路に `local` と明記します。
- 最前面 PID を各サンプルの前後で確認し、背景条件は Finder を前面にします。
  専用 Game View は両条件とも `PlayUnfocused` にし、Play 開始時の自動前面化を防ぎます。
  終了時に元のウィンドウ設定へ戻し、この設定も履歴の比較条件に含めます。
  背景スクリーンショットだけは、既存撮影処理の `GameView.Focus()` により対象 Editor が前面化する
  動作を含めます。開始時は Finder が前面、終了時は同じ Finder または対象 Editor に限定し、
  第三者 PID への移動は許可しません。この例外を JSON 条件の `backgroundScreenshot` に記録します。
  フォーカス逸脱時は周全体を破棄して再計測し、有効な周だけを採用します。
  10 周の破棄、OS スクリーンショットへのフォールバックは失敗です。
  他 Agent の GUI 操作や重い import と同時に計測しません。

Editor の初期予算は [4 条件の実測](verification/issue-394-baseline.json) に基づきます。
操作・フォーカスごとに 2 Unity バージョンの大きい値を取り、p50 は 1.5 倍、p95 は 2 倍して
5ms 単位に切り上げます。背景の p50 のみ、切り上げ前に校正時の p95 を下限とします。
背景 Editor の更新待ちによる位相差で、同一 Unity6 バイナリの material search は p50 が
74.6→154.3ms に動く一方、p95 は 182.0→180.5ms と安定していました。元の FAIL 記録を保持し、
ベースライン JSON に比較値と校正理由を記録します。最前面と全 p95 予算は変更しません。
別途、履歴比で p50 が 20% を超えて劣化した場合も失敗とします。
既存の `editor_eval` と mock CLI の予算は変更していません。

実行コマンドは英語版の [Run](#run) に記載しています。
`cargo build --release --bin unity-cli` 後、
`scripts/e2e-matrix.py --suites perf` に 2 Editor と release CLI を指定してください。
`--perf-focus` の既定値 `both` を使い、毎回新しい出力先を指定します。
専用プロジェクトの `Assets/Scenes/Generated/E2E/Performance/` のみを変更し、
所有マーカーのないプロジェクトでは直接ベンチを実行できません。
このスイートだけなら LSP バイナリは不要です。
併せて既存の `editor_eval` ゲートも最前面で 100 回実行し、
`perf-eval.json`/`perf-eval.log` と同じ JSONL に別条件として記録します。
eval の既存の直接 TCP 経路は変更しません。

全結果を `.unity/perf/editor-ops-history.jsonl` に追記し、同じホスト・OS・CPU・
Unity・フォーカス・Play Mode 設定・計測方式で完全に計測できた直近 5 回の p50 中央値に
対して 20% を超える劣化でも失敗にします。5 回未満でも絶対予算は有効です。
予算超過でも完全な計測値は中央値に含め、操作エラーで不完全な回だけ除外します。
絶対予算はこのローリング中央値と独立に必ず適用します。
相対しきい値は `UNITY_CLI_PERF_REGRESSION_PERCENT` または
`--regression-percent` で上書きできます（絶対予算は無効化されません）。

失敗時は `violations` と条件を確認し、低負荷で再計測して実際の回帰を修正します。
意図的な変更なら実測のレビュー後に該当予算だけを更新してください。
環境変更に伴う基準の作り直しは旧 JSONL を保存し、別の `--history` を指定します。
失敗を隠すための履歴削除や予算引き上げはしません。
リリースでは通常履歴・既定の 20% を使用します。

`/release` は **2 Editor × 2 フォーカス条件の perf PASS を必須** とします。
PR 本文にコマンド・Unity 版・pass/fail 件数・ログ要約を記録してください。
最終コード変更後に再実行し、古い PASS を流用しません。
既存 `editor_eval` 予算（最前面、p50 ≤ 50 ms / p95 ≤ 100 ms）と LSP ゲートも維持します。
`benchmark.sh` と `perf-media-benchmark.sh` は調査用で、上記ゲートの代わりにはなりません。

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

この local runner には `codex login` と PATH 上の `unity-cli` が必要です。
`unity-cli tool schema --output json` をローカルで読み、スキルの例にない
ツール名とトップレベル引数もカタログへ補完します（Editor 不要）。
チェックアウトの実装を評価する場合は `cargo build` 後、runner command に
`--unity-cli ./target/debug/unity-cli` を追加します。schema 取得失敗時は
不完全なカタログで続行せず、その予測をエラーとして扱います。

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
