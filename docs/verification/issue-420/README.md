# Issue #420 verification

Launch mode: autonomous

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)

## Acceptance audit

The initial worktree contained an unfinished async conversion. The issue was
open, had no implementation PR, and had no Editor evidence or parent-SPEC
comment. The remaining work was verified before making further source changes.

| Criterion | Evidence |
| --- | --- |
| AC-1 | Async post-write pipeline; current-thread Tokio regression test; all three public write tools exercised through the CLI |
| AC-2 | CLI host/port/project overrides retained; compilation state and diagnostics returned; conflicting environment variables used in tests and Editor runs |
| AC-3 | Structured refresh/compile failures retain `applied` and `changedFiles`; real LSP retry tests prove full-file writes do not duplicate content |
| AC-4 | Rust CLI integration tests, Tokio regression test, and three real LSP retry cases |
| AC-5 | Both real macOS Editors passed; see [editors.json](editors.json) and the [Issue comment](https://github.com/akiojin/unity-cli/issues/420#issuecomment-5927051029) |
| AC-6 | [Parent SPEC #153 comment](https://github.com/akiojin/unity-cli/issues/153#issuecomment-5927051760) |
| AC-7 | Evaluated by the implementation PR's CI; local checks alone do not satisfy this criterion |

## Reproduction and repair

Installed unity-cli 0.16.0 reproduced exit 101 at
`src/tooling/local_tools.rs:842`, with `Cannot start a runtime from within a
runtime`. The source fix awaits remote work from the existing CLI runtime.

The first real Editor run then exposed `compile_wait_failed: early eof` during
Domain Reload. The connection-drop regression failed before the reconnect fix
and passed afterward. Only compilation-state reads retry, within a 60-second
deadline. File writes and refresh are never replayed by that loop. Expiring the
deadline returns a structured failure instead of an unfinished success.

The public post-write options are supported by `create_csharp_file`,
`write_csharp_file`, and `apply_csharp_edits`. Symbol-edit APIs do not expose
these options. Existing LSP behavior rejects a second create of an existing
file and reports `no_change` for identical full-file writes; no new persistent
deduplication state is needed.

## Test inventory

- `post_write_refresh_failure_preserves_applied_files_without_runtime_panic`
- `post_write_tools_use_explicit_endpoint_and_include_compile_results`
- `retry_after_refresh_failure_preserves_files_and_content`
- `compile_failure_returns_structured_error_and_saved_files`
- `preview_does_not_refresh_or_compile`
- `compilation_read_reconnects_after_domain_reload_without_rewriting`
- `RetryingFullFileWrites_DoesNotDuplicateContent` (`create`, `write`, `apply`)

Quality commands:

```sh
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets -- --test-threads=1
cargo run -- skills lint --severity error
dotnet test lsp/Server.Tests.csproj
```

Local results: all five commands passed; Rust 588 tests passed, .NET 53 tests
passed, and 19 skills had zero error-level violations. Final delivery evidence
is recorded separately by the canonical `verify.run` after commit/push.

## Real Editor verification

Both Editors were launched in owned temporary projects prepared by
`scripts/e2e-matrix.py:prepare`, with the checked-out bridge package and the
batch host. Ports 6493 and 6494 were isolated from other sessions. The LSP was
published from `lsp/Server.csproj` to a temporary tools directory.

Each run used the exact command recorded in `editors.json`. Environment values
`UNITY_CLI_PORT=1` and `UNITY_PROJECT_ROOT=<different project>` deliberately
conflicted with explicit CLI options. Both returned exit 0, `success: true`,
`applied: true`, `isCompiling: false`, `isUpdating: false`, and `errorCount: 0`.
The compilation timestamps followed each refresh timestamp. These runs include
real Domain Reload. Editor bridge logging produced warnings; zero warnings is
not claimed. Owned Editors were shut down after collecting evidence.

The JSON contains response excerpts plus SHA-256 hashes and paths of full local
responses. Temporary raw logs are supplementary; the checked-in excerpts retain
the acceptance evidence.
