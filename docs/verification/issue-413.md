# Issue #413: isolate Rust runtime tests

## Scope and acceptance audit

Baseline: `1393ff6` (develop), macOS arm64, 2026-10-01.
Before edits, AC-1 and AC-3 were missing: the remote-command test used the
home unityd, and `test_env` provided only a mutex. AC-2 and AC-4 had no
corresponding verification/PR evidence. No existing implementation PR was linked.

Only test code changes. `TestEnvironment` allocates a per-test tools/cache root,
registry and Editor lockfile directory, clears inherited endpoint/project
overrides, and restores the original OS-string values on drop (including panic).
It borrows the existing environment mutex guard so restoration precedes unlock.

Applied to runner tests that contact daemons, unityd runtime tests (whose HOME
override alone did not override an inherited tools root), instance tests, and
the default execution-context test. Existing LSP tests already isolate their
tools roots; endpoint tests already isolate registry and Editor lockfiles.

## Reproduction and verification

The developer's existing home daemon remained running; it was not restarted or
stopped for this verification. `unity-cli unityd status` reported:

```json
{"pid":81348,"running":true,"runtimeDir":"/Users/akiojin/.unity/tools/run/unityd"}
```

1. With that daemon running, run
   `cargo test run_with_cli_exercises_remote_command_error_paths -- --test-threads=1`.
   Before the fix: **FAIL**, `non-empty batch should fail when unity is unreachable: ()`.
   The shared daemon returns a batch result containing an error, whereas this test
   expects the direct connection failure of an unavailable daemon/Editor.
2. Apply the test isolation and repeat the same command: **PASS** (1 test).
3. Run `cargo test --all-targets -- --test-threads=1` without externally setting
   `UNITY_CLI_TOOLS_ROOT` or replacing HOME: **PASS**, 572 tests, 0 failures.
   The inventory is [rust-tests.txt](issue-413/rust-tests.txt): 552 unit tests and
   integration suites with 1, 2, 4, 5 and 8 tests.
4. Check the home daemon during and after the suite: the same PID 81348 reports
   `running: true`. In particular, runner status/stop tests no longer stop it.

The new regression test verifies distinct runtime paths, empty Editor discovery,
temporary-directory cleanup, preservation of caller files, and restoration of
both present and absent environment variables after panic.

`cargo fmt --all -- --check`, `cargo clippy --all-targets -- -D warnings`
(stable toolchain), and `git diff --check` also passed.

This runtime evidence is from macOS/Unix sockets. Windows uses fixed daemon TCP
ports; this report does not claim a Windows live-daemon coexistence run.

## Pattern for future tests

```rust
let lock = crate::test_env::env_lock()
    .lock()
    .unwrap_or_else(|p| p.into_inner());
let _environment = crate::test_env::TestEnvironment::new(&lock);
// Set any test-specific environment overrides after creating the guard.
// Finish/join all tasks and daemon threads before dropping it.
```

Keep `--test-threads=1`: environment reads elsewhere are process-global and do
not necessarily acquire the mutex. Tests of default HOME fallback paths retain
their explicit HOME fixtures rather than overriding the behavior being tested.

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)

PR creation belongs to PM per the issue Notes. AC-4 remains pending until that
PR's CI succeeds; local test success alone is not CI evidence.
