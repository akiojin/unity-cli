# Issue #422: composited Game screenshots

## Scope and acceptance audit

The pre-change audit found AC-1 and AC-4 unmet: `includeUI: true` still used
`Camera.Render`, and the handler silently focused the Game View. AC-2's camera
path existed and needed regression coverage. AC-3 had no qualifying four-case
PNG comparison, AC-5 had no parent SPEC amendment, and AC-6 required CI on the
fix. Linked PR #425 documented a workaround rather than fixing capture.

The change uses `ScreenCapture.CaptureScreenshot` for Game captures with UI,
awaits a completed PNG without blocking Editor frames, and only then publishes
the result. It preserves the camera path for `includeUI: false`. A missing focus,
lost focus, unavailable graphics, overlapping request, or frame timeout returns
an error instead of a camera-only success. Requested dimensions resize the final
frame. Scene/explorer/window behavior stays unchanged.

Autonomous implementation assumption: use the focused Game View and reject an
unavailable view; do not steal focus or guess a camera fallback. This is the
smallest behavior consistent with AC-4. Native capture cannot be cancelled, so
its staging file uses Unity's temporary cache and a unique name; a late native
write after an error cannot become a successful output or a subsequent request's
image. No package, Editor version, or graphics setting is changed by the handler.

## Evidence

- Environment: macOS Apple Silicon; graphical Unity Editors using Metal.
- Launch mode: autonomous.
- User Verification Result: n/a (autonomous)
- Agent Visual Check: pass (real Unity Editor, Edit and Play, both versions).
- Acceptance surface: native Game image and CLI error response; no browser or
  web theme surface. The test fixture changes both UI Toolkit and uGUI colors.
- Each version directory contains `tests.json`, `comparison.json`, PNGs and
  capture responses. The comparison records hashes of the actual bridge source
  in the isolated project, verified against this checkout before running.
- `before` and `after` refer to **UI state**, not old and new implementations.

| Unity | Mode | UI Toolkit | Screen Space Overlay | Camera only | Resize/Base64 |
| --- | --- | --- | --- | --- | --- |
| 6000.3.25f1 | Edit | Green Welcome → red Hello, Ada! | Blue → yellow | Identical PNG pixels | Pass |
| 6000.3.25f1 | Play | Green Welcome → red Hello, Ada! | Blue → yellow | Identical PNG pixels | Pass |
| 2022.3.62f3 | Edit | Green Welcome → red Hello, Ada! | Blue → yellow | Identical PNG pixels | Pass |
| 2022.3.62f3 | Play | Green Welcome → red Hello, Ada! | Blue → yellow | Identical PNG pixels | Pass |

The UI color checks require over 1,000 pixels per marker, a nonempty pixel diff,
and no marker colors in camera-only images. Resized captures must be 640×360
and retain both markers. Decoded Base64 must equal the on-disk PNG bytes.

| Version | Edit before | Edit after | Play before | Play after |
| --- | --- | --- | --- | --- |
| Unity 6 | [PNG](6000.3.25f1/edit-before.png) | [PNG](6000.3.25f1/edit-after.png) | [PNG](6000.3.25f1/play-before.png) | [PNG](6000.3.25f1/play-after.png) |
| Unity 2022 | [PNG](2022.3.62f3/edit-before.png) | [PNG](2022.3.62f3/edit-after.png) | [PNG](2022.3.62f3/play-before.png) | [PNG](2022.3.62f3/play-after.png) |

## Test inventory and reproduction

`ScreenshotHandlerTests` runs five cases per Editor, with no skipped tests:

- `CameraOnly_CapturesWithoutGameViewFocus`
- `DefaultCapture_RequiresFocusedGameView`
- `IncludeUi_RejectsFocusLostDuringCapture`
- `IncludeUi_RejectsOverlappingRequests`
- `IncludeUi_RejectsUnfocusedGameViewWithoutChangingFocus`

RED on the original handler: the unfocused test expected
`GAME_VIEW_NOT_FOCUSED` but received a PNG result with no error code; the existing
camera test passed (1 passed, 1 failed). GREEN after the change: all five pass.

Use isolated graphical projects with this checkout's bridge copied to
`Packages/unity-cli-bridge` and that package listed under manifest `testables`.
Install the Unity Test Framework and uGUI. Enable the bridge on separate ports
6482 and 6483. The harness replaces the isolated scene, so do not use a work scene.
The fixture is installed automatically, and generated scenes/assets stay under
`Assets/Scenes/Generated/E2E`. Use Python with Pillow and `unity-cli` on PATH:

```bash
python3 scripts/e2e-screenshot.py --port 6482 --activate --run-tests \
  --project /private/tmp/issue422-evidence/6000.3.25f1/Project \
  --output /private/tmp/issue422-verification/6000.3.25f1
python3 scripts/e2e-screenshot.py --port 6483 --activate --run-tests \
  --project /private/tmp/issue422-evidence/2022.3.62f3/Project \
  --output /private/tmp/issue422-verification/2022.3.62f3
```

Run the two commands sequentially because macOS foreground focus is shared.
The harness verifies the connected project and source hashes, polls compilation
and Domain Reload readiness, exercises the real CLI/router, and stops Play.

Additional checks passed:

- `cargo fmt --all -- --check`
- `cargo clippy --all-targets -- -D warnings`
- `cargo test --all-targets -- --test-threads=1`: 582 passed, zero failures
- `cargo run --quiet -- skills lint --severity error`: 18 skills, zero violations
- `python3 -m unittest discover -s tests/scripts -p 'test_perf_editor.py'`:
  13 passed, including the benchmark's explicit camera-only capture contract
- Markdownlint on this report and `docs/tools.md`; Python syntax; `git diff --check`

The existing foreground/background Editor benchmark now explicitly requests
`includeUI: false` to preserve its previous camera-rendering measurement. Its
new contract test failed before that payload change and passes afterwards.
No LSP code changed; its test suite remains covered by PR CI. AC-6 is recorded
on the Issue only after all PR checks succeed.

## Console observations and limits

Unity's own `LogEntries.GetCountsByType` reports **zero errors** after the
complete matrix. Metal can emit `Ignoring depth surface load/store action as it
is memoryless` warnings. `read_console`'s pre-existing native severity mapping
reports these as errors, so the evidence preserves its diagnostics alongside
Unity's own counts. That discrepancy was reported separately to the PM; it is
not suppressed or fixed by this capture change.

Both versions' actual PNGs contain the label text and overlay marker. This
evidence covers macOS Metal only; it does not claim testing on other platforms,
XR displays, or other graphics backends. Unavailable/focus/concurrency behavior
is tested separately from image-content assertions.
