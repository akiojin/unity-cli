# 機能仕様書: GameObject/Scene管理機能

**機能ID**: `SPEC-3c9871b3`
**作成日**: 2025-10-17
**ステータス**: 完了
**入力**: ユーザー説明: "GameObject/Scene管理機能"

## 実行フロー (main)
```
1. 入力からユーザー説明を解析
   → 完了: "GameObject/Scene管理機能"
2. 説明から主要概念を抽出
   → アクター: Unity開発者、LLMユーザー
   → アクション: GameObject作成/変更/削除/検索、階層取得、Scene管理
   → データ: GameObject属性、Transform、階層構造、Scene情報
   → 制約: メインスレッド動作、大規模シーンでの性能
3. 不明確な側面: なし（既存実装のSpec化）
4. ユーザーシナリオ＆テストセクションを記入
   → 6つの主要ユーザーストーリー定義済み
5. 機能要件を生成
   → 30個の機能要件をテスト可能な形式で定義
6. 主要エンティティを識別
   → GameObject、Transform、Scene、階層パス
7. レビューチェックリストを実行
   → 技術詳細は参考実装セクションに分離
   → 要件はテスト可能
8. 戻り値: SUCCESS (計画準備完了)
```

---

## ⚡ クイックガイドライン
- ✅ ユーザーが「何を」必要とし「なぜ」必要なのかに焦点を当てる
- ❌ 「どのように」実装するかを避ける (技術スタック、API、コード構造なし)
- 👥 ビジネス関係者向けに記述 (開発者向けではない)

---

## 機能概要

Unity Editor内のGameObjectとSceneを作成・変更・削除・検索・階層取得する機能。LLMがUnityシーンを直接操作し、ゲームオブジェクトの配置、構造変更、シーン管理を自動化する。

### ビジネス価値

- **シーン構築の自動化**: LLMによるプロシージャルなシーン生成、反復的なオブジェクト配置作業の削減
- **シーン管理の効率化**: シーンの読み込み・保存・切り替えを自動化し、マルチシーンワークフローを改善
- **検証とデバッグ**: 階層構造の可視化、オブジェクト検索により、複雑なシーンの理解を支援

---

## ユーザーシナリオ＆テスト

### 主要ユーザーストーリー

#### US-1: GameObjectの作成

**AS** Unity開発者
**I WANT** LLMコマンドでGameObjectを作成したい
**SO THAT** 手動でHierarchyウィンドウを操作する必要がない

**受け入れ基準**:
- プリミティブ（Cube/Sphere/Cylinder等）を作成できる
- 空のGameObjectを作成できる
- 親子関係を指定してGameObjectを作成できる
- Position/Rotation/Scaleを指定してGameObjectを作成できる
- Tag/Layerを指定してGameObjectを作成できる

#### US-2: GameObjectの変更

**AS** Unity開発者
**I WANT** 既存のGameObjectの属性を変更したい
**SO THAT** Inspectorで手動編集する手間を省ける

**受け入れ基準**:
- Transform（Position/Rotation/Scale）を変更できる
- 名前を変更できる
- 親子関係を変更できる（親変更、親解除）
- Active状態を変更できる
- Tag/Layerを変更できる

#### US-3: GameObjectの削除

**AS** Unity開発者
**I WANT** GameObjectを削除したい
**SO THAT** 不要なオブジェクトを自動でクリーンアップできる

**受け入れ基準**:
- パス指定でGameObjectを削除できる
- 子オブジェクトを含めて削除できる
- 子オブジェクトを残して親のみ削除できる

#### US-4: GameObjectの検索

**AS** Unity開発者
**I WANT** 名前/Tag/Layerで対象のGameObjectを検索したい
**SO THAT** 大規模シーンでも目的のオブジェクトを素早く特定できる

**受け入れ基準**:
- 名前で検索できる（部分一致/完全一致）
- Tagで検索できる
- Layerで検索できる
- 検索結果には階層パスが含まれる

#### US-5: 階層構造の取得

**AS** LLMユーザー
**I WANT** シーンの階層構造を取得したい
**SO THAT** 現在のシーン構造を理解し、次の操作を判断できる

**受け入れ基準**:
- ルートオブジェクトのリストを取得できる
- 指定した深さまでの階層を取得できる
- 非アクティブオブジェクトを含む/除外できる
- Component情報を含む/除外できる
- Transform情報を含む/除外できる
- オブジェクト数上限を設定できる（LLM最適化）
- 名前のみモードで最小データ取得できる

#### US-6: Sceneの管理

**AS** Unity開発者
**I WANT** Sceneの作成・読み込み・保存を自動化したい
**SO THAT** マルチシーンワークフローを効率化できる

**受け入れ基準**:
- 新規Sceneを作成できる
- Sceneを名前/パスで読み込める（Single/Additive）
- 現在のSceneを保存できる
- 別名で保存できる
- プロジェクト内のSceneリストを取得できる
- ロード済みSceneのリストを取得できる
- Scene情報（名前、パス、ビルド設定、ルートオブジェクト数）を取得できる

### 受け入れシナリオ

1. **前提** 空のシーン、**実行** Cubeプリミティブ作成コマンド、**結果** Cubeが原点に作成される
2. **前提** GameObjectが存在、**実行** Transformを変更、**結果** 指定した位置/回転/スケールに変更される
3. **前提** GameObjectが存在、**実行** 削除コマンド、**結果** GameObjectがシーンから削除される
4. **前提** 複数のGameObjectが存在、**実行** Tag検索、**結果** 該当するGameObjectのリストとパスが返る
5. **前提** 階層構造が存在、**実行** 階層取得（深さ2）、**結果** ルートから2階層までの構造が返る
6. **前提** Sceneファイルが存在、**実行** Scene読み込み、**結果** 指定したSceneが読み込まれる
7. **前提** Scene編集中、**実行** 保存コマンド、**結果** 現在のSceneがファイルに保存される

### エッジケース

- 同名のGameObjectを作成した場合、どのように処理されるか?
- 存在しないGameObjectを変更しようとした場合、エラーメッセージは明確か?
- 親GameObjectを削除した場合、子オブジェクトはどうなるか?
- 大規模シーン（10000+オブジェクト）で階層取得した場合、性能は許容範囲か?
- 名前のみモードで階層取得した場合、レスポンスサイズは十分小さいか?
- 未保存のSceneを切り替えた場合、データは失われるか?

---

## 要件

### GameObject作成機能要件

- **FR-001**: システムはプリミティブ型（Cube/Sphere/Cylinder/Capsule/Plane/Quad）のGameObjectを作成できる必要がある
- **FR-002**: システムは空のGameObjectを作成できる必要がある
- **FR-003**: システムはGameObject作成時に名前を指定できる必要がある
- **FR-004**: システムはGameObject作成時に親子関係を指定できる必要がある
- **FR-005**: システムはGameObject作成時にPosition/Rotation/Scaleを指定できる必要がある
- **FR-006**: システムはGameObject作成時にTag/Layerを指定できる必要がある

### GameObject変更機能要件

- **FR-007**: システムは既存GameObjectのTransform（Position/Rotation/Scale）を変更できる必要がある
- **FR-008**: システムは既存GameObjectの名前を変更できる必要がある
- **FR-009**: システムは既存GameObjectの親を変更できる必要がある
- **FR-010**: システムは既存GameObjectの親を解除できる必要がある
- **FR-011**: システムは既存GameObjectのActive状態を変更できる必要がある
- **FR-012**: システムは既存GameObjectのTag/Layerを変更できる必要がある

### GameObject削除機能要件

- **FR-013**: システムはパス指定でGameObjectを削除できる必要がある
- **FR-014**: システムはGameObject削除時に子オブジェクトを含めて削除できる必要がある
- **FR-015**: システムはGameObject削除時に子オブジェクトを残すオプションを提供する必要がある

### GameObject検索機能要件

- **FR-016**: システムは名前でGameObjectを検索できる必要がある（部分一致）
- **FR-017**: システムは名前でGameObjectを検索できる必要がある（完全一致）
- **FR-018**: システムはTagでGameObjectを検索できる必要がある
- **FR-019**: システムはLayerでGameObjectを検索できる必要がある
- **FR-020**: システムは検索結果に階層パスを含める必要がある

### 階層取得機能要件

- **FR-021**: システムはシーンのルートオブジェクトリストを取得できる必要がある
- **FR-022**: システムは指定した深さまでの階層構造を取得できる必要がある
- **FR-023**: システムは非アクティブオブジェクトを含む/除外するオプションを提供する必要がある
- **FR-024**: システムはComponent情報を含む/除外するオプションを提供する必要がある
- **FR-025**: システムはTransform情報を含む/除外するオプションを提供する必要がある
- **FR-026**: システムは取得するオブジェクト数の上限を設定できる必要がある
- **FR-027**: システムは名前とパスのみを返す最小データモードを提供する必要がある

### Scene管理機能要件

- **FR-028**: システムは新規Sceneを作成できる必要がある
- **FR-029**: システムはSceneを名前またはパスで読み込める必要がある
- **FR-030**: システムはScene読み込み時にSingle/Additiveモードを選択できる必要がある
- **FR-031**: システムは現在のSceneを保存できる必要がある
- **FR-032**: システはSceneを別名で保存できる必要がある
- **FR-033**: システムはプロジェクト内のSceneリストを取得できる必要がある
- **FR-034**: システムはロード済みSceneのリストを取得できる必要がある
- **FR-035**: システムはScene情報（名前/パス/ビルド設定/ルートオブジェクト数）を取得できる必要がある

### 非機能要件

#### 性能要件

- **NFR-001**: 100オブジェクトまでの階層取得は500ミリ秒以内に完了する必要がある
- **NFR-002**: GameObject作成は100ミリ秒以内に完了する必要がある
- **NFR-003**: 中規模シーン（1000オブジェクト）の読み込みは3秒以内に完了する必要がある

#### LLM最適化要件

- **NFR-004**: 階層取得は`nameOnly=true`モードで名前とパスのみ返却できる必要がある
- **NFR-005**: 階層取得は`maxObjects`パラメータでレスポンスサイズを制限できる必要がある
- **NFR-006**: 階層取得は`maxDepth`パラメータで深さを制限できる必要がある

#### 信頼性要件

- **NFR-007**: 不正なパス形式（先頭/なし等）で明確なエラーを返却する必要がある
- **NFR-008**: 存在しないGameObject操作時に明確なエラーメッセージを表示する必要がある
- **NFR-009**: 同名GameObjectが存在する場合、エラーまたは番号付与で対応する必要がある

### 主要エンティティ

- **GameObject**: シーン内の基本オブジェクト。名前、階層パス、Active状態、Tag、Layerを持つ
- **Transform**: GameObjectの位置・回転・スケール情報。3次元空間での配置を表す
- **Scene**: GameObjectの集合体。名前、ファイルパス、ビルド設定、ロード状態を持つ
- **階層パス**: GameObjectをルートからの経路で一意に識別する文字列（例: `/Parent/Child`）

---

## スコープ外

以下の機能は本仕様のスコープ外とし、別機能または将来のバージョンで対応:

- Prefabインスタンス化（Asset/Prefab/Material管理機能で対応）
- Component追加・削除（Component管理機能で対応）
- Material/Mesh変更（Asset/Prefab/Material管理機能で対応）
- シーンマージ機能（将来対応予定）
- Undo/Redo機能（将来対応予定）
- GameObjectのコピー・複製機能（将来対応予定）

---

## 技術制約

- Unity Editorはメインスレッドで動作するため、非同期操作に制限がある
- 大規模シーン（10000+オブジェクト）での階層取得は遅延の可能性がある
- Scene操作はUnity Editorの保存確認ダイアログをバイパスする（スクリプト保存）
- GameObject削除は即座に実行され、Undo不可

---

## 参考実装

本仕様は既存実装を文書化したものです。参考実装:

**GameObject操作**:
- `unity-cli/src/handlers/gameobject/GameObjectCreateToolHandler.js`
- `unity-cli/src/handlers/gameobject/GameObjectModifyToolHandler.js`
- `unity-cli/src/handlers/gameobject/GameObjectDeleteToolHandler.js`
- `unity-cli/src/handlers/gameobject/GameObjectFindToolHandler.js`
- `unity-cli/src/handlers/gameobject/GameObjectGetHierarchyToolHandler.js`

**Scene操作**:
- `unity-cli/src/handlers/scene/SceneCreateToolHandler.js`
- `unity-cli/src/handlers/scene/SceneLoadToolHandler.js`
- `unity-cli/src/handlers/scene/SceneSaveToolHandler.js`
- `unity-cli/src/handlers/scene/SceneListToolHandler.js`
- `unity-cli/src/handlers/scene/GetSceneInfoToolHandler.js`

---

## レビュー＆受け入れチェックリスト

### コンテンツ品質
- [x] 実装詳細なし (言語、フレームワーク、API)
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
- [x] 曖昧さをマーク済み（なし）
- [x] ユーザーシナリオを定義済み
- [x] 要件を生成済み
- [x] エンティティを識別済み
- [x] レビューチェックリスト合格

**ステータス**: 完了
