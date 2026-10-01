# Package Operations

## Package actions

| Action | Required fields beyond `action` | Meaning |
| --- | --- | --- |
| `search` | `keyword` | Search configured registry results; optional `limit` |
| `list` | none | Resolved installed packages; optional `includeBuiltIn` |
| `info` | `packageName` | Installed version, source and dependencies |
| `install` / `add` | `packageId` | Install or change version; optional `version` |
| `remove` / `uninstall` | `packageName` | Remove the manifest dependency |
| `recommend` | none | Built-in suggestions, not a live compatibility guarantee |

There is no `update` action. To update, call `install` with the same unversioned
`packageId` and the requested `version`, then check the returned and resolved
version. If `packageId` already contains `@version`, the handler does not append
the separate `version` field; avoid conflicting specifications. Git/file package
identifiers must retain their full requested identifier; do not append a registry
version to them.

`list` includes `isDirectDependency`, `source` and `version`. A package retained
only as another package's dependency can legitimately remain in the lock file
after removal from the manifest. Report that state rather than deleting lock
entries or removing dependents outside the request.

## Registry actions

| Action | Fields | Behavior |
| --- | --- | --- |
| `list` | none | Inspect names, URLs and scopes |
| `add_openupm` | `scopes`, `autoAddPopular:false` | Add OpenUPM or extend its scopes |
| `add_nuget` | `scopes`, `autoAddPopular:false` | Add the supported NuGet registry |
| `add_scope` | `registryName`, `scope` | Extend an existing registry |
| `remove` | `registryName` | Remove that registry, not just one scope |
| `recommend` | optional `registry` | Built-in suggestions |

The tool has no arbitrary URL registry creation or individual-scope removal
action. Do not invent payload fields, use Editor evaluation as a workaround, or
hand-edit the manifest to hide that limitation. Report the missing operation for
a separate Issue and link it when available. Do not substitute OpenUPM for a
requested private registry. Never put credentials in commands or evidence.

Adding a registry writes its configuration and requests an asset refresh; the
response alone does not prove dependency resolution finished. Verify the exact
URL/scopes and subsequent package resolution. Preserve other scopes in an
existing registry. `autoAddPopular:false` prevents adding defaults but does not
remove pre-existing scopes.

## Evidence and failures

- Record Editor version/project/port and the requested package ID/version.
- Preserve manifest and lock contents before the operation and after each
  registry/install/update/remove step. Summarize only the actual differences.
- Keep the tool response, final idle Editor state, resolved package result and
  console errors/exceptions. Read-only discovery does not authorize installation.
- Preserve baseline console evidence. Do not clear logs to manufacture a clean
  result. Paginate or narrow a truncated console result before asserting no errors.
- A timeout during UPM work is an unknown outcome, not a failed transaction.
  Reconnect to the same project and inspect manifest/list/info before a retry.
- On an unavailable package/version, authentication failure or compiler error,
  stop the dependent workflow and report the exact error and retained changes.
  Do not silently upgrade the Editor, choose another version or undo unrelated
  user edits. Revert a package only when that recovery is within the request.
