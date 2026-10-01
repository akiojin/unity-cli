# PR #437 — develop synchronization

Merged `origin/develop` at `da6eff09582b1201e49ba9c6bcc4d203c98fd306`
(including Localization PR #435) into `work/issue-380`.

Three shared skills conflicted: asset-management, editor-tools and
playmode-testing. Each retains both Audio and Localization sibling entries and
both `Do Not Use When` boundaries. The inventory and routing fixture merged
automatically; all 212 cases from each parent are preserved unchanged, producing
220 unique cases (8 Audio, 8 Localization). Both client symlinks remain valid.
Neither canonical Audio nor Localization skill content changed in this merge.

Fresh validation:

- Skills lint: 23 skills, zero violations.
- `cargo test --all-targets -- --test-threads=1`: 589 tests passed.
- `cargo fmt --all -- --check` and changed-Markdown lint: passed.
- Routing: fresh merged skill/tool catalogs, existing keyword rules followed by
  batch Claude predictions, with benchmark answers withheld from the prompt.
  All 220 cases scored; top-1/top-2 99.55%, tool correctness 99.09%, payload
  validity 96.36%; every configured aggregate threshold passed.
- Audio evidence verifier: passed, including unchanged canonical skill hashes.

Raw routing input/output and predictions are retained beside this file. Replay:

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --benchmark tests/fixtures/skill-routing/benchmark.jsonl \
  --predictions docs/verification/issue-380/merge/predictions.jsonl \
  --summary /tmp/issue380-merge-summary.json --history /tmp/issue380-merge-history.jsonl \
  --model claude-merged-catalog-existing-keywords --json
```

An additional check of the inherited Localization evidence verifier failed at
`Editor/Core/BridgeCommandRouter.cs`: its recorded SHA-256 begins `1a362f4a`,
while both develop and this merge contain `fca71ea6`. The bridge is identical to
both parents; this is an existing evidence-freshness mismatch, not a conflict
resolution change. It was reported to the PM for #381. No recorded hash was
rewritten and no new Localization runtime pass is claimed here.

Issue #380 remains open per the PM instruction.
