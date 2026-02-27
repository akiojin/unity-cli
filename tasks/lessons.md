# tasks/lessons.md

このファイルは、ユーザーからの修正や失敗から得た再発防止ルールを記録する。

## Rules

- ルールは具体的に書く（「何を」「いつ」「どう防ぐか」）
- 再発した場合はルールを更新して、曖昧な文言を削る
- 直近の作業開始前に必ず読み返す

## Entries

### 2026-02-27

- Context: Markdown Lint を `npm run lint:md` で実行した際、ローカルに `markdownlint` が存在しなかった。
- Mistake: コマンド失敗後の代替手順を標準化していなかった。
- Rule: ツールが未インストールの環境では `npx --yes <tool>` で即時フォールバックする。
- Checkpoint: `npx --yes markdownlint-cli CLAUDE.md tasks/*.md --config .markdownlint.json --ignore-path .markdownlintignore`
