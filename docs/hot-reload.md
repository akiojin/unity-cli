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

Add it through Package Manager ("Add package from git URL") or as the
`com.handzlikchris.fastscriptreload` dependency in `Packages/manifest.json`,
then let the Editor compile. `hot_reload_status` reports `supported: true` once
the adapter is active.

Supported hosts are **Mono Editors, Unity 2022.3 or later, on x64 or Apple
Silicon (ARM64) macOS**. Real method replacement is verified on macOS with
6000.3.25f1 and 2022.3.62f3, both as native ARM64 Editors and as x64 Editors
under Rosetta 2. Any other host is rejected before compilation or native
patching with `HOT_RELOAD_PLATFORM_UNSUPPORTED`; on such a host use an x64
build of the Editor. Windows and Linux hardware verification is tracked in
Issue #386.

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
# Error contract without the backend:
scripts/e2e-hot-reload-batch-host.sh --port 6484 --expect missing
# Real method replacement on the Editor named by UNITY_PATH:
UNITY_PATH=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  scripts/e2e-hot-reload-batch-host.sh --port 6486 --expect supported \
  --fsr-path /path/to/FastScriptReload/Assets
# The same run on an x64 Editor under Rosetta 2:
UNITY_PATH=/path/to/x64-editors/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  scripts/e2e-hot-reload-batch-host.sh --port 6486 --expect supported \
  --require-arch X64 --fsr-path /path/to/FastScriptReload/Assets
```

Each batch run creates an isolated project for the selected Editor version and
prints the artifact directory containing `editor.log` and `results.log`
(`--artifacts` chooses it). Without `UNITY_PATH` the Hub Editor of the
repository project version is used. `--require-arch X64|Arm64` fails the run
unless the Editor process really has that architecture. `--expect unsupported`
remains for hosts outside the supported list. For an existing isolated fixture
listener, run `scripts/e2e-hot-reload.py` with `--project`, `--port`,
`--unity-cli` and the explicit expected backend mode.

The supported scenario checks formula changes, scene/object identity and
position/HP/score/static/nonserialized state, invalid source, stale revisions,
unobserved partial transactions and recovery. No mode treats a rejected request
as a successful replacement test.

Unity Hub keeps one architecture per Editor version. To keep an x64 build next
to the Apple Silicon one, download the Intel installer of the same version and
expand it outside the Hub directory as `<root>/<version>/Unity.app`
(`pkgutil --expand-full Unity-<version>.pkg <dir>` needs no administrator
rights; the Editor is in the `Unity.pkg.tmp/Payload/Unity` directory).

`scripts/e2e-matrix.py --fsr-path /path/to/FastScriptReload/Assets` adds the
`hot-reload-apply` suite, which runs the supported scenario in a separate
isolated Editor for every matrix version. With
`--x64-editor-root <root>` it also adds `hot-reload-apply-x64` for each version
that has an x64 build under that root. Without `--fsr-path` the matrix keeps
only the `HOT_RELOAD_PACKAGE_MISSING` contract check.
