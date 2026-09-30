# Prefab workflow verification (#395)

## Environment and command

- Executed 2026-09-30, macOS 26.5 (25F71), arm64.
- Real Unity Editors, isolated project copies, batchmode/nographics, port 6495.
- CLI: this branch's `target/debug/unity-cli`.
- User Verification Result: n/a (autonomous).
- Agent Visual Check: n/a (no UI surface).

```bash
python3 scripts/e2e-prefab.py --launch --editmode \
  --versions 6000.3.25f1,2022.3.62f3 \
  --output /tmp/issue-395-prefab-first
```

The command exited 0. Each version ran the complete suite through the real CLI
and TCP bridge. `eval_csharp` sets up fixtures and independently checks the
resulting Unity objects; the actual operations use the public Prefab tools.

| Unity Editor | E2E assertions | Focused EditMode tests | Failed | Result |
|--------------|----------------|------------------------|--------|--------|
| 6000.3.25f1  | 45             | 22                     | 0      | PASS   |
| 2022.3.62f3  | 45             | 22                     | 0      | PASS   |

## Acceptance evidence

- AC-1: existing `instantiate_prefab` → `create_prefab` creates a Variant.
  A base edit propagates to an inherited property while the Variant override
  remains unchanged; asset reload preserves the result. No redundant Variant
  creation tool was added.
- AC-2: `get_prefab_overrides` returns structured property, added-component,
  and removed-component records, plus the Variant/base apply targets.
- AC-3: `manage_prefab_overrides` applies/reverts individual properties,
  objects and added/removed components, and all overrides. Both Variant and
  base destinations are exercised. Reverting one property preserves an
  unrelated override. Invalid ancestor targets are rejected before mutation.
- AC-4: `unpack_prefab` disconnects an ordinary outer root in both modes.
  Outermost retains nested Prefab connections; Completely removes all of them.
  The NUnit tests also verify Unity's native Variant behavior: Outermost retains
  the base connection, while Completely removes it. Treating Outermost as
  complete recursive disconnection would contradict Unity's API semantics.
- AC-5: both required real Editors passed, as counted above.
- AC-6: `docs/tools.md` and the `unity-prefab-workflow` skill document discovery,
  JSON contracts, explicit apply destinations, and the existing Variant route.
- AC-7: the change and this evidence are recorded as a comment on closed SPEC
  #148; its scope remains Asset/Prefab/Material.

## Test inventory and artifacts

The 45 E2E assertions per Editor cover Variant creation/inheritance/retained
overrides, structured listing, property/object/component operations, all apply
and revert to both destinations, invalid destinations, scene/asset reload,
nested unpack connection checks, and inactive root inspection.

The 22 cases in `PrefabWorkflowHandlerTests` additionally cover Play Mode write
policy, ambiguous roots, duplicate component selection, stale properties,
default root-transform apply rejection, revert destination rejection, whole
ancestor apply with GameObject additions/removals, and Variant unpack semantics.
Each run checks the matching test run ID and requires all tests to pass, with
zero skipped or failed cases in the captured results.

Evidence root: `/tmp/issue-395-prefab-first` (machine-local retained artifacts).

- `results.json`: versions, exact Editor launch arguments, counts and run IDs.
- `<version>/calls.jsonl`: every CLI command, JSON response, exit code and stderr
  (112 calls on Unity 6; 110 on 2022.3, including readiness/test-status polling).
- `<version>/assertions.log`: all 45 named E2E assertions.
- `<version>/editmode.json`: all 22 test names, durations and results.
- `<version>/editor.log`: compilation, import, bridge and test execution log.
- `<version>/project`: retained isolated fixture for inspection.

The tested C# source files match this checkout in both isolated projects.
Unity 2022.3 rewrites some existing importer `.meta` files in its disposable
copy. The runner stops only its own Editors and private unityd; the dedicated
port had no listener after exit.

## Other verification

- `cargo test --all-targets -- --test-threads=1`: 571 passed, 0 failed.
- `cargo +stable clippy --all-targets -- -D warnings`: passed.
- `cargo fmt --all -- --check`: passed.
- `target/debug/unity-cli skills lint --severity error`: 16 skills, 0 violations.
- Markdown lint for `docs/tools.md` and the Prefab skill: passed.
- Python syntax compilation and `git diff --check`: passed.

Rust output is retained at `/tmp/issue-395-rust-tests.log` and
`/tmp/issue-395-clippy.log`. Windows/Linux Editor execution is outside this
macOS acceptance scope and remains tracked by #386.
