# Issue #375 — Project bootstrap skill verification

The change adds `unity-project-bootstrap`, its Claude/Codex discovery links,
adjacent-skill boundaries and SR-158–163 routing cases. It uses the existing
Editor CLI and unity-cli operations; no Rust or bridge runtime code changes.

## Acceptance audit

Before changes, all five acceptance criteria were unmet: no canonical skill,
links or inventory entry; no Claude Code scenario evidence; no routing cases;
no authorship review/PR; no #375 extension in SPEC #160. There was no linked PR.

The parent SPEC uses a legacy body that `issue.spec.edit` cannot roundtrip.
The PM updated AC-5 to permit a comment. Requirements, design and tasks are in
[SPEC #160's extension](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5924768584).

## Static and routing checks

- Skill Contract v1: 17 skills, 0 violations.
- `cargo test --all-targets -- --test-threads=1`: 571 passed.
- `python3 -m unittest discover -s tests/scripts -p test_skill_routing.py`: 3 passed.
- `cargo fmt --all -- --check`, `cargo clippy --all-targets -- -D warnings`
  and Markdown lint: passed.
- New routing cases: RED 3/6 top-1 correct without the skill; GREEN 6/6
  for skill, tool and payload keys with the skill.

Routing predictions were produced by Claude Code (model `claude-opus-5-5`),
with only the catalog and prompts, without expected answers or tool access.
The existing `llm-routing-eval.sh` scores the prediction JSONL. These are
recorded LLM measurements, not deterministic assertions about future responses.

An additional full-catalog measurement found pre-existing coverage gaps in the
evaluation catalog. With the runner's existing keyword overrides applied:

| Catalog | Cases | Top-1 | Top-2 | Tool | Payload |
| --- | ---: | ---: | ---: | ---: | ---: |
| Before #375 | 157 | 99.36% | 100% | 83.44% | 86.62% |
| After #375 | 163 | 98.77% | 98.77% | 84.66% | 87.12% |
| New #375 cases only | 6 | 100% | 100% | 100% | 100% |

The complete benchmark fails its tool (92%) and payload (95%) thresholds even
before this change. Examples include `load_scene`, `list_scenes` and
`clear_console` missing from the catalog hints. This report does not claim a
full benchmark PASS. No unrelated runner/skill changes were made to tune those
results. The first full-catalog measurement without keyword overrides is also
retained in the local evidence directory.

## Real Editor scenario

Claude Code invoked `/unity-project-bootstrap` through the Skill tool in an
isolated `/tmp/issue375-harness` with symlinks to the canonical skill sources.
The CLI was built from this checkout (0.16.0); `setup` installed the released
bridge 0.16.0 from OpenUPM. No local bridge package was injected.

The scenario creates a new built-in 3D project, installs the bridge, verifies
project-matched ping and Input System resolution, creates/saves
`Assets/Scenes/Generated/E2E/Bootstrap.unity`, enters Play, reads state/console,
captures and inspects the Game image, then stops Play and closes only its own
Editor. Both requested Editors run natively on macOS Apple Silicon.

The exploratory Unity 6 run exposed `primitiveType: "Cube"` in the new example
and its scene-authoring sibling. CLI schema requires lowercase `"cube"`.
Both examples were corrected; final evidence comes from fresh projects after
that correction. A separate optional `setup --launch-editor --dry-run`
preview waited for a connection without launching; that existing setup issue
was reported to the #363 owner and is outside this skill's required flow.

Final per-version evidence and screenshots are collected under `issue-375/`:

| Editor | Input System | Result | Evidence |
| --- | --- | --- | --- |
| 6000.3.25f1 | 1.20.0 | PASS | [Manifest](issue-375/6000.3.25f1/manifest.json), [scene](issue-375/6000.3.25f1/Bootstrap.unity), [Game image](issue-375/6000.3.25f1/game.png), [result](issue-375/6000.3.25f1/result.json) |
| 2022.3.62f3 | 1.14.2 | PASS | [Manifest](issue-375/2022.3.62f3/manifest.json), [scene](issue-375/2022.3.62f3/Bootstrap.unity), [Game image](issue-375/2022.3.62f3/game.png), [result](issue-375/2022.3.62f3/result.json) |

Each directory also contains setup/ping/state/console JSON, the package lock,
commands, Claude Skill/tool calls, a creation-log excerpt and SHA-256 checksums.
The complete original logs remain in `/tmp/issue375-evidence/<version>-final/`.
`normalization.json` records removal of the test shell's trailing `exit=0`
from selected JSON outputs; the JSON payloads are unchanged. The image and
scene are copied without editing. `skill-hashes.json` identifies the tested
skill/reference sources.

Re-score the scoped routing evidence with:

```bash
scripts/skill-eval/llm-routing-eval.sh \
  --benchmark docs/verification/issue-375/routing/cases.jsonl \
  --predictions docs/verification/issue-375/routing/predictions.jsonl \
  --model claude-opus-5-5 \
  --summary /tmp/issue375-routing-summary.json \
  --history /tmp/issue375-routing-history.jsonl
```

To repeat live verification, invoke `/unity-project-bootstrap` in Claude Code
with a new empty project path, the selected installed Editor and an unused
explicit port. Run the scenario above. The per-version `commands.txt` records
the actual Editor/CLI invocations used here (6535 and 6536 respectively).

The final Unity 6 console contains zero errors/exceptions and one Editor-internal
QuickSearch startup assertion (`SearchDatabase.EnumerateAll`,
`SearchInit.IndexationOnStartup`). The raw console/result files retain it;
this is not reported as a completely empty console. Scene creation, Play,
capture and Stop all succeeded with zero compilation errors.

## Authorship review

Reviewed the added skill/reference and adjacent-skill diffs against the task
and local command schemas. The workflow text is independently authored. The
only copied reference is this repository's own `unity-cli-usage` runtime
checklist. No text or code from the official `unity-agent-plugin` skills was
copied or adapted (D6). Sources are the public Editor/Hub manuals linked in
the creation reference and this repository's CLI/tool contracts.

User Verification Result: n/a (autonomous). Agent visual verification uses
actual Unity Game images; it is not human confirmation.
