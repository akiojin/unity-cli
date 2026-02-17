# Unity CLI Bridge

`unity-cli` 自動化ワークフロー向けの Unity エディタ連携パッケージです。

本パッケージは、GameObject のコンポーネント列挙・追加・削除・変更などのエディタ操作を `unity-cli` が利用する Unity TCP コマンドとして提供します。

## インストール

- Unity Package Manager で「Add package from Git URL...」を選択します。
- 次の URL（UPM サブフォルダ指定）を使用します。

```
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

## 特長

- コンポーネント操作: GameObject 上のコンポーネントの追加・削除・変更・一覧取得。
- 型安全な値変換: Vector2/3、Color、Quaternion、enum などの Unity 型をサポート。
- CLI/TCP コマンド向けに拡張可能なエディタハンドラ群。

## ディレクトリ構成

- `Editor/`: CLI コマンドハンドラやエディタロジック。
- `Tests/`: エディタ用テスト。
- `docs/`: ドキュメント（英語 README と日本語 README を含む）。

## ライセンス

MIT

## ライセンス帰属表示

本パッケージを再配布したり公開プロジェクトに含める場合、MIT ライセンスでは著作権表示と許諾表示の同梱が求められます。テンプレート付きの帰属表示ガイドがリポジトリルートの [`ATTRIBUTION.md`](../../../../ATTRIBUTION.md) にあります。

帰属表示の記載例:

```
本製品には akiojin が開発したソフトウェアが含まれています。
unity-cli - https://github.com/akiojin/unity-cli
MIT License に基づきライセンスされています。
```

## リポジトリ

GitHub: https://github.com/akiojin/unity-cli
