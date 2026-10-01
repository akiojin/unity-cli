# Localization workflow verification (#381)

## Acceptance audit and scope

Before implementation, `58d2bf1` contained no `unity-localization` canonical
skill, distribution links, inventory entry, routing cases, real-client scenario
evidence or associated PR. AC-1 through AC-4 therefore required work. AC-5 was
already satisfied by the requirements, design and T-370-9 recorded in
[SPEC #160](https://github.com/akiojin/unity-cli/issues/160#issuecomment-5905094372).

This change adds a workflow over existing `package_manager`, `eval_csharp`, UI
state and capture tools. It adds no Rust, LSP or bridge behavior. The skill
preserves existing assets and listeners, requires a saved persistent event
binding, and checks actual rendered English/Japanese text after locale changes.

## Real Unity Editor verification

The clients ran on macOS Apple Silicon in isolated GUI projects under
`/tmp/issue381/<version>/Project`, using ports 6507 and 6508 respectively.
Fresh Claude Code sessions loaded this checkout's plugin and invoked
`/unity-cli:unity-localization`. Each initial session installed Localization,
created settings and two Locale assets, created the `DemoStrings` collection and
`greeting` key, and connected `/LocalizationCanvas/Greeting` through a persistent
`LocalizeStringEvent.OnUpdateString` listener to `UnityEngine.UI.Text.set_text`.

| Editor | Localization | Addressables | UGUI |
| --- | --- | --- | --- |
| 2022.3.62f3 | 1.5.13 | 1.25.0 | 1.0.0 |
| 6000.3.25f1 | 1.5.13 | 2.10.3 | 2.0.0 |

Versions came from each Editor's registry/resolved packages, rather than a fixed
package version assumption. The CLI and embedded bridge identify as 0.16.0.
The generated scenes and localization assets live under
`Assets/Scenes/Generated/E2E/Localization/`. Saved asset copies, including the
Locale and per-language String Table assets, are attached under
[2022.3 assets](issue-381/2022.3.62f3/assets/) and
[6000.3 assets](issue-381/6000.3.25f1/assets/).

After the capture instructions were made concrete in the final skill and
PR #429 was integrated, clients re-read that version, reused the saved assets and
performed the complete readback/runtime check again. Neither session recreated assets or
assigned translated strings directly to the label. Initialization succeeded;
`get_ui_element_state` returned `Hello, world!` → `こんにちは、世界！` →
`Hello, world!` after selecting en → ja → en. Both 1280×720 images were inspected:
white, legible text on a dark background, unchanged framing, no missing glyphs.
The return-to-English UI state contains the original English translation.

| Editor | Final client session | English | Japanese | Report |
| --- | --- | --- | --- | --- |
| 2022.3.62f3 | `8f3a889a-cdc0-490f-8486-b288fe05c303` | [PNG](issue-381/2022.3.62f3/final/en.png) | [PNG](issue-381/2022.3.62f3/final/ja.png) | [JSON](issue-381/2022.3.62f3/final/report.json) |
| 6000.3.25f1 | `2238940c-1677-4cfb-8e86-fb563bc0de35` | [PNG](issue-381/6000.3.25f1/final/en.png) | [PNG](issue-381/6000.3.25f1/final/ja.png) | [JSON](issue-381/6000.3.25f1/final/report.json) |

The accepted 2022.3 pass began at 09:29:59 UTC, after re-reading the final
reference; its report retains the earlier integration pass separately. The
accepted 6000.3 session began at 09:28:21 UTC and includes a preserved stalled
attempt followed by a successful foreground run. All final skill hashes and all
79 embedded bridge Editor/Runtime C# hashes match the checkout. Both accepted
image pairs use the repaired primary `capture_screenshot` path with the target
Game view in the foreground. The agent, not a human, gave the target Editor OS
focus. The scene and project assets were not changed during these checks.

Both scenes were saved and reopened before Play and reopened again after Play.
Independent read-only queries confirmed two locales, matching shared entry IDs,
both exact saved translations, one persistent `set_text` listener targeting the
actual label, Edit Mode and an unmodified saved scene:
[2022.3 readback](issue-381/2022.3.62f3/independent-state.json),
[6000.3 readback](issue-381/6000.3.25f1/independent-state.json).
The [query source](issue-381/independent-check.cs) is included.

### Observed limitations and evidence boundaries

- The bridge at the initial base omitted Screen Space Overlay UI from
  `capture_screenshot` (#422). The rejected camera-only PNGs and tool responses
  remain in the initial evidence. Those initial successful captures used the
  public `ScreenCapture.CaptureScreenshot` Game-view fallback. Upstream PR #429
  was subsequently integrated at `ff3e2f3`; the final images above verify its
  standard-capture path. No OS screenshots or image synthesis were used.
- The repaired primary capture requires a focused Game view and returns
  `GAME_VIEW_NOT_FOCUSED` in a background Editor. A Game-view focus request alone
  need not give the Editor OS focus. With `runInBackground:false`, Play frames can
  also stop while the Editor is inactive. Failed polls and the stalled attempt
  are retained; they are not passing samples. Accepted results were obtained
  after the target Editor became active and frames advanced. The fallback still
  requires a rendered frame; it is not a workaround for a stopped game loop.
- Unity 6000.3 logged one `UnityEditor.Search.SearchDatabase`
  `ArgumentOutOfRangeException` during the first client session. Its full stack
  is retained. It recurred after the bridge-reload integration; the final
  acceptance session preserved it before Play and found no new exceptions.
  Unity's Clear on Play removed the old
  console entry; zero final counters do not mean the whole Editor session was
  error-free. Memoryless depth warnings are also retained. Unity 2022.3 had no
  errors in either client scenario. No localization failure was observed.
- Arial Unicode was imported from the host's installed fonts to provide Japanese
  glyphs. Font binaries are not redistributed; import metadata, source paths and
  hashes are retained. These are evidence assets, not a standalone sample with a
  bundled font. Reproduction needs a locally available Japanese-capable font.
- Client transcripts retain public tool calls, results and summaries; private
  reasoning and inline image payloads are omitted. PNG files are retained
  separately. The evidence checker validates hashes, saved bindings, actual UI
  state, image dimensions and routing freshness; it does not re-run Unity or OCR.

```bash
python3 docs/verification/issue-381/check-evidence.py
```

## Test-first routing and full benchmark

The new benchmark cases first failed the catalog-existence check because
`unity-localization` was absent. The preserved pre-change catalog in
[the baseline prompt](issue-381/red-prompt.txt) also routes the localization
request to generic asset/GameObject skills in
[the baseline model response](issue-381/red-response-after-reset.json).
The first model attempt hit a session quota; its
[429 response](issue-381/red-response.json) is not counted as a routing result.
The old-catalog model baseline ran after the quota reset, independently of the
new-catalog evaluation; the structural RED check ran before authoring the skill.

A fresh Claude Code inference using the final skill catalog evaluated all 203
cases. The prompt contains only IDs and user requests, with no expected answers.
The unchanged runner's keyword rules are applied to raw model predictions;
neither predictions nor benchmark thresholds were manually corrected.

| Metric | Result | Required |
| --- | --- | --- |
| Top-1 | 99.51% | 90% |
| Top-2 | 99.51% | 98% |
| Tool | 98.52% | 92% |
| Payload | 96.06% | 95% |

All eight `LOC381-*` cases pass all four metrics, including package-only,
capture-only, UI interaction, console and Addressables boundaries. Full-suite
failures remain visible in the score report; a passing threshold is not a
claim of perfect routing.

Evidence: [prompt](issue-381/routing/prompt.txt),
[raw response](issue-381/routing/raw.json),
[model predictions](issue-381/routing/llm-predictions.jsonl),
[runner predictions](issue-381/routing/predictions.jsonl),
[case snapshot](issue-381/routing/cases.jsonl),
[score report](issue-381/routing/summary.json).

Re-score the recorded predictions (this does not invoke the model again):

```bash
bash scripts/skill-eval/llm-routing-eval.sh \
  --predictions docs/verification/issue-381/routing/predictions.jsonl \
  --model claude-fable-5-1 \
  --history /tmp/issue381-routing-history.jsonl \
  --summary /tmp/issue381-routing-summary.json
```

## Repository verification

Mode: pre-pr. Launch mode: autonomous. Initial baseline: `58d2bf1`.
Integrated baseline: `ff3e2f3` (`origin/develop`, including PR #429).
Changed surfaces: skill assets, routing fixtures and documentation.
Acceptance surface: Unity Game UI text in two real GUI Editors.
User Verification Result: n/a (autonomous).
Agent Visual Check: pass (both Editors' English/Japanese Game PNGs inspected).

| Command | Result / inventory |
| --- | --- |
| `cargo fmt --all -- --check` | PASS |
| `cargo clippy --all-targets -- -D warnings` | PASS |
| `cargo test --all-targets -- --test-threads=1` | PASS: 588 tests (554 unit, 34 integration) |
| `cargo run -- skills lint --severity error` | PASS: 21 skills, 0 violations |
| `python3 -m unittest discover -s tests/scripts -p 'test_*.py'` | PASS: 52 tests, 1 existing skip |
| `dotnet test lsp/Server.Tests.csproj` | PASS: 53 tests |
| Markdown lint for the new skill and reference | PASS |
| `python3 docs/verification/issue-381/check-evidence.py` | PASS: both Editors, assets, source hashes and current routing catalog |

Rust integration suites cover bridge dependencies, build failure output, C#
post-write compilation, installation, setup dry-run, skill installation, tool
filters and daemon startup. Python suites include routing catalog/UPM boundaries.
No browser application was changed; the graphical acceptance surface is Unity.

## Source provenance

The skill and localization reference were independently authored using the
repository's Skill Contract v1, existing tools and public Unity package APIs.
No official `unity-agent-plugin/skills/` text or code was read or reused. The
runtime checklist is copied from this repository's `unity-cli-usage` skill.
The PR source review must preserve this declaration (D6).
