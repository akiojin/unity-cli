# Skill editing policy verification (#366)

## Scope and environment

- Executed 2026-10-01 on macOS, arm64, with real GUI Editors.
- Unity 6000.3.25f1 / 2022.3.62f3, CLI and bridge 0.16.0.
- Dedicated projects `/tmp/issue366-unity-<version>`, ports 6491 / 6492.
- Claude Code model: `claude-opus-5-5`; fresh sessions loaded this checkout's plugin.
- User Verification Result: n/a (autonomous).
- Agent Visual Check: n/a (no UI implementation change).
- No official Unity plugin text or code was copied.

## Acceptance audit

| Criterion | Evidence | Result |
| --- | --- | --- |
| AC-1 | Existing `unity-cli-usage` already documents install.sh, setup and bridge installation; preserved without rewriting its bootstrap flow. | PASS |
| AC-2 | Four editing skills now require target ping, prohibit reachable-Editor YAML edits, explain sandbox uncertainty and require a fallback declaration. | PASS |
| AC-3 | `cargo run --quiet -- skills lint --severity error`: 16 skills, 0 violations. | PASS |
| AC-4 | Both real Editor transcripts select `unity-gameobject-edit` and call `add_component`; no direct file-edit tools or YAML-writing shell commands occur. Reload confirms persistence. | PASS |
| AC-5 | SR-158–SR-164: 7/7 correct for top-1, top-2, tool and payload keys using the existing scorer. | PASS |
| AC-6 | Requirements, plan and tasks recorded in [parent SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5924900973). | PASS |

Before editing, Claude Code's retrieval check marked the YAML prohibition,
sandbox confirmation, fallback declaration and existing-Player `add_component`
instructions as missing. The same check after editing found all four. Separate
scenario checks rejected direct YAML edits when the Editor is reachable,
requested connection confirmation for a sandbox permission error, and declared
the method and affected files before a confirmed offline repair.

## Real Editor transcripts

Prepare a dedicated project for each version with this checkout's bridge,
create an empty Player using `create_gameobject`, and save the scene under
`Assets/Scenes/Generated/E2E/Issue366.unity`. Put the built CLI on PATH and set
`UNITY_PROJECT_ROOT` / `UNITY_CLI_PORT` to the dedicated project / port. From that
project, start a new session:

```bash
claude -p --plugin-dir <checkout>/.claude-plugin/plugins/unity-cli \
  --setting-sources '' --allowedTools Bash,Read,Glob,Grep,Skill,Edit,Write \
  --output-format stream-json --verbose 'Player に Rigidbody を付けて'
```

Both sessions could use Edit/Write tools; neither used them. The transcripts
contain the actual tool inputs and responses, including the loaded skill,
successful ping, Transform-only initial state, successful Rigidbody addition and
scene save. Independent `load_scene` / `get_gameobject_details` calls verify the
saved state afterward.

| Editor | Session | Tools used | Saved Rigidbody |
| --- | --- | --- | --- |
| 6000.3.25f1 | See [transcript](issue-366/6000.3.25f1.json) | Skill, Bash, Bash | PASS |
| 2022.3.62f3 | See [transcript](issue-366/2022.3.62f3.json) | Skill, Bash, Bash | PASS |

The committed JSON files preserve all tool-call blocks and corresponding tool
results, setup/reload evidence, session/model identifiers and the SHA-256 of the
original stream. Full streams and Editor logs remain in
`/tmp/issue366-evidence/<version>/`.

## Routing and regression checks

The default Codex runner returned HTTP 400 because `gpt-5.4-mini` was unavailable
for the account; this was an execution failure, not a routing prediction. The
fallback used Claude Code with the same catalog and rules produced by
`scripts/skill-eval/run-codex-routing.py`. Only case IDs and prompts were supplied,
without expected answers. The resulting [predictions](issue-366/routing-predictions.jsonl)
were evaluated by the unchanged harness:

```bash
tail -n 7 tests/fixtures/skill-routing/benchmark.jsonl > /tmp/issue366-routing.jsonl
scripts/skill-eval/llm-routing-eval.sh \
  --benchmark /tmp/issue366-routing.jsonl \
  --predictions docs/verification/issue-366/routing-predictions.jsonl \
  --model claude-opus-5-5
python3 -m unittest discover -s tests/scripts -p test_skill_routing.py -v
cargo run --quiet -- skills lint --severity error
cargo fmt --all -- --check
git diff --check
```

- SR-158/159: add Rigidbody to an existing Player (Japanese / English).
- SR-160: new-scene authoring remains owned by scene-create.
- SR-161/162: Prefab and material mutations stay with their respective skills.
- SR-163: sandbox connectivity retry stays with cli-usage.
- SR-164: component inspection stays read-only.
- Three existing routing regression tests passed (action discriminators,
  runtime inspection and input-asset-update boundaries).

This is a documentation/skill-fixture change. No Rust, C# or Unity runtime
implementation changed; the full runtime unit suites were not rerun locally.
