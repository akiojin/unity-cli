# Plan: SPEC-432610ad

## Overview

3つの C# ハンドラファイルに対して、クロスプラットフォームパス不整合を修正する最小限の変更を適用する。

## Steps

### 1. ScreenshotHandler.cs

- `IsLocalPath` ヘルパーメソッドを追加
- `workspaceRoot` パラメータ取得時に `IsLocalPath` チェックを追加
- `outputPath` 構築後に `.Replace('\\', '/')` を追加
- `ResolveWorkspaceRoot` の返却値に `.Replace('\\', '/')` を追加

### 2. VideoCaptureHandler.cs

- `IsLocalPath` ヘルパーメソッドを追加
- `workspaceRoot` パラメータ取得時に `IsLocalPath` チェックを追加
- `s_OutputPath` 構築後に `.Replace('\\', '/')` を追加
- `ResolveWorkspaceRoot` の返却値に `.Replace('\\', '/')` を追加

### 3. ProfilerHandler.cs

- `s_OutputPath` 構築後に `.Replace('\\', '/')` を追加
- `ResolveWorkspaceRoot` の返却値に `.Replace('\\', '/')` を追加

### 4. 検証

- `cargo fmt --all -- --check` / `cargo clippy` / `cargo test` で Rust 側に影響がないことを確認

## Risks

- `IsLocalPath` の判定が不完全な場合、正当なパスを誤って拒否する可能性
  - 対策: UNC パス (`//server/share`) は通す設計、Windows ドライブレター付きパスは Unix 側で拒否
