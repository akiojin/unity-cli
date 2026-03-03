# unity-cli

[English](README.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` 是一个 Rust CLI，可让 Claude Code 通过 TCP 直接控制 Unity Editor。
它是 [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) 的后继项目，从 Node.js + MCP 重构为原生二进制工作流。

## 为什么使用 unity-cli

- 通过按领域划分的 skills 和 typed 命令，从 Claude Code 操作 Unity。
- 提供 `101` 个 Unity Tool API，覆盖场景、资源、代码、测试、UI 和编辑器。
- 单一可执行文件，启动快、开销低。

## 工作方式

```text
Claude Code
  -> Skills (按需加载)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

部分代码工具（如 `read`、`search`、`find_symbol`、`find_refs`）可在本地运行，不需要 Unity 连接。

## 快速开始

### 推荐: Claude Code 插件

从 Claude Code Marketplace 安装 `unity-cli` 插件:

```bash
/plugin marketplace add akiojin/unity-cli
```

如果环境中可用 `cargo`，插件安装流程可自动安装或更新 `unity-cli`。

### Codex Skills

使用 Codex 时，`.codex/skills/` 中已提供指向插件源的符号链接。
只需克隆仓库即可，无需额外配置。

### 手动安装

```bash
cargo install unity-cli
```

Unity 侧 UPM 包 URL:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

连接检查:

```bash
unity-cli system ping
```

## Skills (13)

| 类别 | Skills |
| --- | --- |
| 入门 | `unity-cli-usage` |
| 场景与对象 | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| 资源 | `unity-asset-management`, `unity-addressables` |
| 代码 | `unity-csharp-navigate`, `unity-csharp-edit` |
| 运行与测试 | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| 编辑器 | `unity-editor-tools` |

## 快速示例

```bash
# 连通性检查
unity-cli system ping

# 创建场景
unity-cli scene create MainScene

# 通过 raw 工具调用创建 GameObject
unity-cli raw create_gameobject --json '{"name":"Player"}'

# 搜索 C# 代码（本地工具）
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# 运行 EditMode 测试
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## 配置

| 变量 | 说明 | 默认值 |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | 包含 `Assets/` 和 `Packages/` 的目录 | 自动检测 |
| `UNITY_CLI_HOST` | Unity Editor 主机 | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor 端口 | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | 命令超时 (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP 模式 (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | 已下载工具根目录 | OS 默认 |

不支持旧 MCP 前缀环境变量。请仅使用 `UNITY_CLI_*`。

## 文档

- 完整命令与工具目录: [docs/tools.md](docs/tools.md)
- 开发流程与 CI: [docs/development.md](docs/development.md)
- 贡献指南: [CONTRIBUTING.md](CONTRIBUTING.md)
- 发布流程: [RELEASE.md](RELEASE.md)
- 署名模板: [ATTRIBUTION.md](ATTRIBUTION.md)

## 许可证

MIT。重新分发时的署名模板见 [ATTRIBUTION.md](ATTRIBUTION.md)。
