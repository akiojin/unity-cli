# Issue #392: extended Editor matrix evidence

Measured on 2026-09-30, macOS Apple Silicon. The checkout CLI is v0.16.0;
the Bridge source is based on develop `d22b743`. This change modifies test
runners and documentation, with no Rust CLI or Bridge product changes.

## Commands and retained evidence

```bash
python3 scripts/e2e-matrix.py \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --editor /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  --lsp-root /tmp/issue340-final-a/tools --port 6532 \
  --output /tmp/issue392-final

python3 scripts/e2e-matrix.py \
  --editor /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  --lsp-root /tmp/issue340-final-a/tools --port 6532 \
  --output /tmp/issue392-retry
```

The runner uses `target/debug/unity-cli`, isolated project copies and an isolated
daemon/tools directory. Each Editor runs compilation plus all thirteen default
suites. Input Actions persistence starts its own Editor twice on port 6533.
An installed Windows module selects another isolated host on port 6534.

The first command retained a **FAIL** aggregate: its 6000.3 persistence test
failed after 42 successful assertions because its port availability probe
mistook TCP `TIME_WAIT` after Editor shutdown for a live listener. The Unix
probe now uses `SO_REUSEADDR`; regression tests check both closed-connection
reuse and rejection of a live listener. The first report remains unchanged.

The 2022.3 persistence subprocess started at 11:22:56 UTC, after the corrected
source snapshot at 11:22:19 UTC. Comparing 716 runtime-source hashes before
the first command and after the fix found only
`scripts/e2e-input-actions-persistence.py` changed. Thus its other suites used
unchanged source, and its persistence suite used the corrected script.
6000.3 was rerun in full after that correction: the six additional suites
completed, but the existing all-tools LSP performance gate failed as detailed
below. That second full report also remains FAIL.

Local evidence locations (temporary machine-local paths, not portable links):

- Initial report: `/tmp/issue392-final/matrix.json` (FAIL; retained diagnostic).
- Accepted 2022.3 row: `/tmp/issue392-final/2022.3.62f3-kw_4rd7x/result.json`.
- Corrected 6000.3 run: `/tmp/issue392-retry/matrix.json` (FAIL; LSP performance).
- 6000.3 row: `/tmp/issue392-retry/6000.3.25f1-63vldoqf/result.json`.
- Source snapshots: `/tmp/issue392-source-before.json` and
  `/tmp/issue392-source-after-fix.json`.

## Results

Counts below are successful/failed assertions, except all-tools, which counts
unique tools and additionally reports invocation counts. An error-contract
assertion succeeding does not mean the unsupported feature succeeded.

| Suite | 2022.3.62f3 | 6000.3.25f1 |
| --- | --- | --- |
| Bake, all four types | PASS, 103/0 | PASS, 104/0 |
| macOS Player build and launch | PASS, 18/0 | PASS, 18/0 |
| Windows Player build | UNSUPPORTED, 6/0 contract checks | UNSUPPORTED, 6/0 contract checks |
| Video formats | PASS, 6/0 | PASS, 6/0 |
| Animation curves | PASS, 25/0 | PASS, 25/0 |
| Input Actions persistence | PASS, 50/0, two Editor starts | PASS, 50/0, two Editor starts |
| Reference fetch + resolution | PASS, 5/0 + 4/0 | PASS, 5/0 + 4/0 |
| All-tools | PASS, 122/0 tools, 134/0 calls | FAIL: LSP performance; 122 tools / 134 calls passed |

The accepted 2022.3 row contains 13 PASS items and one UNSUPPORTED item
(`player-build`, comprising a passed macOS step and unsupported Windows step).
The aggregate is therefore **UNSUPPORTED**, not PASS. Only
`BUILD_MODULE_MISSING` with a fresh successful contract summary and retained
Bridge response is accepted as UNSUPPORTED; other nonzero exits fail the suite.

The 6000.3 rerun contains twelve PASS items, one UNSUPPORTED item and one FAIL
item. The existing LSP performance gate measured `find_refs` p95 **7,877 ms**
against its unchanged **4,000 ms** threshold; the other five performance cases
passed. The following functional all-tools sweep passed all 122 called tools
(134 calls). Its summary counts the performance failure separately, yielding
134 passed / 1 failed invocations. No overall 6000.3 PASS is claimed.
The full performance output is retained in
`/tmp/unity-cli-e2e-all-tools-20260930-203355.log` and the machine-local
`.unity/perf/lsp-history.jsonl`. In 2022.3, the same `find_refs` check measured
p95 308 ms. The source hashes are unchanged between these runs; the reason
for the timing difference has not yet been established.

The PM ruled on 2026-09-30 that the six added suites meet #392's acceptance
scope and that the existing LSP timing failure does not block this PR. After
Issues #394 and #395 release the shared host, the LSP-only performance check will run
three times without an Editor. Results will be appended to the PR; persistent
threshold failures will be handed to the PM for a separate regression Issue.
The threshold and original FAIL reports remain unchanged.

## Acceptance boundaries

- Bake uses lighting, legacy NavMesh, NavMeshSurface and occlusion fixtures.
  Each checks generated files, reloads the scene and validates persisted
  references. Navigation fixtures also assert `NavMesh.SamplePosition`.
  Terminal results remain queryable after reload; rebake and empty-scene
  rejection checks run as well.
- macOS Player checks the queued-to-succeeded lifecycle, BuildReport and
  output/data agreement, CLI/TCP report consistency and a live headless Player.
- Both installed Editors have only Mac Build Support. Real
  `StandaloneWindows64` requests return `BUILD_MODULE_MISSING`, as permitted
  by AC-3. No Windows executable or successful cross-build is claimed. When
  Windows Build Support exists, the runner selects Win64 before launch and
  requires matching `.exe`, `_Data` and BuildReport artifacts. Host-free tests
  cover those artifact checks; they do not substitute for a real Windows build.
- Video checks MP4, WebM and PNG sequence file signatures and session lifecycle.
- Animation checks read/edit/remove behavior and that rejected edits preserve
  the original clip.
- Input Actions checks serialized contents and stable IDs after each mutation,
  reimport and full Editor restart, including control schemes and cleanup.
- Reference checks cached provenance, explicit version/branch and real-project
  resolution, including exact 6000.4.12f1 and explicit public-ref cases.
- Optional hot reload checks `HOT_RELOAD_PACKAGE_MISSING`; it does not prove
  method replacement. All-tools exclusions remain explicit.

Only these two Editor versions have the extended results recorded here. The
other six baseline rows remain unmeasured for these additional suites.
Windows hardware execution remains under [#386](https://github.com/akiojin/unity-cli/issues/386).
Tool examples and skill discovery are supplied by
[#393](https://github.com/akiojin/unity-cli/issues/393), merged in PR #402.
The domain owners retain change records:
[build #152](https://github.com/akiojin/unity-cli/issues/152#issuecomment-5910340771),
[bake/animation #148](https://github.com/akiojin/unity-cli/issues/148#issuecomment-5910341331),
[capture #150](https://github.com/akiojin/unity-cli/issues/150#issuecomment-5910342005),
[input #147](https://github.com/akiojin/unity-cli/issues/147#issuecomment-5910342473),
[reference #204](https://github.com/akiojin/unity-cli/issues/204#issuecomment-5910342987).

## Runner checks

```bash
python3 -m unittest discover -s tests/scripts -p 'test_e2e*.py'
bash -n scripts/e2e-animation-curves.sh scripts/e2e-reference-fetch.sh scripts/e2e-reference-resolution.sh
target/debug/unity-cli skills lint --severity error
git diff --check
```

The Python checks cover matrix routing and failure classification (11), Player
target/artifact validation (6), and actual socket restart behavior (2): 19 passed.
User Verification Result: n/a (autonomous). Agent Visual Check: n/a (no UI surface).
