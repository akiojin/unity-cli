# Input Actions persistence verification

Issue: #248. Implementation: `34707a8`. PR: #265.

## Acceptance audit

Before editing production code, both acceptance criteria were unmet: all ten
mutation branches used `SetDirty`/`SaveAssets` without saving the imported source,
and no actual Editor persistence E2E evidence existed.

AC-1 is verified locally by immediate source JSON comparisons, forced reimport,
and a full Editor stop/start. AC-2 is verified by the CLI calling an actual Unity
batch-host listener. Release remains pending CI and merge.

## Environment and commands

- macOS, Unity 6000.4.11f1, Input System 1.19.0, port 6428.
- Windows / Unity 6000.4.12f1 from the original report was not run.
- `cargo build --bin unity-cli`
- `python3 scripts/e2e-input-actions-persistence.py`
- Unity Editor arguments: `-batchmode -nographics -projectPath UnityCliBridge -runTests -testPlatform EditMode -testFilter UnityCliBridge.Tests.InputActionsPersistenceTests -testResults /tmp/issue248-verify.xml -logFile /tmp/issue248-verify.log`
- `cargo fmt --all -- --check`
- `cargo run -- skills lint --severity error`
- `git diff --check`

Canonical record: `vrr-6e3a8dd3b64644b7bd7ef10caa4f6c92`, five commands PASS.
The CLI build also passed. Commit-time Markdown lint passed.
User Verification Result: n/a (autonomous).
Agent Visual Check: n/a (source persistence; no visual UI change).

## Results and inventory

RED: EditMode 1 passed / 10 failed; every edit failed the immediate source JSON
assertion. CLI E2E failed immediately after the first successful map creation.

GREEN: EditMode 13 passed / 0 failed / 0 skipped:

- Ten cases of `SuccessfulEdit_ChangesSourceAndSurvivesReimport`: create/remove
  map, add/remove action, add/remove binding, remove all bindings, composite
  binding, add/remove control scheme.
- `InvalidEdit_DoesNotChangeSource`.
- `ReadOnlySource_ReturnsErrorInsteadOfSuccess`.
- `NativeAsset_EditStillUsesUnitySerialization`.

CLI E2E: 50 passed / 0 failed. Sixteen mutation calls each compare source
map/action/binding contents and IDs, source control schemes, and state after
reimport. Two additional comparisons confirm source and complete state survive
Editor restart. Final cases remove the last map and the remaining control scheme.

The last-map case preserves device `isOptional` and `isOR` flags. Optional binding
strings (`groups`, `interactions`, `processors`) normalize null to empty because
Unity's JSON importer treats these representations equivalently. IDs, paths,
types, structure, and composite flags are not normalized away.

## Evidence artifacts

Local test output: `/tmp/issue248-verify.xml` and `/tmp/issue248-verify.log`.
The E2E run printed artifact directory
`/var/folders/p8/3j934ld94g5drz6_x1zy39m80000gn/T/input-actions-persistence-nfkbjga3/`.
It contains `commands.log`, `unity-1.log`, `unity-2.log`, `before-restart.json`,
`after-restart.json`, and `summary.json`. Artifacts are machine-local; rerunning
the command creates a new directory and prints its path.

Log summary: every successful edit matched disk immediately, every forced
reimport retained the edited state, and both Editor generations reported the
same map/action/binding/scheme IDs and structure. Generated assets were removed.
