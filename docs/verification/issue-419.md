# Issue #419 — complete routing tool catalog

## Scope and result

The routing prompt previously extracted tools only from a few `SKILL.md`
command examples. It omitted 38 tool names expected by the checkout's 170
benchmark cases, including `load_scene`, `list_scenes`, and `clear_console`.
The runner now supplements those examples with all 152 tools and top-level
payload keys from the checkout's `tool schema --output json` output. Schema
variants and operation enums are preserved; nested object fields are excluded.
Schema lookup failures propagate rather than silently reducing the catalog.

| Metric | Before | After | Threshold |
| --- | --- | --- | --- |
| Skill top 1 | 98.88% | 100% | 90% |
| Skill top 2 | 98.88% | 100% | 98% |
| Tool correct | 84.27% | 98.88% | 92% |
| Payload keys valid | 86.52% | 96.63% | 95% |

All 178 cases were scored. Expectations, thresholds, and existing keyword
overrides were unchanged. Payload scoring remains the existing required-key
subset check; this is not a claim of exact argument values or Editor execution.
The remaining errors are recorded in `issue-419/comparison.json`.

## Measurement provenance

- Source baseline: `d8ebeff3456765b9b75ac13a008f6eca5631fcbe` (develop).
- Checkout benchmark: 170 cases. The additional eight cases and the
  `unity-package-management` skill come from #376's then-unmerged snapshot.
  They are retained in the evidence, not added as product changes in #419.
- Before: #376's 178-case `full-keywords-summary.json`, reused without
  alteration. The complete failure classification was posted to
  [Issue #419](https://github.com/akiojin/unity-cli/issues/419#issuecomment-5925648169).
- After: fresh Claude Opus 5.5 batch prediction using this runner's prompt
  builder, the checkout CLI schemas, and the above skill/case snapshot.
  Only case IDs and prompts were sent to the model, not expectations.
- `prompt.txt` is the exact input; `llm-predictions.jsonl` is the model output;
  `predictions.jsonl` applies the unchanged `route_by_keywords` overrides.
  `comparison.json` contains model usage, schema hash, metrics and failures.
- A preliminary run with the installed 149-tool CLI also passed, but the
  recorded final run uses the checkout's 152-tool CLI.
- The default `gpt-5.4-mini` model was unavailable for this ChatGPT account.
  An end-to-end runner smoke with `--model gpt-6-luna` correctly routed
  “Clear the Unity console.” to `unity-editor-tools` / `clear_console` / `[]`.
  See `codex-smoke.json`. The default model was not changed by this fix.

## Reproduce scoring

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --benchmark docs/verification/issue-419/cases.jsonl \
  --predictions docs/verification/issue-419/predictions.jsonl \
  --model claude-opus-5-5-with-existing-overrides
```

For fresh inference with the current checkout and configured Codex account:

```bash
cargo build
bash scripts/skill-eval/llm-routing-eval.sh \
  --runner-cmd 'python3 scripts/skill-eval/run-codex-routing.py --unity-cli ./target/debug/unity-cli --model gpt-6-luna' \
  --model gpt-6-luna
```

Fresh inference is nondeterministic and evaluates the current benchmark/skills;
the recorded batch above is the fixed 178-case comparison.

## Verification

- Routing regressions: 8 passed, including a real built-CLI check that every
  checkout benchmark tool and payload key appears in the canonical catalog.
- Python script suite, Rust formatting, skill contract lint, Markdown lint,
  and diff whitespace checks are included in final verification.
- Canonical `verify.run` was attempted with a one-second admission budget
  and deferred behind another host holder. Per #419's explicit Notes, the
  same matrix is run directly and PR CI is the final delivery gate.
- Skill contract lint: 17 skills, 0 violations.
- Rust, Unity bridge and LSP behavior are unchanged; their full test suites
  are left to the existing PR CI matrix.
- User Verification Result: n/a (autonomous).
- Agent Visual Check: n/a (no UI surface).
- CI delivery is tracked on #419; PR creation is delegated to PM as directed
  by the Issue's Notes.

## Parent specification

The scope and plan are recorded in
[SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5925648503).
