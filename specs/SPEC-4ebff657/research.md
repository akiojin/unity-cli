# Research: Unity メニュー起動とサンプル (SPEC-4ebff657)

## 決定
- メニューは `MCP Server/Start`, `Stop`, `Run Sample (Scene)`, `Run Sample (Addressables)` を追加。
- EditorWindow で状態表示とトグル (HTTP/stdio, Telemetry on/off) を持たせ、設定は EditorPrefs に保存。
- 外部起動は `ProcessStartInfo` で `npx @akiojin/unity-cli` を呼び、ログを Unity コンソールへストリーミング。
- サンプルは既存シーンに影響を与えないよう一時 GameObject/Addressables グループを作成し、実行後にクリーンアップ。

## 未解決/要確認
- Windows 環境での npx パス解決（`cmd /c` 必要性）。
- Addressables 無効プロジェクトでのサンプルスキップ方法（警告表示か自動有効化か）。

## 参考
- Unity Editor coroutines より EditorApplication.update でポーリングし、プロセス終了を検知。
- Play Mode 中のメニュー無効化は `Menu.SetChecked/RemoveMenuItem` ではなく `Validate` 属性で制御。
