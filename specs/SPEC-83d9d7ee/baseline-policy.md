# ベースライン方針と差分棚卸し

**要件ID**: `SPEC-83d9d7ee`
**作成日**: 2026-02-22
**関連**: [移行記録](../migration-notes.md) | [機能仕様書](spec.md)

---

## 1. ベースライン方針

### 方針文

Unity 側コードベース（`UnityCliBridge/Packages/unity-cli-bridge`）は `unity-mcp-server` をベースコピーとし、差分は **MCP→CLI 移行に必要な変更に限定**する。

### 許可差分カテゴリ

| カテゴリ | 説明 |
|---------|------|
| 名称変更 | パッケージ名・名前空間・リポジトリ名を `unity-cli` / `UnityCliBridge` 系へリネーム |
| プロトコル置換 | MCP (JSON-RPC over stdio) → 直接 TCP 通信への変更 |
| 環境変数移行 | `UNITY_MCP_*` → `UNITY_CLI_*` へのリネームとフォールバック追加 |
| MCP 固有コード削除 | MCP サーバモード、stdio ハンドラ等の削除 |
| CLI 固有コード追加 | TCP サーバ初期化、CLI ブリッジホスト等の新規追加 |

### 禁止差分

- 移行に無関係な動作変更（ハンドラのロジック変更、UI 変更等）
- 根拠なきハンドラの削除・改変
- spec 未追跡の Unity 側機能追加

---

## 2. 差分一覧

### 2.1 Runtime & Language（許可）

| 項目 | 旧 (unity-mcp-server) | 新 (unity-cli) |
|------|----------------------|----------------|
| CLI 言語 | TypeScript / Node.js | Rust |
| エントリーポイント | `src/index.ts` | `src/main.rs` |
| パッケージマネージャ | npm | Cargo |
| 配布方式 | `npm install` | `cargo install` |

Unity 側 C# コードはそのまま引き継ぎ。

### 2.2 Protocol & Transport（許可）

| 項目 | 旧 | 新 |
|------|----|----|
| プロトコル | MCP (Model Context Protocol) | 直接 TCP |
| トランスポート | JSON-RPC over stdio | 4byte length + JSON over TCP |
| 接続方式 | ホストアプリが stdio でサーバ起動 | CLI が TCP で Unity Editor に接続 |

### 2.3 Naming & Identity（混在 - 一部要再検討）

#### 完了済みリネーム

| 項目 | 旧 | 新 |
|------|----|----|
| リポジトリ名 | `unity-mcp-server` | `unity-cli` |
| UPM パッケージ名 | `com.akiojin.unity-mcp-bridge` | `com.akiojin.unity-cli-bridge` |
| C# 名前空間 | `UnityMcpBridge` | `UnityCliBridge` |
| 環境変数プレフィクス | `UNITY_MCP_*` | `UNITY_CLI_*`（旧変数はフォールバック） |

#### 未完了 - Mcp 名残留（要再検討）

**305 箇所 / 45 ファイル**に `Mcp` プレフィクスが残留。詳細はセクション 3 を参照。

### 2.4 Architecture & Structure（許可）

| 項目 | 旧 | 新 |
|------|----|----|
| LSP | Node.js 内蔵の静的解析 | 独立 C# LSP サーバ (`lsp/`) |
| CI テスト | `npm test` | `cargo test` + `dotnet test` |
| リリース | `npm publish` | `cargo publish` + GitHub Release |
| スキル | MCP ツール 108 個 | Claude Code Skill に変換 |
| ディレクトリ | `src/` (TS) | `src/` (Rust) + `lsp/` (C#) + `UnityCliBridge/` |

### 2.5 New/Removed Features（許可）

#### 追加

- マルチインスタンス管理（`instances list` / `instances set-active`）
- `--output text|json` 出力切替
- Spec Kit による仕様・TDD 管理
- pre-push フック（`cargo test` + `dotnet test` 自動実行）

#### 削除

- MCP サーバモード（stdio 経由の JSON-RPC サーバ）
- npm 関連ファイル（`package.json`, `tsconfig.json`, `node_modules`）

---

## 3. 要再検討差分（Needs Review）

### 3.1 Mcp プレフィクス付きクラス（コアパッケージ）

| クラス名 | ファイルパス | 名前空間 | 種別 |
|---------|------------|---------|------|
| `McpLogger` | `Editor/Logging/McpLogger.cs` | `UnityCliBridge.Logging` | public static class |
| `McpStatus` | `Editor/Models/McpStatus.cs` | `UnityCliBridge.Models` | public enum |
| `McpEditTarget` | `Editor/Handlers/McpEditTarget.cs` | `UnityCliBridge.Handlers` | public class |
| `McpImguiControlRegistry` | `Runtime/IMGUI/McpImguiControlRegistry.cs` | `UnityCliBridge.Runtime.IMGUI` | public static class |
| `LegacyMcpImguiControlRegistry` | `Runtime/IMGUI/LegacyMcpImguiControlRegistry.cs` | `UnityMCPServer.Runtime.IMGUI` | 互換シム |

パスは `UnityCliBridge/Packages/unity-cli-bridge/` からの相対パス。

### 3.2 McpLogger 参照（241 箇所 / 主要ハンドラ）

| ファイル | 参照数 |
|---------|-------|
| `ScreenshotHandler.cs` | 31 |
| `UnityCliBridgeHost.cs` | 28 |
| `AddressablesHandler.cs` | 20 |
| `McpStatusTests.cs` | 12 |
| `InputSystemHandler.cs` | 12 |
| `ConsoleHandler.cs` | 11 |
| `GameObjectHandler.cs` | 11 |
| `UIInteractionHandler.cs` | 10 |
| `ToolManagementHandler.cs` | 9 |
| その他 36 ファイル | 97 |

### 3.3 TestProject の Mcp 名残留（5 ファイル + 2 アセット）

| ファイル | 種別 |
|---------|------|
| `TestProject/Assets/Editor/McpUiTestSceneGenerator.cs` | テストシーン生成スクリプト |
| `TestProject/Assets/Scripts/McpUiTest/McpUGuiTestSceneController.cs` | MonoBehaviour |
| `TestProject/Assets/Scripts/McpUiTest/McpAllUiSystemsTestBootstrap.cs` | MonoBehaviour |
| `TestProject/Assets/Scripts/McpUiTest/McpUiToolkitTestSceneController.cs` | MonoBehaviour |
| `TestProject/Assets/Scripts/McpUiTest/McpImguiTestPanel.cs` | MonoBehaviour |
| `TestProject/Assets/McpUiTest/UITK/MCP_UITK_Test.uxml` | UI Toolkit XML |
| `TestProject/Assets/McpUiTest/UITK/MCP_UITK_TestPanelSettings.asset` | UI Toolkit 設定 |

### 3.4 LegacyUnityMcpServerProjectSettings（意図的互換シム）

- **ファイル**: `Editor/Settings/LegacyUnityMcpServerProjectSettings.cs`
- **名前空間**: `UnityMCPServer.Settings`
- **親クラス**: `UnityCliBridge.Settings.UnityCliBridgeProjectSettings`
- **目的**: 旧シリアライズ済み `ProjectSettings/UnityMcpServerSettings.asset` の逆シリアライズ互換性を維持するための型エイリアス
- **根拠**: このクラスを削除すると、旧 `unity-mcp-server` から移行したプロジェクトで設定ファイルの読み込みが失敗する。移行完了まで維持が必要。
- **TestProject 実例**: `TestProject/ProjectSettings/UnityMcpServerSettings.asset` が現存

---

## 4. 逸脱対応 Issue

| 対応内容 | Issue |
|---------|-------|
| Mcp クラス名・ファイル名の一括リネーム（305 箇所 / 45 ファイル） | [#20](https://github.com/akiojin/unity-cli/issues/20) |
| `LegacyUnityMcpServerProjectSettings` 互換シムの根拠をコード内コメントで明記 | [#21](https://github.com/akiojin/unity-cli/issues/21) |
