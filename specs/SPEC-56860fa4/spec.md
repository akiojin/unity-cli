# 機能仕様書: UI自動化・操作機能

**機能ID**: `SPEC-56860fa4`
**作成日**: 2025-10-17
**ステータス**: 完了
**入力**: UnityのUI要素（uGUI / UI Toolkit / IMGUI）の検索、クリック、値設定、状態取得、複雑な入力シーケンス実行

## 対応UIシステム（対象範囲）
- **uGUI**（UnityEngine.UI / Canvas）: 既存の階層パス（`/Canvas/...`）で操作
- **UI Toolkit**（UnityEngine.UIElements / UIDocument）: `UIDocument` 配下の `VisualElement` を操作（要素名 `name` を前提）
- **IMGUI**（OnGUI）: 即時モードUIは**階層探索できないため**、制御対象をレジストリ登録して操作（後述）

## UI要素の識別（elementPath 仕様）
本機能は「`elementPath` 文字列」を共通キーとしてUI要素を指定する。

- **uGUI**: `"/Canvas/Panel/Button"` のような**GameObject階層パス**
- **UI Toolkit**: `uitk:<UIDocumentのGameObjectパス>#<VisualElement名>`
  - 例: `uitk:/UITK/UIDocument#UITK_Button`
- **IMGUI**: `imgui:<controlId>`
  - 例: `imgui:IMGUI/Button`
  - `controlId` は実装側で登録するID（OnGUIから登録される前提）

## 検索フィルタ（UI横断）
`find_ui_elements` のフィルタ指定は、UIシステムごとに意味が一部異なる。

- **elementType**: uGUIはComponent型名 / UI ToolkitはVisualElement型名 / IMGUIは登録時controlType
- **tagFilter**: uGUIはGameObjectタグ / UI ToolkitはUSSクラス（class） / IMGUIは未対応
- **canvasFilter**: uGUIはCanvas名 / UI ToolkitはUIDocument GameObject名（互換用途）
- **uiSystem**: `ugui|uitk|imgui|all`（省略時は all）

## UIシステム別の制約（現状）
- **UI Toolkit / IMGUI**: `holdDuration` と `position` は無視（警告で返却）
- **UI Toolkit**: 右/中クリックは左クリック扱い（警告で返却）

## テスト専用シーン（検証用アセット）
本リポジトリには、uGUI/UI Toolkit/IMGUI の検証用シーンを用意する。

- uGUI: `UnityCliBridge/Assets/Scenes/MCP_UI_UGUI_TestScene.unity`（Edit Mode から配置が確認できる）
- UI Toolkit: `UnityCliBridge/Assets/Scenes/MCP_UI_UITK_TestScene.unity`（UIDocument + UXML、Edit Mode から配置が確認できる）
- IMGUI: `UnityCliBridge/Assets/Scenes/MCP_UI_IMGUI_TestScene.unity`（OnGUI のテストパネル、Edit Mode から配置が確認できる）
- uGUI/UI Toolkit/IMGUI 同居: `UnityCliBridge/Assets/Scenes/MCP_UI_AllSystems_TestScene.unity`
  - 注: シーン上に静的配置はせず、`McpAllUiSystemsTestBootstrap` が **Play Mode** 開始時に uGUI/UI Toolkit/IMGUI を生成する（Edit Mode では見えない）
    - 実装: `UnityCliBridge/Assets/Scripts/McpUiTest/McpAllUiSystemsTestBootstrap.cs`
- MCP経由のE2Eテスト（stdio / tools/call）: `unity-cli/tests/e2e/ui-automation-mcp-protocol.test.js`
- UnityConnection直結のE2Eテスト（ツールハンドラ直呼び）: `unity-cli/tests/e2e/ui-automation-scenes.test.js`
- 代表的な要素例:
  - uGUI: `/Canvas/UGUI_Panel/UGUI_Button`
  - UI Toolkit: `uitk:/UITK/UIDocument#UITK_Button`
  - IMGUI: `imgui:IMGUI/Button`

## 実行フロー (main)
```
1. 入力からUI操作要件を解析
   → 操作タイプ（検索/クリック/値設定/状態取得/シーケンス実行）の特定
2. UI要素の特定
   → UIシステム判定（uGUI/UI Toolkit/IMGUI）と、システム別の探索
   → コンポーネントタイプ、タグ、名前パターン、（uGUIはCanvas親 / UI ToolkitはUIDocument）でフィルタリング
3. 操作の実行
   → クリック: マウスボタンタイプ、保持時間、位置指定
   → 値設定: 文字列/数値/真偽値/オブジェクト/配列の設定
   → 状態取得: 現在の値、有効状態、インタラクション可能性
   → シーケンス: 複数の操作を順次実行
4. イベントのトリガー
   → UIイベント（OnClick、OnValueChanged等）の発火制御
5. 結果の検証
   → 操作結果の確認と状態の返却
6. 戻り値: SUCCESS（操作結果情報）
```

---

## ⚡ クイックガイドライン
- ✅ 自動テストとAIエージェントによるUI操作の必要性に焦点
- ❌ 内部的なイベントシステムやUnity UIの実装詳細は避ける
- 👥 QAエンジニア・自動化テスター・AIエージェント向けに記述

---

## ユーザーシナリオ＆テスト

### 主要ユーザーストーリー
QAエンジニアとして、手動テストを自動化するために、UnityのUI要素をプログラムで操作したい。AIエージェントとして、ゲームのUIを理解し、ユーザーの指示に従ってUI操作を実行したい。

### 受け入れシナリオ
1. **前提** ボタンがシーンに存在する、**実行** ボタンをコンポーネントタイプで検索、**結果** ボタンのパスリストが返される
2. **前提** 特定のボタンを操作したい、**実行** 要素パスを指定してクリック、**結果** OnClickイベントが発火し、結果が返される
3. **前提** InputFieldに文字列を入力したい、**実行** 要素パスと値を指定、**結果** テキストが設定され、OnValueChangedが発火
4. **前提** Sliderの現在値を確認したい、**実行** 要素パスで状態取得、**結果** 現在の値、範囲、有効状態が返される
5. **前提** 複数のUI操作を順次実行したい、**実行** 操作シーケンスを定義、**結果** 各操作が順次実行され、結果が返される
6. **前提** 非アクティブなUI要素も含めて検索したい、**実行** includeInactiveオプションで検索、**結果** 非アクティブな要素も含まれる
7. **前提** 特定のCanvas内のUI要素のみ検索したい、**実行** canvasFilterを指定、**結果** 指定したCanvas内の要素のみ返される
8. **前提** UI Toolkit のボタンが存在する、**実行** `uitk:` 形式の要素パスでクリック、**結果** clicked相当の結果が反映される
9. **前提** IMGUI の制御対象が登録されている、**実行** `imgui:` 形式の要素パスでクリック/値設定、**結果** 登録されたコールバック/値が反映される

### エッジケース
- UI要素が存在しない場合、どう処理するか? → エラーメッセージと利用可能な要素のヒントを返す
- UI要素が無効化（Interactable = false）されている場合、クリックは実行されるか? → クリックは実行されるが、警告を返す
- 複雑なシーケンス実行中にエラーが発生した場合、どう処理するか? → エラー発生時点で停止し、実行済みの操作と未実行の操作を報告
- 同名のUI要素が複数存在する場合、どれが選択されるか? → 最初に見つかった要素が選択され、複数ある旨を警告
- クリック位置が要素の範囲外の場合、どう処理するか? → 位置を要素の境界内にクランプし、警告を返す
- IMGUI が未登録の場合、どう処理するか? → `imgui:` 要素は見つからない（検索結果0 or not found エラー）

## 要件

### 機能要件
- **FR-001**: システムはUI要素を（uGUI/UI Toolkit/IMGUI）横断で、コンポーネントタイプ、タグ、名前パターンで検索できる必要がある
- **FR-002**: システムは検索時にCanvas親でフィルタリングできる必要がある
- **FR-003**: システムは非アクティブなUI要素の検索をサポートする必要がある
- **FR-004**: ユーザーは要素パスを指定してUI要素をクリックできる必要がある
- **FR-005**: システムは左クリック、右クリック、中クリックをサポートする必要がある
- **FR-006**: システムはクリック保持時間（0-10000ms）を指定できる必要がある
- **FR-007**: システムはクリック位置を要素内の相対座標（0-1）で指定できる必要がある
- **FR-008**: ユーザーはUI要素の値を設定できる必要がある（文字列、数値、真偽値、オブジェクト、配列、null）
- **FR-009**: システムは値設定時にイベントトリガーの有効/無効を制御できる必要がある
- **FR-010**: ユーザーはUI要素の現在の状態を取得できる必要がある
- **FR-011**: システムは状態取得時に子要素を含めるオプションをサポートする必要がある
- **FR-012**: システムはインタラクション可能性（Interactable状態）の情報を含める必要がある
- **FR-013**: ユーザーは複数のUI操作を含むシーケンスを定義できる必要がある
- **FR-014**: システムは操作間の待機時間を指定できる必要がある
- **FR-015**: システムはシーケンス実行中に各操作の状態を検証できる必要がある
- **FR-016**: システムは `elementPath` からUIシステム（uGUI/UI Toolkit/IMGUI）を判定し、適切な処理系にルーティングできる必要がある
- **FR-017**: システムは UI Toolkit の `UIDocument` 配下 `VisualElement` を `name` ベースで検索/状態取得/値設定できる必要がある
- **FR-018**: システムは IMGUI をレジストリ登録（controlId）経由で検索/状態取得/操作できる必要がある

### 非機能要件
- **NFR-001**: UI要素の検索は1秒以内に完了する必要がある
- **NFR-002**: クリック操作は200ms以内に実行される必要がある
- **NFR-003**: 値設定操作は200ms以内に実行される必要がある
- **NFR-004**: 状態取得操作は500ms以内に完了する必要がある
- **NFR-005**: シーケンス実行は各操作の合計時間+待機時間以内に完了する必要がある

### 主要エンティティ
- **UIElement**: 要素パス、コンポーネントタイプ、タグ、アクティブ状態を含むUI要素情報
- **ClickOperation**: 要素パス、クリックタイプ、保持時間、位置を含むクリック操作情報
- **ValueOperation**: 要素パス、設定値、イベントトリガー有効/無効を含む値設定操作情報
- **UIElementState**: 現在の値、有効状態、インタラクション可能性、子要素情報を含む状態情報
- **InputSequence**: 複数の操作、操作間の待機時間、状態検証設定を含むシーケンス情報

---

## レビュー＆受け入れチェックリスト

### コンテンツ品質
- [x] 実装詳細なし（言語、フレームワーク、API）
- [x] ユーザー価値とビジネスニーズに焦点
- [x] 非技術関係者向けに記述
- [x] すべての必須セクション完成

### 要件完全性
- [x] [要明確化]マーカーが残っていない
- [x] 要件はテスト可能で曖昧さがない
- [x] 成功基準は測定可能
- [x] スコープが明確に境界付けられている
- [x] 依存関係と前提条件が識別されている

---

## 実行ステータス

- [x] ユーザー説明を解析済み
- [x] 主要概念を抽出済み
- [x] 曖昧さをマーク済み
- [x] ユーザーシナリオを定義済み
- [x] 要件を生成済み
- [x] エンティティを識別済み
- [x] レビューチェックリスト合格

---

## 参考実装

### 実装ファイル
- `unity-cli/src/handlers/ui/UIFindElementsToolHandler.js`
- `unity-cli/src/handlers/ui/UIClickElementToolHandler.js`
- `unity-cli/src/handlers/ui/UISetElementValueToolHandler.js`
- `unity-cli/src/handlers/ui/UIGetElementStateToolHandler.js`
- `unity-cli/src/handlers/ui/UISimulateInputToolHandler.js`
- `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/UIInteractionHandler.cs`

### 技術詳細
- Unity UI（uGUI）コンポーネントの操作
- UnityEngine.EventSystemsを使用したイベント発火
- 階層パスによる要素の一意識別
- インタラクション可能性（Interactable）の検証
