# Issue #547: C# LSP automatic installation

Agent: サト / Codex
Date: 2026-11-11
Launch mode: autonomous
Baseline: origin/develop at `3b4239b2b85ccffe993d2267641ba83b0256b5b6`
Changed surfaces: Rust CLI/process lifecycle and skill documentation.
User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)

## Findings and scope

At intake, HEAD and origin/develop were identical. The existing auto-search
installer discarded errors, left its request marker after failure, and used
plain process spawning instead of the Windows handle-safe helper. Setup did
not require the LSP, and the editing skill suggested direct writes before
attempting installation. The report alone does not prove which path caused
the original missing installation.

The change keeps auto-search nonblocking and write operations using their
existing synchronous installation path. Windows uses the existing detached
spawn helper and CREATE_NO_WINDOW. Download URLs, checksum validation, native
path arguments, and macOS/Linux command selection remain unchanged.

## Acceptance tests

| Criteria | Test / evidence |
| --- | --- |
| AC-1 | `release_background_install_survives_cli_exit_in_unicode_directory`: real release download into an initially absent tools root under `日本語 ユーザー/.unity/tools/csharp-lsp/win-x64`; initial CLI exits in under 10 seconds; binary and VERSION exist; doctor reports installed. |
| AC-2 | `failed_background_install_is_diagnosed_and_writes_offer_recovery`: offline proxy produces a real failed child installer; doctor retains the fetch error and recovery command. `background_install_claim_is_atomic_and_spawn_failures_allow_retry` covers spawn errors and retry. |
| AC-3 | The same CLI integration test invokes `write_csharp_file`, checks installation guidance in its error, and verifies the file was not modified. |
| AC-4 | Cross-platform tests and existing Unix auto-search regression tests; only Windows receives the new creation flag. Platform CI must pass before delivery. |
| AC-5 | `missing_lsp_must_be_recovered_before_direct_editing`: doctor/install precede direct editing, with retry and a documented last resort. |
| AC-6 | `usage_bootstrap_and_quick_start_require_lsp_install_and_doctor`: both setup flows require installation and doctor, including `binaryExists: true`. |

## TDD and observed results

- RED: offline CLI integration failed because `.auto-install-error` was never
  produced; doctor exposed no child-install diagnostic.
- RED: two skill tests failed because doctor/install were absent.
- GREEN: skill tests 4/4; offline CLI integration 1/1; lifecycle unit tests 2/2.
- GREEN: explicit real-release integration 1/1 on Windows win-x64, version
  `0.19.1`, 19.01 seconds for the complete test. This ran after the final
  product code change and used isolated storage, preserving the user's install.
- Markdown lint passed for both changed skills.

## Canonical verification matrix

Register and run the following through `verify.plan` and `verify.run` after
the final commit. The immutable run record and final results belong in the PR
and Issue delivery evidence; these preliminary observations do not claim
develop already contains the fix.

```text
cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --all-targets -- --test-threads=1
cargo run -- skills lint --severity error
dotnet test lsp/Server.Tests.csproj
cargo test --test lsp_auto_install release_background_install -- --ignored --test-threads=1 --nocapture
node node_modules/markdownlint-cli/markdownlint.js .claude-plugin/plugins/unity-cli/skills/unity-cli-usage/SKILL.md .claude-plugin/plugins/unity-cli/skills/unity-csharp-edit/SKILL.md docs/verification/issue-547/README.md --config .markdownlint.json --ignore-path .markdownlintignore
```

The real-release test is explicitly selected because normal tests stay
offline. No browser or Unity Editor UI is changed by this patch.
On Windows the canonical run selects the absolute `node.exe` path because
the runner cannot resolve the shell's `npx` shim. The initial run passed the
other six commands; its lint spawn failure is retained as FAIL evidence.

A subsequent full run exposed an existing environment-dependent failure in
`unknown_category_is_rejected`: it contacted a live Editor and returned TIMEOUT.
Its offline fixture now uses a closed ephemeral endpoint and isolated runtime
paths. The focused test passes in 0.17 seconds; no catalog behavior was changed.
