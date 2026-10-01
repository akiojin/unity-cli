AudioClip の取り込みから AudioMixer 経由の再生確認までを一貫して実行する
`unity-audio-setup` スキルを追加します。既存の AudioImporter / Mixer 操作と
公開 API の eval を使い、保存・再読み込み後の参照、Play 中の `isPlaying` と
再生時間の進行、公開音量の変更・復元まで確認します。

正本、Claude/Codex のリンク、一覧、隣接スキルの境界と routing 8 ケースを追加。
CLI / bridge の機能追加はありません。

検証: Rust 589 tests、routing 単体 10 tests、skills lint 22 skills / 0 violations、
fmt / clippy / Markdown lint PASS。routing は新規 8/8、全 212 件の閾値 PASS。
macOS Apple Silicon 上の Unity 2022.3.62f3 / 6000.3.25f1 で Claude Code の
実 Skill 呼び出しによるシナリオを検証済みです。

証跡: [受け入れレポートと作成アセット](./docs/verification/issue-380/README.md)。
各 Editor の Mixer アセット、AudioSource 設定、Play 中の実測値、全コマンド出力、
スキルハッシュ、Claude Code の Skill 呼び出しを含みます。

独自著作: 公式 unity-agent-plugin の `skills/` のテキスト・コードは流用していません。
このリポジトリの契約・ツールスキーマ・既存実装に基づいて独自に作成しました（D6）。
レビュー時に AC-4 の独自著作確認をお願いします。

User Verification Result: n/a (autonomous)

Refs #380; parent #370 / SPEC #160. AC-2 の PR 添付と AC-4 のレビュー確認は
この PR で完了させます。親 SPEC の記録（AC-5）は既存コメントを維持します。
