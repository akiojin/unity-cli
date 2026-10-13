# Issue #504 — Windows lspd cold-start latency

Launch mode: autonomous

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)

## Cause and regression proof

The Windows client used `TcpStream::connect` without a connection deadline.
The initial tool request, status check, stale-file cleanup probes and readiness
polling repeatedly waited for Windows TCP retries when lspd was absent.
The ten-second read/write timeouts were set only after connecting.

On Windows 11, a closed loopback port took 2025ms to reject a connection;
a 100ms deadline returned in 110ms. The new production-path regression test
failed before the fix after 2.027s, while the running-listener test passed.
Both tests passed after the fix in 0.11s total.

Use the same 100ms local connection deadline as unityd. This limits only the
TCP handshake; daemon startup and tool response deadlines remain independent.
Tests use ephemeral ports, avoiding the shared Windows lspd port 6421.

## Measured results

The same Windows host and measurement runner used a real CLI and LSP.
`before.json` and `after.json` retain hashes, responses, differences and timings.

| Condition / write_file | Before n / p50 / p95 (ms) | After n / p50 / p95 (ms) |
| --- | --- | --- |
| daemon_stopped | 3 / 13183.299 / 15134.534 | 5 / 5517.880 / 9375.031 |
| first_download | — | 3 / 6632.017 / 7675.544 |
| warm_indexed | — | 2 / 216.366 / 220.186 |
| unindexed | — | 2 / 712.967 / 719.874 |

The original CI report in #504 measured about 43 seconds on its Windows host.
The local baseline differs; the PR's existing three-OS LSP Write Reliability
workflow supplies a separate CI comparison. Local cold-start p50 is below 10s.

All twelve after-fix tool calls returned a successful LSP edit. Published LSP
0.18.1 still added a BOM (#498); byte equality therefore failed in every sample.
These defects remain in the JSON and are accepted only by the existing
version-scoped regression baseline. This report proves latency improvement,
not qualification for prohibiting direct editing.

## Reproduce

```bash
cargo test --bin unity-cli lsp::daemon::tests::tcp_connect -- --test-threads=1
cargo build --bin unity-cli
dotnet publish lsp/Server.csproj -c Release -r win-x64 --self-contained true -o .cache/lsp-reliability-publish
python -B scripts/lsp-write-reliability.py --unity-cli target/debug/unity-cli.exe --lsp-publish .cache/lsp-reliability-publish --out .cache/issue-504-verified --conditions daemon_stopped --scenarios write_file --cold-repetitions 5 --baseline tests/fixtures/lsp-write-baseline.json
python -B docs/verification/issue-504/check-evidence.py .cache/issue-504-verified/results.json
```

The evidence checker requires at least three completed Windows cold samples,
successful real LSP responses, no new write defect, and p50 strictly below 10s.

## Unrelated baseline check

An additional Windows `dotnet test lsp/Server.Tests.csproj` run produced
54 passes and two failures in the existing
`RequestRouterTests.SymbolQueries_RespectAssetsScopeBeforeScanningPackages`
theory (`workspace/symbol` and `unitycli/referencesByName`): both returned an
empty array instead of `Asset.cs`. No C# source changed in this fix.
`Uri2Path` in `LspRequestRouter.cs` strips `file://` from the test's standard
`file:///C:/...` URI, leaving a Windows path starting with `/C:/...`.
This separate finding was reported to the PM. The full C# suite is verified on
Linux, matching the repository's required `LSP Tests` CI environment.
