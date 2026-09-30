# C# evaluation in the Editor

`editor eval` executes explicitly supplied, trusted C# on the Unity Editor's
main thread, without creating a source file or menu item for each call.

```bash
unity-cli editor eval '1+2' --request-id sum --output json
unity-cli editor eval 'GameObject.Find("Player").name' --output json
unity-cli editor eval 'var go = new GameObject("Probe"); return go;' --mode statements --request-id create-probe --output json
unity-cli editor eval-status create-probe --output json
unity-cli editor eval-stats --output json
```

The equivalent raw tools are `eval_csharp` with `code`, optional `mode` and
optional `requestId`, `get_eval_status` with required `requestId`, and
`get_eval_stats` with optional boolean `collect`.
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

| State                 | Meaning                                                                           |
| --------------------- | --------------------------------------------------------------------------------- |
| `completed`           | Synchronous execution and result conversion finished                              |
| `compile_error`       | Compiler rejected the source; inspect diagnostics                                 |
| `runtime_error`       | Execution threw; inspect exception type/message/stackTrace                        |
| `serialization_error` | Code ran, but its return value could not be represented                           |
| `unsupported`         | An unsupported asynchronous construct or result was detected                      |
| `invalid_request`     | Invalid code, mode, or request ID                                                 |
| `request_conflict`    | The same ID was used with different code or mode                                  |
| `busy`                | Editor cannot start this evaluation now                                           |
| `reload_required`     | New source cannot be compiled: the domain's evaluation assembly limit was reached |
| `unknown`             | No stored result exists in the current domain                                     |

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
source that was not compiled in this domain returns `reload_required`, while
source that was already compiled keeps running. The tool never triggers reload itself.
The result cache keeps the newest 256 request IDs. When it is full, the oldest
stored ID is dropped and later reads as `unknown`, so query a timed-out ID
before issuing hundreds of other evaluations.
Code is limited to 65,536 characters, IDs to 128, return JSON to 65,536 UTF-8
bytes, strings to 16,384 characters, and return graphs to 1,024 nodes/depth 8.

## Performance and caching

Eval is fast when it is warm. Three things are reused inside one Editor domain:

- **Compilation references.** The set of loaded, file-backed assemblies is
  collected once and reused. Loading another file-backed assembly into the
  domain refreshes the set, so its types resolve in the next call.
- **Compiled snippets.** Identical `code` + `mode` reuses the assembly that was
  compiled the first time and runs it again. The code is executed on every
  call; only compilation is skipped. This cache is separate from `requestId`
  replay, which returns a stored result without executing anything.
- **Roslyn warm-up.** After each domain load the Bridge compiles a throwaway
  snippet on a background thread, so the first real call does not pay the
  compiler's start-up cost.

A Domain Reload (script compilation, entering Play Mode with reload enabled)
clears all three, and the warm-up runs again.

| Call                                         | What it costs                            | Measured wall-clock per CLI call |
| -------------------------------------------- | ---------------------------------------- | -------------------------------- |
| First call in a domain, warm-up not finished | Compiler start-up + references + compile | about 0.5–2.4 s                  |
| First call after warm-up                     | One compile                              | about 30–130 ms                  |
| New source, warm                             | One compile                              | p50 about 20–25 ms               |
| Source already compiled in this domain       | No compile                               | p50 14–25 ms, p95 17–81 ms       |

Measured on macOS (Apple M5 Max) with Unity `6000.3.25f1` and `2022.3.62f3`,
the Editor as the frontmost app, one CLI process per call, on a machine that
was running other Editors. With the Editor in the background, macOS throttles
the Editor loop and every command, including eval, takes about 100–200 ms.
Raw results: [`docs/benchmarks/eval-391/`](benchmarks/eval-391).

For agents: to repeat an operation, resend the same `code` and vary nothing
inside it; generating a new string per call (for example, embedding a counter
in the source) compiles a new assembly every time and eventually hits the
128-assembly limit.

`editor eval-stats` (`get_eval_stats`) reports the counters of the current
domain: `evaluations`, `emittedAssemblies` / `maxEmittedAssemblies`,
`cachedCompilations`, `compileCacheHits`, `referenceCount`, `referenceBuilds`,
`warmedUp`, `storedResults` / `maxStoredResults`, `loadedAssemblies` (all
assemblies in the domain), `managedMemoryBytes`, and
`managedMemoryGrowthBytes` (since the first evaluation in the domain).
`--collect` runs a managed garbage collection first, for comparable memory
numbers. It is read-only and does not execute user code.

Benchmark and budget gate (Editor open, Bridge listening):

```bash
cargo build --release
python3 scripts/bench-eval.py --port 6400 --require-frontmost --activate --budget editor_eval
```

The script measures 100 warm `editor eval '1+2'` calls after 3 warm-up calls
and exits 1 when p50/p95 exceed the `editor_eval` entry of
[`perf-budgets.json`](../perf-budgets.json) (p50 ≤ 50 ms, p95 ≤ 100 ms, Editor
frontmost). `--activate` brings the Editor back to the front and re-measures
samples during which another app took focus; it needs macOS Automation
permission for System Events.

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
and Play Mode. It also checks that identical source reuses its compilation,
that a newly loaded assembly and a Domain Reload both leave type resolution
correct, and that 1,000 consecutive evaluations (`--soak-iterations`) of 4
snippets emit at most 8 eval assemblies, add at most 16 assemblies to the
domain, and grow managed memory by at most 64 MiB (measured: 4 assemblies and
3.7–10.8 MB). It creates scenes only under
`Assets/Scenes/Generated/E2E/` and removes its temporary GameObject.
