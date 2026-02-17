# 機能仕様書: Asset/Prefab/Material管理機能

**機能ID**: `SPEC-83cb078a`
**作成日**: 2025-10-17
**ステータス**: 完了
**入力**: ユーザー説明: "Asset/Prefab/Material管理機能"

## 実行フロー (main)
```
1. 入力からユーザー説明を解析
   → 完了: "Asset/Prefab/Material管理機能"
2. 説明から主要概念を抽出
   → アクター: Unity開発者
   → アクション: Prefab作成/変更/インスタンス化、Material作成/変更、Asset管理
   → データ: Prefab、Material、Asset、Import設定
   → 制約: AssetDatabaseシステム、メインスレッド動作
3. 不明確な側面: なし（既存実装のSpec化）
4. ユーザーシナリオ＆テストセクションを記入
   → 5つの主要ユーザーストーリー定義済み
5. 機能要件を生成
   → 23個の機能要件をテスト可能な形式で定義
6. 主要エンティティを識別
   → Prefab、Material、Asset、Import設定
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

Prefab（再利用可能なGameObjectテンプレート）、Material（表面材質）、Asset（プロジェクトファイル）の作成・変更・管理機能。LLMがUnityアセットを直接操作し、再利用性の高いコンテンツ作成を自動化する。

### ビジネス価値

- **Prefab再利用**: テンプレート化により、同じオブジェクトの一括管理・更新を効率化
- **Material自動生成**: シェーダー設定の自動化、プロシージャルなマテリアル作成
- **Asset一括管理**: ファイル操作、検索、依存関係分析でプロジェクト整理を支援

---

## ユーザーシナリオ＆テスト

### 主要ユーザーストーリー

#### US-1: Prefab作成とインスタンス化

**AS** Unity開発者
**I WANT** GameObjectからPrefabを作成し、シーンにインスタンス化したい
**SO THAT** 同じオブジェクトを複数配置・一括更新できる

**受け入れ基準**:
- GameObjectパスを指定してPrefabを作成できる
- 空のPrefabを作成できる
- Prefabをシーンにインスタンス化できる
- インスタンス化時に位置/回転/スケール/親を指定できる

#### US-2: Prefab変更

**AS** Unity開発者
**I WANT** Prefabのプロパティを変更し、すべてのインスタンスに反映したい
**SO THAT** 一箇所の変更で複数オブジェクトを更新できる

**受け入れ基準**:
- Prefabパスを指定してプロパティを変更できる
- 既存インスタンスに変更を適用できる

#### US-3: Material作成と変更

**AS** Unity開発者
**I WANT** Materialを作成・変更したい
**SO THAT** オブジェクトの見た目を自動設定できる

**受け入れ基準**:
- シェーダーを指定してMaterialを作成できる
- 既存MaterialからコピーしてMaterialを作成できる
- Materialのプロパティ（Color、Metallic等）を変更できる
- Materialのシェーダーを変更できる

#### US-4: Asset検索と情報取得

**AS** Unity開発者
**I WANT** Assetを検索し、情報を取得したい
**SO THAT** プロジェクト内のファイルを把握できる

**受け入れ基準**:
- フィルタ（型、ラベル）でAssetを検索できる
- Asset情報（パス、GUID、型、サイズ）を取得できる
- フォルダを作成できる
- Assetを移動・コピー・削除できる
- AssetDatabaseを手動でリフレッシュできる

#### US-5: Asset依存関係分析

**AS** Unity開発者
**I WANT** Assetの依存関係を分析したい
**SO THAT** 削除影響範囲や未使用Assetを特定できる

**受け入れ基準**:
- 指定Assetが依存するAssetリストを取得できる（再帰/非再帰）
- 指定Assetに依存するAssetリストを取得できる
- 循環依存を検出できる
- 未使用Assetを検出できる
- サイズ影響分析ができる

### 受け入れシナリオ

1. **前提** GameObjectが存在、**実行** Prefab作成、**結果** PrefabアセットがAssets/に作成される
2. **前提** Prefabが存在、**実行** インスタンス化、**結果** シーンにPrefabインスタンスが配置される
3. **前提** Prefabが存在、**実行** プロパティ変更、**結果** Prefabとすべてのインスタンスが更新される
4. **前提** なし、**実行** Material作成（Standard、Color: Red）、**結果** 赤色のMaterialが作成される
5. **前提** Materialが存在、**実行** プロパティ変更（Metallic: 0.5）、**結果** Metallic値が0.5に変更される
6. **前提** プロジェクトにAssetが存在、**実行** Asset検索（型: Texture2D）、**結果** すべてのTexture2Dリストが返る
7. **前提** Assetが存在、**実行** 依存関係分析、**結果** 依存するAssetリストが返る

### エッジケース

- 同名のPrefabが既に存在する場合、エラーメッセージは明確か?
- Prefabインスタンス化時に親が存在しない場合、どのように処理されるか?
- 存在しないシェーダーでMaterial作成した場合、エラーメッセージは明確か?
- 大規模プロジェクト（10000+Asset）で依存関係分析時間は許容範囲か?
- AssetDatabase.Refresh実行時間は許容範囲か（5-120秒想定）?

---

## 要件

### Prefab作成機能要件

- **FR-001**: システムはGameObjectパスを指定してPrefabを作成できる必要がある
- **FR-002**: システムは空のPrefabを作成できる必要がある
- **FR-003**: システムは既存Prefabを上書き保存できる必要がある

### Prefabインスタンス化機能要件

- **FR-004**: システムはPrefabをシーンにインスタンス化できる必要がある
- **FR-005**: システムはインスタンス化時に位置/回転/スケールを指定できる必要がある
- **FR-006**: システムはインスタンス化時に親を指定できる必要がある
- **FR-007**: システムはインスタンス化時に名前を上書きできる必要がある

### Prefab変更機能要件

- **FR-008**: システムはPrefabパスを指定してプロパティを変更できる必要がある
- **FR-009**: システムは既存インスタンスに変更を適用できる必要がある

### Material作成機能要件

- **FR-010**: システムはシェーダーを指定してMaterialを作成できる必要がある
- **FR-011**: システムは既存MaterialからコピーしてMaterialを作成できる必要がある
- **FR-012**: システムはMaterial作成時にプロパティを設定できる必要がある
- **FR-013**: システムは既存Materialを上書き保存できる必要がある

### Material変更機能要件

- **FR-014**: システムはMaterialのプロパティ（Color、Metallic等）を変更できる必要がある
- **FR-015**: システムはMaterialのシェーダーを変更できる必要がある

### Asset検索・管理機能要件

- **FR-016**: システムはフィルタ（型、ラベル）でAssetを検索できる必要がある
- **FR-017**: システムはAsset情報（パス、GUID、型、サイズ）を取得できる必要がある
- **FR-018**: システムはフォルダを作成できる必要がある
- **FR-019**: システムはAssetを移動・コピー・削除できる必要がある
- **FR-020**: システムはAssetDatabaseを手動でリフレッシュできる必要がある

### Asset依存関係分析機能要件

- **FR-021**: システムは指定Assetが依存するAssetリストを取得できる必要がある（再帰/非再帰）
- **FR-022**: システムは指定Assetに依存するAssetリストを取得できる必要がある
- **FR-023**: システムは循環依存を検出できる必要がある
- **FR-024**: システムは未使用Assetを検出できる必要がある
- **FR-025**: システムはサイズ影響分析ができる必要がある

### 非機能要件

#### 性能要件

- **NFR-001**: Prefab作成は1秒以内に完了する必要がある
- **NFR-002**: Material作成は500ミリ秒以内に完了する必要がある
- **NFR-003**: Asset検索は1000ファイルで1秒以内に完了する必要がある

#### 信頼性要件

- **NFR-004**: Prefab/Material作成時に同名Asset存在でエラーを返却する必要がある
- **NFR-005**: AssetDatabase操作失敗時に明確なエラーメッセージを表示する必要がある

### 主要エンティティ

- **Prefab**: 再利用可能なGameObjectテンプレート。パス、GUID、インスタンス接続を持つ
- **Material**: 表面材質設定。シェーダー、プロパティセット、テクスチャ参照を持つ
- **Asset**: プロジェクトファイル。パス、GUID、型、サイズ、依存関係を持つ
- **Import設定**: Assetインポート時の設定。型別パラメータ、プリセットを含む

---

## スコープ外

以下の機能は本仕様のスコープ外とし、将来のバージョンで対応:

- Prefabバリアント機能
- NestedPrefab編集
- Assetバンドル管理
- Texture/Audioインポート設定詳細
- カスタムAsset作成

---

## 技術制約

- AssetDatabaseはメインスレッドで動作
- AssetDatabase.Refreshは5-120秒要する可能性
- Prefab編集はPrefabモードまたはプログラム的操作のみ
- Material変更はSerializedObjectシステムに準拠

---

## 参考実装

本仕様は既存実装を文書化したものです。参考実装:

**Prefab操作**:
- `unity-cli/src/handlers/asset/AssetPrefabCreateToolHandler.js`
- `unity-cli/src/handlers/asset/AssetPrefabModifyToolHandler.js`
- `unity-cli/src/handlers/asset/AssetPrefabInstantiateToolHandler.js`
- `unity-cli/src/handlers/asset/AssetPrefabOpenToolHandler.js`
- `unity-cli/src/handlers/asset/AssetPrefabExitModeToolHandler.js`

**Material操作**:
- `unity-cli/src/handlers/asset/AssetMaterialCreateToolHandler.js`
- `unity-cli/src/handlers/asset/AssetMaterialModifyToolHandler.js`

**Asset操作**:
- `unity-cli/src/handlers/asset/AssetDatabaseManageToolHandler.js`
- `unity-cli/src/handlers/asset/AssetImportSettingsManageToolHandler.js`
- `unity-cli/src/handlers/asset/AssetDependencyAnalyzeToolHandler.js`

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
