---
name: unity-package-management
description: Manage Unity UPM packages and scoped registries with unity-cli. Use when the user asks to list installed packages, search, install, update or remove a package, configure OpenUPM or NuGet scopes, or verify compilation after a package change. Do not use for console-only diagnostics or Player builds; use `unity-editor-tools`. For C# package-source navigation use `unity-csharp-navigate`; for new project creation use `unity-project-bootstrap`.
allowed-tools: Bash(unity-cli:*), Read, Grep, Glob
metadata:
  author: akiojin
  version: 0.1.1
  category: editor
  triggers:
    - package
    - upm
    - registry
  siblings:
    - unity-localization
    - unity-editor-tools
    - unity-csharp-navigate
    - unity-project-bootstrap
    - unity-cli-usage
---

# Unity Package Management

With `--output json`, results use `{success, command, data, errors, warnings}`. Check the exit status and envelope `success` first; tool-result fields in this skill are relative to `data`. Read failure codes from `errors[0].code`; see `unity-cli-usage` for exit-code recovery.

Use the existing Editor package tools to change dependencies, then verify the
resolved package state and compilation in the same project.

## Use When

- List installed UPM packages, search the registry or inspect versions/dependencies.
- Install, update or remove a package and verify the resulting compilation.
- Add or inspect supported scoped registries and their package scopes.

## Do Not Use When

- Configure Locale/String Table assets and verify localized UI text: `unity-localization`.

- Only read console, build a Player, change Editor settings or profile:
  `unity-editor-tools`. Only inspect C# package sources: `unity-csharp-navigate`.
- Create a project and install its bridge: `unity-project-bootstrap`. Diagnose
  a missing CLI or unreachable bridge: `unity-cli-usage`.
- Author Addressables groups/content: `unity-addressables`; edit imports or
  materials: `unity-asset-management`; author input actions: `unity-input-system`.

## Preferred Flow

1. Follow [runtime prerequisites](references/runtime-checklist.md). Select the
   exact project and endpoint, pass the same `--project-path` and `--port` on
   every call when multiple Editors exist, and check `system ping` returns that
   project. Require Edit Mode and idle compilation/import before mutations.
2. Read `Packages/manifest.json`, `Packages/packages-lock.json` when present,
   `package_manager list`, `registry_config list` and the console. Retain this
   baseline so pre-existing errors and unrelated dependencies remain visible.
3. Establish the requested package ID, version and registry. Use `search` for
   discovery; its results are registry-dependent, not every package on the
   internet. `info` reads an installed package. Keep explicit versions and
   choose an Editor-compatible version when none was specified; inspect package
   metadata rather than assuming the newest version supports Unity 2022.3.
4. For a supported registry, configure only the needed scopes. Set
   `autoAddPopular:false` on `add_openupm` / `add_nuget`; otherwise unrelated
   scopes may be added. Re-read `registry_config list` and the manifest.
   See [package operations](references/package-operations.md) for supported
   actions and limitations.
5. Apply one requested change at a time. Install with `packageId` and optional
   `version`; update with the same `install` action and the target version.
   Remove with the unversioned `packageName`. Read dependency information first:
   removing a direct dependency can leave it installed transitively. Do not
   remove a registry still used by other packages, or remove the active bridge.
6. After each change, inspect both transport and tool results: a JSON `error`
   inside a successful transport response is still a failure. Wait for reload
   and compilation as described below; never send duplicate mutations just
   because a response timed out. Reconcile actual state first.
7. Re-read package/registry lists, manifest and lock file; confirm the requested
   version/source or removal. Read console errors/exceptions after compilation
   settles. Report the actual result, manifest diff, resolved versions and
   console evidence. Existing errors prevent an unqualified clean-compilation
   claim; retain and distinguish them from errors introduced by this change.

### Reload and compilation completion

Keep the selected endpoint while the domain reloads. Retry only read-only
`system ping` and `get_editor_state` with a bounded deadline (for example,
180 seconds, adjustable for a known large import). Continue when the responding
project matches and `isCompiling` and `isUpdating` are both false. Confirm the
resolved package state and another idle state reading before checking console;
one early idle response alone does not prove an import finished. Do not require
observing a reload: packages without scripts may not trigger one.

If the connection does not recover, collect the Editor log and use
`unity-cli-usage` diagnostics. A modal package/registry notification can suspend
the Editor update loop even when its TCP listener is running. Inspect the exact
prompt in the selected Editor; close informational notices through its UI before
resuming. If that UI is inaccessible or the prompt requires a new trust decision,
report the blocker. Do not disable package validation to make polling pass.
If a mutation response was lost but its result is
already present, verify it instead of repeating it. If the state is ambiguous,
stop with the evidence; do not edit the manifest by hand or restart an unrelated
Editor. Compilation errors require diagnosis, not console clearing or a claimed
PASS. Unsupported operations are tracked separately from this workflow (#376).

## Examples

Run from the chosen project, adding its explicit endpoint flags when needed.
The package/version below illustrate a user-selected target, not a universal
recommendation. Inspect compatibility before applying it.

```bash
unity-cli system ping
unity-cli raw get_editor_state --json '{}'
unity-cli raw package_manager --json '{"action":"search","keyword":"UniTask","limit":10}'
unity-cli raw registry_config --json '{"action":"list"}'
unity-cli raw registry_config --json '{"action":"add_openupm","scopes":["com.cysharp"],"autoAddPopular":false}'
unity-cli raw package_manager --json '{"action":"install","packageId":"com.cysharp.unitask","version":"2.5.5"}'
# Wait for compilation, then verify the actual resolved version and console.
unity-cli raw package_manager --json '{"action":"info","packageName":"com.cysharp.unitask"}'
unity-cli raw package_manager --json '{"action":"list","includeBuiltIn":false}'
unity-cli raw get_editor_state --json '{}'
unity-cli raw read_console --json '{"count":100,"logTypes":["Error","Exception"],"includeStackTrace":true}'
# Only when removal was requested; repeat the completion checks afterward.
unity-cli raw package_manager --json '{"action":"remove","packageName":"com.cysharp.unitask"}'
```

- “Install this package, wait for reload, then show the manifest change and errors.”
- “指定した UPM パッケージを更新して、コンパイル結果も確認して。”
- “Only show console errors” → `unity-editor-tools`.

## References

- [runtime-checklist.md](references/runtime-checklist.md): binary and instance selection.
- [package-operations.md](references/package-operations.md): payloads, version changes and evidence.
