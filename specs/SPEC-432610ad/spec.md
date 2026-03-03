# SPEC-432610ad: Docker(Linux) → Windows Unity ホストでキャプチャ出力パス不整合の修正

## Status

- **Phase**: Implementation
- **Issue**: #54
- **Branch**: `bugfix/issue-54`

## Problem

Linux Docker 上の `unity-cli` から Windows ホスト上の Unity を操作した場合、`capture_screenshot` / `capture_video_start` が成功レスポンスを返すが、返却パスが不整合でファイルにアクセスできない。

### 根本原因

1. **異なる OS のパスをそのまま使用**: Linux 側から送られる `workspaceRoot`（例: `/151-Zuri.Client/...`）を Windows 側の `Path.Combine` に渡すと壊れたパスになる（Windows では `/path` がドライブルート相対として解釈される）
2. **返却パスのセパレータ不統一**: Windows の `Path.Combine` が `\` を生成し、JSON で `\\` にエスケープされてクライアント側で不整合になる

### 証拠（Issue レポートより）

- 返却パス: `/151-Zuri.Client/feature/ccs/ab_view_swap\\.unity\\captures\\image_game_...`
- Unity Console: `D:\Repository\...\xixi-client\..\..\..\..\..\..\151-Zuri.Client\...\captures\video_...`

## Solution

C# ハンドラ側のみ修正。Rust CLI 側は変更不要。

### 修正パターン（3ファイル共通）

1. **`IsLocalPath` ガード**: `workspaceRoot` パラメータが現在の OS にとって無効なパスの場合、ローカル自動解決にフォールバック
2. **返却パスの正規化**: `outputPath` 構築後に `.Replace('\\', '/')` で統一
3. **`ResolveWorkspaceRoot` のセパレータ正規化**: `UnityCliBridgeHost` 版に合わせ `.Replace('\\', '/')` を追加

### 対象ファイル

| ファイル | IsLocalPath | outputPath正規化 | ResolveWorkspaceRoot正規化 |
|---|---|---|---|
| `ScreenshotHandler.cs` | Yes | Yes | Yes |
| `VideoCaptureHandler.cs` | Yes | Yes | Yes |
| `ProfilerHandler.cs` | N/A (workspaceRoot パラメータなし) | Yes | Yes |

## Non-Goals

- Rust CLI 側の変更
- パス変換ロジックの共通ユーティリティ化（既存パターンに従い各ハンドラに配置）

## Verification

- 同一 OS: `workspaceRoot` が有効なローカルパスの場合、従来通り使用される
- クロス OS: Linux パスが Windows で `IsLocalPath` に弾かれ、フォールバック
- 返却パスが常にフォワードスラッシュで統一
- `encodeAsBase64: true` の動作に影響なし
