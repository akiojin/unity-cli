# 機能仕様書: Input System統合機能

**機能ID**: `SPEC-0d5d84f9`
**作成日**: 2025-10-17
**ステータス**: 完了
**入力**: Unity Input Systemを使用したキーボード、マウス、ゲームパッド、タッチ入力のシミュレーション、Input Actionsアセットの管理

## 実行フロー (main)
```
1. 入力から入力操作要件を解析
   → 操作タイプ（キーボード/マウス/ゲームパッド/タッチ/シーケンス/状態取得）の特定
2. デバイス固有のパラメータ検証
   → キーボード: キー名、アクション（press/release/type/combo）
   → マウス: ボタン、座標、スクロール
   → ゲームパッド: ボタン、スティック、トリガー、方向パッド
   → タッチ: タップ、スワイプ、ピンチ、マルチタッチ
3. Input Systemコマンド生成
   → 適切なUnity Input Systemコマンドに変換
4. 入力シミュレーション実行
   → Unityエディタまたはランタイムで入力イベントを発火
5. Input Actionsアセット管理（オプション）
   → Action Map、Action、Binding、Control Schemeの作成/削除/変更
6. 戻り値: SUCCESS（入力実行結果）
```

---

## ⚡ クイックガイドライン
- ✅ 自動テストとAIエージェントによる入力シミュレーションの必要性に焦点
- ❌ 内部的なInput Systemの実装やイベント処理の詳細は避ける
- 👥 ゲーム開発者・QAエンジニア・AIエージェント向けに記述

---

## ユーザーシナリオ＆テスト

### 主要ユーザーストーリー
開発者として、ゲームの入力処理をテストするために、キーボード、マウス、ゲームパッド、タッチ入力をプログラムでシミュレートしたい。AIエージェントとして、ユーザーの指示に従ってゲーム内のキャラクターを操作したい。

### 受け入れシナリオ
1. **前提** ゲームがキーボード入力を受け付ける、**実行** スペースキーのプレスをシミュレート、**結果** ジャンプアクションが実行される
2. **前提** 文字入力フィールドが存在する、**実行** テキストをタイプ入力、**結果** 指定した文字列が入力される
3. **前提** ゲームがマウス入力を受け付ける、**実行** 左クリックをシミュレート、**結果** クリックイベントが発火
4. **前提** ゲームがゲームパッドに対応している、**実行** アナログスティックの入力をシミュレート、**結果** キャラクターが移動する
5. **前提** モバイルゲームのテスト中、**実行** スワイプジェスチャーをシミュレート、**結果** スワイプアクションが認識される
6. **前提** 複雑な入力シーケンスが必要、**実行** 複数の入力を順次実行するシーケンスを定義、**結果** 各入力が順番に実行される
7. **前提** Input Actionsアセットを作成したい、**実行** Action Mapとアクションを定義、**結果** アセットファイルが作成される
8. **前提** キーバインドを追加したい、**実行** 既存のActionにBindingを追加、**結果** バインディングが設定される

### エッジケース
- Input Systemパッケージがインストールされていない場合、どう処理するか? → エラーメッセージとインストール手順を返す
- 無効なキー名を指定した場合、どう処理するか? → エラーメッセージと有効なキー名のリストを返す
- マウス座標が画面外の場合、どう処理するか? → 座標を画面内にクランプし、警告を返す
- ゲームパッドが接続されていない場合、シミュレーションは実行されるか? → 仮想デバイスとして実行され、警告を返す
- Input Actionsアセットが存在しない場合、どう処理するか? → エラーメッセージとアセット作成の提案を返す

## 要件

### 機能要件
- **FR-001**: システムはキーボード入力のpress、release、type、comboアクションをサポートする必要がある
- **FR-002**: システムはタイピング速度（ミリ秒/文字）を指定できる必要がある
- **FR-003**: システムはマウスの移動、クリック、ドラッグ、スクロールをサポートする必要がある
- **FR-004**: システムはマウス座標を絶対座標または相対座標で指定できる必要がある
- **FR-005**: システムはゲームパッドのボタン、アナログスティック、トリガー、方向パッドをサポートする必要がある
- **FR-006**: システムはアナログスティックとトリガーの値を-1から1の範囲で指定できる必要がある
- **FR-007**: システムはタッチ入力のタップ、スワイプ、ピンチ、マルチタッチをサポートする必要がある
- **FR-008**: システムはタッチIDを0-9の範囲で指定できる必要がある
- **FR-009**: システムは複数の入力を順次実行するシーケンス機能をサポートする必要がある
- **FR-010**: システムは現在の入力状態を取得できる必要がある
- **FR-011**: ユーザーはInput ActionsアセットにAction Mapを作成できる必要がある
- **FR-012**: ユーザーはAction Mapにアクション（Button/Value/PassThrough）を追加できる必要がある
- **FR-013**: ユーザーはアクションにバインディングを追加できる必要がある
- **FR-014**: システムは2D Vector、1D Axis等のコンポジットバインディングをサポートする必要がある
- **FR-015**: ユーザーはControl Schemeを管理（追加/削除/変更）できる必要がある

### 非機能要件
- **NFR-001**: 入力シミュレーションは50ms以内に実行される必要がある
- **NFR-002**: タイピング入力は指定した速度で正確に実行される必要がある
- **NFR-003**: Input Actionsアセット操作は1秒以内に完了する必要がある
- **NFR-004**: シーケンス実行は各入力の合計時間+遅延時間以内に完了する必要がある
- **NFR-005**: 入力状態取得は100ms以内に完了する必要がある

### 主要エンティティ
- **KeyboardInput**: アクション（press/release/type/combo）、キー名、テキスト、タイピング速度を含むキーボード入力情報
- **MouseInput**: アクション（move/click/drag/scroll）、ボタン、座標、絶対/相対フラグを含むマウス入力情報
- **GamepadInput**: アクション（button/stick/trigger/dpad）、ボタン名、スティック値、トリガー値を含むゲームパッド入力情報
- **TouchInput**: アクション（tap/swipe/pinch/multi）、タッチID、座標、持続時間を含むタッチ入力情報
- **InputActionsAsset**: Action Map、アクション、バインディング、Control Schemeを含むアセット情報
- **InputSequence**: 複数の入力操作、操作間の遅延、検証設定を含むシーケンス情報

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
- `unity-cli/src/handlers/input/InputSystemControlToolHandler.js`
- `unity-cli/src/handlers/input/InputKeyboardToolHandler.js`
- `unity-cli/src/handlers/input/InputMouseToolHandler.js`
- `unity-cli/src/handlers/input/InputGamepadToolHandler.js`
- `unity-cli/src/handlers/input/InputTouchToolHandler.js`
- `unity-cli/src/handlers/input/InputActionMapCreateToolHandler.js`
- `unity-cli/src/handlers/input/InputActionAddToolHandler.js`
- `unity-cli/src/handlers/input/InputBindingAddToolHandler.js`
- `unity-cli/src/handlers/input/InputBindingCompositeCreateToolHandler.js`
- `unity-cli/src/handlers/input/InputControlSchemesManageToolHandler.js`
- `UnityCliBridge/Packages/unity-cli-bridge/Editor/Handlers/InputSystemHandler.cs`

### 技術詳細
- Unity Input System（com.unity.inputsystem）パッケージを使用
- InputAction、InputActionMap、InputBindingの管理
- 仮想デバイスによる入力シミュレーション
- JSON形式でのInput Actionsアセット操作
