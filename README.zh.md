# unity-cli

[English](README.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` 是一个 Rust CLI，可让 Claude Code 通过 TCP 直接控制 Unity Editor。
它是 [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) 的后继项目，从 Node.js + MCP 重构为原生二进制工作流。

## 为什么使用 unity-cli

- 通过按领域划分的 skills 和 typed 命令，从 Claude Code 操作 Unity。
- 提供 143 个 runtime Unity Tool API 和 11 个 Reference Cache 工具，覆盖场景、资源、代码、测试、UI 和编辑器。
- 单一可执行文件，启动快、开销低。

## v0.13 之后新增的功能

[可执行工具示例](docs/tools.md#v013v015-executable-examples) 包括：

- 跨 Domain Reload 的 PlayMode 测试结果；键盘 / 手柄按住、鼠标 / 触摸模拟、输入序列，以及 InputAction 通知检查。
- MP4、WebM 和 PNG 帧序列录制，以及方法热重载预览。
- Timeline 资源 / 轨道 / 剪辑编辑，Lighting / NavMesh / NavMeshSurface / Occlusion 烘焙作业，以及可轮询状态的独立 Player 构建。

运行时输入、捕获和热重载从 `unity-playmode-testing` 开始，Timeline 从 `unity-asset-management` 开始，构建和烘焙从 `unity-editor-tools` 开始。每个示例都列出了包和场景前提。

## 工作方式

```text
Claude Code
  -> Skills (按需加载)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

部分代码工具（如 `read`、`search`、`find_symbol`、`find_refs`）可在本地运行，不需要 Unity 连接。

## 快速开始

### Editor 兼容性

Bridge 要求 Unity 2022.3 或更高版本。macOS 验证矩阵覆盖
2022.3.62f3、6000.0.84f1、6000.3.25f1、6000.4.11f1、6000.5.3f1、
6000.6.3f1、6000.7.0a2 和 6000.7.0b2（2026-09-30 验收 E2E: 全部 PASS）。
选择版本前请查看[验证日期与结果](docs/editor-compatibility.md)。
Unity 6000.7 的 Editor 运行在 Mono 上，CoreCLR Player 构建不在此矩阵范围内。
Windows 基线与全工具检查已在 6000.3.28f1、6000.6.8f1 和 Unity 7.0 Alpha 7000.0.0a16 上，针对 2026-11-10 的更新源通过。
其中 7000.0.0a16 的 Editor 运行在 CoreCLR 上；确切的 alpha 构建、包和测试范围见所链接的 Windows 结果。

### 使用 LLM 代理安装或更新

在 Unity 项目中打开 Claude Code、Codex 或其他编码代理，然后直接粘贴以下提示词。之后再次运行
同一提示词，即可同时更新 CLI 和 Unity 端的 bridge 包:

```text
请为这个 Unity 项目安装或更新 unity-cli，让 Unity 端的 bridge 包与 CLI 保持相同版本，
并将其连接到 Unity Editor。

1. 判断我的操作系统（macOS、Linux 或 Windows）。
2. 如果 PATH 中还没有 `unity-cli`，运行官方快速安装脚本:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows（PowerShell）: irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. 如果 `unity-cli` 已在 PATH 中，请改为将其更新到最新版本。
   如果设置了 UNITY_CLI_NO_AUTO_UPDATE=1 或 UNITY_CLI_VERSION，说明我有意固定了版本:
   不要更新，先告诉我并征得我的确认。否则，请找出二进制文件的实际位置（跟随符号链接），
   并按照安装方式进行更新:
   - Quick Install（~/.unity/tools 下的 managed 副本）: unity-cli cli install
   - Homebrew: brew upgrade akiojin/tap/unity-cli
   - 其他方式（cargo install、手动下载）: 告诉我安装方式，并在修改前征得我的确认。
4. 确认可以从 PATH 运行 `unity-cli --version`。在 macOS / Linux 上如果找不到，
   请把 ~/.local/bin 加入当前 shell 的 PATH，并告诉我需要写入 shell 配置文件的那一行。
   在 Windows 上，请打开新的 shell 以应用更新后的用户 PATH。
5. 修改项目之前，请告诉我 setup 会编辑 Packages/manifest.json（添加 OpenUPM
   scoped registry 以及固定为 CLI 同版本的 com.akiojin.unity-cli-bridge），并且对于
   只使用旧 Input Manager 的项目，会把 ProjectSettings/ProjectSettings.asset 中的
   activeInputHandler 设为 Both。等待我的确认。
6. 在 Unity 项目根目录运行: unity-cli --output json setup --launch-editor
   如果 JSON 返回 ok: false，请按照 editor.hint 处理（首次导入可能需要几分钟；
   重新运行 setup 可继续等待）。
7. 使用以下命令检查 bridge 版本: unity-cli --output json bridge status
   - 如果 source 为 registry，且 versionCheck.status（此结果或 setup 报告中）为 mismatch:
     告诉我这会把 Packages/manifest.json 中 com.akiojin.unity-cli-bridge 这一行改为 CLI 版本，
     并等待我的确认。然后运行 unity-cli bridge upgrade，并让 Unity Editor 重新导入该包。
   - 如果 source 为 custom 或 embedded（Git URL、本地路径或 Packages/ 下的副本），
     或 versionCheck.status 为 unknown: 不要改写它，而是向我报告 source 和两个版本。
8. 使用以下命令验证连接: unity-cli system ping
   确认 versionCheck.status 为 match（CLI 与 bridge 版本相同）。
9. 如果任何步骤失败，请运行: unity-cli doctor --output json
   报告 diagnosis 代码，并按照其提示处理后再重试。
```

**Claude Code 插件**: 从 Claude Code Marketplace 安装 `unity-cli` 插件，
让代理可以使用 Unity 工作流 Skills:

```bash
/plugin marketplace add akiojin/unity-cli
```

插件只提供 Skills，二进制文件和 Unity bridge 会在需要时自动引导安装。
当任务需要 Unity 但尚未安装 `unity-cli` 时，`unity-cli-usage` skill 会让代理运行
快速安装脚本，然后在 Unity 项目中运行 `unity-cli setup`
（编辑 `Packages/manifest.json` 之前会先告知你）。

**Codex Skills**: 在 Codex 中使用本仓库时，可以通过 `.agents/skills/` 使用 Skills
（指向插件源码的符号链接，也可通过 `.claude/skills/` 访问）。只需克隆仓库，无需额外设置。

### 手动安装

#### 快速安装（推荐）

macOS（Apple silicon / Intel）和 Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows（PowerShell）:

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

两个安装脚本都会把发布版二进制文件下载到 managed 目录
`~/.unity/tools/unity-cli/{rid}/`，并在安装前使用发布版的 `SHA256SUMS` 校验；
校验不一致时会中止安装，且不会改动已有的二进制文件。`install.sh` 会创建
`~/.local/bin/unity-cli` 符号链接；`install.ps1` 会把 managed 目录加入用户 `PATH`。
设置 `UNITY_CLI_VERSION=v0.19.1` 可固定发布版本。首次安装后，CLI 会在后台自动检查更新，
每次更新都使用相同的校验和进行验证。

如果 `~/.local/bin` 不在 PATH 中，请将以下内容加入 shell 配置文件
（例如 `~/.zshrc` 或 `~/.bashrc`）:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

设置 `UNITY_CLI_NO_AUTO_UPDATE=1` 可关闭自动更新。

#### 包管理器

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux（Homebrew）
```

通过 Homebrew 安装时，使用 `brew upgrade` 更新。
Windows winget 包尚未发布（#537）；发布前请使用上方的 PowerShell 快速安装命令。
在 Intel macOS 上，reference 嵌入（`unity-cli reference embed-build` / `embed-search`）会动态加载 ONNX Runtime；
Homebrew formula 会安装 `onnxruntime`，其他安装方式请运行
`brew install onnxruntime` 或设置 `ORT_DYLIB_PATH`。

#### Releases 或 cargo install

从 crates.io 安装已发布的 crate。发布工作流通过 `cargo publish` 发布它。

```bash
cargo install unity-cli
```

也可以从 [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) 下载最新二进制文件，
或从本地 checkout 安装:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### 项目设置

一条命令完成项目设置（二进制检查 → bridge 安装 → Editor 连接）:

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` 会向 `Packages/manifest.json` 添加 OpenUPM scoped registry 和
`com.akiojin.unity-cli-bridge`（固定为 CLI 同版本），必要时启动项目对应的 Unity Hub Editor，
等待 bridge 响应，并返回一份 JSON 报告。`unity-cli bridge install | upgrade | status`
可单独管理该包；`install` 是幂等的，CLI 与 bridge 版本不一致时，`setup` / `system ping`
会通过 `versionCheck` 报告。当项目仍只使用旧 Input Manager 且 Editor 已关闭时，
`install` 还会把 `ProjectSettings/ProjectSettings.asset` 中的 `activeInputHandler` 设为 Both，
从而在导入 Input System 依赖时避免阻塞式的重启提示。

Unity 端 bridge 包的手动安装方式（任选其一）:

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL**（Unity Package Manager）:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### 连接检查

```bash
unity-cli system ping
unity-cli doctor --output json   # ping 失败时: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### managed 二进制维护

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` 和 `lspd` 会在 daemon 启动时自动更新 managed `unity-cli` / C# LSP 二进制文件。
managed 副本位于 `UNITY_CLI_TOOLS_ROOT`（未设置时为操作系统默认的 tools 目录）下，更新时不会弹出交互式确认。

## Skills (23)

这些技能是工作流指南。清单与 [docs/skills.md](docs/skills.md) 以及插件目录 `.claude-plugin/plugins/unity-cli/skills/` 一致。

| 类别 | Skills |
| --- | --- |
| 入门 | `unity-project-bootstrap`, `unity-cli-usage` |
| 场景与对象 | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| 资源 | `unity-2d-sprite-tilemap`, `unity-asset-management`, `unity-audio-setup`, `unity-addressables` |
| 代码 | `unity-csharp-navigate`, `unity-csharp-edit`, `unity-csharp-reference` |
| 运行与测试 | `unity-playmode-testing`, `unity-input-system`, `unity-ui-toolkit-build`, `unity-localization`, `unity-ui-automation`, `unity-development-loop`, `unity-vfx-graph` |
| 编辑器 | `unity-editor-tools`, `unity-package-management`, `unity-urp-setup` |

`gh-skills-sync` 位于 `dev-skills/`，仅用于开发，不随插件分发。

## 快速示例

无需对话框即可导入本地 `.unitypackage` 或已下载的 Asset Store 包，并等待编译和 Domain Reload。

```bash
unity-cli package import /absolute/path/Small.unitypackage --output json
unity-cli package cache-list --output json
unity-cli package cache-import "Small Package" --output json
# 打开 My Assets，在 Unity 中手动按 Download，然后自动导入:
unity-cli package asset-store-import 12345 --download-timeout-ms 600000 --output json
```

用 `--cache-path` 或 `UNITY_CLI_ASSET_STORE_CACHE_PATH` 指定自定义缓存。
选择 ID、超时和恢复见 [package import commands](docs/tools.md#unitypackage-and-asset-store-cache)。`package asset-store-open [name-or-id]` 和 `package asset-store-wait <name-or-id>` 分别执行显示和等待。下载、登录和所有权检查仍在 Unity 中手动完成；CLI 不会按下 Download，也不会读取 Unity 账号令牌。

```bash
# 连通性检查
unity-cli system ping

# 创建场景
unity-cli scene create MainScene

# 通过 raw 工具调用创建 GameObject
unity-cli raw create_gameobject --json '{"name":"Player"}'

# 搜索 C# 代码（本地工具）
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# 查看机器可读的工具 schema
unity-cli tool schema create_scene --output json

# 对会改动状态的工具调用做 dry-run（无副作用）
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# 运行 EditMode 测试
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## GWT Spec 工作流

功能规格由带 `gwt-spec` 标签的 GitHub Issue 管理。
`Spec`、`Plan`、`Tasks`、`TDD` 以 Issue 正文为准。

## 贡献

外部贡献应指向 `develop` 分支。打开 PR 前请阅读
[CONTRIBUTING.md](CONTRIBUTING.md)，尤其是分支策略、必需 CI 和技能文档变更。

## 配置

| 变量 | 说明 | 默认值 |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | 包含 `Assets/` 和 `Packages/` 的目录 | 自动检测 |
| `UNITY_CLI_HOST` | Unity Editor 主机 | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor 端口 | `6400` |
| `UNITY_CLI_AUTH_TOKEN_FILE` | 含 `authToken` 的 Editor lockfile JSON，用于远程或挂载的凭据 | *(未设置)* |
| `UNITY_CLI_ALLOW_UNAUTHENTICATED` | 临时迁移 opt-out。仅 `1` 启用 | *(未设置)* |
| `UNITY_CLI_TIMEOUT_MS` | 命令超时 (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP 模式 (`off` / `auto` / `required`)。`auto` 在符号搜索中优先使用 C# LSP，不可用时回退到本地索引（`backend: "index"`, `fallbackReason`） | `auto` |
| `UNITY_CLI_LSP_AUTO_TIMEOUT_MS` | `auto` 符号搜索等待 LSP 的上限 (ms)，超时后回退到本地索引 | `10000` |
| `UNITY_CLI_TOOLS_ROOT` | 已下载工具根目录 | OS 默认 |
| `UNITY_CLI_UNITYD_PORT` | Windows unityd 回环 TCP 端口（客户端与 daemon） | `6422` |
| `UNITY_CLI_LSPD_PORT` | Windows LSP daemon 回环 TCP 端口（客户端与 daemon） | `6421` |
| `UNITY_CLI_NO_AUTO_UPDATE` | 关闭后台自动更新（`1` 为关闭） | *(未设置)* |

不支持旧 MCP 前缀环境变量。请仅使用 `UNITY_CLI_*`。
Windows daemon 端口接受 1 到 65535 的整数；取消设置即恢复默认值。请向 daemon 和 CLI 客户端传入相同的值。
认证、Docker 和 WSL2 见[配置指南](docs/configuration.md)。

## 文档

- 完整命令与工具目录: [docs/tools.md](docs/tools.md)
- 技能契约与清单: [docs/skills.md](docs/skills.md)
- 配置指南: [docs/configuration.md](docs/configuration.md)
- 架构: [docs/architecture.md](docs/architecture.md)
- 热重载: [docs/hot-reload.md](docs/hot-reload.md)
- 迁移说明: [docs/migration-notes.md](docs/migration-notes.md)
- MCP 薄适配器与客户端配置: [docs/mcp.md](docs/mcp.md)
- 与官方 Unity CLI 的对比（功能、延迟基准、同时使用）: [docs/comparison.md](docs/comparison.md)
- 开发流程与 CI: [docs/development.md](docs/development.md)
- Asset Store 下载可行性与约束: [研究](docs/asset-store-download-research.md)
- 贡献指南: [CONTRIBUTING.md](CONTRIBUTING.md)
- 发布流程: [RELEASE.md](RELEASE.md)
- 署名模板: [ATTRIBUTION.md](ATTRIBUTION.md)

## 许可证

MIT。重新分发时的署名模板见 [ATTRIBUTION.md](ATTRIBUTION.md)。
