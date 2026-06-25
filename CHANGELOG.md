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
