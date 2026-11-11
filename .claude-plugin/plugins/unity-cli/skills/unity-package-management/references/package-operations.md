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
| `import` | `path` | Import a local `.unitypackage`, always noninteractive |
| `list_cached` | none | List downloaded Asset Store packages; optional `cachePath` |
| `import_cached` | `nameOrId` | Import exactly one cached package; optional `cachePath` |
| `import_status` | `requestId` | Read the saved import and compilation state |
| `open_asset_store` | none | Open My Assets; optional `nameOrId` requests selection; Download stays manual |
| `wait_download` | `nameOrId` | Wait for one complete cached package; optional `cachePath`, `downloadTimeoutMs` |
| `acquire` | `nameOrId` | Open → manual Download wait → existing import; separate `timeoutMs` for import |

The typed `package import`, `cache-list`, `cache-import` and `import-status`
commands use these same actions. Imports return exact `importedAssets` paths
after stable idle compilation and domain reload. Use the returned cache `id`
when names collide (`PACKAGE_AMBIGUOUS`). Asset Store gzip metadata provides
product ID, title, publisher and version when present; otherwise the filename,
publisher folder and a stable relative-path hash ID are used, with `version:null`.

Typed `asset-store-open`, `asset-store-wait`, and `asset-store-import` use the
three acquisition actions. Opening uses public display APIs only, requires a
headed Editor, and returns `ASSET_STORE_UI_UNAVAILABLE` otherwise. Selection is
`requested` or `unresolved`, never confirmed: the public API cannot read whether
the target is actually selected. Select it manually if needed, then press
Download. No internal API, reflection, synthetic input or account token is used.
The download deadline defaults to 600000 ms; expiry is
`ASSET_STORE_DOWNLOAD_TIMEOUT` with `status:waiting_download` and no submitted
import. Incomplete archives keep waiting, duplicate matches fail, and existing
complete packages can be reused. Independent wait returns `ready`/`path` without
importing. The one-command flow returns `importedAssets` and `assetStore` display
details through the existing import/reload pipeline.

`PACKAGE_NOT_FOUND`, `INVALID_UNITYPACKAGE`, `PACKAGE_IMPORT_BUSY` and
`PACKAGE_IMPORT_FAILED` are explicit failures. Invalid/truncated archives and
paths outside `Assets` are rejected before import. On `PACKAGE_IMPORT_TIMEOUT`,
read `import_status` with the returned `requestId`; the import can still be
running. Request IDs are idempotent within the same Editor session and bound to
one package path. This is an asset import, so it can overwrite existing assets;
UPM manifest and lock files are not used to select these packages.

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
