---
description: 利用可能な設計アーティファクトに基づいて、実行可能で依存関係順に並んだ tasks.md を生成します。
---

## ユーザー入力

```text
$ARGUMENTS
```

空でない場合、続行する前にユーザー入力を考慮する**必要があります**。

## 概要

`/speckit.tasks` は、plan.md と spec.md（必須）を中心に、利用可能な資料からタスクを生成します。タスクは**ユーザーストーリー単位**で整理し、各ストーリーが独立して実装・検証できるようにします。

## 実行手順

1. リポジトリルートから `.specify/scripts/bash/check-prerequisites.sh --json` を実行し、JSON出力から以下を取得します（パスはすべて絶対パス）:
   - `FEATURE_DIR`
   - `AVAILABLE_DOCS`

2. `FEATURE_DIR` から設計ドキュメントを読み込みます:
   - **必須**: plan.md（技術スタック、構造）, spec.md（ユーザーストーリーと優先度）
   - **任意**: data-model.md（エンティティ）, contracts/（API）, research.md（決定事項）, quickstart.md（検証シナリオ）
   - ないドキュメントがあっても、存在する情報からタスクを生成します。

3. タスク生成を行います:
   - spec.md からユーザーストーリー（P1, P2, P3...）と独立テスト条件を抽出
   - plan.md から構造・技術選択・制約を抽出
   - data-model.md があれば、エンティティをストーリーへマッピング
   - contracts/ があれば、エンドポイントをストーリーへマッピング
   - research.md があれば、セットアップ/方針タスクへ反映

4. `FEATURE_DIR/tasks.md` を生成します（ファイル書き込みあり）:
   - `.specify/templates/tasks-template.md` の構造に従う
   - Phase 1: Setup、Phase 2: Foundational、Phase 3+: 各ユーザーストーリー（優先度順）
   - 各タスクはチェックボックス＋ID（T001..）＋必要に応じて `[P]`＋`[USx]`＋**ファイルパス**を含める
   - 依存関係と並列化の説明も含める

5. 最後に、ユーザーへ以下を返します:
   - 生成した `tasks.md` のパス
   - 総タスク数とストーリー別内訳
   - 並列化可能なポイント
   - 次ステップ（`/speckit.implement`）への案内
