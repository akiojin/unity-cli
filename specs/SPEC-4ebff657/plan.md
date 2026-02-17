# 実装計画: Unity メニュー起動とサンプルワークフロー

**機能ID**: `SPEC-4ebff657` | **日付**: 2025-12-04 | **仕様**: [spec.md](./spec.md)
**入力**: `/specs/SPEC-4ebff657/spec.md` の機能仕様

## 概要
Unity エディタメニューに MCP Server の Start/Stop と HTTP/stdio/Telemetry 設定切替を追加し、非開発者が GUI で操作できるようにする。導入直後に試せるシーン操作・Addressables 登録のサンプルワークフローを同梱する。

## 技術コンテキスト
**言語/バージョン**: C# (Unity 2020.3+), Node.js 側 CLI 呼び出しをラップ
**主要依存関係**: UnityEditor APIs (MenuItem, EditorWindow), Addressables (サンプル用), 既存 MCP CLI
**ストレージ**: なし（設定は EditorPrefs または ScriptableObject）
**テスト**: Unity playmode/edittime tests (サンプルの副作用確認), node --test で CLI フック
**対象プラットフォーム**: Unity エディタ (Win/Mac)
**プロジェクトタイプ**: Unity package (UnityCliBridge/Packages/unity-cli-bridge/)
**パフォーマンス目標**: Start/Stop ボタン応答 <1s、サンプル実行 <3s
**制約**: Play 中は Start/Stop を無効化または警告
**スケール/スコープ**: GUI 1 画面 + サンプル 2 本

## 憲章チェック
シンプルさ: EditorWindow 1 つ + サンプル2本の最小構成 → OK
アーキテクチャ: 既存 CLI をラップ、独自サーバー実装を増やさない → OK
テスト: edittime で Start/Stop・サンプルの副作用確認を先に書く → 厳守
可観測性: UI 上に状態/ログを表示、ログファイルにも残す → OK
バージョニング: パッケージ minor 追加 (非破壊) → OK

## プロジェクト構造
```
UnityCliBridge/Packages/unity-cli-bridge/
  Editor/McpServerWindow.cs         # Start/Stop, toggles
  Editor/MenuItems.cs               # メニュー登録
  Editor/SampleWorkflows.cs         # シーン/Addressables サンプル
  Tests/Editor/ServerWindowTests.cs # UI/副作用テスト
  Tests/Editor/SampleWorkflowsTests.cs
specs/SPEC-4ebff657/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/gui-behavior.md
  tasks.md
```

## Phase 0: リサーチ (research.md)
- EditorWindow から外部プロセス (npx) を起動する安全な方法（ProcessStartInfo、パス解決）。
- Play Mode 中のメニュー無効化方法。
- Addressables をテスト用に一時グループへ登録し、後処理でクリーンに戻す手順。

## Phase 1: 設計 & 契約
- data-model: StartupSettings, SampleWorkflowResult。
- contracts: GUI 状態遷移、ボタンクリック時の期待結果、サンプルの副作用範囲。
- quickstart: メニュー操作手順とサンプル実行手順。

## Phase 2: タスク計画アプローチ
tasks.md で TDD 順に整理（次フェーズ）。
