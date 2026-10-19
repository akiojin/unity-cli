# Issue #386: reconstructed staff coverage and Windows execution entrypoints

Read-only scope investigation, 2026-10-04. The original
`project-state/pm-scratch/staff-report.md` has **not been obtained**. Its text,
original row order, precise ○/△/— marks, all positive scenarios, and attachment
identity cannot be guaranteed by this reconstruction. Do not mark AC-5 complete
solely because the rows below pass.

The following **13 verification categories** consolidate primary Issues
[#243–260](https://github.com/akiojin/unity-cli/issues/243), plus
[#390–395](https://github.com/akiojin/unity-cli/issues/390). This grouping is an
inference for managing coverage, not a claim to reproduce the original report's
13 rows. #393 explicitly says the report had 13 △/— rows, of which 12 had been
implemented by v0.13–v0.15. Original issues record the actual Windows investigation
on 2026-09-26; #390–395 describe the report received on 2026-09-30. Both dates
belong in provenance, rather than silently treating them as the same event.

| Reconstructed category | Primary source and original expected behavior | Current verification entrypoint | Windows fixture/package/module requirements | Coverage beyond the public 23 operations |
| --- | --- | --- | --- | --- |
| 1. Keyboard / gamepad / touch simulation | #243: keyboard hold duration uses elapsed seconds at 30/60/120/unlimited fps; original measurement also compared physical-priority and isolated Keyboard, absent/0.2/1/2-second holds, three repeats (72 trials). #245: tap/swipe occupy multiple game frames without blocking Update. #246: processed individual stick axes match zero, single-axis, diagonal and maximum requests. #393 names this combined feature row. | Real PlayMode fixtures `InputSimulationAutomationPlayModeTests` (including four fps hold cases), `TouchGesturePlayModeTests`, `GamepadStickInputPlayModeTests`; `scripts/e2e-input-tools.sh` for TCP input calls. The original observation C# source is embedded verbatim in fetched `243.md`/`244.md`/`245.md`/`246.md`; use its `Issue Repro/Setup`, Configure, Reset, Snapshot and Positive menus to reproduce original trials. | `com.unity.inputsystem` 1.19.0 for the reported stream, activeInputHandler Both or Input System, game Update running, InputSystem focus routing. Input UI script additionally needs UGUI, `UnityCliInputTestSceneGenerator.cs`, `UnityCliInputSimulationTestBootstrap.cs`, `InputStateDisplay.cs`; generated scenes remain inside the disposable project's `Assets/Scenes/Generated/E2E`. | None of the input variants is among the 23 benchmark operations. Basic command success alone does not prove the original 72 duration trials or game-frame observations. |
| 2. InputAction callback notification | #244: pre-enabled PassThrough actions must receive both press=1 and release=0 for keyboard, mouse and gamepad, regardless of Editor/Dynamic update timing. Positive QueueStateEvent control must still notify. | `InputActionNotificationTests` real EditMode suite; original `IssueInputProbe` records callbacks with frame, time and InputState currentUpdateType. The Input Tools script observes device states but is not a replacement for callback evidence. | Input System; observer must be active before the CLI input. Test 30/120/unlimited fps and compare positive controls. | Not measured by the 23 operations. |
| 3. Input Actions type and persistence | #247: both create_action_map and add_input_action preserve Button/PassThrough/Value. #248: JSON source, memory state, reimport and full Editor restart agree. | `InputActionsHandlerTests` has all type cases for both creation paths; `InputActionsPersistenceTests` checks 10 edit variants and negative cases. `scripts/e2e-input-actions-persistence.py` performs disk/reimport/restart verification (50 assertions in macOS evidence). | Input System, Newtonsoft.Json, package listed in manifest testables. Standalone dedicated fixture with `UnityCliInputBatchHost.cs`; **no live Editor already using that project or its selected port**. Supply --unity-path and --project-path explicitly. | Asset copy/move/delete benchmarks do not exercise .inputactions source rewriting. Persistence E2E adds Button on map creation and Value via add; also run full type cases to cover PassThrough. |
| 4. Video recording formats | #249: mp4/webm/png_sequence must correspond to actual file headers; returned outputPath exists, PNG sequence contains multiple frames. | Python payload embedded in `scripts/e2e-video-formats.sh`, six named tests, validates MP4/WebM/PNG signatures and session lifecycle. | `com.unity.recorder` 5.1.6 for Unity 6000.4, Game View with a rendered Camera, graphics-enabled real Editor. Native Python extraction of the heredoc is appropriate; Linux Python plus Windows CLI cannot use Linux params-file paths. | Screenshot benchmark does not prove recording or any video format. |
| 5. PlayMode test results and Domain Reload | #250: two real FrameAdvances/RigidbodyFalls tests finish with Domain Reload enabled and disabled, with Scene Reload preserved. #251: totals count exactly two leaf tests, not fixture/assembly/root suites. | `scripts/e2e-test-domain-reload.py` against existing listener, **without --batch-host on Windows**; it exercises both settings and checks leaf counts. Focused real tests: `DomainReloadResultTests`, `TestResultCountingPlayModeTests`; collector/persistence unit tests separately. | `com.unity.test-framework`, package in `testables`, `DomainReloadE2ESettings.cs` menus available. Existing listener mode avoids lsof. The script restores settings. Do not run the full Bridge EditMode suite through the Bridge's own TestExecutionHandler: handler tests deliberately replace/reset the same static state. Use Unity's external native test runner for that suite. | Benchmark Play/Stop readiness is not run_tests/get_test_status and does not prove Domain Reload result recovery or counts. |
| 6. C# reference source resolution | #252: project version 6000.4.12f1 plus explicit branch 6000.4 works. #254: discover exact public tag / explicit branch, retain ref/commit provenance, never silently substitute an unrelated minor. | Python payloads embedded in `scripts/e2e-reference-fetch.sh` (5 checks) and `scripts/e2e-reference-resolution.sh` (4 checks), real Editor readiness plus isolated project-version fixtures and caches. | Network, native Git, license acceptance already requested by these suites; scratch cache only. Native Python must own the temp files supplied to the Windows CLI. No extra Unity package required. | `read_csharp` benchmark is local file reading; it does not exercise reference source acquisition, public ref selection or provenance. |
| 7. unityd on-demand / idle recovery and latency | #253: ordinary operations start a stopped daemon; recover after idle exit; concurrent first calls start only one daemon; preserve endpoint and report cold/warm costs. #394 adds performance regression coverage. | `scripts/e2e-unityd.py` owns its Editor but hardcodes the repository UnityCliBridge project: use its logic in a dedicated copied fixture, not that launch entrypoint against the live checkout. `scripts/bench-eval.py` can run native Windows without --require-frontmost/--activate; use 100 samples and explicit CLI/port. | Dedicated tools root; normal CLI daemon path; own project/port. Windows foreground observation requires a Windows implementation rather than the script's macOS osascript/lsof checks. | Existing 23 calls demonstrate successful ordinary operations, not concurrent cold-start / idle recovery. One sample per operation is not a performance comparison. |
| 8. Animation numeric curves | #255: localPosition.x keys 0s=0 and 1s=2, interpolation, key editing/removal, reject unsupported bindings, saved asset persists. | `scripts/e2e-animation-curves.sh` against existing listener; `AnimationCurveHandlerTests`. Port its shell assertions or run Git Bash with jq and native paths; its launch wrapper is macOS oriented. | Animation module, Newtonsoft.Json, disposable asset path. No optional package; jq required by shell runner. | Not covered by the 23 operations. |
| 9. Timeline track / clip / binding editing | #256: Animation Track with start/duration/binding, save/reload persistence, PlayableDirector evaluation reaches expected Animator pose, explicit unsupported-track/package errors. | `scripts/e2e-timeline.py` is native Windows compatible with existing listener and explicit --unity-cli / --port. `TimelineHandlerTests` separately. | `com.unity.timeline` 1.8.12, animation/director modules, `UnityCliTimelineE2EFixture.cs` copied into isolated Assets/Editor. Current full suite also calls capture_video_status, so Recorder is needed; --without-timeline expects both Timeline and Recorder missing. | Not covered by the 23 operations. |
| 10. In-Editor C# eval and caching | #257: numeric3, scene object read/create/modify, syntax error vs runtime exception, no persistent .cs file required. #391: correct reference invalidation across domain/new assembly and 1000 evals within memory/assembly bounds, plus 100-sample latency. | `scripts/e2e-eval.py` is native Windows compatible; default --soak-iterations1000, explicit CLI/port. `scripts/bench-eval.py` measures 100 samples; omit macOS-only focus flags. | Roslyn bundled with Bridge, graphics or batch listener. Set UNITY_PROJECT_ROOT to owned fixture. Its no-source-write assertion snapshots repository UnityCliBridge, so independently inspect the actual selected fixture when claiming AC-5 no persistent C# writes. | Benchmark eval1+2 is only one successful sample, not error contracts, objects, invalidation, soak or performance. |
| 11. Play Mode method hot reload | #258/#390: change existing calculation without stopping Play or losing scene/object/HP/score/state; invalid source, stale revision and partial/unobserved patch must not be successful. | `scripts/e2e-hot-reload.py --project ... --unity-cli ... --port ... --expect supported --require-arch X64`; prepare an isolated fixture from the existing batch-host wrapper. | **FastScriptReload 1.8.0**, separately installed at pinned commit `51140b71d9e5df1de231b33ec20ee089b18bebec`, plus copied `tests/fixtures/hot-reload/HotReloadProbe.cs` and `HotReloadE2EFixture.cs`. Disable FSR automatic/on-demand reload as documented. Adapter code allows X64 on Windows; ARM64 is limited to macOS. | Missing-package or unsupported-platform error-contract PASS does not prove real method replacement. No benchmark coverage. |
| 12. Lighting / navigation / occlusion bake | #259/#392: actual lightmap data plus saved scene references; legacy NavMesh and NavMeshSurface distinct; SamplePosition / path usable; Occlusion data assigned; failed/empty/no-output cases cannot report success. | `scripts/e2e-bake.py --unity-cli ... --project-root ... --port ... --targets lighting,navmesh-legacy,navmesh-surface,occlusion`; `UnityCliBakeE2EFixture.cs` creates/validates isolated bake scenes. | `com.unity.ai.navigation` 2.0.13, AI/terrain/lighting modules, fixture Editor source. **Graphics enabled** for real lightmap bake, not -nographics. Test legacy support for selected Unity; an UNSUPPORTED error is separate from a successful bake. | No benchmark coverage. Job acceptance alone is not generated/persisted artifact proof. |
| 13. Windows Player build / launch | #260/#392: .exe, _Data and BuildReport paths agree; execute generated Player; distinguish headless process from actual rendered game/input; preserve settings; errors are failures. | `scripts/e2e-player-build.py --cli ... --unity ... --project-path ... --target StandaloneWindows64` on existing listener; dedicated --launch --isolated-project mode adds compiler/build-failure/interruption cases. Safe Windows auth helper has been fixed in this worktree. | Windows Standalone Build Support for selected Editor, writable scratch output, saved minimal scene. Use --isolated-project with --launch to avoid Unix fcntl branch. Existing module probing looks under editor.parent.parent/PlaybackEngines; audit native Hub Editor/Data layout. | Script validates Windows artifacts but currently only launches the generated Player for StandaloneOSX. Windows headless and graphical/input launch observations require additional actual execution; script's final "Windows hardware pending" must not be misreported as verified. |

## Additional known original positive row

Issue #395 explicitly quotes the original report's **Prefab creation/placement/editing**
row as ○ for both products, with only direct editing/Prefab Mode save verified on
unity-cli and Variant/Overrides/Unpack verified on official CLI. This row is
outside the inferred 13 △/— categories above and illustrates why the missing
complete report still matters. Native existing-listener `scripts/e2e-prefab.py`
adds 45 meaningful runtime assertions plus 22 focused EditMode tests, covering
Variant inheritance, preserved overrides, apply destinations, individual/whole
revert/apply, and Outermost/Completely semantics. No optional Unity package is
needed. Supply --project-root, --unity-cli, --port and new --output; omit --launch
because its launch path and ownership lsof are macOS specific.

## Native Windows commands for an already prepared owned listener

These are reviewable commands, **not evidence that they have been executed**.
The owner must set the process-local UNITY_PROJECT_ROOT, UNITY_CLI_TOOLS_ROOT,
UNITY_CLI_NO_AUTO_UPDATE and any owned lockfile directory consistently. Invoke
all shell wrappers using `bash -lc` and the repository workdir as required.

```text
py.exe -3 -X utf8 -B scripts/e2e-prefab.py --unity-cli <workspace>/target/debug/unity-cli.exe --project-root <workspace>/.cache/issue-386/red-project --port 6487 --output <workspace>/.cache/issue-386/staff-prefab-final --editmode

py.exe -3 -X utf8 -B scripts/e2e-eval.py --unity-cli <workspace>/target/debug/unity-cli.exe --port 6487 --soak-iterations 1000

py.exe -3 -X utf8 -B scripts/e2e-timeline.py --unity-cli <workspace>/target/debug/unity-cli.exe --port 6487

py.exe -3 -X utf8 -B scripts/e2e-bake.py --unity-cli <workspace>/target/debug/unity-cli.exe --project-root <workspace>/.cache/issue-386/red-project --port 6487 --targets lighting,navmesh-legacy,navmesh-surface,occlusion

py.exe -3 -X utf8 -B scripts/e2e-test-domain-reload.py --cli <workspace>/target/debug/unity-cli.exe --port 6487 --output <workspace>/.cache/issue-386/staff-domain-reload.json

py.exe -3 -X utf8 -B scripts/bench-eval.py --unity-cli <workspace>/target/debug/unity-cli.exe --port 6487 --iterations 100 --warmup 3 --out <workspace>/.cache/issue-386/staff-eval-100.json

py.exe -3 -X utf8 -B scripts/e2e-player-build.py --cli <workspace>/target/debug/unity-cli.exe --unity C:/Program\ Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe --project-path <workspace>/.cache/issue-386/red-project --port 6487 --target StandaloneWindows64
```

For Input Actions restart E2E, close any Editor using **that dedicated copied
fixture only**, then call:

```text
py.exe -3 -X utf8 -B scripts/e2e-input-actions-persistence.py --unity-cli <workspace>/target/debug/unity-cli.exe --unity-path "C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe" --project-path <workspace>/.cache/issue-386/staff-persistence-project --port 6489
```

Use the actual installed Editor path; the path above is illustrative. The owner
uses a process argument array, so shell backslash escaping is unnecessary there.
For video/reference scripts whose Python bodies are heredocs, extract the body
without changing assertions, set UNITY_CLI_BIN, and run it through native Python
with native Windows artifact/project/CLI paths. Keep original source hash and
extraction hash in the evidence. Running Linux Python with native CLI params-file
paths creates false failures. The macOS e2e-matrix entrypoint cannot be directly
claimed Windows-compatible: Hub layout, lsof, osascript and os.killpg are embedded.

## Safety repair performed during this investigation

`scripts/bridge_auth.py` now shares `process_alive()` with
`scripts/e2e-editor-lifecycle.py`. Windows uses OpenProcess with SYNCHRONIZE,
WaitForSingleObject(timeout0) and closes the handle. Unix preserves signal-zero
existence behavior, with invalid/out-of-range PID rejection.

The original Windows Python signal-zero path is **not an observation**:
CPython 3.14 maps zero to CTRL_C_EVENT and calls GenerateConsoleCtrlEvent; with
nonzero target PID it can succeed without observing the process. It therefore
reported already exited children as alive and selected stale credentials.
PID zero broadcasts a console event. The first exploratory RED was interrupted
by that behavior; the formal safe RED uses only already exited owned child PIDs.
No Editor PID was passed to these regression tests.

- Formal RED: `process-liveness-exited-red.txt`, child-based authentication and
  lifecycle checks both fail because an exited child is considered live, exit1.
- Windows GREEN: `process-liveness-green.txt`, 4/4 tests; existing auth tests
  `bridge-auth-green.txt`, 3/3.
- Linux regression: `process-liveness-linux.txt`, 3 PASS / 1 Windows-specific
  exit-code skip; existing auth3/3 PASS.
- New tests cover a live child surviving both probes, exited child rejection,
  zero/negative/nonrepresentable IDs, and Windows child exit259, which must not be
  confused with STILL_ACTIVE. No source commit was made by this investigator.

Primary runtime references:
[CPython os.kill implementation](https://github.com/python/cpython/blob/3.14/Modules/posixmodule.c#L9067)
and [Microsoft GenerateConsoleCtrlEvent semantics](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent).

## Canonical verification / PM handoff ruling provenance

The exact clause "canonical verify.run が他の holder のホスト排他で待たされる場合は
待たず、PR の CI を最終確認とする。push したら Board で『PR作成可』と引き継ぐ。
PR は PM が作成する" appears in the live owner-authored **Issue bodies #390–395**,
under Notes. #390/#391/#393 comments explicitly applied it to their own work.
Issue #392 author is akiojin; body last updated 2026-09-30T11:48:06Z in this snapshot.

This proves a ruling shared by that Issue batch. It does **not** establish a
project-wide permanent override, and #386's current body has no such clause.
It does not authorize bypassing #386's blocked execution.reopen or protected
verification records. Ask the PM for a #386-specific ruling if needed; preserve
direct local PASS evidence while canonical tooling recovery is coordinated.

## Original-report search result

Parent's filename searches covered the worktree, E:/unity-cli, E:/gwt and user
home. Additional investigation found no matching tracked file in `git log --all
-- '*staff*report*' '*pm-scratch*' '*project-state*'`. GitHub code search for
staff-report in akiojin/unity-cli found only `scripts/bench-editor-ops.py`, not the
report. Issue-body search found references, not an attached complete report.
Fetched primary issue snapshots and complete comments remain in this directory
as `<number>.json` / `<number>.md`, and the pre-implementation scenario summaries
are in `original-scenarios.md`.

Next verification should run the portable focused suites on prepared isolated
Windows fixtures and record each category's actual PASS/FAIL/UNSUPPORTED plus
raw evidence. Keep the completeness comparison and original positive feature
rows pending until the original report is supplied or the owner explicitly
rules that a reconstructed scope replaces it.
