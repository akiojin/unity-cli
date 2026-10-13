# unity-cli

[日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` is a Rust CLI that lets Claude Code control Unity Editor over direct TCP.
It is the successor to [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), redesigned from Node.js + MCP to a native binary workflow.

## Why unity-cli

- Operate Unity from Claude Code with focused skills and typed commands.
- Access `101` Unity Tool APIs across scene, asset, code, test, UI, and editor domains.
- Run as a single binary with fast startup and low overhead.

## Features added since v0.13

The [executable tool examples](docs/tools.md#v013v015-executable-examples) cover:

- PlayMode test results across Domain Reload; keyboard/gamepad holds, mouse/touch
  simulation, input sequences and InputAction notification checks.
- MP4, WebM and PNG frame-sequence recording, plus method hot reload previews.
- Timeline asset/track/clip editing, Lighting/NavMesh/NavMeshSurface/Occlusion bake
  jobs, and standalone Player builds with status polling.

Start with `unity-playmode-testing` for runtime/input/capture/hot reload,
`unity-asset-management` for Timeline, and `unity-editor-tools` for build/bake jobs.
Package and scene prerequisites are listed with each example.

## How It Works

```text
Claude Code
  -> Skills (on demand)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Some code tools (`read`, `search`, `find_symbol`, `find_refs`, etc.) run locally without a Unity connection.

## Getting Started

### Editor compatibility

The Bridge requires Unity 2022.3 or newer. The macOS matrix covers
2022.3.62f3, 6000.0.84f1, 6000.3.25f1, 6000.4.11f1, 6000.5.3f1,
6000.6.3f1, 6000.7.0a2 and 6000.7.0b2 (acceptance E2E: PASS on 2026-09-30). See the
[verification dates and results](docs/editor-compatibility.md) before choosing a version.
Unity 6000.7 Editor runs on Mono; CoreCLR Player builds are outside this matrix.

### Install or update with an LLM agent

Copy this prompt into Claude Code, Codex, or another coding agent opened in
your Unity project. Run the same prompt again later to update the CLI and the
Unity bridge package together:

```text
Install or update unity-cli for this Unity project, keep the Unity bridge
package on the same version as the CLI, and connect it to the Unity Editor.

1. Detect my OS (macOS, Linux, or Windows).
2. If `unity-cli` is not already on PATH, run the official Quick Install script:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. If `unity-cli` is already on PATH, update it to the latest release instead.
   If UNITY_CLI_NO_AUTO_UPDATE=1 or UNITY_CLI_VERSION is set, I pinned the
   version on purpose: do not update it; tell me and ask first. Otherwise
   resolve where the binary really lives (follow symlinks) and update it the
   way it was installed:
   - Quick Install (managed copy under ~/.unity/tools): unity-cli cli install
   - Homebrew: brew upgrade akiojin/tap/unity-cli
   - winget: winget upgrade akiojin.unity-cli
   - Anything else (cargo install, manual download): tell me how it was
     installed and ask before changing it.
4. Check that `unity-cli --version` works from PATH. On macOS / Linux, if it is
   not found, add ~/.local/bin to PATH for this shell and tell me the line to add
   to my shell profile. On Windows, open a new shell so the updated user PATH applies.
5. Before changing my project, tell me that setup edits Packages/manifest.json
   (adds the OpenUPM scoped registry and com.akiojin.unity-cli-bridge pinned to
   the CLI version) and, for legacy-input-only projects, sets activeInputHandler
   to Both in ProjectSettings/ProjectSettings.asset. Wait for my confirmation.
6. From the Unity project root, run: unity-cli --output json setup --launch-editor
   If the JSON has ok: false, follow editor.hint (the first import can take
   several minutes; rerun setup to keep waiting).
7. Check the bridge version with: unity-cli --output json bridge status
   - If source is registry and versionCheck.status (here or in the setup
     report) is mismatch: tell me that this changes the
     com.akiojin.unity-cli-bridge line in Packages/manifest.json to the CLI
     version and wait for my confirmation. Then run: unity-cli bridge upgrade
     and let the Unity Editor reimport the package.
   - If source is custom or embedded (Git URL, local path, or a copy under
     Packages/), or versionCheck.status is unknown: do not rewrite it. Report
     the source and both versions to me instead.
8. Verify the connection with: unity-cli system ping
   Confirm that versionCheck.status is match (the CLI and the bridge use the
   same version).
9. If any step fails, run: unity-cli doctor --output json
   Report the diagnosis code and follow its hint before retrying.
```

**Claude Code plugin**: install the `unity-cli` plugin from Claude Code
Marketplace to give the agent the Unity workflow skills:

```bash
/plugin marketplace add akiojin/unity-cli
```

The plugin ships skills; the binary and the Unity bridge are bootstrapped on
demand. When a task needs Unity and `unity-cli` is missing, the
`unity-cli-usage` skill has the agent run the Quick Install script and then
`unity-cli setup` in your Unity project (it tells you before editing
`Packages/manifest.json`).

**Codex Skills**: when using this repository with Codex, skills are available
via `.agents/skills/` (symlinks to the plugin source, also exposed through
`.claude/skills/`). No additional setup is required - just clone the repository.

### Install manually

#### Quick Install (recommended)

macOS (Apple silicon / Intel) and Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

Both installers download the release binary into the managed layout
`~/.unity/tools/unity-cli/{rid}/` and verify it against the release
`SHA256SUMS` before installing; a checksum mismatch aborts the install and
leaves the existing binary untouched. `install.sh` symlinks it to
`~/.local/bin/unity-cli`; `install.ps1` adds the managed directory to the user
`PATH`. Set `UNITY_CLI_VERSION=v0.16.0` to pin a release. After the initial
install the CLI checks for updates automatically in the background, and every
update is verified against the same checksums.

If `~/.local/bin` is not in your PATH, add the following to your shell profile
(e.g. `~/.zshrc` or `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Set `UNITY_CLI_NO_AUTO_UPDATE=1` to disable auto-update.

#### Package Managers

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux (Homebrew)
winget install akiojin.unity-cli       # Windows (winget)
```

Package-manager installs are updated by `brew upgrade` / `winget upgrade`.
On Intel macOS, reference embeddings (`unity-cli reference embed-build` / `embed-search`) load ONNX Runtime
dynamically; the Homebrew formula installs `onnxruntime`, otherwise run
`brew install onnxruntime` or set `ORT_DYLIB_PATH`.

#### Releases or cargo install

Download the latest binary from [GitHub
Releases](https://github.com/akiojin/unity-cli/releases), or install from a
local checkout:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### Project setup

One-command project setup (binary check → bridge install → Editor connection):

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` adds the OpenUPM scoped registry and `com.akiojin.unity-cli-bridge`
(pinned to the CLI version) to `Packages/manifest.json`, optionally launches
the project's Unity Hub Editor, waits for the bridge, and returns one JSON
report. `unity-cli bridge install | upgrade | status` manage the package on
their own; `install` is idempotent, and `setup` / `system ping` report a
`versionCheck` when the CLI and bridge versions differ. When the project still
uses only the legacy Input Manager and the Editor is closed, `install` also sets
`activeInputHandler` to Both in `ProjectSettings/ProjectSettings.asset`, so the
Input System dependency imports without a blocking restart prompt.

Unity-side bridge package, manual alternatives (choose one):

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### Connection check

```bash
unity-cli system ping
unity-cli doctor --output json   # when ping fails: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### Managed binary maintenance

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` and `lspd` automatically refresh their managed `unity-cli` / C# LSP binaries on daemon startup.
The managed copies live under `UNITY_CLI_TOOLS_ROOT` (or the OS default tools directory) and are updated without an interactive confirmation prompt.

## Skills (14)

These skills are written as workflow guides, not just command catalogs. They are designed to trigger from natural Unity requests such as "create a test scene", "trace references to this MonoBehaviour", or "run PlayMode tests and capture a screenshot".

| Category | Skills |
| --- | --- |
| Getting Started | `unity-cli-usage` |
| Scenes & Objects | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime & Testing | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |
| Maintenance | `gh-skills-sync` |

## Quick Examples

```bash
# Connectivity
unity-cli system ping

# Create a scene
unity-cli scene create MainScene

# Create a GameObject through raw tool call
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Search C# code (local tool)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Inspect machine-readable tool schema
unity-cli tool schema create_scene --output json

# Dry-run mutating tool calls (no side effects)
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# Run EditMode tests
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## GWT Spec Workflow

Feature specifications are managed in GitHub Issues labeled `gwt-spec`.
Use the Issue body as the source of truth for `Spec`, `Plan`, `Tasks`, and `TDD`.

## Contributing

External contributions should target the `develop` branch. Read
[CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR, especially for branch
policy, required CI, and skill documentation changes.

## Configuration

| Variable | Description | Default |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directory containing `Assets/` and `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Unity Editor host | `127.0.0.1` |
| `UNITY_CLI_PORT` | Unity Editor port | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Command timeout (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP mode (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Downloaded tools root directory | OS default |
| `UNITY_CLI_NO_AUTO_UPDATE` | Disable background auto-update (`1` to disable) | *(unset)* |

Legacy MCP-prefixed variables are not supported. Use `UNITY_CLI_*` only.

## Documentation

- Full command and tool catalog: [docs/tools.md](docs/tools.md)
- MCP thin adapter and client configuration: [docs/mcp.md](docs/mcp.md)
- Comparison with the official Unity CLI (features, latency benchmark, using both together): [docs/comparison.md](docs/comparison.md)
- Development workflow and CI: [docs/development.md](docs/development.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Release process: [RELEASE.md](RELEASE.md)
- Attribution templates: [ATTRIBUTION.md](ATTRIBUTION.md)

## License

MIT. See [ATTRIBUTION.md](ATTRIBUTION.md) for redistribution attribution templates.
