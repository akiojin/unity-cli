# Constitution Update Checklist

English | [日本語](#日本語)

When amending the constitution (`docs/constitution.md`), update all dependent docs/templates to avoid drift.

## What to Update

### Always

- [ ] `docs/constitution.md` (source of truth)
- [ ] `.specify/memory/constitution.md` (mirror for Spec Kit / Claude commands)
- [ ] `CLAUDE.md` references (if they point at old paths)
- [ ] `docs/README.md` (docs index)
- [ ] `templates/plan-template.md` and `.specify/templates/plan-template.md` (footer link/version)
- [ ] `.claude/commands/speckit.plan.md` / `.claude/commands/speckit.analyze.md` / `.claude/commands/speckit.constitution.md`

### If you touch requirements that affect specs

- [ ] `.specify/templates/spec-template.md`
- [ ] `.specify/templates/tasks-template.md`
- [ ] `templates/spec-template.md` and `templates/tasks-template.md` (legacy)

## Validation

1. **Search for stale links:**
   - [ ] `rg -n \"(^|\\s)/memory/constitution\\.md\" -S --hidden .` returns nothing
   - [ ] `rg -n \"(^|\\s)memory/constitution\\.md\" -S --hidden .` returns nothing
2. **Confirm the mirror is in sync:**
   - [ ] `bash scripts/sync-constitution.sh`
   - [ ] `bash scripts/sync-constitution.sh --check` is clean
3. **Keep version/date consistent:**
   - [ ] `docs/constitution.md` version/dates are correct
   - [ ] Plan template footer matches the constitution version

---

## 日本語

`docs/constitution.md`（開発憲章）を更新したら、関連ドキュメントの参照ズレを必ず潰します。

## 更新対象

### 常に

- [ ] `docs/constitution.md`（正）
- [ ] `.specify/memory/constitution.md`（Spec Kit / Claude用ミラー）
- [ ] `CLAUDE.md` の参照先（旧パスが残っていないか）
- [ ] `docs/README.md`（導線）
- [ ] `templates/plan-template.md` と `.specify/templates/plan-template.md`（フッターのリンク/バージョン）
- [ ] `.claude/commands/speckit.plan.md` / `.claude/commands/speckit.analyze.md` / `.claude/commands/speckit.constitution.md`

### 仕様書テンプレに影響する場合

- [ ] `.specify/templates/spec-template.md`
- [ ] `.specify/templates/tasks-template.md`
- [ ] `templates/spec-template.md` と `templates/tasks-template.md`（レガシー）

## バリデーション

1. **古いリンクを検索:**
   - [ ] `rg -n \"(^|\\s)/memory/constitution\\.md\" -S --hidden .` がヒットしない
   - [ ] `rg -n \"(^|\\s)memory/constitution\\.md\" -S --hidden .` がヒットしない
2. **ミラーが一致していることを確認:**
   - [ ] `bash scripts/sync-constitution.sh`
   - [ ] `bash scripts/sync-constitution.sh --check` が差分なし
3. **バージョン/日付の整合:**
   - [ ] `docs/constitution.md` の version / 日付が正しい
   - [ ] plan-template のフッターが同じ version を指している
