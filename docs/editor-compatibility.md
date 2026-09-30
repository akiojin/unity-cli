# Editor compatibility

The minimum Bridge version is Unity 2022.3 (`package.json`). The local acceptance
matrix covers the following installed release and prerelease Editors. New streams
must be added to `scripts/e2e-matrix.py` and verified before claiming support.

## macOS verification matrix

Verified on 2026-09-30 (macOS, Apple Silicon) against the final Bridge source.
The baseline run below includes package compilation, input, Timeline, VFX, C# eval,
hot-reload error contracts, PlayMode/domain-reload results and the all-tools
sweep (including LSP performance).

| Editor | Runtime | VFX Graph | Baseline suites | VFX E2E | All-tools |
| --- | --- | --- | --- | --- | --- |
| 2022.3.62f3 | Mono | 14.0.12 | PASS | 73/73 | 121/121, 133 calls |
| 6000.0.84f1 | Mono | 17.0.4 | PASS | 64/64 | 121/121, 133 calls |
| 6000.3.25f1 | Mono | 17.3.0 | PASS | 64/64 | 121/121, 133 calls |
| 6000.4.11f1 | Mono | 17.4.0 | PASS | 64/64 | 121/121, 133 calls |
| 6000.5.3f1 | Mono | 17.5.0 | PASS | 64/64 | 121/121, 133 calls |
| 6000.6.3f1 | Mono | 17.6.0 | PASS | 65/65 | 121/121, 133 calls |
| 6000.7.0a2 | Mono | 17.7.0 | PASS (retry) | 65/65 | 121/121, 133 calls |
| 6000.7.0b2 | Mono | 17.7.0 | PASS | 64/64 | 121/121, 133 calls |

Each run selects VFX/URP from the selected Editor's bundled Package Manager
catalog and verifies the resolved VFX version. Optional hot reload is absent from
the fixture: `HOT_RELOAD_PACKAGE_MISSING` is the expected tested result, not a
claim that method replacement was performed. Real method replacement is a
separate `hot-reload-apply` suite enabled by `--fsr-path` (see
[hot-reload.md](hot-reload.md)). The all-tools sweep reports its
documented exclusions separately; excluded tools do not count as passed.
See the [acceptance evidence and supplemental test limitations](verification/issue-340.md).

6000.7.0a2 is an alpha and failed twice for engine-side reasons before passing a
clean retry: once Mono aborted while importing built-in modules
(`implement type compare for 0!`, before the Bridge compiled), and once the VFX
suite's clean-console check caught Unity's internal
`deleting an allocation that is older than its permitted lifetime` error after
an SDF bake while two matrix groups shared the machine. Neither involves Bridge
code; both are recorded rather than counted as passes.

Windows hardware verification remains deferred under Issue #340 AC-6.

### Extended acceptance suites

Issue [#392](https://github.com/akiojin/unity-cli/issues/392) adds the following
suites to the default runner. A dash means not measured in this extension;
the baseline results above do not imply these additional suites passed.
`UNSUPPORTED` is a verified error contract and is not counted as a successful build.

| Editor | Bake | Player build (macOS / Windows) | Video formats | Animation curves | Input Actions persistence | Reference |
| --- | --- | --- | --- | --- | --- | --- |
| 2022.3.62f3 | PASS (4 types) | PASS / UNSUPPORTED | PASS | PASS | PASS | PASS |
| 6000.0.84f1 | — | — | — | — | — | — |
| 6000.3.25f1 | PASS (4 types) | PASS / UNSUPPORTED | PASS | PASS | PASS | PASS |
| 6000.4.11f1 | — | — | — | — | — | — |
| 6000.5.3f1 | — | — | — | — | — | — |
| 6000.6.3f1 | — | — | — | — | — | — |
| 6000.7.0a2 | — | — | — | — | — | — |
| 6000.7.0b2 | — | — | — | — | — | — |

See the [commands, assertion counts and retry evidence](verification/issue-392.md).
The Windows result asserts `BUILD_MODULE_MISSING`; no successful Windows build
is claimed for these installations.

Tool examples and skill discovery are tracked by
[#393](https://github.com/akiojin/unity-cli/issues/393), as permitted by #392 AC-6.
Windows hardware execution remains outside this matrix under
[#386](https://github.com/akiojin/unity-cli/issues/386).

## VFX Graph contract per package version

Acceptance criterion (AC-3): on every Editor's bundled VFX Graph version, each
`vfx_*` call either works or returns a stable error code. Reflection that cannot
resolve a member returns `VFX_API_UNSUPPORTED` with the missing member in the
message; unknown template, context, block or operator names return
`INVALID_ARGUMENT` listing the names that package offers (for example VFX 17.5
renamed `01_Minimal_System` to `Minimal_System`, and VFX 14 uses flat context
names such as `Output Particle Point`).

Version-specific behavior:

- VFX Graph 14 (Unity 2022.3) supports SDF baking in Play Mode. An Edit Mode
  request returns `VFX_SDF_EDIT_MODE_UNSUPPORTED` before creating an asset
  because that package's cleanup uses deferred `Destroy`. The matrix checks the
  rejection and performs the real bake in Play Mode.
- VFX 14 has no blackboard custom-attribute API: `add_custom_attribute` returns
  `VFX_API_UNSUPPORTED`. `convert_to_property` resolves VFX 14 parameter types.
- VFX 14 has no Custom HLSL: requesting the `Custom HLSL` block or operator
  returns `VFX_API_UNSUPPORTED`. It also has no `VFXErrorReporter`, so
  `includeErrors` returns one entry with `code: VFX_API_UNSUPPORTED` instead of
  validation errors.
- Sticky notes work on every version. Indexed `colorTheme` exists from VFX 17.4;
  on older packages an explicit `colorTheme` returns `VFX_API_UNSUPPORTED`
  without changing the graph, while notes without it are created normally.
  `group_nodes` rejects a note `colorTheme` before it creates the group.
- `designate_template` needs the GraphView template descriptor, absent in VFX 14
  and 17.0; there it returns `VFX_API_UNSUPPORTED`.
- GPU Event contexts and Trigger Event blocks are experimental before VFX 17.3:
  enable `displayExperimentalOperator` (`vfx_settings` preferences) to list them.
  VFX 17.0 reports a composed strip output without a Shader Graph as an Error.
- VFX 17.6+ removed the `allowShaderExternalization` preference: `get` omits it
  and `set` returns `VFX_API_UNSUPPORTED`.
- VFX 17.6+ reimports rebuild a Shader Graph output's nested shading object.
  `set_context_setting` refreshes the output's setting cache before writing and
  verifies the value, so a `shaderGraph` assignment is never silently dropped.

The Bridge's `VfxGraphHandlerTests` (EditMode NUnit) select template names,
library names and expectations from the installed VFX Graph package, and verify
`VFX_API_UNSUPPORTED` where a package lacks a feature. They pass on 2022.3.62f3
(VFX 14.0.12), 6000.0.84f1 (17.0.4), 6000.4.11f1 (17.4.0) and 6000.7.0b2
(17.7.0). See the [Issue #344 evidence](verification/issue-344.md).

## Unity 6000.7 and CoreCLR

Unity 6000.7 runs the Editor, including Play Mode, on Mono. CoreCLR is an
experimental **Player** scripting backend; choosing it for a build does not
switch Editor Bridge, eval or hot reload to CoreCLR. See Unity's official
[CoreCLR documentation, Code reload](https://docs.unity.com/en-us/engine/6000.7/manual/scripting/compilation-and-code-reload/script-compilation/backends/coreclr).

6000.7 の Editor は Mono で検証済みです。CoreCLR は Player バックエンドであり、
Editor 機能の実行ランタイムを変更しません。CoreCLR Editor 検証は対象外です。
CoreCLR Player ビルド・実行も本 Issue の対象外であり、検証済みとは扱いません。

6000.7.0a2's bundled Test Framework clears, rather than invalidates, its loaded
test-assembly cache when entering Play Mode without Domain Reload. The Bridge
invalidates that cache before execution on this exact alpha to prevent a false
zero-test success. Both Domain Reload settings run two real tests in the matrix.
If the compatibility hook is unavailable, execution fails explicitly with
`TEST_RUNNER_DOMAIN_RELOAD_REQUIRED`.

## Reproduce

Build the checkout CLI and C# LSP, then run `scripts/e2e-matrix.sh`. The runner
creates new isolated projects and a dedicated daemon/tools directory, retains
Editor/suite logs and writes `matrix.json` plus per-Editor `result.json`.
See [development instructions](development.md#editor-version-matrix).
