# 機能仕様書: Component管理機能

**機能ID**: `SPEC-5873e340`
**作成日**: 2025-10-17
**ステータス**: 完了
**入力**: ユーザー説明: "Component管理機能"

## 実行フロー (main)
```
1. 入力からユーザー説明を解析
   → 完了: "Component管理機能"
2. 説明から主要概念を抽出
   → アクター: Unity開発者、LLMユーザー
   → アクション: Component追加/削除/変更、プロパティ取得、一覧取得、型検索
   → データ: Componentプロパティ、型情報、GameObject参照
   → 制約: Unity標準Componentのみ、SerializedProperty準拠
3. 不明確な側面: なし（既存実装のSpec化）
4. ユーザーシナリオ＆テストセクションを記入
   → 7つの主要ユーザーストーリー定義済み
5. 機能要件を生成
   → 27個の機能要件をテスト可能な形式で定義
6. 主要エンティティを識別
   → Component、プロパティ、型情報
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

GameObject上のComponent（Rigidbody、Collider、Light、Camera等）の追加・削除・変更・一覧取得・検索機能。LLMがUnity Componentを直接操作し、ゲームオブジェクトの動作・物理・レンダリング設定を自動化する。

### ビジネス価値

- **Component設定の自動化**: Inspectorでの手動設定を排除、一括Component操作で効率化
- **動的なゲーム設定**: LLMによる物理/レンダリングパラメータの調整、プレイテストの自動化
- **検証とデバッグ**: Component状態の取得、型別検索により、複雑な設定を可視化

---

## ユーザーシナリオ＆テスト

### 主要ユーザーストーリー

#### US-1: Componentの追加

**AS** Unity開発者
**I WANT** LLMコマンドでGameObjectにComponentを追加したい
**SO THAT** Inspectorで「Add Component」ボタンを押す手間を省ける

**受け入れ基準**:
- Component型を指定してGameObjectに追加できる（例: Rigidbody、BoxCollider）
- 追加時に初期プロパティを設定できる（例: Rigidbody.mass = 10）
- 同じ型のComponentを複数追加できる場合、インデックスで管理できる

#### US-2: Componentの削除

**AS** Unity開発者
**I WANT** GameObjectからComponentを削除したい
**SO THAT** 不要なComponentを自動でクリーンアップできる

**受け入れ基準**:
- Component型を指定して削除できる
- 同じ型が複数存在する場合、インデックスで削除対象を指定できる

#### US-3: Componentプロパティの変更

**AS** Unity開発者
**I WANT** 既存Componentのプロパティを変更したい
**SO THAT** Inspectorで手動編集する手間を省ける

**受け入れ基準**:
- Component型とプロパティ名を指定して値を変更できる
- 複雑な型（Vector3、Color等）を変更できる
- 配列/リストプロパティを変更できる

#### US-4: Componentプロパティの取得

**AS** LLMユーザー
**I WANT** Componentの現在のプロパティ値を取得したい
**SO THAT** 現在の設定を理解し、次の操作を判断できる

**受け入れ基準**:
- Component型を指定してすべてのプロパティを取得できる
- 継承されたプロパティを含む/除外できる
- プライベートフィールドを含む/除外できる

#### US-5: GameObjectのComponent一覧

**AS** Unity開発者
**I WANT** GameObjectに付いているすべてのComponentリストを取得したい
**SO THAT** オブジェクトの構成を把握できる

**受け入れ基準**:
- GameObjectパスを指定してComponent一覧を取得できる
- Componentの型名が含まれる
- 基底クラス型を含む/除外できる

#### US-6: Component型による検索

**AS** Unity開発者
**I WANT** 特定のComponent型を持つGameObjectを検索したい
**SO THAT** 大規模シーンで特定機能を持つオブジェクトを特定できる

**受け入れ基準**:
- Component型を指定してGameObjectを検索できる（例: Light、AudioSource）
- 検索スコープ（Scene/Prefabs/All）を指定できる
- 完全一致/派生型を含むオプションを選択できる
- 非アクティブオブジェクトを含む/除外できる

#### US-7: Component型の取得

**AS** LLMユーザー
**I WANT** 利用可能なComponent型のリストを取得したい
**SO THAT** 正しいComponent名を指定できる

**受け入れ基準**:
- AddComponentで追加可能なComponent型リストを取得できる
- カテゴリ別（Physics、Rendering、UI等）にフィルタできる
- 検索キーワードで型を絞り込める

### 受け入れシナリオ

1. **前提** GameObjectが存在、**実行** Rigidbody追加コマンド、**結果** GameObjectにRigidbodyが追加される
2. **前提** GameObjectにRigidbodyが付与済み、**実行** Rigidbody.mass = 5に変更、**結果** massプロパティが5に変更される
3. **前提** GameObjectにBoxColliderが付与済み、**実行** BoxCollider削除コマンド、**結果** BoxColliderが削除される
4. **前提** GameObjectにLightが付与済み、**実行** Lightプロパティ取得、**結果** Color、Intensity等のプロパティ値が返る
5. **前提** GameObjectが存在、**実行** Component一覧取得、**結果** 付与されているすべてのComponent型名が返る
6. **前提** シーンに複数のGameObjectが存在、**実行** Light型検索、**結果** Lightを持つGameObjectのリストが返る
7. **前提** なし、**実行** Component型リスト取得（カテゴリ: Physics）、**結果** Rigidbody、Collider等の物理系Component型が返る

### エッジケース

- 削除不可のComponent（Transform等）を削除しようとした場合、エラーメッセージは明確か?
- 存在しないComponent型を追加しようとした場合、エラーメッセージは明確か?
- 同じ型のComponentが複数ある場合、インデックス指定は正しく機能するか?
- 複雑な型（Vector3、Quaternion、Color）のプロパティ変更は正しく動作するか?
- 継承されたプロパティ除外時、正しくフィルタリングされるか?
- Component依存関係がある場合（例: Rigidbody削除時にJointが存在）、警告が表示されるか?

---

## 要件

### Component追加機能要件

- **FR-001**: システムはComponent型を指定してGameObjectに追加できる必要がある
- **FR-002**: システムはComponent追加時に初期プロパティを設定できる必要がある
- **FR-003**: システムは同じ型のComponentを複数追加できる必要がある（許可されている場合）
- **FR-004**: システムはComponent追加時にインデックスで管理できる必要がある

### Component削除機能要件

- **FR-005**: システムはComponent型を指定してGameObjectから削除できる必要がある
- **FR-006**: システムは同じ型が複数存在する場合、インデックスで削除対象を指定できる必要がある
- **FR-007**: システムは削除不可のComponent（Transform等）削除時にエラーを返却する必要がある

### Componentプロパティ変更機能要件

- **FR-008**: システムはComponent型とプロパティ名を指定して値を変更できる必要がある
- **FR-009**: システムは複雑な型（Vector3、Color、Quaternion等）のプロパティを変更できる必要がある
- **FR-010**: システムは配列/リストプロパティを変更できる必要がある
- **FR-011**: システムはプロパティ値の型不一致時にエラーを返却する必要がある

### Componentプロパティ取得機能要件

- **FR-012**: システムはComponent型を指定してすべてのプロパティを取得できる必要がある
- **FR-013**: システムは継承されたプロパティを含む/除外するオプションを提供する必要がある
- **FR-014**: システムはプライベートフィールドを含む/除外するオプションを提供する必要がある

### Component一覧取得機能要件

- **FR-015**: システムはGameObjectパスを指定してComponent一覧を取得できる必要がある
- **FR-016**: システムはComponent一覧にComponent型名を含める必要がある
- **FR-017**: システムは基底クラス型を含む/除外するオプションを提供する必要がある

### Component検索機能要件

- **FR-018**: システムはComponent型を指定してGameObjectを検索できる必要がある
- **FR-019**: システムは検索スコープ（Scene/Prefabs/All）を指定できる必要がある
- **FR-020**: システムは完全一致/派生型を含むオプションを提供する必要がある
- **FR-021**: システムは非アクティブオブジェクトを含む/除外するオプションを提供する必要がある

### Component型取得機能要件

- **FR-022**: システムはAddComponentで追加可能なComponent型リストを取得できる必要がある
- **FR-023**: システムはカテゴリ別（Physics、Rendering、UI等）にフィルタできる必要がある
- **FR-024**: システムは検索キーワードで型を絞り込める必要がある

### 非機能要件

#### 性能要件

- **NFR-001**: Component追加は100ミリ秒以内に完了する必要がある
- **NFR-002**: プロパティ取得は50ミリ秒以内に完了する必要がある
- **NFR-003**: シーン内1000オブジェクトのComponent検索は1秒以内に完了する必要がある

#### 信頼性要件

- **NFR-004**: 存在しないComponent型指定時に明確なエラーを返却する必要がある
- **NFR-005**: 存在しないプロパティ名指定時に明確なエラーを返却する必要がある
- **NFR-006**: プロパティ値の型不一致時に明確なエラーを返却する必要がある
- **NFR-007**: Component削除時に他Componentが依存している場合、警告または拒否する必要がある

#### LLM最適化要件

- **NFR-008**: プロパティ取得は継承/プライベート除外オプションでペイロード削減できる必要がある

### 主要エンティティ

- **Component**: GameObjectに付与される機能モジュール。型名、プロパティセット、有効/無効状態を持つ
- **プロパティ**: Componentの設定値。名前、型、値、可視性（public/private）を持つ
- **型情報**: Component型のメタデータ。型名、カテゴリ、追加可否、依存関係を含む

---

## スコープ外

以下の機能は本仕様のスコープ外とし、将来のバージョンで対応:

- カスタムComponent（ユーザー定義スクリプト）のリフレクション操作
- Componentのコピー・ペースト機能
- Component有効化/無効化トグル
- Componentプリセット保存・読み込み
- Component依存関係の自動解決

---

## 技術制約

- Unity標準Componentを優先サポート（カスタムスクリプトは限定的）
- プロパティ変更はSerializedPropertyシステムに準拠
- 一部のComponentは削除不可（Transform、RectTransform等）
- プロパティのリフレクションはパフォーマンスに影響する可能性がある

---

## 参考実装

本仕様は既存実装を文書化したものです。参考実装:

- `unity-cli/src/handlers/component/ComponentAddToolHandler.js`: Component追加
- `unity-cli/src/handlers/component/ComponentRemoveToolHandler.js`: Component削除
- `unity-cli/src/handlers/component/ComponentModifyToolHandler.js`: プロパティ変更
- `unity-cli/src/handlers/analysis/GetComponentValuesToolHandler.js`: プロパティ取得
- `unity-cli/src/handlers/component/ComponentListToolHandler.js`: Component一覧
- `unity-cli/src/handlers/analysis/FindByComponentToolHandler.js`: Component検索
- `unity-cli/src/handlers/component/ComponentGetTypesToolHandler.js`: Component型取得

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
