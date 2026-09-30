# Issue #394: latency gate verification

Measured on 2026-09-30 with release CLI v0.16.0 on Apple M5 Max, macOS 26.5,
arm64. This change adds measurement, CI and release gates; it does not modify
the Rust CLI or Bridge operation implementations.

## Real Editor commands and results

```bash
python3 scripts/e2e-matrix.py --suites perf \
  --unity-cli "$PWD/target/release/unity-cli" \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --editor /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  --output /tmp/p394-cal3 --port 6509

# First budgeted run; Unity 2022.3 passed, Unity 6 required the retry below.
python3 scripts/e2e-matrix.py --suites perf \
  --unity-cli "$PWD/target/release/unity-cli" \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --editor /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  --output /tmp/p394-verify1 --port 6509

python3 scripts/e2e-matrix.py --suites perf \
  --unity-cli "$PWD/target/release/unity-cli" \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --output /tmp/p394-verify2 --port 6509
```

All runs used isolated GUI projects and the host verification lease. Each focus
condition has 3 warmup cycles followed by 30 samples of each of 23 operations.
The 22 remote operations use warm unityd; C# read uses the existing local executor.
Play/Stop include polling until the requested state is observed. The CLI process
starts for every sample. The Game Views temporarily use `PlayUnfocused`, then
restore their original setting. Background screenshot measurement starts with
Finder frontmost and permits the existing capture handler to activate the target
Editor; other PID changes invalidate the whole cycle.

| Unity | Focus | Operation PASS / FAIL | Samples per operation | Discarded cycles | Artifact directory |
| --- | --- | --- | --- | --- | --- |
| 6000.3.25f1 | frontmost | 23 / 0 | 30 | 0 | `/tmp/p394-verify2/6000.3.25f1-33jyk7r7` |
| 6000.3.25f1 | background | 23 / 0 | 30 | 0 | `/tmp/p394-verify2/6000.3.25f1-33jyk7r7` |
| 2022.3.62f3 | frontmost | 23 / 0 | 30 | 0 | `/tmp/p394-verify1/2022.3.62f3-rbeo6zda` |
| 2022.3.62f3 | background | 23 / 0 | 30 | 0 | `/tmp/p394-verify1/2022.3.62f3-rbeo6zda` |

Each directory retains `perf.json`, `perf.log`, `perf-eval.json`, `perf-eval.log`
and `editor.log`. Portable results are committed in
[`issue-394-results.json`](issue-394-results.json).

| Frontmost eval | Samples | p50 / p95 (ms) | Budget p50 / p95 (ms) | Result |
| --- | --- | --- | --- | --- |
| 6000.3.25f1 | 100 | 6.34 / 9.53 | 50 / 100 | PASS |
| 2022.3.62f3 | 100 | 6.33 / 6.79 | 50 / 100 | PASS |

The Unity 6 retry returned exit 0 through canonical verification record
`vrr-6fb2d8e5041f4df0aaaa822552bd4675`. The earlier combined matrix retains its
exit 1 and Unity 6 failures; its Unity 2022.3 row is independently PASS.

## Calibration and retained failures

[`issue-394-baseline.json`](issue-394-baseline.json) contains all four initial
complete measurements, with zero discarded cycles. Those original calibration
runs returned FAIL only because Editor budgets had not yet been initialized.
For each focus/operation, budgets use the larger value across the two versions:
p50 times 1.5, p95 times 2, rounded up to 5 ms. Background p50 additionally uses
the original calibration p95 as its floor. Existing eval and mock budgets remain
unchanged. The relative gate remains greater than 20% against the median p50 of
the last five complete matching runs; unit tests cover its boundary and history
selection, including complete runs that failed absolute budgets.

In `/tmp/p394-verify1`, Unity 6 frontmost hit the 10-cycle discard limit because
external PIDs repeatedly became frontmost. Its background material search
completed at p50 154.3 ms against the original 115 ms budget; p95 was 180.5 ms,
matching calibration p95 182.0 ms. Other background operations moved in the
opposite direction, consistent with the phase of background Editor update waits.
PM decision `e553203b-73e9-4caa-99ab-4c2708df74dd` approved the background-only
p50 floor. The original FAIL JSON is unchanged. The subsequent Unity 6 run passed
both conditions and eval, without discarding samples.

Earlier focus/transport failures and overlapping host measurements were excluded
from initial calibration and retained locally. The history distinguishes the
current screenshot/focus policy with `suite_version: 3`.

## Mock transport and supporting checks

```bash
python3 scripts/bench-cli-latency.py --out .unity/perf/cli-normal-exclusive.json
python3 scripts/bench-cli-latency.py --delay-ms 30 --out .unity/perf/cli-delayed-v016.json
python3 -m unittest discover -s tests/scripts -p 'test_*.py'
node --test tests/scripts/*.mjs
target/release/unity-cli skills lint --severity error
cargo fmt --all -- --check
```

- Normal mock: 8 cases, 50 samples each, all PASS; p50 6.3–7.2 ms and p95
  6.9–8.9 ms. Direct TCP used 159 connections; warm unityd used one connection.
- Injected 30 ms delay: all 8 cases completed with 50 samples, ping p50 46–47 ms;
  the benchmark returned exit 1. CI checks the expected failure and uploads both
  normal and delayed reports, so an unrelated crash cannot satisfy the proof.
- Python contracts cover percentiles, absolute limits, missing budgets, five-run
  regression history, interrupted runs, semantic responses, focus handling,
  Game View restoration and matrix integration. The optional CLI integration
  unittest is skipped by default; CI runs its normal/delayed measurements explicitly.
- Markdown lint, JSON/YAML formatting, skill lint and affected script tests are
  included in final verification.

The measured runtime commits are `0b453af` (2022.3) and `eb46714` (Unity 6).
Subsequent base synchronization includes the independently verified Prefab APIs
from #395. Inspection confirmed additive Prefab routing/catalog changes; the
23 measured operation handlers, performance scripts, budgets and measured binary
are unchanged. PR CI builds and tests the synchronized CLI.

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no product UI change; real GUI performance measured above)
