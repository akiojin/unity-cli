# tasks/todo.md

このファイルは、非自明タスクの計画・進捗・検証結果を記録する作業ノート。

## Current Task

- Title: Issue #54 — Docker(Linux) → Windows Unity ホストでキャプチャ出力パス不整合の修正
- Request Date: 2026-03-02
- Owner: Claude
- Scope: `ScreenshotHandler.cs`, `VideoCaptureHandler.cs`, `ProfilerHandler.cs` のパス正規化修正
- Spec: `specs/SPEC-432610ad/`

## Plan

- [x] Step 1: 3ファイルの現状把握と `UnityCliBridgeHost.ResolveWorkspaceRoot` リファレンス確認
- [x] Step 2: `ScreenshotHandler.cs` — `IsLocalPath` + `outputPath` 正規化 + `ResolveWorkspaceRoot` 修正
- [x] Step 3: `VideoCaptureHandler.cs` — `IsLocalPath` + `s_OutputPath` 正規化 + `ResolveWorkspaceRoot` 修正
- [x] Step 4: `ProfilerHandler.cs` — `s_OutputPath` 正規化 + `ResolveWorkspaceRoot` 修正
- [x] Step 5: `cargo fmt / clippy / test` — Rust 側影響なし確認

## Verification

- [x] `cargo fmt --all -- --check` — pass
- [x] `cargo clippy --all-targets -- -D warnings` — pass
- [x] `cargo test --all-targets` — 132 tests pass
- [ ] C# コンパイル確認（Unity Editor）— オフライン環境のため未実施

## Review

- Summary: Linux→Windows クロスプラットフォームでの `workspaceRoot` パス不整合を3ハンドラで修正。`IsLocalPath` ガードで異OS パスをフォールバック、返却パスをフォワードスラッシュに統一。
- Risks: `IsLocalPath` の判定が edge case で誤判定する可能性（UNC パスは通す設計で対応済）。
- Follow-ups: C# コンパイル確認は Unity Editor 接続時に実施。

## History

- 2026-02-27: CLAUDE.md 運用強化 / `tasks/*.md` 作成と参照追記を完了
- 2026-03-02: Issue #54 パス不整合修正 — 3ファイル修正完了、Rust 品質ゲート全通過
