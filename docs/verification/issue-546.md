# Issue #546: legacy Unity identity API regression guard

Agent: サト. Launch mode: autonomous. Bridge version: 0.19.1.

User Verification Result: n/a (autonomous)
Agent Visual Check: n/a (no UI surface)

## Scope and acceptance

The fetched `origin/develop` already contained the six Prefab handler fixes and
test migration through PR #551 (`69d1a0d2e`). There were no undelivered source
commits on this branch. This change adds only the missing AC-3 regression guard
and verification evidence; no Bridge C#, wire IDs or matrix infrastructure changes.

| Criterion | Evidence |
| --- | --- |
| AC-1 | Delivered Prefab handler uses `ObjectIdentity.GetInstanceId` / `FindObject`; the helper still returns integer wire IDs |
| AC-2 | Delivered Prefab tests use the same helper; source scan covers tests as well as production C# |
| AC-3 | `test_bridge_object_identity.py` scans maintained Bridge C# and fails with file, line and helper replacement guidance |
| AC-4 | Current-source batchmode results and compressed logs in [compile.json](issue-546/compile.json); validated against every Bridge C# source hash |
| AC-5 | Diff is limited to the guard, its required lint step, this evidence and required Work records |

The guard exempts only `Packages/unity-cli-bridge/Editor/Helpers/ObjectIdentity.cs`.
An unrelated file with the same name is checked. It scans source text, including
inactive preprocessor branches, and ignores Unity's generated root caches
(`Library`, `Temp`, `Obj`, `Logs`, `UserSettings`). Whitespace and multiline calls
are detected. To repair a failure, use the compatible helper at the reported
location and rerun the test. This intentionally strict text check also matches
legacy call syntax in comments or string literals outside the helper.

The guard runs in the existing **Rust Format & Lint** required CI job. The full
Python discovery also finds it in **CLI Latency (required)**, but the live branch
protection does not require that latter job despite its name. An explicit step
in the protected lint job therefore makes source violations block delivery.

## Automated verification

The guard was developed RED → GREEN: the scanner contract tests failed before
the detector existed, then passed with the implementation. Its five cases cover
the actual Bridge source, both prohibited APIs in Editor/tests/Assets,
inactive branches with UTF-8 BOM, exact helper exemption, and compatible calls
plus generated cache exclusion. The existing matrix suite adds 25 passing cases.
The actual CI test command was also run in an isolated fixture containing both
legacy APIs: it failed with the expected file/line diagnostics, retained in
[lint-red.txt](issue-546/lint-red.txt). The unchanged checkout passes that command.

Canonical verification registers and runs this complete scoped matrix:

```bash
CARGO_TARGET_DIR=target python -m unittest discover -s tests/scripts -p test_bridge_object_identity.py -v
CARGO_TARGET_DIR=target PYTHONUTF8=1 python -m unittest discover -s tests/scripts -p test_e2e_matrix.py -v
CARGO_TARGET_DIR=target python docs/verification/issue-546/verify_compilation.py
cargo fmt --all -- --check
"C:/Program Files/nodejs/npx.cmd" --yes markdownlint-cli docs/verification/issue-546.md --config .markdownlint.json
```

The inline target declares this checkout's actual Cargo artifact directory for
Python wrapper commands, using gwt's supported admission contract. These commands
retain their Heavy classification and acquire a normal host slot. Markdown lint
uses the explicit Windows `npx.cmd` launcher for the installed validator; gwt
recognizes that bounded lint command as Light. Native subprocess invocation was
verified before registration. A bare `pnpm` shell shim was unavailable to gwt's
direct process launcher, so that failed plan was replaced and all five commands
were registered for a fresh run. No partial failed-run evidence is reused.

Rust/LSP implementations are unchanged. Their full checks and Linux Python
discovery run in required CI. An exploratory Windows full Python discovery
failed on existing locale and POSIX-pipe assumptions; it is not reported as PASS
and is not the scoped local regression command.

## Compilation evidence

All three installed Editors use isolated projects prepared by the existing
`scripts/e2e-matrix.py` preparer, then run with `-batchmode -nographics -quit`.
The source project is never opened. Exact commands, UTC timestamps, return codes,
compiler error counts and C# source hashes are retained in `compile.json`.
All 144 Bridge C# hashes match the current checkout. Measured on 2026-11-10 UTC:

| Editor | Started UTC | Finished UTC | Exit code | Compiler errors |
| --- | --- | --- | --- | --- |
| 6000.3.28f1 | 13:07:14 | 13:09:56 | 0 | 0 |
| 6000.6.8f1 | 13:09:57 | 13:13:43 | 0 | 0 |
| 7000.0.0a16 | 13:13:44 | 13:18:12 | 0 | 0 |

The verifier requires all three versions, a successful batchmode exit, zero
compiler errors, matching source/version and matching decompressed log hashes.
License/session identifiers are redacted from retained logs; original logs
remain in the local `.cache/issue-546/<version>/compile.log` files.

The earlier Issue #544 fingerprint validator rejects the 0.19.1 release's
Cargo/package version changes. Its evidence was preserved; these fresh compile
logs establish AC-4 for 0.19.1 independently of that older snapshot.

For a standard rerun, build the CLI and LSP as described in
[development.md](../development.md#editor-version-matrix), then select the
existing compile-only matrix (Windows defaults select all three required Editors):

```bash
python scripts/e2e-matrix.py --suites compile \
  --lsp-root .cache/matrix-tools --output .cache/issue-546-rerun
```
