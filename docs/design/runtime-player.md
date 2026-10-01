# Development Player 接続・ランタイム操作の設計

Owner: [#446](https://github.com/akiojin/unity-cli/issues/446) / 親 SPEC [#155](https://github.com/akiojin/unity-cli/issues/155)。
状態: 設計のみ。以下の CLI、型、ファイル形式、検査用成果物は後続実装の契約であり、現在利用できる機能ではない。
本 Issue では Player の実装・ビルド・実機確認を行わない。

## 方針と範囲

D10 に従い、macOS のローカル Development Player を最初の対象とする。
後続の実機検証は Apple Silicon、Unity 2022.3.62f3 / 6000.3.25f1、Mono / IL2CPP で行う。
Windows/Linux の検証は #386、モバイル、ネットワーク越しの接続、Release Player 操作は対象外。

既存 Editor リスナーを Player に移植する案は UnityEditor 依存と広すぎる権限を持ち込む。
公式 Pipeline への全面委譲は本 CLI の独立動作と既存ツール契約を失う。
そこで Development 専用 assembly と小さな許可ツール一覧を採用し、TCP の framing、認証方式、CLI 出力契約だけを共有する。
Editor assembly を Runtime から参照せず、既存 Editor の接続選択も変更しない。

## 1. リスナーの有効化条件とコンパイル除外（AC-1a）

後続実装で `Runtime/UnityCliBridge.Runtime.asmdef` を追加する。
`defineConstraints` は `DEVELOPMENT_BUILD` と `!UNITY_EDITOR`、初期 `includePlatforms` は `OSXStandalone`。
Runtime に UnityEditor 参照を置かない。リスナー、起動フック、認証・発見処理、ハンドラーの全型を
`#if DEVELOPMENT_BUILD && !UNITY_EDITOR` で囲む。Input System 依存は別の任意 assembly とする。
Release 向け assembly や `link.xml` から Runtime 型を参照・保存しない。

起動にはコンパイル条件に加え `Debug.isDebugBuild` と環境変数
`UNITY_CLI_RUNTIME_ENABLE=1` の両方を要求する。既定は停止。
有効時のみ `UnityCliBridge.Runtime.RuntimeBridgeBootstrap` が main thread dispatcher を作り、
`UnityCliBridge.Runtime.RuntimeTcpListener` が `127.0.0.1` の OS 割当ポート（bind 時は 0）で待ち受ける。
bind と安全な port-file 公開が両方成功するまで要求を受け付けない。失敗時は閉じて理由を Player log に残す（秘密は出力しない）。

起動フックはシーン遷移で重複生成されない。シーン操作は main thread に渡す。
heartbeat と要求期限は time scale に影響されない時計を使い、time scale 0 でも通信を処理する。
正常終了では listener と仮想入力デバイスを破棄し、自分の sessionId と一致する port-file だけを削除する。

Unity の `DEVELOPMENT_BUILD` はスクリプトのビルド時点を表すため、後から実行バイナリだけを置換する方法で
Release と判定しない。非 Development 設定でスクリプトからクリーンビルドし、下記の成果物検査を必須とする。
根拠: [Unity 2022.3 条件付きコンパイル](https://docs.unity3d.com/2022.3/Documentation/Manual/PlatformDependentCompilation.html)、
[assembly の制約](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AssemblyDefinitionImporter.html)。

## 2. 認証トークンの受け渡し（AC-1b）

[#440](https://github.com/akiojin/unity-cli/issues/440) の現行契約
（[architecture.md](../architecture.md)、[configuration.md](../configuration.md)）を継承する。
起動ごとに暗号学的乱数で256-bitのトークンと sessionId を生成し、port-file の `authToken` に保存する。
ディレクトリは0700、ファイルは0600。秘密を書き込む前に権限を設定し、heartbeat 更新も同じ権限の一時ファイルから atomic rename する。
他ユーザー所有、symlink、group/other 読取可能なファイルは CLI と Player の両方が拒否する。
パスの親にも同じ所有者・書込権限検査を行い、失敗を無認証接続へ降格しない。

CLI は選択した port-file を読み、4-byte big-endian 長さ + UTF-8 JSON の各要求に `authToken` を付ける。
ping も認証必須。Player はキュー投入前に定数時間比較し、欠落・不一致は `UNAUTHORIZED`。
トークンを argv、候補一覧、stdout、ログ、スクリーンショット付随情報へ出さない。
Editor 用 `UNITY_CLI_AUTH_TOKEN_FILE` は Runtime の認証元に使わず、`--runtime-path` に統一する。
Editor の移行措置 `UNITY_CLI_ALLOW_UNAUTHENTICATED=1` は Player では無効とする。

初期 Runtime 接続は CLI から直接 TCP に送る。unityd の Editor 接続キャッシュは使わない。
再接続時は port-file と sessionId を再検査する。別 session への自動切替、認証失敗時の自動再送、
結果不明の変更要求の再送はしない。同一OSユーザーによる秘密読取からの隔離は提供しない。

## 3. port-file 発見と CLI セレクタ（AC-1c）

[#362](https://github.com/akiojin/unity-cli/issues/362) の Editor lockfile と同じ camelCase、PID、heartbeat の考え方を使うが、
領域は `~/.unity-cli/runtimes/<pid>-<sessionId>.json` に分離する。
`UNITY_CLI_RUNTIMES_DIR` を双方で指定して変更可能。Editor の `editors/`、active instance、既定ポート6400へ fallback しない。
以下は schemaVersion 1 の例（authToken の値は説明用であり実際には256-bit乱数）。

```json
{
  "schemaVersion": 1,
  "kind": "runtime",
  "pid": 4242,
  "sessionId": "e503a0e6-6733-420e-b79f-772b87d10f3d",
  "productName": "RuntimeFixture",
  "buildId": "fixture-build-001",
  "host": "127.0.0.1",
  "port": 49152,
  "unityVersion": "6000.3.25f1",
  "bridgeVersion": "runtime-protocol-v1",
  "developmentBuild": true,
  "heartbeatAt": 1790899200.0,
  "authToken": "<redacted>"
}
```

heartbeat は5秒間隔、120秒超または死んだPIDは stale（#362 と同じ閾値）。
PID再利用は sessionId を認証済み ping の応答と照合して検出する。
ping は kind、sessionId、buildId、developmentBuild、protocolVersion、capabilities を返す。
違う kind / session、未知 schema、非loopback、欠落フィールドは接続対象にしない。
自動発見では不正ファイルを秘密なしの警告で除外し、明示ファイルではエラーとして返す。
異常終了した port-file は候補から除外し、別プロセスのファイルを勝手に削除しない。

提案 CLI（まだ未実装）:

```bash
unity-cli --runtime 'RuntimeFixture*' system ping
unity-cli --runtime-path "$HOME/.unity-cli/runtimes/4242-<sessionId>.json" system ping
unity-cli --runtime 'RuntimeFixture*' raw find_gameobject --json '{"name":"Cube"}'
```

`--runtime <pattern>` は productName に対する大小文字区別ありの全体一致 glob（`*` と `?` のみ）。
シェルに展開させないため quote する。生存する候補が1件なら選択、0件は `RUNTIME_NOT_FOUND`、
複数は `AMBIGUOUS_RUNTIME` と `data.candidates`（pid、sessionId、productName、buildId、port、portFile のみ）。
`--runtime-path <path>` は1つの JSON ファイルを直接指定し、同じ検査を省略しない。
両 runtime セレクタの同時指定、および明示 `--project-path` / `--host` / `--port` との併用は `INVALID_ARGUMENT`。
Runtime 指定時は Editor 関連の環境変数・cwd・active instance を参照しない。
Runtime 指定がなければ既存 Editor 解決順のまま。公式 CLI の runtime 優先とは異なり、混在指定を明示的に拒否する。

## 4. 初期ツールと Editor 版との差分（AC-1d）

各候補の最終利用可否は ping の capabilities で決め、許可一覧以外は raw 経由でも拒否する。
既存ツールは同じ名称を使い、Player固有の制約をサーバーでも検証する。
`set_time_scale` は新規のRuntime専用ツール案であり、既存Editorツールではない。

| ツール候補                                   | Player の初期範囲                                       | Editor 版との差分・拒否条件                                                                                                                   |
| -------------------------------------------- | ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `ping`                                       | 認証済みの生存・session・能力応答                       | Editor projectPath や編集中状態を返さない                                                                                                     |
| `get_gameobject_details` / `find_gameobject` | ロード済みシーンのオブジェクト、inactive を明示指定可能 | AssetDatabase、Prefab asset、未ロードシーンは検索しない。IDは同一session内のみ有効                                                            |
| `modify_gameobject`                          | transform の position / rotation / scale のみ           | rename、追加、削除、component編集、Undo、asset保存は不可。重複パスは拒否、scene handle + instance ID を使用                                   |
| `set_time_scale`                             | 有限値0〜10の設定、変更前後値を返す                     | Editor pause / step は提供しない。範囲外は `INVALID_ARGUMENT`                                                                                 |
| `capture_screenshot`                         | Player の描画済みフレームをPNG化                        | SceneView、Editor window、OSデスクトップ fallback は不可。画像はCLI側で保存し、Player任意パス書込を許さない。描画不能は `PRECONDITION_FAILED` |
| `input_keyboard` / `input_mouse`             | Input System が有効な場合の仮想デバイスイベント         | OS全体への入力注入、旧Input Manager、touch/gamepadは初期対象外。切断・期限切れで押下を解除                                                    |

上表の screenshot / input の payload は後続実装で既存 `tools` カタログと照合し、
非互換がある場合は capabilities に Runtime 専用 schema を公開する。既存 Editor payload を黙って別解釈しない。
対象不在、破棄済みID、同名複数、シーン遷移中は変更前に検出する。
`eval_csharp`、ファイル操作、ビルド、アセット操作、テストランナー、hot reload は初期許可一覧に含めない。

## 5. 終了コードとエラー envelope（AC-1e）

[#441](https://github.com/akiojin/unity-cli/issues/441) の `src/app/output.rs` と
`src/core/failure.rs` を正本とし、text / JSON で同じ終了コードを使う。
Bridgeの応答は `data` に保持し、CLI は `success`、`command`、`data`、`errors`、`warnings` の envelope に包む。

| 状態                                                  | code                           | 終了コード |
| ----------------------------------------------------- | ------------------------------ | ---------- |
| 成功                                                  | errors は空                    | 0          |
| 内部の分類不能エラー                                  | `GENERAL_ERROR`                | 1          |
| セレクタ競合、payload不正                             | `INVALID_ARGUMENT`             | 2          |
| トークン欠落・不一致、資格情報ファイルの権限不正      | `UNAUTHORIZED`                 | 3          |
| capability不在、描画不能、未知protocol、session不一致 | `PRECONDITION_FAILED`          | 4          |
| 複数候補                                              | `AMBIGUOUS_RUNTIME`            | 6          |
| 対象オブジェクト不在、操作失敗、実行期限切れ          | `OPERATION_FAILED` / `TIMEOUT` | 6          |
| 候補なし・明示port-fileなし、stale                    | `RUNTIME_NOT_FOUND`            | 7          |
| 接続拒否・切断・接続タイムアウト                      | `RUNTIME_UNREACHABLE`          | 7          |

新しい `RUNTIME_NOT_FOUND` / `RUNTIME_UNREACHABLE` は後続で `code_exit` と直接TCPの分類に追加する必要がある。
現行実装の未知codeは6になるため、文書追加だけで7になるとは扱わない。
終了コード8は既存のテスト失敗用として予約し、Player初期ツールには使わない。
明示port-fileの構文不正は `INVALID_ARGUMENT`、OS読込失敗は `PRECONDITION_FAILED`。

```json
{
  "success": false,
  "command": "system ping",
  "data": { "candidates": [] },
  "errors": [{ "code": "RUNTIME_NOT_FOUND", "message": "No live Development Player matched" }],
  "warnings": []
}
```

## 6. Release 成果物に対する除外検査（AC-2）

後続実装では同じコミット・シーン・backendから Development / Release を別の空の出力先へビルドする。
Release は Development Build OFF とし、キャッシュ済み Development managed DLL を流用しない。
Runtime assembly 内の全型を `UnityCliBridge.Runtime` 名前空間に置き、検査対象の命名規約とする。
両Unityバージョン・両backendの組合せごとに次を実行する。現在のIssueでは実行結果を主張しない。

以下は macOS の bash、Python 3、`rg` を用いる成果物検査コマンド。
`dev` / `release` はその組合せの実際の `.app` に置き換える。

```bash
set -euo pipefail
dev='/absolute/path/Development/RuntimeFixture.app'
release='/absolute/path/Release/RuntimeFixture.app'
python3 - "$dev" "$release" <<'PY'
import pathlib
import sys

markers = [b'UnityCliBridge.Runtime', b'RuntimeTcpListener', b'RuntimeBridgeBootstrap']
for root, expected in [(pathlib.Path(sys.argv[1]), True), (pathlib.Path(sys.argv[2]), False)]:
    assert root.is_dir(), f'missing build: {root}'
    files = [p for p in root.rglob('*') if p.is_file()]
    assert files, f'empty build: {root}'
    hits = []
    for path in files:
        data = path.read_bytes()
        if any(m in data or m.decode().encode('utf-16-le') in data for m in markers):
            hits.append(str(path.relative_to(root)))
    print(root, 'runtime marker files:', hits)
    assert bool(hits) == expected, f'Runtime exclusion/positive control failed: {root}'
    if not expected:
        assert not list(root.rglob('UnityCliBridge.Runtime*.dll'))
PY
```

Mono は managed DLL のファイル名と metadata 内の型名、IL2CPP は `.app` 内の
`global-metadata.dat` と native binary を含めて検査する。Development側の陽性対照が必須で、
strip・難読化・暗号化等により文字列が観測できない場合は合格にしない。
文字列不在だけでは機械語の不在を証明できないため、後続ビルドランナーは
Unity BuildReport のコンパイル済み assembly 一覧と IL2CPP 入力/生成C++を検査用artifactとして保存する。
以下で生成物も確認する（ディレクトリはそのビルドの保存先。存在・非空を先に確認する）。

```bash
release_cpp='/absolute/path/evidence/release/il2cppOutput'
test -d "$release_cpp"
test -n "$(rg --files "$release_cpp")"
if rg -n 'UnityCliBridge[._]Runtime|RuntimeTcpListener|RuntimeBridgeBootstrap' "$release_cpp"; then
  exit 1
else
  result=$?
  test "$result" -eq 1
fi
```

MonoではReleaseのassembly一覧に Runtime が無いこと、IL2CPPでは入力一覧にも無いことを確認する。
保存artifact不足や検索エラーは検査失敗。`nm` 単独はstrip後の偽陰性があるため証明として使わない。
最後に実 Release Player を `UNITY_CLI_RUNTIME_ENABLE=1` 付きで起動し、
`lsof -nP -a -p <PLAYER_PID> -iTCP -sTCP:LISTEN` で Bridge の待受がなく、
隔離した `UNITY_CLI_RUNTIMES_DIR` に port-file が生成されないことを確認する。
Development の同条件ではport-file作成と認証ping成功を確認し、環境不備による偽のPASSを防ぐ。

## 7. 公式 Runtime Pipeline との関係と安全制約（AC-3）

D2 は「補完＋上位互換」という製品方針であり、公式のwire protocolやport-fileの互換性を約束しない。
公式の [integration-advanced.md](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/integration-advanced.md)
は `unity command ... --runtime` / `--runtime-path` によるPlayer選択を説明している（2026-10-02参照）。
本案は似た選択語彙を使うが、自前の `kind: runtime` と認証付きファイルだけを読む。
公式の Runtime Pipeline の設定、登録コマンド、port-file は変更せず、独立したポートで共存する。
公式利用中のプロジェクトへ本 listener を自動導入しない。両方が同じゲーム状態を変更するテストでは
テストランナーが操作を直列化し、どちらの接続を使ったか記録する。

補完する価値は既存 unity-cli のツール契約とスキルからの実機検証である。
公式の全Runtimeコマンド・plugin登録機構を初期段階で再実装する意味ではない。
Developmentでも明示有効化・loopback限定・毎要求認証・許可一覧を必須とし、
任意コード実行、外部公開、無認証opt-outを提供しない。Releaseでの除外は起動時判定だけに頼らない。

## 8. 後続 Issue 案（AC-4）

以下は登録前の案。PMが重複検索して既存Issueへ統合または登録する。本Issueでは登録・実装しない。
すべて macOS 実機（Apple Silicon、Unity 2022.3.62f3 / 6000.3.25f1）確認を受け入れ条件とする。

### A. feat(runtime): Development Player の認証付きlistenerとRelease除外

- Runtime asmdef、起動opt-in、loopback、256-bit認証、0700/0600 port-file、heartbeat、終了cleanupを実装する。
- 両Unity・Mono/IL2CPPの実 Playerで正しいtokenのping成功、欠落/不一致拒否、opt-out無効を証明する。
- 第6節のRelease検査、Development陽性対照、assembly一覧とIL2CPP生成物をartifactとして保存する。
- 公式 Pipeline 共存時も別ポート・別発見領域となり、Editor接続が回帰しない。

### B. feat(cli): Runtime セレクタと共通エラー契約

- A完了後、`--runtime` / `--runtime-path` と明示セレクタ競合検出を実装する。
- 両UnityのmacOS Playerを複数起動し、0/1/複数候補、stale、PID再利用、未知schema、権限不正を検証する。
- 直接TCP、各要求認証、秘密の非表示、JSON/textの終了コード一致、Editor fallback不発生を確認する。
- #441 の新Runtime codeを7に分類するテストと、既存Editor/daemon経路の回帰テストを通す。

### C. feat(runtime): 最小操作ツールと実 Player E2E

- A/B完了後、第4節の許可一覧、payload/capability schema、main thread実行を実装する。
- 両UnityのmacOS実機で検索/transform/time scale 0からの復帰/PNG取得/Input Systemイベントを検証する。
- inactive・破棄済みID・シーン遷移・同名複数・描画不能・Input System未導入を安定codeで拒否する。
- 切断/期限切れ後の仮想入力解除、任意コード/asset操作拒否、秘密の非表示を検証する。
- Release除外マトリクスを再実行し、全Runtimeツール追加後も成立する証跡を残す。
