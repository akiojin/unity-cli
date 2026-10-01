# Issue #410: v0.16.0 onboarding verification

## Scope and environment

- Date: 2026-10-01; macOS 26.5 (25F71), Apple Silicon (arm64).
- Editors: 2022.3.62f3 and 6000.3.25f1, installed through Unity Hub.
- CLI: released v0.16.0 `unity-cli-osx-arm64`, SHA-256
  `01922e815d9b44bfce3c722e6a6d260bb3c4ddeafa6f2e243b2220f00f82076c`.
- Claude Code: 2.1.286; a fresh session per run, plugin supplied with
  `--plugin-dir`, hooks disabled, no MCP servers, no human tool responses.
- Prompt in every session: `シーンに Cube を追加して`.
- User Verification Result: n/a (autonomous).

Each project was created by its actual Editor using `-batchmode -quit
-createProject`. It had no Bridge dependency and no running Editor when the
Claude session started. `UNITY_CLI_VERSION=v0.16.0` pinned the installer; each
project had its own `UNITY_CLI_PORT`. The transcript records the installer
downloading and checksum-verifying the release, then running `setup`.

**Environment limitation:** `unity-cli` and its `~/.local/bin` launcher were
absent from the session's command lookup at the start of each run, but the
shared home already contained a managed v0.16.0 binary from another task.
That shared binary was not deleted or relocated. The launcher created by a
completed test was removed before the next test. The agent ran the installer
instead of being told where to find the cached binary. A completely empty
home or separate macOS account was not tested.

## Results

| Scenario | 2022.3.62f3 | 6000.3.25f1 |
| --- | --- | --- |
| AC-1, v0.16.0 plugin baseline | FAIL: asks human to install | FAIL: asks human to install |
| AC-1 workflow, corrected scene skill | PASS: installer → setup → Cube | PASS: installer → setup → Cube |
| AC-2, released CLI `bridge install` → Editor → ping | PASS | PASS |
| Independent final ping, Cube components, compilation | PASS | PASS |

The baseline selected `unity-scene-create`, attempted `create_gameobject`,
then read that skill's runtime checklist after `command not found`. The
checklist only described an existing PATH binary or `cargo run` in a source
checkout. Neither session reached the foundation skill's installer/setup
instructions. Both ended by asking the user for installation or a binary path.

The correction adds an explicit bootstrap prerequisite linking the existing
foundation instructions and checklist. It also corrects the primitive example
from `Cube` to the schema's lowercase `cube`. No Rust or Bridge behavior changed.
PM's Board ruling `66f6f56c-0631-4ae3-ab80-51f15e466ca3` assigns this correction
to #410, independently of #366's Editor safety rules.

Both final Claude sessions downloaded v0.16.0, ran `setup --launch-editor`,
received `ok:true` and `editor.projectMatches:true`, then created `/Cube`.
Independent reads found exactly one Cube with `MeshFilter`, `MeshRenderer`,
and `BoxCollider`. Both Editors reported compilation success and zero errors.
The 2022.3 session left the scene unsaved after creating the Cube; persistence
was not part of the Cube-creation acceptance check.

For AC-2, `bridge install` ran while each Editor was closed. It declared the
registry package at 0.16.0 and changed `activeInputHandler` from 0 to 2.
After GUI startup, both `system ping` calls returned `pong`, the correct
project path, and `versionCheck.status: match`. No manual package edits or
restart-dialog interaction were needed.

## Evidence and checks

[Evidence directory](issue-410/) contains:

- `baseline-*-transcript.json`: tool calls/results and final responses from
  the released-plugin failures.
- `final-*-transcript.json` and `final-*-invocation.json`: final-skill sessions,
  original prompt, command, environment overrides, and launch configuration.
- `final-*-{ping,hierarchy,compilation}.json`: independent live verification.
- `bridge-install-*.json`, `ping-*.json`, `*-get_compilation_state.json`,
  `*-get_editor_state.json`, manifests and project versions: AC-2 evidence.
- `environment.json`: release digest and exact tested skill digest.

The normalized transcripts retain tool inputs, tool outputs, session IDs, and
final responses. Internal reasoning and unrelated telemetry are omitted.
Full raw transcripts and Editor logs remain in `/tmp/issue410-evidence/`.

Local checks passed: `unity-cli skills lint --severity error` (16 skills,
zero violations), targeted Markdown lint, `cargo fmt --all -- --check`,
and the eight live assertions across both final Editors. Rust/C# sources
were unchanged, so their full unit suites were not repeated for this skill edit.

The same four-command matrix was registered with `verify.plan`. The first
canonical `verify.run` was deferred by a host lease held by another task
(`max_wait_secs: 1`, queue position 3); it did not produce a passing canonical
record. Per #410's Notes, this run does not wait for the holder. Local checks
and the eventual PR CI remain the verification evidence for the handoff.

The released-plugin failure and corrected-plugin success are distinct results:
the original v0.16.0 plugin is not reported as passing. The shared-home
limitation above remains part of interpreting AC-1; AC-2 has no such limitation.
