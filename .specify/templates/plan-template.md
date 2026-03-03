# 実装計画: [機能名]

**要件ID**: `SPEC-[UUID8桁]` | **日付**: [日付] | **仕様**: [リンク]  
**入力**: `/specs/SPEC-[UUID8桁]/spec.md` の機能仕様

**注記**: このテンプレートは `/speckit.plan` コマンドにより埋められます。実行ワークフローは `.specify/templates/commands/plan.md` を参照してください。

## 概要

[機能仕様から抽出: 主要要件 + リサーチ結果に基づく技術アプローチ]

## 技術コンテキスト

<!--
  要対応: このセクションの内容はプレースホルダーです。
  プロジェクトに合わせて技術的詳細に置き換えてください。
-->

**言語/バージョン**: [例: Python 3.11, Swift 5.9, Rust 1.75 または 要明確化]  
**主要依存関係**: [例: FastAPI, UIKit, LLVM または 要明確化]  
**ストレージ**: [該当する場合、例: PostgreSQL, CoreData, files または N/A]  
**テスト**: [例: pytest, XCTest, cargo test または 要明確化]  
**対象プラットフォーム**: [例: Linuxサーバー, iOS 15+, WASM または 要明確化]  
**プロジェクトタイプ**: [single/web/mobile - ソース構造を決定]  
**パフォーマンス目標**: [ドメイン固有、例: 1000 req/s, 60 fps など または 要明確化]  
**制約**: [ドメイン固有、例: <200ms p95, <100MB memory, オフライン対応 など または 要明確化]  
**スケール/スコープ**: [ドメイン固有、例: 10kユーザー, 50画面 など または 要明確化]

## 憲章チェック

*GATE: Phase 0 のリサーチ前に必ず合格。Phase 1 設計後に再チェック。*

[constitution.md（または .specify/memory/constitution.md）に基づくゲート条件]

## プロジェクト構造

### ドキュメント（この要件）

```text
specs/SPEC-[UUID8桁]/
├── plan.md              # このファイル (/speckit.plan コマンド出力)
├── research.md          # Phase 0 出力 (/speckit.plan コマンド)
├── data-model.md        # Phase 1 出力 (/speckit.plan コマンド)
├── quickstart.md        # Phase 1 出力 (/speckit.plan コマンド)
├── contracts/           # Phase 1 出力 (/speckit.plan コマンド)
└── tasks.md             # Phase 2 出力 (/speckit.tasks コマンド出力 - /speckit.plan では作成しない)
```

### ソースコード（リポジトリルート）

<!--
  要対応: 下のプレースホルダーツリーを、この要件の実際の構造に置き換えてください。
  未使用のオプションは削除し、選択した構造を実パスで具体化します。
  最終的な計画には「Option」ラベルを含めないでください。
-->

```text
# [未使用なら削除] Option 1: 単一プロジェクト（デフォルト）
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [未使用なら削除] Option 2: Webアプリ（"frontend" + "backend" 検出時）
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [未使用なら削除] Option 3: モバイル + API（"iOS/Android" 検出時）
api/
└── [上記 backend と同様]

ios/ または android/
└── [プラットフォーム固有の構造: 機能モジュール、UIフロー、プラットフォームテスト]
```

**構造決定**: [選択した構造を明記し、上記ツリーの実ディレクトリを参照する]

## 複雑さトラッキング

> **憲章チェックで「違反が正当化される必要がある」場合のみ記入**

| 違反 | 必要な理由 | より単純な代替案が却下された理由 |
|---|---|---|
| [例: 4つ目のプロジェクト] | [現在のニーズ] | [なぜ3つのプロジェクトでは不十分か] |
| [例: Repositoryパターン] | [特定の問題] | [なぜ直接DBアクセスでは不十分か] |
