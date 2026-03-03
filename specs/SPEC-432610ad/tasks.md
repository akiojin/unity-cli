# Tasks: SPEC-432610ad

## Implementation

- [x] T1: ScreenshotHandler.cs に IsLocalPath + outputPath 正規化 + ResolveWorkspaceRoot 修正
- [x] T2: VideoCaptureHandler.cs に IsLocalPath + s_OutputPath 正規化 + ResolveWorkspaceRoot 修正
- [x] T3: ProfilerHandler.cs に s_OutputPath 正規化 + ResolveWorkspaceRoot 修正

## Verification

- [x] T4: cargo fmt / clippy / test で Rust 側影響なしを確認
- [x] T5: tasks/todo.md 更新
