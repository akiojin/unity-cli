# URP workflow skill verification (#378)

## Scope and environment

- macOS, Apple Silicon; real GUI Editors 2022.3.62f3 and 6000.3.25f1.
- URP versions resolved by each Editor: 14.0.12 and 17.3.0, respectively.
- CLI / bridge 0.16.0; checkout bridge source, no new bridge tools.
- Dedicated projects `/tmp/unity-issue378/<version>`, ports 6493 / 6494.
- Fresh Claude Code sessions load this checkout's plugin and invoke
  `/unity-cli:unity-urp-setup`. The real-client sessions report `claude-fable-5-1`.
- User Verification Result: n/a (autonomous).
- Agent Visual Check: pass for the initial and final Bloom A/B pairs in both Editors.

## Acceptance audit before implementation

AC-1 through AC-4 were missing: no canonical URP skill, links, routing cases,
real-client evidence or implementation PR existed. AC-5 was already satisfied by
the requirements, design and T-370-6 in the
[parent SPEC comment](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5905094372).
Only the missing skill, routing and verification surfaces are implemented.

## Real Editor scenario

Each initial session started from a project without URP or an assigned pipeline.
It resolved and installed the compatible package, created Renderer / Pipeline /
Volume Profile assets, assigned Graphics and all six Quality levels, prepared a
stationary emissive sphere on black, and captured the same Game Camera at
1280×720 with `osFallback:false`. Only Bloom intensity changed from 0 to 2;
threshold stayed at 1. The images show a hard edge before and a soft halo after.

After Play stopped, the session restored intensity 2, saved, opened another scene,
reimported the assets and reloaded the saved scene. Independent Editor queries
confirmed the effective pipeline, all Quality assignments, persistent Bloom
subasset, Camera HDR/post-processing, Volume layer mask and profile reference.

| Editor | Clean installation session | Independent persistence |
| --- | --- | --- |
| 2022.3.62f3 | `23fffebc-dda3-4130-920f-39005491192b` | [State](issue-378/2022.3.62f3/independent-state.json) |
| 6000.3.25f1 | `8f985f07-83ba-4d5f-95b1-d57af6369498` | [State](issue-378/6000.3.25f1/independent-state.json) |

The final skill's package-only boundary was then aligned with #376. Fresh
follow-up sessions verified reuse and the same A/B/persistence scenario with that
final wording. They intentionally preserved the existing assets rather than
claiming another clean installation.

| Editor | Final session | Result |
| --- | --- | --- |
| 2022.3.62f3 | `76c1315f-67a5-45b0-b7c4-daa8a5b5552d` | A/B, saved state, 0 errors/exceptions/asserts |
| 6000.3.25f1 | `256fd27b-9af9-4f15-866a-37d387a8ce6b` | A/B, saved state, 0 errors/exceptions; Search indexer Assert noted below |

After both sessions, the dedicated Editors were closed and restarted, and the
same persistence assertions passed again:
[2022.3 state](issue-378/2022.3.62f3/restart-state.json),
[6000.3 state](issue-378/6000.3.25f1/restart-state.json).
The restart fixtures use copies of the identical bridge package so Editor
importer normalization cannot modify the repository's `.meta` files. The 43
default importer extensions produced by the original file-package setup were
removed after checking that they were the only changes and preserved each GUID.

### Unity 2022.3

![Bloom disabled, Unity 2022.3](issue-378/2022.3.62f3/before.png)
![Bloom enabled, Unity 2022.3](issue-378/2022.3.62f3/after.png)

- [Installation transcript](issue-378/2022.3.62f3/installation-transcript.json)
- [Installation result and diagnostics](issue-378/2022.3.62f3/installation-result.json)
- [Final transcript](issue-378/2022.3.62f3/transcript.json)
- [Final result](issue-378/2022.3.62f3/result.json)
- [Saved assets and project assignments](issue-378/2022.3.62f3/assets.zip)
- [Asset SHA-256 hashes](issue-378/2022.3.62f3/asset-hashes.json)

### Unity 6000.3

![Bloom disabled, Unity 6000.3](issue-378/6000.3.25f1/before.png)
![Bloom enabled, Unity 6000.3](issue-378/6000.3.25f1/after.png)

- [Installation transcript](issue-378/6000.3.25f1/installation-transcript.json)
- [Installation result and diagnostics](issue-378/6000.3.25f1/installation-result.json)
- [Final transcript](issue-378/6000.3.25f1/transcript.json)
- [Final result](issue-378/6000.3.25f1/result.json)
- [Saved assets and project assignments](issue-378/6000.3.25f1/assets.zip)
- [Asset SHA-256 hashes](issue-378/6000.3.25f1/asset-hashes.json)

The ZIP files contain the generated scene, pipeline, renderer, profile and Bloom
subasset, material, metadata, Graphics/Quality settings and package manifests.
The JSON transcripts retain tool calls/results and final reports, omit model
thinking and embedded image bytes, and include the SHA-256 of the original
stream. Full streams and Editor logs remain under `/tmp/unity-issue378/`.

### Diagnostics and limits

- Package installation triggers Domain Reload. Initial state polling encountered
  disconnects/timeouts; the sessions confirmed the resolved package afterward
  instead of blindly resending installation.
- Exploratory eval compilation errors (an internal URP property and ambiguous
  `Object`) were inspected and corrected under new request IDs. Failed snippets
  executed no asset changes; accepted mutations returned `state: completed`.
- Unity 6000.3 logged a `UnityEditor.Search.SearchDatabase.EnumerateAll`
  `ArgumentOutOfRangeException` Assert around the Play domain reload in both runs.
  The trace is retained in both results; it has no URP, Volume or bridge frames.
  Compilation had zero errors, saved-state assertions passed and both captures
  rendered correctly. This engine-owned diagnostic is reported, not silently
  cleared or represented as a clean console.
- Initial 2022.3 captures were static Game Camera renders with background Play
  frames stalled. The follow-up fixture enables `Application.runInBackground`
  so live frames advance before the final captures. This is test-host preparation,
  not a change to the distributed skill or project defaults.
- The representative scenario verifies Bloom. It does not claim a full Built-in
  material migration, custom RenderGraph/Renderer Feature validation, or a
  Color Adjustments visual matrix.

## Routing and contract checks

The RED catalog test selected `unity-editor-tools` for a URP/Bloom workflow before
the new skill existed. The initial GREEN run passed all eight added cases using
Claude Code without exposing expected answers: three URP positives and five
boundaries (capture, material, package-only, scene-only, object movement).

PM subsequently assigned package-only requests to #376's
`unity-package-management`. SR-URP-006 and the description reflect that ruling;
after merging #424 and #419 from develop, the final eight cases pass all four
metrics at 100% with fresh Claude Opus 5.5 predictions. Expected answers were
excluded from the prompt. The [prompt](issue-378/routing/routing-prompt.txt),
[raw response](issue-378/routing/routing-green-raw.json),
[predictions](issue-378/routing/routing-predictions.jsonl) and
[score report](issue-378/routing/routing-summary.json) record this final catalog.

The integrated 187-case benchmark also passes: top-1/top-2 100%, tool 98.93%,
payload 97.33%. This fresh Claude Opus 5.5 batch uses #419's complete catalog.
It exposed an existing package-list keyword override that replaced the correct
URP prediction with source-package navigation. The override now defers to the
model when the prompt explicitly names UPM or `package_manager`. Two regression
tests cover these requests and preservation of source-package navigation; the
same raw model predictions were rescored after this deterministic boundary fix.
All eight URP cases pass through the complete runner as well as direct inference.
Thresholds and expected answers were not changed.
The [full report](issue-378/full-routing/summary.json) retains
individual errors; the sibling files contain the exact prompt, raw response,
model predictions, final predictions and cases. Passing the benchmark thresholds
does not mean every legacy case is correct.

- Final `cargo run -- skills lint --severity error`: 19 skills, 0 violations.
- `python3 -m unittest discover -s tests/scripts -p test_skill_routing.py -v`:
  all 10 routing-runner regressions pass after #419 integration and the UPM fix.
- `cargo fmt --all -- --check`: pass.
- `cargo clippy --all-targets -- -D warnings`: pass.
- `cargo test --all-targets -- --test-threads=1`: all 582 tests pass after
  integrating develop's setup dry-run fix.
- `python3 docs/verification/issue-378/check-evidence.py`: both Editors' asset
  hashes, saved/restarted state, distinct PNG pairs and current skill hashes pass.
  This validates recorded evidence; it does not substitute for the real runs above.
- Canonical verification is subject to the shared host lease. PM ruling
  `4f999923-f520-4f53-bcff-5286ae5cec19` directs #378 to run local checks, push
  the evidence, and hand Ready/merge settlement to PM without waiting for another
  project's lease. The PR CI is the final delivery gate; no canonical PASS is
  claimed here.

## Originality review (D6)

The new skill and workflow reference were independently authored from this
repository's tool schemas, existing skill contract, public Unity APIs and actual
Editor observations. No text or code from the official `unity-agent-plugin`
`skills/` was copied or consulted. The runtime checklist is reused from this
repository's own `unity-cli-usage` skill. The PR records this provenance explicitly.
