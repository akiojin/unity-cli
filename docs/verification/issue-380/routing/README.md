# Audio skill routing evidence

The added `AUD380-*` cases cover three English/Japanese orchestration requests
and five boundaries: import-only, component-only, scene-only, Play-only and
console-only. Expected answers were withheld from the model prompts.

- `red-*`: catalog without the new skill. The first frontmatter-only probe routed
  all three positive cases to generic asset management. `red-final-*` repeats the
  test with the complete baseline skill bodies: top-1 5/8, positives still fail.
- `green2-*`: final unambiguous eight-case test, complete skill bodies, no keyword
  override: top-1/top-2/tool/payload all 8/8. The initial component-only wording
  allowed both `modify_component` and `set_component_field`; the fixture now
  explicitly requests `set_component_field`, matching the single-tool scorer.
- `full-*`: exploratory 212-case model-only run without the canonical tool catalog.
  Tool/payload thresholds fail; retained to avoid hiding the failed experiment.
- `canonical-*`: full 212-case evaluation using the repository's unchanged
  `route_by_keywords`, `format_skill_catalog`, `load_tool_catalog`, and
  `build_prompt` from `scripts/skill-eval/run-codex-routing.py`. The fallback is a
  batch Claude Code call rather than the script's per-request Codex call. The raw
  response and unmodified LLM predictions are retained separately; keyword rules
  run first, as in the repository runner. No new keyword rule was added.

Canonical results: top-1/top-2 99.53%, tool correctness 99.06%, payload validity
95.75%; all configured aggregate thresholds pass. The eight new cases are each
fully correct. These aggregate gates do not claim every legacy case is correct.

Replay the stored predictions (no model invocation required):

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --benchmark docs/verification/issue-380/routing/cases.jsonl \
  --predictions docs/verification/issue-380/routing/green2-predictions.jsonl \
  --summary /tmp/issue380-focused-summary.json --history /tmp/issue380-routing-history.jsonl \
  --model claude --json
bash scripts/skill-eval/llm-routing-eval.sh \
  --benchmark tests/fixtures/skill-routing/benchmark.jsonl \
  --predictions docs/verification/issue-380/routing/canonical-predictions.jsonl \
  --summary /tmp/issue380-full-summary.json --history /tmp/issue380-routing-history.jsonl \
  --model claude-canonical-tools-existing-keywords --json
```

Claude Code version: 2.1.286. The actual model identifiers, token usage and result
status are preserved in each `*-raw.json` response. Prompts contain catalog and
request text only, not benchmark answers. The later audio reference diagnostics
do not change the routing input (descriptions, use/boundary sections and tool hints).
