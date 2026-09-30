## [0.16.0] - 2026-09-30

### 🚀 Features

- *(diagnostics)* Add unity-cli doctor for unreachable-bridge diagnosis
- *(tool)* Add query/category/compact/limit/offset filters to tool list
- *(capture)* Fall back to an OS screenshot when the Editor does not respond
- *(setup)* Add unity-cli setup and bridge install/upgrade/status (#363)
- *(connection)* Discover Editors via lockfiles and target them by project path

### 🐛 Bug Fixes

- *(bridge)* Declare com.unity.ugui so projects without uGUI compile
- *(unityd)* Reconnect pooled Unity connections closed by an Editor restart
- *(setup)* Avoid the Input System restart dialog when installing the bridge (#363)
- *(hot-reload)* Replace methods for real on macOS ARM64 and x64 Editors

### 📚 Documentation

- *(hot-reload)* Record real Editor evidence for issue 390
- *(eval)* Record the isolated-HOME test run for #391

### ⚡ Performance

- *(eval)* Cache compilation references and compiled snippets

### ⚙️ Miscellaneous Tasks

- *(work)* Record issue 396 release scope

## [0.15.3] - 2026-09-30

### ⚡ Performance

- *(cli)* Default the Unity host to the 127.0.0.1 loopback literal

### ⚙️ Miscellaneous Tasks

- *(work)* Record issue 358 release start

## [0.15.2] - 2026-09-29

### 🧪 Testing

- *(bridge)* Run async tests on Unity 2022.3 test framework
- *(bridge)* Make collector tests pass on Unity 2022.3 test framework
- *(vfx)* Select VFX NUnit fixtures per installed VFX Graph package

### ⚙️ Miscellaneous Tasks

- *(work)* Record issue 354 release start

## [0.15.1] - 2026-09-29

### 🐛 Bug Fixes

- *(bridge)* Resolve animationRoot without APIs removed in Unity 6000.7

### ⚙️ Miscellaneous Tasks

- *(work)* Record issue 349 release start

## [0.15.0] - 2026-09-29

### 🚀 Features

- *(compat)* Verify all installed Unity Editors and close VFX version gaps
- *(animation)* Add numeric curve editing and Unity E2E

### 🐛 Bug Fixes

- *(e2e)* Cover tool catalog and stabilize daemon responses
- *(animation)* Integrate curve tools with current develop catalog
- *(animation)* Preserve dedicated e2e coverage after develop sync

### ⚙️ Miscellaneous Tasks

- *(work)* Record issue 339 verification handoff
- *(work)* Record issue 346 release start

## [0.14.1] - 2026-09-29

### 🚀 Features

- Add verified Play Mode hot reload previews
- *(reference)* Resolve published Unity source refs and track provenance
- Add verified scene baking jobs
- *(build)* Add player builds and structured reports

### 🐛 Bug Fixes

- Clear preview revision while verifying native patches
- *(build)* Reconcile tool catalog count with Timeline tools
- *(build)* Integrate player builds with current develop
- *(build)* Preserve player tools alongside vfx and hot reload
- *(input)* Integrate elapsed-time holds with editor notifications
- *(test)* Recover PlayMode results across domain reload
- *(input)* Preserve action types on latest develop
- *(bridge)* Use compatible object IDs in VFX handlers

### 🧪 Testing

- Observe pending hot reload inside the Editor
- *(build)* Compare complete CLI build reports
- *(build)* Retry E2E refresh while restarted Editor compiles
- *(instances)* Isolate runner instances test registry

### ⚙️ Miscellaneous Tasks

- *(work)* Record hot reload PR handoff
- *(hot-reload)* Integrate previews with current develop
- *(work)* Record hot reload integration verification
- *(hot-reload)* Preserve VFX tools during develop integration
- *(work)* Record issue 254 pull request handoff
- *(reference)* Integrate published source resolution with develop
- *(work)* Record scene baking PR
- Integrate verified scene baking with develop
- *(work)* Record player build replacement PR handoff
- *(work)* Record player build replacement verification
- *(work)* Finalize player build replacement handoff
- Preserve baking alongside player builds and hot reload
- Preserve reference checks alongside player and bake tools
- *(work)* Record issue 318 integration
- *(work)* Finalize issue 318 integration record

## [0.14.0] - 2026-09-29

### 🚀 Features

- *(editor)* Evaluate C# snippets in memory with structured results
- Automatically start unityd for remote operations
- *(vfx)* Add first-class Visual Effect Graph authoring commands
- *(vfx)* Add set_block_setting op and block settings in describe
- *(vfx)* Add add_context op and flow links in describe
- *(vfx)* Add add_operator and link_slots ops with operator/slot oracle
- *(vfx)* Add add_parameter op and parameters in describe
- *(vfx)* Add vfx_runtime tool for VisualEffect public-API control
- *(vfx)* Add link_flow op and context settings for Events breadth (#6)
- *(vfx)* Add set_bounds op + slot-value oracle
- *(vfx)* Add add_sticky_note op + describe oracle
- *(vfx)* Add set_instancing op + describe oracle
- *(vfx)* Prove Custom HLSL via existing primitives + relax oracle
- *(vfx)* Add Block subgraph multi-asset wiring
- *(vfx)* Prove Systems breadth via add_context chain + dataInstanceId oracle
- *(vfx)* Add Templates breadth — list + create_from_template
- *(vfx)* Add vfx_settings tool for VFX project settings
- *(vfx)* Extend vfx_settings with scope:preferences for VFX EditorPrefs
- *(vfx)* Prove Attributes (#7) via existing primitives — Pass-1 complete
- *(vfx)* Add set_slot_value op — write constants into input slots (#5)
- *(vfx)* Add unlink_slots op — break slot connections (#5)
- *(vfx)* Add set_operator_setting op + operator settings oracle (#4)
- *(vfx)* Add remove_* family — delete blocks/operators/parameters/contexts (#1/#2/#4/#9)
- *(vfx)* Add set_context_setting op — Spawn/Update/Output + Init data settings (#2)
- *(vfx)* Complete Blackboard parameter type matrix + constant + min/max (#9)
- *(vfx)* Add update_sticky_note + remove_sticky_note ops (#10)
- *(vfx)* Add set_block_enabled + reorder_block + move_block ops (#1)
- *(vfx)* Expose allowShaderExternalization preference (#13)
- *(vfx)* Add delete_system op + simulation space setting (#3)
- *(vfx)* Add add_custom_attribute op + attribute depth proofs (#7)
- *(vfx)* Close Custom HLSL — external ShaderInclude + function selector (#15)
- *(vfx)* Events depth — GPU events, payloads, Output Event, Initial Event Name (#6)
- *(vfx)* Operator cascaded inputs + operand type (#4)
- *(vfx)* Blackboard management — rename/category/reorder/duplicate params (#9)
- *(vfx)* Unlink_flow op + per-mode value slot proof (#2)
- *(vfx)* Per-instance initialEventName runtime override (vfx_runtime)
- *(vfx)* Runtime set_texture (Object-typed exposed property) + get_state oracle
- *(vfx)* Runtime set_mesh (Object-typed exposed Mesh property) + get_state oracle
- *(vfx)* Send_event attributes payload propagation, PlayMode-verified (#6 runtime tail)
- *(vfx)* Instancing multi-instance render + 3-gate reconciliation (#16 runtime tail)
- *(vfx)* Set_slot_value supports Object-typed slots by asset path (#5 full type coverage)
- *(vfx)* Link_slots/unlink_slots descend into descriptor-named child sub-slots (#5)
- *(vfx)* Inline<->property node conversion (convert_to_property / convert_to_inline) (#5)
- *(vfx)* Curve/gradient slot values + gradient describe oracle (#5 niche)
- *(vfx)* Set_slot_space — spaceable slot coordinate space (World/Local/None) finishes #5
- *(vfx)* Duplicate_block / duplicate_operator — clone blocks & operators (#1/#4)
- *(vfx)* Set_system_name op + systemName describe oracle (#3)
- *(vfx)* System (.vfx) subgraph create + reference (#8)
- *(vfx)* Operator subgraph define-outputs via add_parameter isOutput (#8)
- *(vfx)* Assign shaderGraph asset to composed Shader Graph output (#2)
- *(vfx)* Reorder_sticky_note (#10)
- *(vfx)* Insert_template — merge a template into an existing graph (#11)
- *(vfx)* Vfx_settings surfaces + sets VFXManager Object-ref fields (#12)
- *(vfx)* Link-driven block activation via activation:true endpoint (#1)
- *(vfx)* Rename + reorder cascaded operator inputs (#4)
- *(vfx)* Reorder_category for blackboard category order (#9)
- *(vfx)* Designate_template marks a .vfx as a custom template (#11)
- *(vfx)* Vfx_bake_sdf — bake a Mesh into an SDF Texture3D asset (#17)
- *(vfx)* Address block ops by contextIndex to disambiguate same-typed contexts
- *(vfx)* Vfx_runtime simulate op + runtime (live-component) eval tier
- *(vfx)* Canvas node positioning — position on add ops, move_node, describe oracle
- *(vfx)* Group_nodes op + describe groups oracle; move_node seeds parameter nodes
- *(vfx)* Compile feedback, scoped auto_layout, HLSL guard, skill restructure
- *(vfx)* Calibrate auto_layout spacing and parameter splitting; notes and groups survive layout
- *(vfx)* New nodes always take free canvas space
- *(vfx)* Node-level auto_layout default, framed placement, group notes
- *(vfx)* Defer-compile batching and describe-graph filtering
- *(vfx)* Integrate six VFX Graph tools with safe validation

### 🐛 Bug Fixes

- *(input)* Match requested processed gamepad stick axes
- *(input)* Persist input action edits to source json
- *(capture)* Honor video formats and validate recorded output
- *(tests)* Exclude suites from test result counts
- *(input)* Advance touch gestures across game frames
- *(ci)* Satisfy clippy question_mark lint
- *(reference)* Honor explicit branch for unknown Unity versions
- *(input)* Deliver action notifications from editor updates
- *(ci)* Satisfy current clippy question-mark lint
- *(bridge)* Preserve object IDs across Unity versions
- *(skills)* Gate unix-only symlink test for cross-platform builds
- Clippy question_mark in skills discover_root; refresh vfx_apply op list
- *(vfx)* Surface compound sub-slot links in describe oracle
- *(vfx)* Validate arguments before loading the graph and fail quietly

### 🚜 Refactor

- *(vfx)* Return { error } from handlers to match bridge convention

### 📚 Documentation

- *(agents)* Align agent instructions with current workflows
- *(input)* Record persistence verification evidence
- *(vfx)* Correct instancingMode enum in skill (Auto/Custom/Disabled, not ForceOn)
- *(skill)* Teach unity-vfx-graph the two new throughput flags

### 🧪 Testing

- *(input)* Verify processed stick axes through Unity batch host
- *(lsp)* Wait for complete mock daemon requests
- *(vfx)* Add VFX Graph to test project and behavioral handler tests
- *(vfx)* Tighten Custom HLSL breadth proof + add Tier-2 error oracle
- *(vfx)* Prove Operator subgraph reference end-to-end (#8)
- *(vfx)* Add Tier-2 error-oracle negative control
- *(vfx)* PlayMode runtime verification harness (live VisualEffect rig)
- *(vfx)* Output Event CPU callback PlayMode-verified (#6 runtime tail)
- *(vfx)* Particle-strip system compose-proof (#3)
- *(vfx)* Mesh-output + static-mesh system compose-proof (#3)
- *(vfx)* Cross-system spawning compose-proof (#3)
- *(vfx)* Subgraph exposed-input surfaces as parent input slot (#8)
- *(vfx)* Block subgraph Suitable Contexts via set_context_setting (#8)
- *(vfx)* Output flipbook size + blend + motion vectors (#2)
- *(vfx)* Custom HLSL buffer/texture types + multi-file include (#15)
- *(vfx)* Agent-mode eval harness (outcome + routing layers)
- *(vfx)* Expand agent eval to 15 tasks + fix flipbook uvMode doc bug
- *(vfx)* Eval coverage for particle-strip + mesh-output systems (17 tasks)

### ⚙️ Miscellaneous Tasks

- *(auto-merge)* Limit develop auto-merge to akiojin and dependabot PRs
- *(work)* Record issue 305 pull request
- *(input)* Keep develop skill discovery and drop issue-246 work records
- *(work)* Record issue 295 work events
- *(work)* Record issue-293 work events
- *(work)* Record issue 290 pull request
- *(work)* Record issue 251 pull request
- *(work)* Record issue 289 work events
- Merge develop Timeline support with editor eval
- *(work)* Record issue 253 pull request handoff
- *(work)* Record issue 252 pull request handoff
- *(vfx)* Lock VFX Graph 17.4.0 after 6000.4.11f1 resolution
- *(vfx)* Exclude fork-only outcome-eval harness from PR branch
- Merge latest develop workflow updates
- *(work)* Record VFX integration delivery status
- Merge v0.13.0 release updates
- Merge latest develop integrations
- *(work)* Record VFX integration pull request
- *(work)* Record release 0.14.0 scope
## [0.13.0] - 2026-09-29

### 🚀 Features

- Add Timeline animation track editing and evaluation

### ⚙️ Miscellaneous Tasks

- *(auto-merge)* Enable auto-merge for PRs into develop

## [0.12.0] - 2026-06-23

### 🚀 Features

- Complete gwt spec implementation

## [0.11.5] - 2026-06-20

### 🚜 Refactor

- *(core)* Split bridge and lsp services

### 📚 Documentation

- *(skills)* Clarify unity-cli-usage — raw/tool call is primary, not fallback
- *(skills)* Correct typed-subcommand enumeration in unity-cli-usage
- Clarify contribution branch policy

## [0.11.4] - 2026-06-02

### 🐛 Bug Fixes

- *(ci)* Cargo test を --test-threads=1 で実行して env race を解消

### 📚 Documentation

- *(lessons)* Gwt-spec の集約 close 例外条項を追加
- *(skills)* Unity-asset-management の get_asset_info action を明記 (#206)

### ⚙️ Miscellaneous Tasks

- *(hooks)* Husky を auto-install して commit-msg を CI と一致させる
- *(hooks)* Pre-push を fmt check のみに絞る

## [0.11.3] - 2026-05-12

### 🐛 Bug Fixes

- *(release)* aarch64-unknown-linux-gnu ジョブを GitHub Actions の native ARM64 runner (`ubuntu-24.04-arm`) に切替。cross-compile 関連ステップ (gcc-aarch64-linux-gnu の apt インストール、`CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER` env) を削除し、`ort` (ONNX Runtime) が要求していた `libstdc++:arm64` の cross-link 問題を解消

## [0.11.2] - 2026-05-12

### 🐛 Bug Fixes

- *(reference)* fastembed の依存を `default-features = false` + `hf-hub-rustls-tls` + `ort-download-binaries` に切り替え、`openssl-sys` 依存を完全に除去。aarch64-unknown-linux-gnu の cross-build で発生していた `openssl` not found 問題を恒久的に解消し、GitHub Release のバイナリ upload を復旧
- *(release)* v0.11.1 で試した workflow 側の ARM64 OpenSSL インストールは Ubuntu 24 deb822 sources の制約で不安定だったため revert

## [0.11.1] - 2026-05-12

### 🐛 Bug Fixes

- *(release)* aarch64-unknown-linux-gnu cross-build で OpenSSL ARM64 ライブラリと pkg-config 環境変数を設定し、v0.11.0 で skip された GitHub Release バイナリ upload を復旧 (Ubuntu 24 deb822 形式により部分的に失敗 — v0.11.2 で rustls-tls 移行に再修正)

## [0.11.0] - 2026-05-12

### 🚀 Features

- *(reference)* UnityCsReference のローカルキャッシュ参照機構（Phase 1）
- *(reference)* Phase 2 - reference find-symbol と Phase 1 振り返り
- *(reference)* Phase 3 - reference diff と resolve-symbol-at（SPEC #188）
- *(reference)* Member-level シンボル抽出 MVP（Phase 4-A）
- *(reference)* Compute_line_diff を LCS / Myers 化（Phase 4-B）
- *(reference)* Extract_token_at_cursor を C# lexer 化（Phase 4-C）
- *(reference)* Reference fetch に zip fallback を追加（Phase 4-D）
- *(reference)* Vector embedding 検索 MVP（Phase 4-E）

### 🐛 Bug Fixes

- *(ci)* Upgrade to pnpm 10 for action-setup v6 compatibility
- *(ci)* Revert pnpm/action-setup to v5 for lockfile compatibility
- *(daemon)* Skip cross-compilation test when target is unavailable
- *(daemon)* Suppress unused import warning on Windows builds
- *(daemon)* Use compile probe to detect Windows target availability
- *(reference)* CI clippy 1.95 warnings と coverage gate を解消
- *(reference)* Clippy 1.95 useless-conversion を解消（Phase 4-E）
- *(release)* Guard closing issue collection
- *(runner)* Clippy 1.95 警告を解消（unused tool var と await_holding_lock attr 位置）
- *(unity-cli-bridge)* Align BridgeCommandStats accessibility
- Guard Unity 2022 editor scripts
- Guard Unity 2022.3 API differences

### 🎨 Styling

- *(daemon)* Apply rustfmt to cross-compilation test

### 🧪 Testing

- *(coverage)* Reference 系 / self_update / managed_binaries に追加テストを投入
- *(release)* Follow agents gh-pr paths

### ⚙️ Miscellaneous Tasks

- *(reference)* Test 残骸の .unity-cli-index/ を gitignore へ
- Add Unity 2022/6 manifest switch
- Apply cargo fmt to test imports and assert_eq formatting
- Ignore tests/fixtures/**/.unity-cli-index/
- Remove legacy hook scripts and hooks config from settings.json

## [0.10.0] - 2026-04-10

### 🚀 Features

- *(skills)* Introduce Skill Contract v1, unity-cli skills lint, dual plugin distribution (#160)
- *(skills)* Add unity development loop skill

### 🐛 Bug Fixes

- Resolve issue 137 and align unity project updates

### 🎨 Styling

- Format skill coverage tests

### 🧪 Testing

- *(skills)* Raise coverage and normalize workflow skill
- *(skill-routing)* Tighten runner rules

### ⚙️ Miscellaneous Tasks

- *(deps)* Add cargo ecosystem to dependabot config
- *(deps)* Bump pnpm/action-setup from 4 to 5
- *(deps)* Bump the npm_and_yarn group across 1 directory with 5 updates
- *(deps)* Bump codecov/codecov-action from 5 to 6
- *(deps)* Bump rustls-webpki in the cargo group across 1 directory
- *(deps)* Bump undici in the npm_and_yarn group across 1 directory
- *(deps)* Bump the npm_and_yarn group with 2 updates

## [0.9.0] - 2026-03-13

### 🚀 Features

- *(input)* Stabilize simulation e2e with batch host
- Add media perf benchmark and capture telemetry

### 🐛 Bug Fixes

- *(input)* Address review regressions
- *(ci)* Align skill contract checks with csharp edit docs

### 🚜 Refactor

- Remove speckit and local specs

### 📚 Documentation

- *(skills)* Strengthen unity csharp edit workflow

### 🎨 Styling

- *(lsp)* Format deterministic daemon stop test
- *(rust)* Format review fixes

### 🧪 Testing

- *(lsp)* Wait for daemon readiness before stop assertion
- *(daemon)* Cover timing response paths
- *(lsp)* Relax daemon stop polling under coverage
- *(lsp)* Wait for daemon socket before stop
- *(lsp)* Send stop request directly in daemon test
- *(lsp)* Make daemon stop test deterministic
- *(lsp)* Retry nonblocking daemon accept in CI
- *(lsp)* Isolate pid file cleanup checks

### ⚙️ Miscellaneous Tasks

- *(unity)* Update project editor version

## [0.7.3] - 2026-03-11

### 🐛 Bug Fixes

- Improve self-update logging with match expression

## [0.7.2] - 2026-03-11

### 🐛 Bug Fixes

- Use Path instead of PathBuf for borrowed references in self_update

## [0.7.1] - 2026-03-11

### 🐛 Bug Fixes

- Ensure self-update completes before process exit and prevent binary loss

## [0.7.0] - 2026-03-11

### 🚀 Features

- Add animator controller creation command
- Add animation clip and sprite atlas commands

### 📚 Documentation

- Add PATH setup instructions to Quick Install section

## [0.6.0] - 2026-03-11

### 🚀 Features

- Add install script and CLI auto-update on startup
## [0.5.1] - 2026-03-11

### 🐛 Bug Fixes

- *(ci)* Stabilize perf and daemon validation

### ⚙️ Miscellaneous Tasks

- Skip test workflow for main pull requests
- *(claude)* Update local settings
- Use local lsp publish for perf checks

## [0.5.0] - 2026-03-11

### 🐛 Bug Fixes

- *(ci)* Stabilize runtime tests and skill contract check
- *(ci)* Stabilize checks and keep unity e2e local-only
- *(ci)* Repair markdown docs and rust test stability
- *(ci)* Harden linux daemon checks

### 🚜 Refactor

- Modularize runtime and upgrade lsp to dotnet 10

### ⚙️ Miscellaneous Tasks

- Add codecov coverage reporting

## [0.4.1] - 2026-03-11

### 🐛 Bug Fixes

- *(ci)* Align unity-cli artifact names with detect_rid() convention

## [0.4.0] - 2026-03-11

### 🚀 Features

- *(auto-update)* Add managed daemon auto-update

### 📚 Documentation

- *(skills)* Align unity skills with Anthropic guidance

## [0.3.0] - 2026-03-10

### 🚀 Features

- Strengthen C# edit workflow
- *(ci)* Add cargo publish step to release workflow

### 🐛 Bug Fixes

- Retry transient lsp manifest fetches
- *(publish)* Restrict crate package to src and root files only

### 📚 Documentation

- Codify issue completion criteria
- Add OpenUPM install instructions to all READMEs

### 🧪 Testing

- Tighten E2E coverage

### ⚙️ Miscellaneous Tasks

- Ignore local cache directory

## [0.2.4] - 2026-03-06

### 🐛 Bug Fixes

- *(ci)* Skip lsp-perf job on release commits to avoid 404 race condition
- *(release)* Publish crate and align install docs
## [0.2.3] - 2026-03-05

### 🚀 Features

- Add gh skills sync skill for codex and claude
- *(cli)* Add strict schema introspection and action-aware validation
- *(cli)* Tighten schema variants and align issue-first spec templates

### 🐛 Bug Fixes

- *(ci)* Use PAT only for auto-merge to enable closing keywords

### ⚙️ Miscellaneous Tasks

- Add gh skills to project codex skills
- Add gh skills sync script
- *(spec)* Regenerate specs index for current repository state

## [0.2.2] - 2026-03-03

### 🐛 Bug Fixes

- *(plugin)* Remove invalid manifest fields that broke marketplace install (#57)
- *(bridge)* Separate compile errors from console errors, fix test filter and watchdog (#59)
- *(lsp)* Use github token for lsp manifest fetch
- *(ci)* Format lsp_manager test helper
## [0.2.1] - 2026-03-02

### 🐛 Bug Fixes

- Cross-platform path mismatch in capture handlers (#54)
- *(ci)* Release workflow now triggers on merge commits

### ⚙️ Miscellaneous Tasks

- Update specs index
## [0.2.0] - 2026-03-02

### 🚀 Features

- *(skills)* Add skill accuracy evaluation pipeline

### 🐛 Bug Fixes

- *(ci)* Stabilize lspd tests and lint failures

### 📚 Documentation

- *(claude)* Add workflow and task tracking templates
- Restructure README and add multilingual docs

### 🧪 Testing

- Improve coverage to 90 percent
- *(e2e)* Honor env host and wait for test completion
- *(lspd)* Relax brittle daemon response assertions

### ⚙️ Miscellaneous Tasks

- *(release)* Add linux arm64 artifacts
- *(docker)* Install tiktoken for perf scripts
- *(git)* Ignore local history artifacts

## [0.1.3] - 2026-02-26

### 🐛 Bug Fixes

- *(lsp)* Restore safe defaults and improve local tool errors

### ⚙️ Miscellaneous Tasks

- *(release)* V0.1.2
- Remove release-please references and enable auto-merge for develop→main
- *(tools)* Register csharp write tools and docs

## [0.1.2] - 2026-02-24

### 🐛 Bug Fixes

- *(release)* Include linux-arm64 LSP server binary in release pipeline

### ⚙️ Miscellaneous Tasks

- *(deps)* Bump the npm_and_yarn group with 5 updates
- *(deps)* Bump actions/checkout from 4 to 6
- *(deps)* Bump actions/setup-node from 4 to 6
- *(deps)* Bump actions/upload-artifact from 4 to 6
- *(deps)* Bump actions/download-artifact from 4 to 7
- *(deps)* Bump actions/setup-dotnet from 4 to 5

## [0.1.1] - 2026-02-24

### 🐛 Bug Fixes

- *(ci)* Align action versions in build-lsp job

### ⚙️ Miscellaneous Tasks

- *(release)* Add LSP server build and manifest to release pipeline

## [0.1.0] - 2026-02-23

### Features

- *(docker)* Add gh auth setup-git to entrypoint ([052ea97](https://github.com/akiojin/unity-cli/commit/052ea97))
- *(release)* Adopt gwt-style CI release flow and add git-cliff ([05c3286](https://github.com/akiojin/unity-cli/commit/05c3286))
- Persist LSP perf history and remove UNITY_CLI_UNITYD ([9de986b](https://github.com/akiojin/unity-cli/commit/9de986b))
- Add unityd control commands and reduce queue latency ([6455285](https://github.com/akiojin/unity-cli/commit/6455285))
- *(skills)* Add unity-cli bootstrap instructions ([77d4dc3](https://github.com/akiojin/unity-cli/commit/77d4dc3))
- **[breaking]** Complete unity-cli migration and remove MCP compatibility ([3b3fe01](https://github.com/akiojin/unity-cli/commit/3b3fe01))
- *(test)* Migrate Unity test project for unity-cli ([dc07a2b](https://github.com/akiojin/unity-cli/commit/dc07a2b))
- Rebuild skills as task-workflow units (13 skills, 1 agent) ([af9df77](https://github.com/akiojin/unity-cli/commit/af9df77))
- Apply GitHub repo settings and branch protection ([cb3bcb8](https://github.com/akiojin/unity-cli/commit/cb3bcb8))
- Resolve all follow-up tasks for unity-cli migration ([28d6724](https://github.com/akiojin/unity-cli/commit/28d6724))
- unity-cliへ開発環境一式を移行 ([c84ea73](https://github.com/akiojin/unity-cli/commit/c84ea73))
- Migrate UnityCliBridge/UPM + LSP rename and cargo install metadata ([f0171b4](https://github.com/akiojin/unity-cli/commit/f0171b4))

### Bug Fixes

- Sync lspd with develop merge state ([54baeaa](https://github.com/akiojin/unity-cli/commit/54baeaa))
- Restore unityd config and cli command wiring ([61db17a](https://github.com/akiojin/unity-cli/commit/61db17a))
- Support large LSP daemon responses and giga-file perf checks ([35e7771](https://github.com/akiojin/unity-cli/commit/35e7771))
- Include unityd module and restrict auto fallback ([707c033](https://github.com/akiojin/unity-cli/commit/707c033))
- Support screenshot base64 analysis and standardize e2e scene handling ([f36848f](https://github.com/akiojin/unity-cli/commit/f36848f))
- Restore migrated skill alias links ([977ef24](https://github.com/akiojin/unity-cli/commit/977ef24))
- *(lsp)* Stabilize bridge io and prebuilt daemon workflow ([56b6f53](https://github.com/akiojin/unity-cli/commit/56b6f53))
- Resolve remaining markdownlint MD060 errors in docs ([a89ba12](https://github.com/akiojin/unity-cli/commit/a89ba12))
- Resolve markdownlint MD060 table column style errors ([47f07c7](https://github.com/akiojin/unity-cli/commit/47f07c7))
- Resolve CI failures (fmt, lockfile, specs.md) ([0b7c967](https://github.com/akiojin/unity-cli/commit/0b7c967))

### Refactoring

- Make unityd mode always auto and align specs ([da5eda1](https://github.com/akiojin/unity-cli/commit/da5eda1))
- *(unity-bridge)* Resolve issue #20 and remove legacy Mcp remnants ([901453d](https://github.com/akiojin/unity-cli/commit/901453d))
- *(release)* Rewrite /release command with git-cliff automation ([1082cde](https://github.com/akiojin/unity-cli/commit/1082cde))
- Flatten UnityCliBridge directory structure ([54f1959](https://github.com/akiojin/unity-cli/commit/54f1959))

### Documentation

- Refresh specs index for active requirement ([9e49567](https://github.com/akiojin/unity-cli/commit/9e49567))
- Consolidate docs and package readmes ([c3aad97](https://github.com/akiojin/unity-cli/commit/c3aad97))
- Document legacy shim rationale and removal criteria ([2431ecd](https://github.com/akiojin/unity-cli/commit/2431ecd))
- Add baseline policy and diff inventory for MCP→CLI migration ([b7632c5](https://github.com/akiojin/unity-cli/commit/b7632c5))

### Testing

- Add full tool E2E and LSP performance checks ([91cad13](https://github.com/akiojin/unity-cli/commit/91cad13))

### CI

- Trigger lint workflow re-run ([0eb38f4](https://github.com/akiojin/unity-cli/commit/0eb38f4))
