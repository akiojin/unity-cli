# タスク: Unity メニュー起動とサンプル (SPEC-4ebff657)

**入力**: plan.md, research.md, data-model.md, contracts/
**前提条件**: Unity 2020.3+ 環境, Addressables 有効 (サンプル用)

## Phase 3.1: セットアップ
- [ ] T201 UnityCliBridge/Packages/unity-cli-bridge/Editor/ に Editor 用フォルダ/asmdef 追加
- [ ] T202 [P] Addressables テスト用一時グループの作成/削除ユーティリティ雛形

## Phase 3.2: テストファースト
- [ ] T203 Tests/Editor/ServerWindowTests.cs に Start/Stop UI の edittime テスト（Play 中無効化含む）
- [ ] T204 [P] Tests/Editor/SampleWorkflowsTests.cs に Scene/Addressables サンプルの副作用テスト（生成→クリーンアップ）

## Phase 3.3: コア実装
- [ ] T205 Editor/McpServerWindow.cs 実装（Start/Stop, 状態表示, toggles）
- [ ] T206 Editor/MenuItems.cs でメニュー登録と Play 中無効化
- [ ] T207 Editor/SampleWorkflows.cs 実装（Scene/Addressables サンプル）

## Phase 3.4: 統合
- [ ] T208 外部プロセス起動/停止連携（npx CLI 呼び出し、ログストリーム）
- [ ] T209 Telemetry/HTTP 設定をウィンドウ設定から CLI に反映

## Phase 3.5: 仕上げ
- [ ] T210 [P] README/quickstart に GUI 手順とサンプル手順を追記
- [ ] T211 [P] 手動スモーク: Start/Stop 3 回、サンプル 2 種実行で成功ログを記録

## 依存関係
- T203,T204 → T205,T207
- T205 → T208,T209

## 並列実行候補
- [P] T202 と T203/T204 並列可
- [P] T210,T211 は実装後並列可
