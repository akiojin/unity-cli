# Play Mode C# previews

`hot_reload` previews existing method-body changes without restarting Play Mode.
The source is supplied explicitly; the preview does not save or overwrite the
original `.cs` file. To keep a change, stop Play, edit the original source and
perform normal Unity compilation with Domain Reload.

## Optional backend

Install **FastScriptReload 1.8.0**, MIT licensed, separately:
`https://github.com/handzlikchris/FastScriptReload.git?path=Assets#51140b71d9e5df1de231b33ec20ee089b18bebec`.
The Bridge neither installs nor forks this dependency. Its adapter is compiled
only with the supported package version. Without it, ordinary Bridge tools
continue to work and preview requests return `HOT_RELOAD_PACKAGE_MISSING`.

The initial runtime is an **x64 Unity Editor / Mono**, Unity 2022.3 or later.
Apple Silicon ARM64 Editors are rejected before compilation or native patching.
Unity 6000.4.11f1 on ARM64 is used for package absence, package integration,
unsupported-platform and recovery E2E. Actual x64 method replacement requires
the supported-runtime E2E below; ARM64 error checks are not proof of replacement.
Windows実機検証: 保留（オーナー確認待ち）。

Disable Fast Script Reload's automatic and on-demand reload before beginning.
The Bridge rejects concurrent ownership instead of changing those preferences.
Do not run another patcher or edit the disk source during a preview session.

## Commands

```bash
unity-cli raw hot_reload_status --json '{}'
unity-cli raw hot_reload --json '{"action":"begin","path":"Assets/Player.cs"}'
# Supply the complete candidate source and the revision returned by begin/status.
unity-cli raw hot_reload --json '{"action":"apply","source":"...","expectedRevision":"...","timeoutSeconds":10}'
# Explicitly stop Play and discard previews through clean compilation / Domain Reload.
unity-cli raw hot_reload --json '{"action":"recover"}'
```

Begin requires a matching compiled baseline; stop and perform a normal
compilation if the baseline cannot be established. A session targets one source
file containing one non-generic, non-partial class (plain class or direct
MonoBehaviour subclass). Only existing synchronous block-bodied methods can
change. Fields and declarations remain identical. Properties, explicit
constructors, overloads, nested types, preprocessor directives, async/iterator,
local/anonymous functions and FSR callbacks are rejected in this initial scope.

The adapter compiles an isolated temporary copy with transaction-specific
execution markers. It checks the actual detours installed by FSR and waits for
the game to naturally execute every changed method. It never invokes arbitrary
user methods to test them. Rarely called methods can therefore time out; the
timeout is 1–60 seconds and includes compilation. Use a matching CLI
`--timeout-ms` (for example 120000) for long requests.

Only a fully verified transaction updates `appliedRevision`. Syntax and
unsupported changes fail before patching. A partial or unverified patch returns
failure with method lists and requires recovery; a mixed runtime has no single
verified applied revision. There is no automatic rollback or automatic Play
restart. Poll `hot_reload_status` after recovery until the new domain is idle.

## Real Editor verification

```bash
cargo build --bin unity-cli
scripts/e2e-hot-reload-batch-host.sh --port 6484 --expect missing
scripts/e2e-hot-reload-batch-host.sh --port 6485 --expect unsupported \
  --fsr-path /path/to/FastScriptReload/Assets
# On a supported x64 Editor:
scripts/e2e-hot-reload-batch-host.sh --port 6486 --expect supported \
  --fsr-path /path/to/FastScriptReload/Assets
```

Each batch run creates an isolated project and prints the artifact directory
containing `editor.log` and `results.log`. Set `UNITY_PATH` for the Editor binary.
For an existing isolated fixture listener, run `scripts/e2e-hot-reload.py` with
`--project`, `--port`, `--unity-cli` and the explicit expected backend mode.
The supported scenario checks formula changes, scene/object identity and
position/HP/score/static/nonserialized state, invalid source, stale revisions,
unobserved partial transactions and recovery. No mode silently treats the
unsupported platform as a successful replacement test.
