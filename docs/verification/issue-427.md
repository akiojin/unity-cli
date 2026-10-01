# URP skill integration verification (#427)

## Acceptance audit and delivery scope

The initial checkout (`edd1e54`) did not contain `unity-urp-setup`. AC-1 through
AC-4 were not delivered to develop. AC-5 was already recorded in
[SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5905094372).
The requested handoff commit `b57047b` was fast-forwarded into `work/issue-427`.
This replacement for PR #421 preserves the existing implementation and addresses
its remaining integration and delivery work.

The canonical skill, both symlinks, skill list, eight URP routing cases and the
UPM/source-navigation regression tests are inherited from #378. No additional
CLI or bridge behavior was added during this handoff.

## Real Editor evidence and provenance

[The original report](issue-378.md) contains the Claude Code installation and
rerun transcripts, saved assets, independent state queries and restart checks
for macOS Apple Silicon, Unity 2022.3.62f3 and 6000.3.25f1.
`python3 docs/verification/issue-378/check-evidence.py` passes for both versions
and confirms that the recorded skill hashes match this checkout. CLI/bridge and
URP skill sources are unchanged between `807789e` and the handoff commit.
The four committed PNGs were inspected again: Bloom adds a visible halo around
the stationary bright subject with unchanged framing in each Editor.
This is a validation of inherited real-Editor evidence, not a claim that Unity
was rerun for #427. The original Unity 6000.3 Search indexer Assert remains a
documented limitation.

- User Verification Result: n/a (autonomous).
- Agent Visual Check: pass (inherited Game Camera before/after PNGs inspected).
- D6: the inherited skill and reference were independently authored; no official
  `unity-agent-plugin/skills/` text or code was reused. The runtime checklist is
  reused from this repository. The prior PR records the author's provenance
  declaration, but has no submitted GitHub reviews. The replacement PR must
  record the source/provenance review explicitly.

## Fresh routing evaluation after integration

The previous 187-case routing evidence predates the UI Toolkit skill integration.
Its catalog no longer matches this checkout, so it is retained only as historical
evidence. A fresh Claude Code batch (`claude-opus-5-5`, no tools) evaluated the
current 20-skill catalog and all 195 cases. The prompt contains case IDs and user
requests, with no expected answers. The canonical tool catalog is unchanged.
The existing runner's keyword rules are applied to the raw model predictions,
exactly as in the normal runner; no selections or benchmark expectations were
manually corrected.

| Metric | Result | Required |
| --- | --- | --- |
| Top-1 | 99.49% | 90% |
| Top-2 | 99.49% | 98% |
| Tool | 98.46% | 92% |
| Payload | 96.92% | 95% |

All eight `SR-URP-*` cases pass all four metrics. The complete suite passes its
existing thresholds; it is not perfect. The report retains failures for SR-016,
SR-087, SR-116, SR-119, SR-134, UI377-06 and UI377-07.

Evidence: [prompt](issue-427/routing/prompt.txt),
[raw response](issue-427/routing/raw.json),
[model predictions](issue-427/routing/llm-predictions.jsonl),
[runner predictions](issue-427/routing/predictions.jsonl),
[case snapshot](issue-427/routing/cases.jsonl),
[score report](issue-427/routing/summary.json).

Re-score the recorded predictions against the current benchmark:

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --predictions docs/verification/issue-427/routing/predictions.jsonl \
  --model claude-opus-5-5 \
  --history /tmp/issue427-routing-history.jsonl \
  --summary /tmp/issue427-routing-summary.json
```

This command re-scores the fresh recorded inference; it does not invoke the model
again. The prompt and case snapshot must still match the checkout when reusing it.

## Integration checks (2026-10-01)

Mode: pre-pr. Launch mode: autonomous.
Baseline: `origin/develop` after merging the C# post-write integration.
Changed surfaces: skill assets, Python routing evaluator/tests, documentation.
Acceptance Surface: Unity Game rendering, covered by the unchanged URP skill's
recorded real-Editor runs and the renewed asset/state/hash and image inspection.
No browser UI or Unity rendering implementation changed in this PR.

| Command | Result / inventory |
| --- | --- |
| `cargo fmt --all -- --check` | PASS |
| `cargo clippy --all-targets -- -D warnings` | PASS |
| `cargo test --all-targets -- --test-threads=1` | PASS: 588 tests (554 unit, 34 integration) |
| `cargo run -- skills lint --severity error` | PASS: 20 skills, 0 violations |
| `python3 -m unittest discover -s tests/scripts -p 'test_*.py'` | PASS: 51 tests, 1 existing skip |
| `dotnet test lsp/Server.Tests.csproj` | PASS: 53 tests |
| `python3 docs/verification/issue-378/check-evidence.py` | PASS: both Editors and skill hashes |
| Recorded routing re-score (command above) | PASS: 195 cases, all thresholds |
| `pnpm exec markdownlint docs/verification/issue-427.md --config .markdownlint.json` | PASS |

Rust integration suites cover bridge dependencies, build failure output, C#
post-write compilation, installation, setup dry-run, skill installation, tool
filters and daemon startup. Python includes the two inherited regressions for
UPM package lists versus C# source package navigation. The recorded routing
case snapshot is byte-identical to the benchmark and its prompt contains the
current skill catalog. The source/provenance review confirms the skill uses
this repository's tool names and workflow contract; the runtime checklist is
explicitly attributed to this repository, with no official-plugin source in
the change. The author provenance declaration is preserved above.

These are fresh local checks. Canonical verification is run again after the
delivery commit/push; its actual result belongs in the replacement PR rather
than being inferred from this report. CI and merge remain delivery gates.
