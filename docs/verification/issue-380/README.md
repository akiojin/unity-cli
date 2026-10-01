# Issue #380 — Audio setup acceptance

The `unity-audio-setup` skill connects AudioClip import, AudioMixer authoring,
AudioSource routing, saved-state verification and measured Play Mode playback.
No CLI or bridge operation was added: authoring uses the tools delivered by
[#431 / PR #434](https://github.com/akiojin/unity-cli/pull/434).

## Acceptance inventory

| Criterion | Evidence / handoff |
| --- | --- |
| AC-1 | Canonical skill, Claude/Codex links, reciprocal sibling boundaries and docs inventory; 22 skills, zero lint violations |
| AC-2 | Fresh Claude Code Skill invocations on both required real Editors; results, mixer/scene assets and raw source/playback readbacks below; attach this directory in the PR |
| AC-3 | Eight new positive/boundary cases all correct; full 212-case benchmark passes its aggregate thresholds |
| AC-4 | Independently authored from this repository's tool schemas, handlers and public-API eval patterns; no official unity-agent-plugin skill text/code used; PR statement prepared, PR reviewer confirmation still required |
| AC-5 | Already recorded on [parent SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5905094372); no duplicate requirement added |

PR creation is handed to the PM after push, per the explicit execution ruling.
Do not mark PR attachment/reviewer confirmation complete merely because these
local artifacts exist. The Issue remains open for that handoff.

## Real Editor results

Host: macOS / Apple Silicon. CLI and bridge: 0.16.0 from this checkout. Claude
Code: 2.1.286. Both runs invoked the actual `unity-audio-setup` Skill tool and read
the canonical references, using separate projects and explicit ports. The final
skill hashes were captured before and after each final run; both match this tree.

| Editor | Claude assertions | `isPlaying` | `time` samples (seconds) | `timeSamples` |
| --- | --- | --- | --- | --- |
| 2022.3.62f3 | 23 passed | true → true | 0.213311 → 1.834648 | 9407 → 80908 |
| 6000.3.25f1 | 25 passed | true → true | 1.578662 → 2.538662 | 69619 → 111955 |

Both use a generated 30-second 440 Hz mono PCM fixture. Import settings read back
as `DecompressOnLoad` / `PCM`. `Assets/Audio/Final2.mixer` contains `Master/Music`
with Volume exposed as `MusicVolume`. `/Music` routes the saved clip to that group,
with volume 0.25, loop enabled, playOnAwake disabled and spatialBlend 0. The scene
is saved as `Assets/Scenes/Generated/E2E/Audio380Final2.unity` and reloaded before
Play. Each run checks GetFloat, SetFloat(-12 dB), readback and restoration to 0 dB.
Both finish in Edit mode with zero console errors/exceptions.

- [2022.3 result](2022.3.62f3/result.json), [raw commands/results](2022.3.62f3/tool-results.json), [Claude invocation](2022.3.62f3/claude-session.json).
- [6000.3 result](6000.3.25f1/result.json), [raw commands/results](6000.3.25f1/tool-results.json), [Claude invocation](6000.3.25f1/claude-session.json).
- Each version's `assets/` retains the actual mixer, scene, metadata and package
  manifests. `fixture.json` records the tone generation formula and file hash.
- `tool-results.json` maps the original evidence-relative filename to its exact
  text, including command/exit-code records, eval sources and unmodified responses.
- `parent-verification.json` and `parent-transcript.json` hold independent live
  reload/routing/playback checks. Those checks temporarily enable runInBackground
  to avoid shared-desktop focus changes, then verify both Application and
  PlayerSettings values are restored. Claude's final runs used a focused Editor
  with runInBackground false. Neither check claims audible speaker output.

## Recovery observations

Initial test-project setup displayed the Input System activation dialog and
interrupted bridge calls. Recovery stayed inside each isolated test Editor.
The final runs began after this setup. A Play domain reload can briefly refuse a
connection; `stop_game` can reply before the transition finishes, so the workflow
polls actual Editor state. Eval's CS0162 wrapper warning is retained in raw output;
all accepted evals completed without exceptions.

Initial playback checks showed `isPlaying:true` with a stalled clock when the
Editor lost focus. The final reference explicitly checks focus, frame count,
dspTime and runInBackground, and requires measured progress. The original
implicit source-object creation step was also made explicit. The retained final
matrix was rerun after both skill corrections.

## Validation

- `cargo fmt --all -- --check`: passed.
- `cargo clippy --all-targets -- -D warnings`: passed.
- `cargo test --all-targets -- --test-threads=1`: 589 tests passed across 9 suites.
- `cargo run -- skills lint --severity error`: 22 skills, zero violations.
- Routing evaluator unit tests: 10 passed.
- Markdown lint: passed for changed Markdown.
- [Routing evidence and replay commands](routing/README.md): focused 8/8; full
  top-1/top-2 99.53%, tool 99.06%, payload 95.75%, all aggregate thresholds pass.
- `python3 docs/verification/issue-380/verify_evidence.py` checks exact skill hashes,
  Skill invocation, raw sample provenance, saved asset hashes and routing results.

Quality logs are retained under `quality/`. C# production code did not change;
the acceptance matrix exercises the existing bridge on both required Editors.

Launch mode: autonomous. User Verification Result: n/a (autonomous).
Agent Visual Check: n/a (audio state workflow; no UI surface authored).
