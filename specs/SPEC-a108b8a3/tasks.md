# タスク: Unity Addressablesコマンドサポート

**入力**: `/specs/SPEC-a108b8a3/`の設計ドキュメント
**前提条件**: plan.md (✅), research.md (✅), data-model.md (✅), contracts/ (✅), quickstart.md (✅)

## Phase 3.1: セットアップ

- [ ] **T001** [P] Addressablesパッケージインストール確認とテスト環境セットアップ
  - ファイル: なし（確認作業）
  - Unity Editorで `com.unity.addressables v2.7.4` がインストールされていることを確認
  - UnityCliBridgeプロジェクトの `Packages/manifest.json` に依存関係が記載されていることを確認
  - UnityCliBridgeプロジェクトを開いてAddressables Windowが表示されることを確認

- [ ] **T002** [P] Integration testsディレクトリ構造作成
  - ファイル: `tests/integration/addressables/` (新規ディレクトリ)
  - `tests/integration/addressables/` ディレクトリを作成
  - `.gitkeep` または README.md を配置

- [ ] **T003** [P] Node側ハンドラーディレクトリ構造作成
  - ファイル: `unity-cli/src/handlers/addressables/` (新規ディレクトリ)
  - `unity-cli/src/handlers/addressables/` ディレクトリを作成

## Phase 3.2: テストファースト (TDD) ⚠️ 3.3の前に完了必須

**重要: これらのテストは記述され、実装前に失敗する必要がある (RED)**

- [ ] **T004** [P] `tests/integration/addressables/addressables-manage.test.js` に addressables_manage コマンドの contract test
  - ファイル: `tests/integration/addressables/addressables-manage.test.js` (新規)
  - 10個のアクションをテスト: add_entry, remove_entry, set_address, add_label, remove_label, list_entries, list_groups, create_group, remove_group, move_entry
  - 各アクションで成功ケースとエラーケース（存在しないアセットパス等）をテスト
  - テスト実行 → **FAIL** (実装がないため)

- [ ] **T005** [P] `tests/integration/addressables/addressables-build.test.js` に addressables_build コマンドの contract test
  - ファイル: `tests/integration/addressables/addressables-build.test.js` (新規)
  - 2個のアクションをテスト: build, clean_build
  - ビルド成功ケース、ビルドキャッシュクリアをテスト
  - テスト実行 → **FAIL** (実装がないため)

- [ ] **T006** [P] `tests/integration/addressables/addressables-analyze.test.js` に addressables_analyze コマンドの contract test
  - ファイル: `tests/integration/addressables/addressables-analyze.test.js` (新規)
  - 3個のアクションをテスト: analyze_duplicates, analyze_dependencies, analyze_unused
  - 重複検出、依存関係解析、未使用アセット検出をテスト
  - テスト実行 → **FAIL** (実装がないため)

- [ ] **T007** すべてのContract testsが失敗することを確認 (RED確認)
  - ファイル: なし（確認作業）
  - `npm test tests/integration/addressables/` を実行
  - すべてのテストが FAIL することを確認
  - 失敗ログをスクリーンショット/記録

## Phase 3.3: Unity側実装 (テストが失敗した後のみ)

- [ ] **T008** `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` を作成（スケルトン）
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (新規)
  - `using UnityEditor.AddressableAssets;` 追加
  - `public static class AddressablesHandler` クラス定義
  - `public static object HandleCommand(string action, JObject parameters)` メソッド定義
  - switchステートメントで全actionを列挙（未実装）

- [ ] **T009** AddressablesHandler: add_entry, remove_entry, set_address 実装
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - `AddressableAssetSettings.GetDefault()` で設定取得
  - `CreateOrMoveEntry()` でエントリ追加
  - `RemoveAssetEntry()` でエントリ削除
  - `SetAddress()` でアドレス名変更
  - エラーハンドリング（存在しないアセットパス等）

- [ ] **T010** AddressablesHandler: add_label, remove_label, list_entries 実装
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - `SetLabel()` でラベル追加/削除
  - 全エントリを列挙してJSON配列で返却
  - ページング実装 (pageSize, offset)

- [ ] **T011** AddressablesHandler: list_groups, create_group, remove_group, move_entry 実装
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - `CreateGroup()` でグループ作成
  - `RemoveGroup()` でグループ削除（空グループのみ）
  - `MoveEntry()` でエントリを別グループに移動
  - グループ一覧を列挙してJSON配列で返却

- [ ] **T012** AddressablesHandler: build, clean_build 実装
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - `AddressableAssetSettings.BuildPlayerContent()` でビルド実行
  - `AddressableAssetSettings.CleanPlayerContent()` でキャッシュクリア
  - ビルド結果（success, duration, outputPath, errors）を返却

- [ ] **T013** AddressablesHandler: analyze_duplicates, analyze_dependencies, analyze_unused 実装
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - 重複アセット検出ロジック実装
  - 依存関係解析（AssetDatabase.GetDependencies使用）
  - 未使用アセット検出ロジック実装

- [ ] **T014** Unity側でUnityCliBridgeにAddressablesHandlerをルーティング
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Core/UnityCliBridge.cs` (更新)
  - `addressables_manage`, `addressables_build`, `addressables_analyze` コマンドを AddressablesHandler にルーティング
  - switchステートメントまたはディスパッチャーに追加

## Phase 3.4: Node側実装 (Unity側実装完了後)

- [ ] **T015** [P] `unity-cli/src/handlers/addressables/AddressablesManageToolHandler.js` 実装
  - ファイル: `unity-cli/src/handlers/addressables/AddressablesManageToolHandler.js` (新規)
  - BaseToolHandlerを継承
  - ツール名: `addressables_manage`
  - JSON Schemaでリクエストパラメータを定義（contracts/addressables-manage.jsonに準拠）
  - validate()メソッドでパラメータ検証
  - execute()メソッドでUnity接続経由でコマンド送信
  - formatResponse()メソッドでレスポンス整形

- [ ] **T016** [P] `unity-cli/src/handlers/addressables/AddressablesBuildToolHandler.js` 実装
  - ファイル: `unity-cli/src/handlers/addressables/AddressablesBuildToolHandler.js` (新規)
  - BaseToolHandlerを継承
  - ツール名: `addressables_build`
  - JSON Schemaでリクエストパラメータを定義（contracts/addressables-build.jsonに準拠）
  - タイムアウトを5分に設定（ビルドは時間がかかる）
  - validate(), execute(), formatResponse() 実装

- [ ] **T017** [P] `unity-cli/src/handlers/addressables/AddressablesAnalyzeToolHandler.js` 実装
  - ファイル: `unity-cli/src/handlers/addressables/AddressablesAnalyzeToolHandler.js` (新規)
  - BaseToolHandlerを継承
  - ツール名: `addressables_analyze`
  - JSON Schemaでリクエストパラメータを定義（contracts/addressables-analyze.jsonに準拠）
  - validate(), execute(), formatResponse() 実装

- [ ] **T018** `unity-cli/src/handlers/index.js` で新規ハンドラーをエクスポート
  - ファイル: `unity-cli/src/handlers/index.js` (更新)
  - AddressablesManageToolHandler, AddressablesBuildToolHandler, AddressablesAnalyzeToolHandler をimport
  - exportリストに追加

- [ ] **T019** MCPサーバーで新規ツールを登録
  - ファイル: `unity-cli/src/core/server.js` または該当する登録箇所 (更新)
  - 新規ハンドラーをツールリストに登録
  - MCPサーバー再起動で3つの新規ツールが利用可能になることを確認

## Phase 3.5: Integration Tests実行 (GREEN確認)

- [ ] **T020** すべてのContract testsが合格することを確認 (GREEN確認)
  - ファイル: なし（確認作業）
  - Unity Editorが起動していることを確認
  - MCPサーバーが起動していることを確認
  - `npm test tests/integration/addressables/` を実行
  - すべてのテストが PASS することを確認
  - 1つでも失敗した場合は実装に戻る

- [ ] **T021** quickstart.mdのP1シナリオ（アセット登録管理）を手動実行
  - ファイル: `specs/SPEC-a108b8a3/quickstart.md` (参照)
  - プレハブをAddressableとして登録
  - アドレス名変更
  - ラベル追加
  - 一覧取得で確認

- [ ] **T022** quickstart.mdのP2シナリオ（グループ管理）を手動実行
  - ファイル: `specs/SPEC-a108b8a3/quickstart.md` (参照)
  - 新規グループ作成
  - アセット移動
  - グループ一覧確認
  - グループ削除

- [ ] **T023** quickstart.mdのP3シナリオ（ビルド自動化）を手動実行
  - ファイル: `specs/SPEC-a108b8a3/quickstart.md` (参照)
  - Addressablesビルド実行
  - 成功確認
  - キャッシュクリア

- [ ] **T024** quickstart.mdのP4シナリオ（依存関係分析）を手動実行
  - ファイル: `specs/SPEC-a108b8a3/quickstart.md` (参照)
  - 重複アセット作成
  - 分析実行
  - 重複検出確認

## Phase 3.6: 仕上げ

- [ ] **T025** [P] エラーハンドリング強化とLLM最適化
  - ファイル: `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/AddressablesHandler.cs` (更新)
  - すべてのエラーケースで `{error, message, solution, context}` 形式で返却
  - エラーメッセージを日本語化
  - 推奨解決策を明示

- [ ] **T026** [P] ページング機能の検証とトークン最適化
  - ファイル: `unity-cli/src/handlers/addressables/*.js` (更新)
  - list_entries, list_groups, analyze_duplicates, analyze_unused のページングが正しく動作することを確認
  - デフォルトpageSize=20を確認
  - 大規模プロジェクト（100+アセット）でテスト

- [ ] **T027** [P] 構造化ロギング追加
  - ファイル: `unity-cli/src/handlers/addressables/*.js` (更新)
  - Node側で logger を使用してコマンド実行ログを記録
  - Unity側で Debug.Log を使用してAddressables操作ログを記録
  - エラー時のスタックトレース記録

- [ ] **T028** [P] README.md更新（新規ツール追加の記載）
  - ファイル: `README.md` (更新)
  - "Available Tools" セクションに addressables_manage, addressables_build, addressables_analyze を追加
  - 簡単な使用例を記載

- [ ] **T029** [P] README.ja.md更新（新規ツール追加の記載）
  - ファイル: `README.ja.md` (更新)
  - "利用可能なツール" セクションに addressables_manage, addressables_build, addressables_analyze を追加
  - 簡単な使用例を記載（日本語）

- [ ] **T030** リファクタリング: 重複コード削除とクリーンアップ
  - ファイル: すべての実装ファイル (更新)
  - DRY原則に従って重複コードを削除
  - コメント追加
  - 不要なデバッグコード削除

- [ ] **T031** パフォーマンス検証
  - ファイル: なし（検証作業）
  - アセット登録: <5秒を確認
  - グループ一覧取得(100個): <3秒を確認
  - 依存関係分析(1000個): <30秒を確認
  - 不合格の場合は最適化を実施

## 依存関係

- **Setup (T001-T003)** が **Tests (T004-T007)** をブロック
- **Tests (T004-T007)** が **Implementation (T008-T019)** より先（TDD必須）
- **T007 (RED確認)** が **T008 (実装開始)** をブロック
- **Unity側実装 (T008-T014)** が **Node側実装 (T015-T019)** をブロック
- **Implementation (T008-T019)** が **Integration Tests実行 (T020-T024)** より先
- **T020 (GREEN確認)** が **Polish (T025-T031)** をブロック

## 並列実行例

### Phase 3.1: Setup (すべて並列実行可能)
```
T001, T002, T003
```

### Phase 3.2: Tests (すべて並列実行可能)
```
T004, T005, T006
```

### Phase 3.4: Node側実装 (すべて並列実行可能)
```
T015, T016, T017
```

### Phase 3.6: Polish (一部並列実行可能)
```
T025, T026, T027 (並列)
T028, T029 (並列)
```

## 注意事項

- **[P]** タスク = 異なるファイル、依存関係なし、並列実行可能
- **TDD厳守**: T007でREDを確認してからT008以降の実装を開始
- **T020でGREEN確認**: すべてのテストが合格するまで実装を繰り返す
- 各タスク完了後に日本語でコミット（Conventional Commits準拠）
- エラーが発生している状態で完了としない

## タスク完全性検証

- [x] すべてのcontracts (3ファイル) に対応するテストがある → T004, T005, T006
- [x] すべてのactions (15個) にUnity側実装がある → T009-T013
- [x] すべてのhandlers (3個) にNode側実装がある → T015-T017
- [x] すべてのテストが実装より先にある → T004-T007 → T008-T019
- [x] 並列タスクは本当に独立している → T001-T003, T004-T006, T015-T017等
- [x] 各タスクは正確なファイルパスを指定 → すべてのタスクにファイルパス記載
- [x] 同じファイルを変更する[P]タスクがない → 確認済み
