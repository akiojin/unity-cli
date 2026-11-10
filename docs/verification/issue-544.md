# Issue #544: Windows Editor compatibility

Measured on Windows x64, 2026-11-10 UTC. CLI/Bridge version fields were 0.19.0;
results apply to the updated checkout, including these compatibility fixes,
identified by the source fingerprint below. Agent: サト.

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)

## Scope and source

The retained matrix measures Bridge compilation, matching version ping, input,
Timeline, VFX, eval including the 1,000-evaluation bounded-resource soak,
optional hot-reload rejection, PlayMode tests with Domain Reload on/off,
all-tools including LSP performance, and two focused EditMode fixtures.
The report records `full_matrix: false` because the additional extended suites
are not selected. Passing baseline results do not establish Player builds,
bakes, media formats, reference downloads or real method replacement.

`issue-544/metadata.json` records the measured producer-source fingerprint and
CLI binary SHA256. `python docs/verification/issue-544/verify_evidence.py` rejects
source drift and checks the retained JSON, suite transcripts and NUnit XML.
Canonical pre-PR verification must also execute the complete registered matrix;
the source-bound runtime evidence does not replace that gate.

## Measured results

All three runs passed on 2026-11-10 (Windows 11 x64). Times below are UTC;
fractional timestamps, commands, return codes, package versions, compilation
state and matching pings are retained in [matrix.json](issue-544/matrix.json).

| Editor | Runtime | Started UTC | Finished UTC | Duration (seconds) | Result |
| --- | --- | --- | --- | --- | --- |
| 7000.0.0a16 | CoreCLR | 11:01:12 | 11:10:31 | 559.01 | PASS |
| 6000.3.28f1 | Mono | 11:10:31 | 11:17:16 | 405.02 | PASS |
| 6000.6.8f1 | Mono | 11:17:16 | 11:23:49 | 393.41 | PASS |

Each Editor passed all nine selected suites: compilation, six baseline suites,
all-tools and focused Prefab/identity tests. Compilation errors were zero and
`system ping` reported CLI/Bridge version match. Compilation warnings are
retained in the report; zero warnings is not claimed.

| Check, per Editor | Passed result |
| --- | --- |
| Input | 39 calls |
| Timeline | 40 checks |
| VFX | 64 checks; 6000.3: 17.3.0, 6000.6: 17.6.0, 7000: 17.7.0 |
| Eval | 18 checks, including 1,000 consecutive evaluations |
| Optional hot-reload missing-package contract | 8 checks |
| PlayMode, Domain Reload enabled / disabled | 2 / 2 real tests |
| All-tools | 123 unique tools, 135 passed invocations, zero failures |
| LSP performance | 6 cases passed, 5 measured runs and 1 warmup each |
| PrefabWorkflowHandlerTests / ObjectIdentityTests | 23 / 3 passed, zero failed or skipped |

The all-tools catalog contains 154 tools: its 31 exclusions (including the
intentional `quit_editor` skip) are listed separately and do not count as passes.
The three eval soaks also stayed within the existing resource limits:

| Editor | Emitted / loaded assemblies | Managed memory growth (bytes) | Stored results |
| --- | --- | --- | --- |
| 7000.0.0a16 | 4 / 4 | 1,743,040 | 256 |
| 6000.3.28f1 | 4 / 4 | 4,034,560 | 256 |
| 6000.6.8f1 | 4 / 4 | 6,135,808 | 256 |

The bounds remain 8 emitted assemblies, 16 loaded assemblies, 64 MiB managed
memory growth and 256 stored results. The retained verifier checks the actual
soak values and every LSP p95 against the original thresholds.

## Plan and reproduction

Changed surfaces are the Windows CLI daemon transport, Editor compatibility,
test infrastructure and documentation. No UI is changed. The selected runners
are Cargo, dotnet test, Python unittest, the real Unity Editor and markdownlint.

After building the CLI and publishing the LSP as described in
[development.md](../development.md#editor-version-matrix), reproduce the runtime
acceptance with native Python from Git Bash:

```bash
python scripts/e2e-matrix.py \
  --editor 'C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe' \
  --editor 'C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe' \
  --editor 'C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe' \
  --lsp-root .cache/issue-544/tools --output .cache/windows-matrix \
  --port 6548 --suites input,timeline,vfx,eval,hot-reload,reload,all-tools,prefab-tests
```

Canonical pre-PR verification invokes the same selection once per Editor,
with separate output directories, to fit the host's 20-minute command limit.
The retained direct run above also exercises transitions between Editors using
the same private daemons.

The registered pre-PR plan also includes the repository's five quality gates:
`cargo fmt --all -- --check`, `cargo clippy --all-targets -- -D warnings`,
`cargo test --all-targets -- --test-threads=1`,
`cargo run -- skills lint --severity error` and
`dotnet test lsp/Server.Tests.csproj`. The harness regressions run with
`python -m unittest discover -s tests/scripts -p test_e2e_matrix.py`;
the retained runtime evidence is checked with
`python docs/verification/issue-544/verify_evidence.py`.
Markdownlint checks README, development instructions, the compatibility matrix
and this evidence page. These gates cover the changed transport, the shared LSP,
platform harness behavior and the acceptance documentation.

## Acceptance mapping

| Criterion | Evidence |
| --- | --- |
| AC-1 | 7000.0.0a16 compilation state with zero errors and matching CLI/Bridge ping in the retained matrix |
| AC-2 | All three native Hub paths in the actual matrix, plus platform layout/default/ownership contract tests |
| AC-3 | Two Unity 6 baseline and all-tools transcripts, with UTC start/finish times and assertion counts |
| AC-4 | Unity 7 baseline and all-tools transcripts, runtime probes and focused NUnit XML |
| AC-5 | The dated Windows compatibility table and README support scope, checked by markdownlint |

Each suite transcript lists its individual checks. The two EditMode XML files
list every NUnit test and result; `reload.json` includes the two PlayMode cases
for each Domain Reload setting. The matrix records each exact command and exit
code. Each Editor's `lsp-performance.json` lists the six cases, five measured
runs after one warmup, response sizes, p95 latency and unchanged thresholds.
The automatic canonical run record retains the quality-gate outputs.

## Root causes and regression coverage

- Windows Hub layout has `Editor/Data` instead of the macOS app bundle layout.
  The runner now discovers all three installed versions and inspects native
  TCP listener ownership before accepting Bridge readiness.
- Windows daemon TCP endpoints formerly shared fixed global ports. The runner
  now selects two private loopback ports, isolates the Editor registry, clears
  inherited authentication, and reads only the owned Editor's token file.
  Invalid configured ports fail closed; existing default ports stay unchanged.
- Native jq under Git Bash converted Unity paths and emitted CRLF. The shell
  initialization preserves JSON argument values while translating actual file
  inputs, and requests binary/LF output. Python metrics use UTF-8 explicitly.
- Unity 7 requires newer optional package versions from its bundled catalog.
  Reusing Addressables 2.9.1 failed with CS0052 before Bridge compilation.
- The Prefab handler's six legacy identity calls caused CS0619 on newer Editors.
  Issue #546 owns the compatibility helper migration; the Windows matrix also
  checks the listed component's wire ID resolves to the same component for a
  real property revert.
- Unity 7 removed ProgressiveCPU; the common fixture now uses ProgressiveGPU
  under `UNITY_7000_0_OR_NEWER`. This verifies fixture compilation, not a bake run.
- Unity 7 Windows runs CoreCLR. A completed negative Mono probe alone is
  insufficient; the runner additionally requires `System.Private.CoreLib`.
- A Python dependency warning on stderr corrupted valid LSP size-metrics JSON
  when both streams were merged. Metrics parsing now uses stdout, retains
  diagnostics in the log, and still fails on a nonzero producer exit.
- A client disconnect while lspd wrote its response terminated the entire
  daemon. The next Editor's LSP checks then failed to restart it on Windows
  with socket error 10048. Per-connection failures now leave the listener
  running; a real-socket regression sends a malformed request, then requires
  the same listener to answer a new ping and stop normally.
  A native Windows probe also forces TCP reset (10054) and checks that the
  same daemon still answers ping and exits successfully on stop; its report
  is retained as `issue-544/lspd-native-reset.json`.
- Focused EditMode fixtures can leave a new unnamed scene after teardown.
  Their successful NUnit results are followed by no automatic scene save;
  saving that disposable scene without a path caused a false harness failure
  on 6000.3.28f1 after all nine suites had already passed.
- A private Editor credential file was briefly absent after a Unity 7 reload
  during the 1,000-evaluation soak. Before opening TCP, the client now waits
  only for that same explicitly configured file, only on file-not-found, within
  the existing connection timeout. Malformed/denied credentials fail directly.
  No evaluation is replayed and no other Editor's token is used. Regressions
  cover delayed publication and a bounded permanent absence with no TCP request.

The Python contract suite covers the platform layouts, package selection,
listener ownership, strict ping/runtime evidence, timeout cleanup, isolated
environment, real native jq paths/files/LF, UTF-8 metrics, and warning/nonzero
metrics distinction. The Rust suite checks configurable daemon port validation
and JSON envelope failures with isolated TCP ports.

## Limits

The all-tools sweep explicitly excludes fixture-dependent tools and skips
`quit_editor` so subsequent NUnit fixtures can run. Exclusions are listed in
each retained all-tools transcript and do not count as passes. Dedicated VFX
and focused Prefab tests provide additional checks beyond this sweep.
Hot reload tests expect `HOT_RELOAD_PACKAGE_MISSING`; optional method replacement
and its CoreCLR compatibility remain unmeasured. Graphics are enabled for VFX;
no CoreCLR Player/backend build is claimed. Alpha support applies only to the
recorded exact Editor and catalog versions. Failed intermediate compiler and
harness runs are retained separately and are not counted as successful retries.
Private Editor authentication files and Editor license logs are not published.
