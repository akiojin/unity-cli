# Speckit（GitHub Spec Kit）アップデート手順

このリポジトリでは、GitHub Spec Kit（通称Speckit）の「プロジェクト側ファイル」（テンプレート/スクリプト/コマンド定義）を組み込んでいます。上流更新が入ったときに、同じ手順でアップデートできるように、このRunbookに手順と不変条件（カスタム要件）を残します。

## このリポジトリのカスタム要件（不変条件）

Speckitをアップデートするときは、以下が必ず成立する状態に戻してください。

1. **完全日本語対応**
   - `.claude/commands/speckit.*.md` が日本語であること
   - `.specify/templates/*.md` と `.specify/templates/commands/*.md` が日本語であること

2. **ブランチを作成しない**
   - `/speckit.specify`（`create-new-feature`）は **Gitブランチ/Worktreeを作成しない**

3. **要件IDの形式**
   - 要件IDは `specs/SPEC-[UUID8桁]`（例: `specs/SPEC-a1b2c3d4/`）

4. **要件一覧の自動生成**
   - `specs/specs.md` に要件一覧を出力する（自動生成/上書きOK）

5. **現在の対象要件の解決方法**
   - `speckit.plan` / `speckit.tasks` などが対象を特定できるよう、`.specify/current-feature`（gitignore対象）または `SPECIFY_FEATURE=SPEC-xxxxxxxx` を使用する

## どこが「Speckit」か（更新対象）

上流（`github/spec-kit`）の「プロジェクト側ファイル」に相当するもの:

- `.specify/templates/`
- `.specify/scripts/bash/`
- `.specify/scripts/powershell/`

このリポジトリ固有のマッピング/運用:

- `.claude/commands/speckit.*.md`（上流には無いので、このリポジトリで管理）
- `specs/specs.md`（本リポジトリで自動生成）

## アップデート手順（推奨フロー）

### 1) 上流のバージョン（タグ）を決める

例（最新タグを確認）:

```bash
git ls-remote --tags --refs https://github.com/github/spec-kit.git | awk -F/ '{print $3}' | sort -V | tail -n 20
```

### 2) 上流スナップショットを取得（比較用）

比較用に上流のテンプレ/スクリプトを取得します。

```bash
bash scripts/upgrade-speckit.sh --tag v0.0.90
```

このスクリプトは `.specify/upstream/spec-kit-<tag>/` に上流ファイルを展開します（現行ファイルは上書きしません）。
同じタグで再実行する場合は、出力先が既に存在するため `--force` を付けて作り直してください。

### 3) 差分を見て、必要な取り込みを判断する

取り込み対象:

- 上流で新規追加されたスクリプト/テンプレート
- 既存スクリプトの改善（ただし、このリポジトリの不変条件を崩さない範囲）

確認例:

```bash
git diff --no-index .specify/templates .specify/upstream/spec-kit-v0.0.90/templates
git diff --no-index .specify/scripts/bash .specify/upstream/spec-kit-v0.0.90/scripts/bash
```

### 4) 不変条件に戻す（カスタム要件の再確認）

アップデートで壊れやすいポイント:

- `create-new-feature` がブランチを作成してしまう
- `SPEC-[UUID8桁]` 以外のディレクトリ名になる
- `.specify/current-feature` を参照しない/更新しない
- `specs/specs.md` が生成されない
- `.claude/commands` / `.specify/templates` が英語に戻る

### 5) 動作確認（最低限）

```bash
# bash構文チェック
bash -n .specify/scripts/bash/*.sh

	# 要件一覧生成（既存specsを読み取れること）
	bash .specify/scripts/bash/update-specs-readme.sh --quiet

	# Speckitコマンドの不変条件チェック（.claude/.specify の整合）
	bash .specify/scripts/checks/check-speckit-commands.sh

	# 要件未選択時にエラーになること（安全側）
	rm -f .specify/current-feature
	bash .specify/scripts/bash/check-prerequisites.sh --json --paths-only || true
	```

補足:

- GitHub Actionsで `specs/specs.md` の最新性を検証しています。CIが落ちた場合は `bash .specify/scripts/bash/update-specs-readme.sh --quiet` を実行して再コミットしてください。
- GitHub Actionsで Speckit コマンド（`.claude/commands` と `.specify/templates/commands`）の不変条件も検証しています。必要なら `bash .specify/scripts/checks/check-speckit-commands.sh` を実行してください。

### 6) 変更を記録（コミット）

おすすめ:

- コミットメッセージ例: `chore: upgrade speckit project files`
- このRunbookに、取り込んだ上流タグと要点を追記

## 依頼のしかた（AIに同じ作業をさせたい場合）

このRunbookへの参照と、タグ指定があると再現性が上がります。

例:

> 「Speckitを上流v0.0.xxへ更新して。Runbook（docs/runbooks/speckit-upgrade.md）の不変条件（日本語/ブランチ無し/SPEC-ID/specs/specs.md生成）を必ず満たして。差分は .specify/upstream に取り込んで確認してから反映して。」

## 更新履歴

| 日付       | 上流タグ | 対応者 | 概要                                                  |
|------------|----------|--------|-------------------------------------------------------|
| 2026-01-30 | v0.0.90  | Claude | テンプレート拡張を日本語化。スクリプトは現状維持。    |
| 2026-02-14 | v0.0.95  | Codex  | `specs/specs.md`生成のbash3.2互換化、CI/不変条件チェック追加。 |
