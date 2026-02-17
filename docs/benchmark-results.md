# Benchmark Results (unity-cli)

This repository no longer tracks Node-based benchmark data from `unity-mcp-server`.

Current performance validation policy:

- Rust CLI regressions are checked by code review + test benchmarks.
- LSP behavior is validated by `dotnet test lsp/Server.Tests.csproj`.
- Any migration-related performance claim must be backed by reproducible measurements in the relevant PR.

When adding new benchmark reports, place them under `docs/benchmarks/` and include:

1. test environment (OS, CPU, Unity version)
2. exact command lines
3. before/after numbers
4. raw logs or scripts used
