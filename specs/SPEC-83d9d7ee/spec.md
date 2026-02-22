# 機能仕様書: Rust版 unity-cli 置換・UPM統合・TDD整備

**要件ID**: `SPEC-83d9d7ee`  
**作成日**: 2026-02-17  
**ステータス**: 実装完了
**関連文書**: [ベースライン方針と差分棚卸し](baseline-policy.md)
**入力**: ユーザー説明: "unity-cli を CLI 化し、Node.js から Rust へ置換。仕様と TDD も整備する。UPM同梱・Cargo配布・LSP同梱も行う"

## ユーザーシナリオ＆テスト *(必須)*

### ユーザーストーリー1 - Rust CLIでUnityコマンドを直接実行したい (優先度: P1)

開発者が Node.js 実行基盤を介さず、Rust 製 `unity-cli` から Unity TCP に直接接続してコマンドを実行したい。

**この優先度の理由**: 基盤置換の中心価値であり、これが無いと移行は成立しないため。

**独立テスト**: `unity-cli raw ping --json '{}'` が Unity TCP 応答を受け取れることを確認する。

**受け入れシナリオ**:

1. **前提** Unity が TCP 待受中、**実行** `unity-cli system ping`、**結果** ping 応答が表示される。  
2. **前提** 任意ツール名とJSONパラメータを指定、**実行** `unity-cli raw <tool> --json {...}`、**結果** Unity 応答が JSON で返る。

---

### ユーザーストーリー2 - マルチインスタンスを安全に切り替えたい (優先度: P1)

複数 Unity インスタンス運用時に、`list` と `set-active` で接続先を安全に管理したい。

**この優先度の理由**: 誤送信防止は自動化導入時の必須要件であり、運用事故を直接防ぐため。

**独立テスト**: 到達可能ポート/到達不能ポートを使って、`instances list` と `instances set-active` の成功/失敗を確認する。

**受け入れシナリオ**:

1. **前提** 指定ポートに Unity が待受中、**実行** `unity-cli instances list --ports 6400`、**結果** `status=up` が返る。  
2. **前提** 到達不能ポート指定、**実行** `unity-cli instances set-active host:port`、**結果** `unreachable` エラーで失敗する。

---

### ユーザーストーリー3 - 仕様とTDD前提で保守できる状態にしたい (優先度: P2)

将来の機能追加時に、仕様/計画/タスクとテストが揃った状態で開発を継続したい。

**この優先度の理由**: CLI置換は長期施策のため、仕様欠落やテスト不足を残すと回帰リスクが高くなるため。

**独立テスト**: `spec.md / plan.md / tasks.md` が存在し、`cargo test` が通ることを確認する。

**受け入れシナリオ**:

1. **前提** 新しい CLI 機能を追加する、**実行** tasks.md の RED→GREEN→REFACTOR 手順に従う、**結果** 仕様と実装の追跡可能性を維持できる。  
2. **前提** 変更を加える、**実行** `cargo test` を実行、**結果** 追加した回帰テストが常に通る。

---

### ユーザーストーリー4 - Unity側パッケージとCargo配布を同時に運用したい (優先度: P1)

`unity-cli` リポジトリだけで CLI 本体・Unity UPM パッケージ・LSP を一体で保守し、`cargo install` と UPM URL の両方を提供したい。

**この優先度の理由**: 今後の保守対象が `unity-cli` のみであり、配布導線の分断を避ける必要があるため。

**独立テスト**: `unity-cli/README.md` に Cargo install 手順と UPM URL が記載され、`UnityCliBridge/Packages/unity-cli-bridge/package.json` のメタデータが `akiojin/unity-cli` を参照することを確認する。

**受け入れシナリオ**:

1. **前提** Unity Package Manager で Git URL 指定、**実行** `https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge` を追加、**結果** `com.akiojin.unity-cli-bridge` が導入できる。  
2. **前提** Rust 環境がある、**実行** `cargo install unity-cli`（または git install）、**結果** `unity-cli` コマンドを実行できる。  
3. **前提** LSP 連携が必要、**実行** `UNITY_CLI_LSP_MODE=auto` で script/index 系を呼び出す、**結果** LSP利用またはRust実装フォールバックで処理できる。

### エッジケース

- Unity から未フレームJSONが返る場合でも、CLI は応答を安全に解釈できること。  
- `--json` にオブジェクト以外（配列/数値など）が渡された場合、送信前に失敗させること。  
- `instances` レジストリファイルが欠落/破損していても、再初期化して継続できること。

## 要件 *(必須)*

### 機能要件

- **FR-001**: `unity-cli` は Rust 実装として提供され、`raw`, `tool call`, `system ping`, `scene create`, `instances list`, `instances set-active` を実行できる必要がある。  
- **FR-002**: Unity 通信は TCP 4byte length + JSON の既存フレーミング互換で送受信する必要がある。  
- **FR-003**: 応答は `--output text|json` で切替可能で、JSONモードでは機械可読形式で出力する必要がある。  
- **FR-004**: 設定は `UNITY_CLI_*` を優先しつつ `UNITY_MCP_*` 互換入力も受理する必要がある。  
- **FR-005**: インスタンス切替情報はローカルレジストリに保存し、`list` で active 状態を表示できる必要がある。  
- **FR-006**: 仕様ドキュメントとして `spec.md`, `plan.md`, `tasks.md` を作成し、移行方針と作業順序を定義する必要がある。  
- **FR-007**: TDD 準拠のため、少なくともパラメータ検証・TCP応答処理・インスタンス切替に対する自動テストを実装する必要がある。  
- **FR-008**: 呼び出し方法を Codex/Claude 双方のスキルとして提供する必要がある。
- **FR-009**: `unity-cli` リポジトリ内に Unity UPM パッケージ（`UnityCliBridge/Packages/unity-cli-bridge`）を同梱する必要がある。
- **FR-010**: Unity 側の主要公開名は `UnityCliBridge` を使用せず、`UnityCliBridge` 系名称へ統一する必要がある。
- **FR-011**: LSP 実装は `unity-cli/lsp` として同梱し、Rust CLI から `UNITY_CLI_LSP_MODE` で利用可否を制御できる必要がある。
- **FR-012**: Cargo 配布要件として `cargo install unity-cli` 導線を README と `Cargo.toml` メタデータで提供する必要がある。
- **FR-013**: 性能方針として script/index 系ローカル処理は Rust 実装を維持し、LSP 失敗時は即時フォールバックして性能劣化を回避する必要がある。

### 主要エンティティ

- **RuntimeConfig**: CLI 実行時の host/port/timeout を保持し、環境変数とCLI引数を統合する。  
- **UnityClient**: Unity TCP への接続、フレーム送信、応答正規化を担う。  
- **InstanceRegistry**: active 接続先と既知インスタンスを管理するローカル永続状態。

## 成功基準 *(必須)*

### 測定可能な成果

- **SC-001**: `cargo test --manifest-path unity-cli/Cargo.toml` が成功し、CLI コア挙動の回帰テストが自動実行される。  
- **SC-002**: `unity-cli --help` で主要サブコマンドが表示される。  
- **SC-003**: `instances list` は到達可能ポートを `up`、到達不能ポートを `down` と識別できる。  
- **SC-004**: 旧repo README に移行ガイドとコマンド対応表が記載される。
- **SC-005**: `unity-cli/README.md` に Cargo install 手順が記載される。
- **SC-006**: `UnityCliBridge/Packages/unity-cli-bridge/package.json` の `name` が `com.akiojin.unity-cli-bridge` で、`repository` が `akiojin/unity-cli` を指す。
- **SC-007**: `UNITY_CLI_LSP_MODE=off`（既定）で既存 `cargo test` が成功し、`UNITY_CLI_LSP_MODE=auto|required` で LSP 経路を選択可能である。
