# Dependency integration (#342)

This integration supersedes Dependabot PRs #238 and #212 (Cargo),
PRs #222, #228, #232, #233, and #235 (GitHub Actions), and #229 (npm).
The PM should close those original PRs after this integration merges.

## Compatibility decisions

- Pin `bincode` to `=1.3.3`: version 3.0.0 contains an unconditional
  `compile_error!("https://xkcd.com/2347/")`. The original #238 CI failed
  compiling that crate. Retaining 1.3.3 also preserves existing embedding
  index serialization. Revisit only with a working replacement and an
  explicit index-format migration.
- Upgrade `fastembed` to 6.x. Its inference method now requires mutable
  access; keep that access inside `FastEmbedder` without changing callers.
- Upgrade `serde_yml` to 0.0.13. Mapping keys are now strings. Explicitly
  reject empty frontmatter keys to preserve the existing validation contract.
- Upgrade `similar` to 3.x, `zip` to 8.x, and `dirs` to 7.x, plus the
  compatible updates from #238. The former transitive `tar` dependency
  targeted by #212 is absent from the resulting dependency graph.
- Synchronize both npm and pnpm lockfiles with the #229 manifest updates.
- Run the JavaScript lint job on Node 24: the updated commitlint requires
  Node >=22.12 and markdownlint-cli requires Node >=22.

## Verification

Run Rust formatting, clippy with warnings denied, all Rust tests serially,
Rust line coverage (90% minimum), skill contract lint, LSP tests, Markdown
lint, and the all-tools E2E sweep on macOS Unity 6000.4.11f1.
Record results and CI links on #342 before marking its criteria complete.

Local results (2026-09-30, macOS):

- `cargo fmt --all -- --check`: PASS.
- `cargo clippy --all-targets -- -D warnings`: PASS.
- `cargo test --all-targets -- --test-threads=1`: 464 unit and 10 integration
  tests PASS, including YAML frontmatter validation, embedding index
  serialization, reference diff/archive handling, and daemon startup.
- `cargo llvm-cov --all-targets --summary-only --fail-under-lines 90 -- --test-threads=1`:
  PASS, 91.77% line coverage. Run separately from live daemon E2E to avoid
  sharing daemon state with transport error-path tests.
- `cargo run -- skills lint --severity error`: 16 skills, zero violations.
- `dotnet test lsp/Server.Tests.csproj`: 50 tests PASS.
- Node 24: Markdown lint, commitlint, changed-file Prettier checks, and
  `node --test tests/scripts/*.mjs` (8 tests) PASS.
- Both `pnpm install --frozen-lockfile` and isolated
  `npm ci --ignore-scripts --no-audit --no-fund`: PASS.
- `cargo build --release`: PASS.
- `scripts/e2e-all-tools.sh --port 6492 --unity-cli "$PWD/target/release/unity-cli"`:
  PASS on Unity 6000.4.11f1, 133 invocations, zero failures, 122 unique tools,
  26 existing documented exclusions; all six LSP performance cases PASS.
  Log: `/tmp/unity-cli-e2e-all-tools-20260930-012211.log`.
- User Verification Result: n/a (autonomous).

For local E2E, stop daemons using the dedicated `.cache/csharp-lsp` tools
root, publish this checkout's LSP under
`.cache/csharp-lsp/csharp-lsp/osx-arm64`, then start the CLI under test with
`lspd serve` and `unityd serve` using that `UNITY_CLI_TOOLS_ROOT`.
For a framework-dependent LSP publish on Homebrew, give both the daemon and E2E
`DOTNET_ROOT` pointing to the installed runtime's `libexec` directory.
Otherwise automatic daemon startup can download the published CLI and LSP
instead of using the checkout's implementation. The test-only
`UNITY_CLI_TEST_SKIP_MANAGED_UPDATE` variable does not disable that behavior
in normal CLI builds.
In particular, v0.14.1 predates the existing LSP scope-forwarding fix and
scans package sources even for an assets-only performance test.
