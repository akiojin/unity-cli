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
