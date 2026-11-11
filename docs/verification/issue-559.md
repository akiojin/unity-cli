# Issue #559: unitypackage import verification

Windows, Unity **6000.3.28f1**, 2026-11-11. The isolated real-Editor run passed
20 acceptance checks and all 11 `UnityPackageHandlerTests` EditMode cases.
The machine-readable inventory and responses are in [issue-559.json](issue-559.json).

```bash
cargo build --bin unity-cli
python -X utf8 scripts/e2e-package-import.py \
  --editor "C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" \
  --cli target/debug/unity-cli.exe --output .unity/issue-559-verify
```

The runner creates a separate project, copies the current Bridge, exports both
test packages through `AssetDatabase.ExportPackage`, and imports them through
the built CLI. It checks local paths, cached names and IDs, metadata, environment
overrides, missing/corrupt files, exact imported contents and compilation state.
The script package produced one measured domain reload, then returned both
asset paths with `isCompiling:false`, `isUpdating:false` and `errorCount:0`.
The imported script's static initializer was observed executing after reload.

A 500 ms deadline returned `PACKAGE_IMPORT_TIMEOUT`, a saved request ID and
compilation state. Read-only status polling recovered that same request as
`completed`, without resubmitting the import. Rust mock-transport regressions
also cover a lost initial response, temporary authentication rotation during
status reads, and rejection of an unauthorized initial mutation.

The real-import EditMode test also uses the explicit request ID `Active` and
verifies idempotent dispatch and final status. Request records use a separate
key namespace from the active-job pointer, so caller IDs cannot collide with
that internal metadata.

The [small text fixture](../../tests/fixtures/package-import/small.unitypackage)
is the real Unity-exported package from this run. The runner exports fresh
fixtures on each execution. Raw Editor logs, NUnit XML and CLI response files
are retained under the printed `.unity/issue-559-verify/run-*` directory by
the canonical verification run; they are generated artifacts.

Plan: verify the Rust CLI/transport with `cargo fmt`, `cargo clippy` and
`cargo test --all-targets -- --test-threads=1`; verify the Bridge and lifecycle
through the real-Editor runner; check the distributed skill with `skills lint`
and the changed documentation with Markdownlint. LSP implementation and browser
UI are outside this change's surfaces.

User Verification Result: n/a (autonomous)

Agent Visual Check: n/a (no UI surface)
