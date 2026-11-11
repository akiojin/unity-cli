# Asset Store 新規ダウンロード方式の調査（Issue #560）

調査日: 2026-11-11。担当: テツヤ（Codex）。対象は Asset Store の
`.unitypackage` を新規取得する操作。ローカルファイルや取得済みキャッシュの
インポートは別 Issue の範囲とする。

## 推奨判断

**現時点では unity-cli に Asset Store の新規自動ダウンロードを実装しないことを推奨する。**
My Assets の内部経路は確認できたが、第三者 CLI から利用できる公開ダウンロード契約と、
自動アクセスの許可を確認できない。Editor 内から呼ぶ案でも規約上の懸念は解消しない。
技術的に呼べることと、利用・配布が許されることは別に判定する。
根拠は以下の参照ソース・公開 API・規約の各節に示す。

これは調査上の推奨であり、機能導入に関するオーナーの最終判断を代行しない。
今回配信するのは調査資料と再確認用の証跡のみ。認証処理、購入、利用規約への同意、
Asset Store API の試行、アセットのダウンロードは行っていない。

## 調査方法と対象バージョン（AC-1）

Unity が公開した UnityCsReference の正確なバージョンタグを調べた。
`master` の将来の変更に依存しないよう、参照先を commit に固定する。
ローカルの Editor に同じ 3 ビルドがあることも確認したが、実行時の動作試験や
バイナリと参照ソースの同一性検証は行っていない。結果は各タグの静的調査であり、
6000.3 / 6000.6 系列全体や将来の 7000.0 alpha の動作保証ではない。

| 対象 | 参照 commit | My Assets REST クライアント | 認証を確認したソース |
| --- | --- | --- | --- |
| 6000.3.28f1 | `efa8fffa1235e83ebc71a52b49887135e2aa5602` | [AssetStoreRestAPI][rest63] | [AssetStoreOAuth][oauth63] |
| 6000.6.8f1 | `b66d07c652a67a46f8ca9a24ea4143940b106c84` | [AssetStoreRestAPI][rest66] | [AssetStoreOAuth][oauth66] |
| 7000.0.0a16 | `cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b` | [AssetStoreRestAPI][rest70] | [UnityConnectProxy][connect70] |

取得元・ファイルごとの SHA-256・根拠行は
[source-evidence.json](verification/issue-560/source-evidence.json) に記録した。
[check_sources.py](verification/issue-560/check_sources.py) は公開 GitHub 上の固定ソース
24 ファイルを読み直し、84 個の観察項目とそのハッシュを照合する。
Unity のソース全文はリポジトリに転載していない。

### 共通するダウンロード経路

3 タグとも `UnityEditor.PackageManager.UI.Internal` の内部型を通る。

1. `AssetStoreDownloadManager.Download(IEnumerable<long>)` が規約同意状態を確認する。
2. `AssetStoreDownloadOperation.Download(bool)` が product ID からダウンロード情報を要求する。
3. `AssetStoreRestAPI.GetDownloadDetail` が設定値 `CloudPackagesApi` をホストとして
   `GET /-/api/legacy-package-download-info/{productId}` を呼ぶ。認証は Bearer。
   同意状態確認は `/-/api/terms/check`。ホストを固定文字列として推測しない。
4. 返却された URL、key、upload ID と保存先情報を利用し、内部ラッパーから
   `UnityEditor.AssetStoreUtils.Download` のネイティブ処理に渡す。

根拠: 各タグの REST ソース（上表）と、代表として 7000.0.0a16 の
[manager][manager70]、[operation][operation70]、[download info][info70]、
[wrapper][wrapper70]、[native binding][binding70]。各処理の 3 タグ分の照合結果は証跡にある。
メソッドが `public` でも、所属型は `internal` であり外部向け公開 API にはならない。
返却 URL・key を長期利用できる契約や有効期間は、このソースからは確認できない。

### 認証トークンの扱いと差分

| 対象 | 取得・更新 | 保持・破棄 | CLI への含意 |
| --- | --- | --- | --- |
| 6000.3.28f1 / 6000.6.8f1 | Editor の UnityOAuth から `packman` の認可コードを取得し、設定された Identity ホストの `/v1/oauth2/token` で交換。期限後は refresh token を使う | access / refresh token を Editor サービスで保持し、`expires_in` から 15 秒を引いて期限判定。ログイン状態の変化でキャッシュを消去 | Editor 設定由来の client credential に依存する。外部 CLI 用 OAuth とみなして再実装しない |
| 7000.0.0a16 | REST 側は `IUnityConnectProxy.GetAccessToken` を使用。proxy は `UnityConnect.instance.userInfo.accessToken` を読み、ユーザー状態変更時に再読込 | 空トークンは未ログイン等のエラー。旧 OAuth クラスの refresh / expiry 処理はこの経路にない | 旧 `AssetStoreOAuth` を前提にした reflection は移植できない。proxy から実トークンの寿命や永続保存先は確定できない |

根拠: [6000.3 OAuth][oauth63]、[6000.6 OAuth][oauth66]、
[7000.0 proxy][connect70] と各 REST ソース。
7000.0 のトークン更新全体を proxy だけで説明できるとは扱わない。
本調査はユーザートークン・client secret の値を取得・保存・表示していない。
将来の案でも CLI 引数、環境変数、ログやリポジトリへのトークン転送を前提にしない。

## 公開 API の範囲と規約（AC-2）

### 公開 API の調査結果

- Unity の [API 解説][api-glossary] は外部開発者向けの public API と内部利用の private API を区別する。
  公開された参照ソースは内部実装を読める資料であり、その全メソッドが公開契約になるものではない。
- 公式 Scripting API の [6000.3 Client][client63]、[6000.6 Client][client66]、
  [7000.0 Client][client70] は依存パッケージの Add / List / Search 等を提供するが、
  Asset Store product ID で `.unitypackage` を取得する公開メソッドは確認できない。
- `UnityEditor.PackageManager.UI.Window.Open(string)` は public な選択・表示用ヘルパーであり、
  ダウンロードを開始しない（[固定ソース][window70]、3 タグ分は証跡参照）。
  My Assets を人が操作する手順は [公式マニュアル][my-assets] にある。
- [AssetDatabase.ImportPackage][import-package] はローカルパッケージのインポート用。
  [UPM と asset package の違い][package-types] のとおり、UPM 形式の Asset Store 商品もあるが、
  `Client.Add` の存在だけでは従来の `.unitypackage` の取得を代替できない。
- UnityCsReference の [README][reference-readme] は参照用途のみで、ファイル構成がバージョン間で
  変わることを明記する。今回調べた内部 API に外部利用・互換性保証を与える資料は確認できない。

上記は公開 API の文書化範囲と規約を突き合わせた評価である。
全サービスに共通する独立した「Asset Store 公開ダウンロード API ポリシー」や、
CLI 用の公開認証・ダウンロード仕様は見つけられなかった。
文書が見つからないことだけで絶対に存在しないとは断定しない。

### 利用条件の判断材料

以下は調査日時点の公開文書の要約と、本プロジェクトへの適用に関する推論。
個別契約の有無やオーナーの最終的な規約判断は未確認である。

| 一次資料・改定日 | 確認事項 | 採用判断への含意 |
| --- | --- | --- |
| [Asset Store Terms][asset-terms]（2024-12-04）、1.3 / 3.2 / 3.3 | 資産取得は規約に従う。Unity 提供インターフェース以外のアクセスは別契約による許可が必要で、自動的なアクセスも制限される | 購入済み・無料というだけでは CLI 自動取得の許可にならない。Editor 内の内部メソッド呼出しも許可済みとは判断できない |
| 同規約 3.8、Appendix 1 EULA | 技術的保護の回避や権利侵害を禁止し、取得した資産の利用は適用 EULA に従う | トークン・ダウンロード key の抽出や共有で取得を代替しない。利用許諾と API 利用許可を混同しない |
| [Unity Terms of Service][unity-terms]（2026-06-30）、17.2(g)(l)(bb)(ff)(gg)、26 | 提供・許可されたインターフェース、文書化された利用、CLI 等の自動呼出しに対する Authorized Agentic Access と第三者統合の許可条件を規定 | 公開 C# API や通常ログインの存在だけでは自動アクセスの許可を立証できない。Unity 指定 gateway・対象の許可・認証等の条件も確認が必要 |
| [My Assets の公式操作手順][my-assets] | 所有済み商品をログイン中の ID で選び、人が Download と Import を操作する | 文書化された手動手順は代替になるが、第三者 CLI からの無人実行許可を示す資料ではない |

**判断:** 別途の許可を確認できない状態で、CLI による新規自動ダウンロードを
許される機能として提供する根拠は不足している。
Unity のインターフェースを内部的に使うこと、規約同意チェックを通ること、
アカウントに所有権があることは、第三者による自動アクセスの許可を置き換えない。

## 方式比較（AC-3）

| 案 | 実現できる範囲 | 規約・認証上の課題 | 破壊的変更・保守リスク | 推奨 |
| --- | --- | --- | --- | --- |
| Editor 内で My Assets の内部ダウンロードを開始 | 既存 Editor のログイン、保存先、進捗管理を利用できる可能性 | 自動アクセスの許可は別途必要。規約同意 UI、ログアウト、所有権エラーを扱う必要がある | 高い。内部型・DI・コールバック・ネイティブ境界に依存し、7000.0 では認証依存先が既に変更 | 現状は不採用 |
| 公開 API の範囲に限定 | Package Manager の表示・選択、UPM 操作、取得済みファイルのインポート | 新規 `.unitypackage` 取得を満たす公開 API を確認できない。自動呼出しの許可も別途評価 | 内部 reflection より依存は少ないが、新規取得は人の My Assets 操作に残る | 別の実装 Issue の境界として維持 |
| 外部 CLI が REST・トークン交換・CDN 取得を再実装 | 原理上は内部経路を模倣する案 | 非公開の認証契約、資格情報・key 管理、自動アクセスの許可が必要 | 非常に高い。サーバー・認証・配信形式の変更を CLI が直接引き受ける | 不採用 |
| 新規ダウンロードに対応しない | 手動の My Assets と、別 Issue のローカルインポートを利用 | 自動取得の新しい認証・規約境界を増やさない | 新規機能の破壊的変更は生じない。手動操作が残るという制約がある | **現時点の推奨** |

## 非対応の理由と再検討条件（AC-4）

非対応を推奨するため、新規ダウンロード機能の実装 Issue は起票しない。
理由は (1) 自動アクセスの許可が未確認、(2) product ID から取得する公開契約が未確認、
(3) 認証経路に既にバージョン差があり、内部 API の追従負担を負うため。
手動 Download を自動操作へ置き換えるコードや、認証情報の持出しを回避策にしない。

再検討は、Unity による当該 CLI 利用・配布の明示的な許可と、利用可能な API / gateway・
認証条件が確認できた場合に行う。その時点でオーナーが導入を判断し、実装 Issue に
少なくとも以下の受入条件を設ける。

- 許可の対象、適用規約、API と対応 Editor バージョンを特定する。
- CLI にユーザートークンを持ち出さず、未ログイン・未所有・未同意は失敗として返す。
- 開始・完了・取消・期限切れ・通信失敗を区別し、未知のバージョンで内部呼出しを推測しない。
- 許可された検証用商品で 3 系列の取得と失敗系を確認し、ログに資格情報や配信 key を残さない。

これらは将来の検討項目であり、本調査で実装・実機検証した機能ではない。
今回の受入証跡と再現コマンドは [検証記録](verification/issue-560/README.md) を参照する。

[rest63]: https://github.com/Unity-Technologies/UnityCsReference/blob/efa8fffa1235e83ebc71a52b49887135e2aa5602/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreRestAPI.cs
[rest66]: https://github.com/Unity-Technologies/UnityCsReference/blob/b66d07c652a67a46f8ca9a24ea4143940b106c84/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreRestAPI.cs
[rest70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreRestAPI.cs
[oauth63]: https://github.com/Unity-Technologies/UnityCsReference/blob/efa8fffa1235e83ebc71a52b49887135e2aa5602/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreOAuth.cs
[oauth66]: https://github.com/Unity-Technologies/UnityCsReference/blob/b66d07c652a67a46f8ca9a24ea4143940b106c84/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreOAuth.cs
[connect70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/Proxies/UnityConnectProxy.cs
[manager70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreDownloadManager.cs
[operation70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreDownloadOperation.cs
[info70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreDownloadInfo.cs
[wrapper70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/Services/AssetStore/AssetStoreUtils.cs
[binding70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Editor/Mono/AssetStoreUtils.bindings.cs
[window70]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/Modules/PackageManagerUI/Editor/UI/MainWindow/PackageManagerWindow.cs
[reference-readme]: https://github.com/Unity-Technologies/UnityCsReference/blob/cf583fbe6da6b9fb11e3a1ac03f23df4eb120e9b/README.md
[api-glossary]: https://unity.com/glossary/api
[client63]: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/packagemanager/client
[client66]: https://docs.unity.com/en-us/engine/6000.6/script-reference/unityeditor/packagemanager/client
[client70]: https://docs.unity.com/en-us/engine/7000.0/script-reference/unityeditor/packagemanager/client
[import-package]: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/assetdatabase/importpackage
[my-assets]: https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/managing-packages-window/upm-ui-actions-ap/upm-ui-import
[package-types]: https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/packages/upm-package-types
[asset-terms]: https://unity.com/legal/as-terms
[unity-terms]: https://unity.com/legal/terms-of-service
