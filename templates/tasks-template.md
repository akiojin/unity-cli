# タスク: [機能名]

**入力**: `/specs/SPEC-[UUID8桁]/`の設計ドキュメント
**前提条件**: plan.md (必須), research.md, data-model.md, contracts/

## 実行フロー (main)
```
1. 機能ディレクトリからplan.mdを読み込み
   → 見つからない場合: ERROR "実装計画が見つかりません"
   → 抽出: 技術スタック、ライブラリ、構造
2. オプション設計ドキュメントを読み込み:
   → data-model.md: エンティティを抽出 → modelタスク
   → contracts/: 各ファイル → contract testタスク
   → research.md: 決定を抽出 → setupタスク
3. カテゴリ別にタスクを生成:
   → Setup: プロジェクト初期化、依存関係、リンティング
   → Tests: contract tests, integration tests
   → Core: models, services, CLIコマンド
   → Integration: DB、ミドルウェア、ロギング
   → Polish: unit tests, performance, docs
4. タスクルールを適用:
   → 異なるファイル = [P]をマーク (並列実行可能)
   → 同じファイル = 順次実行 ([P]なし)
   → テストが実装より先 (TDD)
5. タスクを順次番号付け (T001, T002...)
6. 依存関係グラフを生成
7. 並列実行例を作成
8. タスク完全性を検証:
   → すべてのcontractsにテストがあるか?
   → すべてのentitiesにmodelsがあるか?
   → すべてのendpointsが実装されているか?
9. 戻り値: SUCCESS (タスク実行準備完了)
```

## フォーマット: `[ID] [P?] 説明`
- **[P]**: 並列実行可能 (異なるファイル、依存関係なし)
- 説明には正確なファイルパスを含める

## パス規約
- **単一プロジェクト**: リポジトリルートの `src/`, `tests/`
- **Webアプリ**: `backend/src/`, `frontend/src/`
- **モバイル**: `api/src/`, `ios/src/` または `android/src/`
- 以下のパスは単一プロジェクトを前提 - plan.mdの構造に基づいて調整

## Phase 3.1: セットアップ
- [ ] T001 実装計画に従ってプロジェクト構造を作成
- [ ] T002 [フレームワーク]依存関係で[言語]プロジェクトを初期化
- [ ] T003 [P] リンティングとフォーマットツールを構成

## Phase 3.2: テストファースト (TDD) ⚠️ 3.3の前に完了必須
**重要: これらのテストは記述され、実装前に失敗する必要がある**
- [ ] T004 [P] tests/contract/test_users_post.py に POST /api/users の contract test
- [ ] T005 [P] tests/contract/test_users_get.py に GET /api/users/{id} の contract test
- [ ] T006 [P] tests/integration/test_registration.py にユーザー登録の integration test
- [ ] T007 [P] tests/integration/test_auth.py に認証フローの integration test

## Phase 3.3: コア実装 (テストが失敗した後のみ)
- [ ] T008 [P] src/models/user.py にユーザーモデル
- [ ] T009 [P] src/services/user_service.py にUserService CRUD
- [ ] T010 [P] src/cli/user_commands.py にCLI --create-user
- [ ] T011 POST /api/users エンドポイント
- [ ] T012 GET /api/users/{id} エンドポイント
- [ ] T013 入力検証
- [ ] T014 エラーハンドリングとロギング

## Phase 3.4: 統合
- [ ] T015 UserServiceをDBに接続
- [ ] T016 認証ミドルウェア
- [ ] T017 リクエスト/レスポンスログ
- [ ] T018 CORSとセキュリティヘッダー

## Phase 3.5: 仕上げ
- [ ] T019 [P] tests/unit/test_validation.py に検証の unit tests
- [ ] T020 パフォーマンステスト (<200ms)
- [ ] T021 [P] docs/api.md を更新
- [ ] T022 重複を削除
- [ ] T023 manual-testing.md を実行

## 依存関係
- Tests (T004-T007) が implementation (T008-T014) より先
- T008 が T009, T015 をブロック
- T016 が T018 をブロック
- Implementation が polish (T019-T023) より先

## 並列実行例
```
# T004-T007 を一緒に起動:
Task: "tests/contract/test_users_post.py に POST /api/users の contract test"
Task: "tests/contract/test_users_get.py に GET /api/users/{id} の contract test"
Task: "tests/integration/test_registration.py に登録の integration test"
Task: "tests/integration/test_auth.py に認証の integration test"
```

## 注意事項
- [P] タスク = 異なるファイル、依存関係なし
- 実装前にテストが失敗することを確認
- 各タスク後にコミット
- 回避: 曖昧なタスク、同じファイルの競合

## タスク生成ルール
*main()実行中に適用*

1. **Contractsから**:
   - 各contractファイル → contract testタスク [P]
   - 各endpoint → 実装タスク

2. **Data Modelから**:
   - 各entity → model作成タスク [P]
   - 関係 → サービス層タスク

3. **User Storiesから**:
   - 各story → integration test [P]
   - クイックスタートシナリオ → 検証タスク

4. **順序**:
   - Setup → Tests → Models → Services → Endpoints → Polish
   - 依存関係は並列実行をブロック

## 検証チェックリスト
*ゲート: 戻る前にmain()でチェック*

- [ ] すべてのcontractsに対応するテストがある
- [ ] すべてのentitiesにmodelタスクがある
- [ ] すべてのテストが実装より先にある
- [ ] 並列タスクは本当に独立している
- [ ] 各タスクは正確なファイルパスを指定
- [ ] 同じファイルを変更する[P]タスクがない
