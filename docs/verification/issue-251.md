# Issue #251 — leaf-only test result counts

## Acceptance evidence

- AC-1: `TestFinished` ignores suites in all result collections. Two passing
  leaf tests produce `totalTests=2`, `passedTests=2`, zero failed/skipped/
  inconclusive tests and exactly two `tests` entries. Exported JSON agrees.
  Suite-only failures (such as `OneTimeTearDown`) remain in `failures` and
  make the run unsuccessful without increasing the failed leaf count.
- AC-2: Actual Unity Editor **6000.4.11f1**, macOS, project `UnityCliBridge`,
  TCP listener `127.0.0.1:6451`, default `DisableDomainReload` setting.
  No settings changes or scene assets were needed.
- Launch mode: autonomous (Issue Monitor).
- User Verification Result: n/a (autonomous).
- Agent Visual Check: n/a (no UI surface).

## Commands

```bash
cargo build --release
UNITY_CLI_ALLOW_BATCH_HOST=1 UNITY_CLI_PORT_OVERRIDE=6451 \
  UNITY_CLI_BATCH_HOST_SHUTDOWN_FILE=/tmp/unity-cli-issue-251-stop \
  /Applications/Unity/Hub/Editor/6000.4.11f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD/UnityCliBridge" \
  -executeMethod UnityCliBridge.TestScenes.UnityCliInputBatchHost.Run \
  -logFile "$PWD/UnityCliBridge/.unity/issue-251/editor.log"
# From another shell, after the Editor is ready:
UNITY_CLI_PORT=6451 scripts/e2e-test-results.sh
```

The E2E script runs `run_tests` / `get_test_status` through the built CLI,
checks the current run ID, then compares status, `latestResult.summary`,
`latestResult.fileContent` and the exported file. It requires the project's
default Domain Reload disabled setting and refuses a busy Editor.
To stop the dedicated batch host, create `/tmp/unity-cli-issue-251-stop`;
remove that file before launching the host again.

## RED → GREEN

On 2026-09-27, before the production change:

- PlayMode: 2 actual cases passed, but `passedTests=8` and export `passed=8`.
  The six excess entries were fixture/namespace/assembly/root suites.
  The E2E assertion failed as expected.
- Collector regression: 6/6 cases failed for suite contamination. The
  two-leaf/three-suite case observed 5 instead of 2; empty and individual
  status cases observed 1 instead of 0.

Review additionally found the suite-only failure boundary. Its regression
failed with `success=true` after two passing leaves (6/7 cases passed).
Retaining suite failure diagnostics independently fixed it.

After the final production change:

```text
PASS: 7/7 collector regression tests; 0 failures.
PASS: 2/2 PlayMode tests; CLI counts, leaf results, latest result and export agree; 0 failures.
```

Artifacts: `UnityCliBridge/.unity/issue-251/{editor.log,e2e-red.log,e2e-green.log,collector-red.json}`
and `UnityCliBridge/.unity/test-counting-e2e/<timestamp>/` (ignored local outputs).
Final successful combined run: `20260927-230450`.

## Test inventory

- `TwoLeavesAndThreeSuites_ReportOnlyTwoTestsInStatusAndExport`
- `SuiteStatus_IsIgnoredWhileLeafStatusIsRetained` for Passed, Failed,
  Skipped and Inconclusive (four cases)
- `EmptySuite_HasZeroTestsAndNoResults`
- `FailedSuiteAfterTwoPassedLeaves_PreservesRunFailureWithoutIncreasingTestCounts`
- `TestResultCountingPlayModeTests.FrameAdvances`
- `TestResultCountingPlayModeTests.RigidbodyFalls`

## Verification scope

Changed surfaces: C# test-result aggregation, Unity test fixtures, local E2E
script and documentation. Unity compilation and the nine cases above cover
the changed behavior; `bash -n`, Markdown lint and `git diff --check` cover
the script/docs. Rust and LSP implementations are unchanged. The original
Windows/Unity 6000.4.12f1 environment was not re-run; Domain Reload enabled
test-run lifecycle is outside this suite-counting fix.
