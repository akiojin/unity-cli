# C# evaluation in the Editor

`editor eval` executes explicitly supplied, trusted C# on the Unity Editor's
main thread, without creating a source file or menu item for each call.

```bash
unity-cli editor eval '1+2' --request-id sum --output json
unity-cli editor eval 'GameObject.Find("Player").name' --output json
unity-cli editor eval 'var go = new GameObject("Probe"); return go;' --mode statements --request-id create-probe --output json
unity-cli editor eval-status create-probe --output json
```

The equivalent raw tools are `eval_csharp` with `code`, optional `mode` and
optional `requestId`, and `get_eval_status` with required `requestId`.
The CLI treats evaluation as mutating, including expressions: `--dry-run`
skips execution. Eval calls, including calls inside a batch, use direct TCP
without automatically resending through the daemon fallback path.

## Language and execution context

- `expression` (default) accepts one expression and returns its value.
- `statements` accepts a method body. `return value;` returns a value;
  reaching the end returns JSON `null`.
- C# 9 syntax is compiled using the pinned Editor-only Roslyn bundle. The
  implicit namespaces are `System`, `UnityEngine`, and `UnityEditor`.
  Use fully qualified names for other namespaces.
- References come from loaded, file-backed assemblies in the Editor domain,
  including Unity and compiled project assemblies. There is no NuGet restore,
  `#r`, persistent REPL state, or replacement of existing compiled types.
- Edit Mode and Play Mode are supported. Evaluation is rejected as `busy`
  during compilation, asset refresh, mode transition, or another evaluation.
  The tool does not enter Play Mode or save assets/scenes automatically.
- Async/await and Task results are unsupported. Do not launch background work
  from a snippet: work that outlives the call cannot be tracked by this tool.
- This executes with the Editor's privileges and is not a security sandbox.
  Explicit scene and asset changes take effect immediately; undo and rollback
  are the snippet's responsibility.

## Results

Each result has `requestId`, `state`, `value`, `logs`, `diagnostics`, and
`exception`. A successful expression `1+2` returns numeric `value: 3` and
`state: "completed"`.

| State | Meaning |
| --- | --- |
| `completed` | Synchronous execution and result conversion finished |
| `compile_error` | Compiler rejected the source; inspect diagnostics |
| `runtime_error` | Execution threw; inspect exception type/message/stackTrace |
| `serialization_error` | Code ran, but its return value could not be represented |
| `unsupported` | An unsupported asynchronous construct or result was detected |
| `invalid_request` | Invalid code, mode, or request ID |
| `request_conflict` | The same ID was used with different code or mode |
| `busy` | Editor cannot start this evaluation now |
| `reload_required` | The domain's evaluation assembly limit was reached |
| `unknown` | No stored result exists in the current domain |

The command transport can succeed while `state` is a failure. Always inspect
`state`; a successful CLI exit alone does not prove the snippet succeeded.
Diagnostics include severity, compiler ID, message, and source line/column.
Runtime exceptions are separate from compiler diagnostics.

Null, strings, booleans and finite numbers preserve their JSON types.
Enums, including flag combinations and unnamed values, use their underlying
integer value. Arrays,
enumerables, string-keyed dictionaries, and public instance fields/properties
are converted with bounded depth and size. Cycles and unsupported values fail
explicitly. Property getters and enumeration execute user code too.
For structs with recursive computed properties, such as `Vector3.normalized`,
return selected data: `var p = go.transform.position; return new { p.x, p.y, p.z };`
in statements mode. Returning the entire struct can exceed the depth limit.
Unity objects return a descriptor with `kind`, `type`, `name`, and `instanceId`;
instance IDs are references within the current Editor session, not durable asset
identifiers. Destroyed Unity objects return null.

Logs are captured during the synchronous evaluation interval and are bounded.
They are not a per-thread isolation guarantee. Return and diagnostic limits
protect the transport; they do not limit what the evaluated code can allocate.

## Timeouts and request IDs

Supply a unique `--request-id` before starting a mutation. Reusing a stored ID
with identical code/mode returns the stored result without rerunning the code.
Different input under the same ID returns `request_conflict`.
If omitted, the CLI generates an ID and includes it in transport error output
so that a timeout can still be followed by a status query.

`--timeout-ms` ends the CLI's wait. It does **not** interrupt compilation or
execution, undo effects, or terminate a loop. An infinite synchronous loop can
block the Editor, including status requests. Do not automatically rerun after
a timeout, especially with a new ID. Query `editor eval-status ID` instead.

Results and IDs are local to the current Editor domain. A Domain Reload loses
them; `unknown` never means that a prior operation had no effects. Inspect the
actual scene/asset state before deciding whether to issue new work. Loaded eval
assemblies cannot be individually unloaded; after 128 emitted assemblies,
new execution returns `reload_required`. The tool never triggers reload itself.
The result cache also stops accepting new IDs at 256 entries. Stored results
remain available at either limit; IDs are never evicted to make room for new work.
Code is limited to 65,536 characters, IDs to 128, return JSON to 65,536 UTF-8
bytes, strings to 16,384 characters, and return graphs to 1,024 nodes/depth 8.

## Local verification

Use the Editor version in `UnityCliBridge/ProjectSettings/ProjectVersion.txt`.
For an existing listener:

```bash
cargo build
scripts/e2e-eval.sh --unity-cli "$PWD/target/debug/unity-cli" --port 6400
```

With no GUI listener, launch the documented batch host and the eval suite:

```bash
UNITY_CLI_NO_AUTO_UPDATE=1 scripts/e2e-input-batch-host.sh --suite eval --port 6402 --unity-cli "$PWD/target/debug/unity-cli"
```

The suite checks expressions, scene object creation/read/change, descriptors,
compile/runtime errors, replay/status, logs, arrays, no persistent source files,
and Play Mode. It creates scenes only under
`Assets/Scenes/Generated/E2E/` and removes its temporary GameObject.
