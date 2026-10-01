# Issue #376 — Package management skill verification

`unity-package-management` owns UPM package discovery, installed-package lists,
installation, explicit-version updates, removal and scoped registries. It uses
the existing `package_manager` and `registry_config` tools and checks resolved
state and compilation after each mutation. No Rust or bridge runtime behavior
is changed.

## Acceptance audit and design

The initial audit found no canonical skill, discovery links, inventory entry,
dedicated routing cases, real Editor scenario evidence or linked implementation
PR. AC-1 through AC-4 required work. The parent SPEC already contained umbrella
requirements; the concrete #376 requirements, design and tasks were recorded in
[SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5925433368)
before adding the skill (AC-5).

Package-only requests move out of `unity-editor-tools`; console-only diagnostics
stay there. `unity-project-bootstrap` delegates optional package work to the
new skill. `unity-csharp-navigate` retains source-index package inspection;
its `list_packages` example now exposes that distinct operation to the routing
catalog. Both Claude and Codex symlinks resolve to the canonical skill.

## Routing evidence

Claude Code (`claude-opus-5-5`) received the skill catalog and user prompts,
without expected answers or tool access. The existing
`scripts/skill-eval/llm-routing-eval.sh` scored its predictions.

- RED: 3/8 top-1 matches before the skill existed; all five package/registry
  positive cases chose `unity-editor-tools`.
- GREEN: all nine final cases pass skill, tool and payload checks, including
  the package-list-only boundary requested by the PM.
- An intermediate negative case exposed a missing `list_packages` command
  example in the C# navigation catalog. Adding that existing operation's
  example fixed tool selection without changing the routing runner.

The full catalog has pre-existing missing tool hints, including `load_scene`,
`list_scenes` and `clear_console`, already noted in #375's evidence. A separate
full-catalog measurement, applying the runner's existing keyword overrides,
produced the following results:

| Catalog | Cases | Top-1 | Top-2 | Tool | Payload |
| --- | ---: | ---: | ---: | ---: | ---: |
| Before #376 | 170 | 99.41% | 100% | 85.29% | 88.82% |
| Intermediate #376 catalog | 178 | 98.88% | 98.88% | 84.27% | 86.52% |
| Final #376 cases | 9 | 100% | 100% | 100% | 100% |

The first two runs fail the full benchmark's tool (92%) and payload (95%)
thresholds. These are independent LLM measurements, not a deterministic
regression comparison. The intermediate full run preceded the final navigation
example and package-list case. No full benchmark PASS is claimed.

The PM explicitly accepted the scoped positive/boundary benchmark for #376 AC-3
(Board decision `a4294597-b1bd-421c-8052-4bd98d6864ad`) and assigned the broader
catalog problem to [#419](https://github.com/akiojin/unity-cli/issues/419).

Re-score the final evidence with:

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --benchmark docs/verification/issue-376/routing/cases.jsonl \
  --predictions docs/verification/issue-376/routing/predictions.jsonl \
  --model claude-opus-5-5 \
  --summary /tmp/issue376-routing-summary.json \
  --history /tmp/issue376-routing-history.jsonl
```

## Real Editor scenario

The acceptance scenario runs the canonical `/unity-package-management` through
the Claude Code Skill tool in dedicated projects on macOS Apple Silicon,
using Unity 6000.3.25f1 and 2022.3.62f3. Each project contains a local copy of
this checkout's bridge and its normal package dependencies. This is a real
Editor/UPM execution, not a mocked package server or a hand-written manifest
transition.

The sequence is: capture baseline; add OpenUPM for `com.cysharp` with
`autoAddPopular:false`; install UniTask 2.5.4; update it to 2.5.5; remove it.
The configured registry remains, as requested. Each stage records the manifest,
lock, tool responses, idle Editor state and console. The harness independently
checks the final state and confirms the Skill invocation in the transcript.

Exploratory GUI runs revealed modal notifications: “Importing a scoped registry”
on both versions and “Missing Signature” on Unity 6 suspend the Editor update
loop while its TCP listener remains active. Process samples and window captures
identified the cause. Only informational notices on the dedicated fixtures were
closed; no package validation setting was disabled. The skill now explains
this diagnostic distinction and stops for inaccessible UI or a new trust
decision. Exploratory timeout responses are retained locally and are not
misreported as compiler errors or clean full runs.

Final runs use fresh isolated projects with the documented
`UnityCliInputBatchHost` (`-batchmode -nographics`) so native notifications do not
interfere with the package/compilation acceptance scenario. Both use the actual
installed arm64 Editor binaries. They start after the final skill edits.
This change has no rendered UI acceptance requirement.

Final per-version results and exact artifact checks are recorded under
`issue-376/`; the local original transcripts, Editor logs and exploration
artifacts remain in `/private/tmp/issue376-evidence/`.

| Editor | Result | Manifest transitions | Compiler check | Console after removal |
| --- | --- | --- | --- | --- |
| 6000.3.25f1 | [PASS](issue-376/6000.3.25f1/result.json) | [Diff](issue-376/6000.3.25f1/manifest.diff) | [0 errors](issue-376/6000.3.25f1/independent-get_compilation_state.json) | [Raw log](issue-376/6000.3.25f1/remove/console-errors.json) |
| 2022.3.62f3 | [PASS](issue-376/2022.3.62f3/result.json) | [Diff](issue-376/2022.3.62f3/manifest.diff) | [0 errors](issue-376/2022.3.62f3/independent-get_compilation_state.json) | [Raw log](issue-376/2022.3.62f3/remove/console-errors.json) |

Both versions resolved 2.5.4 and then 2.5.5 from OpenUPM, removed the direct
dependency and lock entry, and retained exactly the requested `com.cysharp`
registry scope. No unrelated manifest changes occurred. The original Editor
logs contain zero `error CS` diagnostics; their hashes and compilation excerpts
are retained alongside the independently queried final compilation state.

Console output is not claimed to be empty: the baseline contains existing bridge
compiler warnings; the 2022.3 batch host also reports a null graphics device.
Read-only polling across reload produces dropped-client warnings. The existing
`read_console` severity mapping can label compiler warnings as `Exception` and
`Debug.LogWarning` output as `Error`; the raw messages and stacks are preserved.
That runtime classification issue was reported to the PM separately. Compilation
success is checked against actual compiler diagnostics and the independent
`get_compilation_state` response, rather than those misleading severity labels.

Validate the recorded artifacts and tested skill hashes with:

```bash
python3 docs/verification/issue-376/check-evidence.py
```

This validates evidence; it does not pretend to rerun Unity. To repeat the live
scenario, use a fresh isolated project with this checkout's bridge and the
documented `UnityCliInputBatchHost`, select either required Editor with an unused
port, and invoke `/unity-package-management` in a fresh Claude Code session with
the per-version `prompt.txt`. The corresponding `launch.json`, `commands.txt`,
`claude-command.json` and `claude-tool-calls.json` record the exact invocation.
Adapt the project, evidence directory, binary and port paths for the new run.

## Static and regression verification

- Skill Contract v1: 18 skills, zero violations.
- Rust tests: 580 passed across seven targets (552, 1, 2, 4, 8, 5, 8).
- Routing runner unit tests: three passed.
- Rust formatting, clippy with warnings denied and changed Markdown lint: pass.
- No LSP or bridge implementation changes; the two-version package workflow
  supplies the relevant integration evidence.

## Authorship review

The skill, package reference and adjacent-skill diff were reviewed against the
Issue and this repository's tool schemas and handler implementation. The text
is independently authored. The shared runtime checklist is copied only from
this repository's existing `unity-editor-tools` reference. No text or code from
the official `unity-agent-plugin` skills was copied or adapted (D6).

User Verification Result: n/a (autonomous).
Agent Visual Check: n/a (no UI surface).
