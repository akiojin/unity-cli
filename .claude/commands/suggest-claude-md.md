---
description: 会話履歴を分析してCLAUDE.mdへの追記候補を提案します
tags: [documentation]
---

# CLAUDE.md改善提案コマンド

このコマンドは、現在の会話履歴を分析し、CLAUDE.mdに追記すべきルールや知識を自動的に提案します。

## 概要

AppBrewのたっくん氏が開発した手法に基づき、以下の3つの観点から会話を分析します：

1. **プロジェクト独自のルール**: 標準的な実装ではなく、このプロジェクト特有の方法や制約
2. **繰り返し指摘される内容**: 同じ指摘が複数回出現し、ルール化が必要なパターン
3. **複数箇所で統一すべき実装**: 関連箇所で実装を揃えるべきパターン

## 使い方

1. **コマンド実行**
   ```
   /suggest-claude-md
   ```

2. **提案内容の確認**
   - AIが会話履歴を分析し、CLAUDE.mdへの追記候補を提示します
   - 各提案には、なぜそのルールが必要かの理由も含まれます

3. **CLAUDE.mdへの適用**
   - 提案内容を確認後、以下のように指示してください：
     ```
     この提案をCLAUDE.mdに追記してください
     ```
   - または、手動で必要な部分のみをCLAUDE.mdに追記することも可能です

## 自動実行

セッション終了時（SessionEnd）またはコンテキスト圧縮前（PreCompact）に自動的にこのコマンドが実行されます。

自動実行を一時的に無効化したい場合は、環境変数を設定してください：
```bash
export SUGGEST_CLAUDE_MD_RUNNING=1
```

## スクリプト実行

以下のスクリプトが会話履歴を分析します：

```bash
scripts/suggest-claude-md.sh
```

## 参考記事

- [Claude CodeのHook機能でCLAUDE.mdの自動更新提案を実現する](https://zenn.dev/appbrew/articles/e2f38677f6a0ce)
- [CLAUDE.mdに書くべきルールを判断する仕組み](https://zenn.dev/appbrew/articles/7eb12fff5738f4)
