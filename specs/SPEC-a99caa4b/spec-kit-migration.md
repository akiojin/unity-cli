# Spec Kit Migration記録

**日付**: 2025-10-24
**目的**: 公式GitHub Spec Kit (v0.0.79) を最新版にマージし、カスタム拡張を保持

## 拡張内容

### 1. SPEC ID命名規則

**公式**: `[数字3桁]-[短縮名]` (例: `001-user-auth`)
**拡張**: `SPEC-[UUID8桁]` (例: `SPEC-a1b2c3d4`)

**理由**:
- 人間が付ける名前による曖昧性を排除
- 自動生成により命名の一貫性を保証
- ブランチ名に組み込んで一意性を確保

### 2. ブランチ＆Worktree運用

**公式**: ブランチ名 = 機能ID（例: `001-user-auth`）
**拡張**: ブランチ名 = `feature/SPEC-[UUID8桁]`、Worktree配置 = `.worktrees/SPEC-xxx/`

**理由**:
- 複数機能の並行開発をWorktreeで効率化
- Git Flowに近い運用（feature/プレフィックス）
- ローカルマージによる軽量なワークフロー（PR不要）

**完了時の運用**:
```bash
# 機能完了時
.specify/scripts/bash/finish-feature.sh

# 内部動作:
# 1. mainブランチに切り替え
# 2. feature/SPEC-xxx を --no-ff でマージ
# 3. Worktreeを削除
# 4. feature ブランチを削除
# 5. main を push
```

**実装**: `.specify/scripts/bash/create-new-feature.sh`の`generate_spec_id()`関数

```bash
generate_spec_id() {
    for _ in 1 2 3 4 5; do
        if uuid=$(cat /proc/sys/kernel/random/uuid 2>/dev/null); then
            local short="${uuid:0:8}"
            short=$(echo "$short" | tr '[:upper:]' '[:lower:]')
            echo "SPEC-$short"
            return
        fi
    done
    local ts=$(date +%s%N)
    echo "SPEC-${ts: -8}"
}
```

### 2. 日本語化

**公式**: 英語
**拡張**: 日本語（日本語チームでの運用効率向上）

### 3. 日本語化

**公式**: 英語
**拡張**: 日本語（日本語チームでの運用効率向上）

**対象ファイル**:
- `.claude/commands/*.md` (specify, plan, tasks)
- `.specify/templates/spec-template.md`

## 公式v0.0.79の追加機能

### 新コマンド

1. `/speckit.constitution` - プロジェクト憲章の管理
2. `/speckit.implement` - 実装支援
3. `/speckit.analyze` - 仕様分析
4. `/speckit.clarify` - 明確化支援
5. `/speckit.checklist` - チェックリスト管理

### ワークフロー改善

1. **検証チェックリスト**: 仕様品質の自動検証
2. **明確化プロセス**: `[NEEDS CLARIFICATION]`マーカーの体系的な解決
3. **優先度管理**: ユーザーストーリーにP1/P2/P3を付与
4. **独立テスト**: 各ストーリーの独立テスト基準を明示

## マージ戦略

### Phase 1: コマンドファイルの更新

**対象**: `.claude/commands/`

1. 公式の8ファイルをコピー
2. 各ファイルを日本語化
3. コマンド名を`/speckit.*`のまま維持（公式との互換性）

**変換ルール**:
- `description:` → 日本語説明
- `## Outline` → `## 概要`
- `## User Input` → `## ユーザー入力`
- 手順説明を日本語化

### Phase 2: スクリプトの更新

**対象**: `.specify/scripts/bash/create-new-feature.sh`

1. 公式の最新版をベースにする
2. `generate_spec_id()`関数を追加（UUID8桁生成）
3. ブランチ作成ロジックを修正（HAS_GIT=trueでもスキップ）
4. FEATURE_ID変数を`SPEC-[UUID8桁]`形式に変更
5. JSON出力を`FEATURE_ID`に変更（`BRANCH_NAME`の代わり）

**具体的な変更箇所**:

```bash
# 213行目付近: BRANCH_NAMEではなくFEATURE_IDを使用
FEATURE_ID=$(generate_spec_id)

# 243行目付近: FEATURE_DIRをFEATURE_IDベースに
FEATURE_DIR="$SPECS_DIR/$FEATURE_ID"

# 252行目付近: JSON出力をFEATURE_ID中心に
printf '{"FEATURE_ID":"%s","SPEC_FILE":"%s"}\n' "$FEATURE_ID" "$SPEC_FILE"

# 237-241行目: Gitブランチ作成を削除またはスキップ
if [ "$HAS_GIT" = true ]; then
    echo "[specify] Gitブランチは作成しません（既定動作）"
fi
```

### Phase 3: テンプレートの更新

**対象**: `.specify/templates/spec-template.md`

1. 公式の最新版構造を採用
2. `**Feature Branch**` → `**機能ID**`に変更
3. `SPEC-[UUID8桁]`形式に変更
4. 全体を日本語化
5. 優先度管理（P1/P2/P3）と独立テスト基準を維持

**変換例**:

```markdown
# 機能仕様書: [機能名]

**機能ID**: `SPEC-[UUID8桁]`
**作成日**: [日付]
**ステータス**: 下書き
**入力**: ユーザー説明: "$ARGUMENTS"

## ユーザーシナリオ＆テスト

### ユーザーストーリー1 - [簡潔なタイトル] (優先度: P1)

[このユーザージャーニーを平易な言葉で説明]

**この優先度の理由**: [価値とこの優先度レベルの説明]

**独立テスト**: [独立してテスト可能な方法を説明]

**受け入れシナリオ**:

1. **前提** [初期状態]、**実行** [アクション]、**結果** [期待される結果]
```

### Phase 4: 新コマンドの日本語化

**新規追加ファイル**:

1. `.claude/commands/speckit.constitution.md`
2. `.claude/commands/speckit.implement.md`
3. `.claude/commands/speckit.analyze.md`
4. `.claude/commands/speckit.clarify.md`
5. `.claude/commands/speckit.checklist.md`

各ファイルを英語版から日本語化。

## 検証計画

### 1. コマンド動作確認

```bash
# 新規SPEC作成
/speckit.specify "テスト機能"

# 期待: SPEC-[UUID8桁]形式のディレクトリ作成
# 期待: spec.mdが日本語テンプレートで生成
# 期待: Gitブランチ作成なし

# 実装計画作成
/speckit.plan

# 期待: plan.mdが日本語で生成
# 期待: research.md, data-model.md, contracts/が生成

# タスク作成
/speckit.tasks

# 期待: tasks.mdが日本語で生成
# 期待: 優先度順のタスク編成
```

### 2. 既存SPECとの互換性

- 既存の`SPEC-*`ディレクトリが認識されること
- 既存のspec.md/plan.md/tasks.mdが正しく読み込まれること

### 3. 新機能の動作確認

- `/speckit.constitution` でconstitution.mdが管理できること
- `/speckit.clarify` で明確化プロセスが機能すること
- チェックリスト機能が正常に動作すること

## ロールバック計画

万が一問題が発生した場合:

1. Gitで`spec-kit-migration`ブランチから元のmainに戻す
2. `.claude/commands/`と`.specify/`を元のバージョンに復元

## 実施タイミング

- 準備: 2025-10-24
- 実施: 本記録作成後、即座に実行
- 検証: 実施直後
- コミット: 検証完了後

## 移行実施記録（2025-10-24）

### 既存SPEC移行

**日時**: 2025-10-24 04:31 UTC

**対象**: 16個の既存SPEC

**実施内容**:

1. `.specify/scripts/bash/migrate-specs-to-worktrees.sh` スクリプト作成
2. 各SPECに対して以下を実行:
   - `feature/SPEC-[UUID8桁]` ブランチ作成（mainから）
   - `.worktrees/SPEC-[UUID8桁]/` にworktree作成
   - SPEC内容は自動的にworktreeに反映（mainから継承）

**結果**:

- 16個のfeatureブランチ作成完了
- 16個のworktree作成完了
- 各worktreeはmainブランチのコミット2095cceから開始
- すべてのSPECファイルがworktreeに存在することを確認

**移行済みSPEC**:

- SPEC-0d5d84f9
- SPEC-2e6d9a3b
- SPEC-3c9871b3
- SPEC-3d9b5f4e
- SPEC-56860fa4
- SPEC-5873e340
- SPEC-5c438276
- SPEC-7a2c8e6d
- SPEC-83cb078a
- SPEC-9d2bc43b
- SPEC-9f4b1a7c
- SPEC-c00be76f
- SPEC-e757a01f
- SPEC-e7c9b50c
- SPEC-e8f7a2c1
- SPEC-e7035f70

**確認コマンド**:

```bash
# ブランチ一覧
git branch | grep "feature/SPEC-"

# Worktree一覧
git worktree list

# 特定SPECのWorktree確認
ls -la .worktrees/SPEC-0d5d84f9/specs/SPEC-0d5d84f9/
```

---

**Note**: このドキュメントは実装後にアーカイブし、今後の参考資料とする。
