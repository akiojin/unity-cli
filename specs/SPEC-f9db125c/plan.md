# 実装計画: Node/Unityパッケージversion不一致検出

**機能ID**: `SPEC-f9db125c` | **ステータス**: 実装完了

## 概要

Node側とUnity側のパッケージversionを比較し、不一致時に警告またはエラーを出す。挙動は環境変数で `warn` / `error` / `off` を切り替える。

## 実装状況

- ✅ Node: `UNITY_CLI_VERSION_MISMATCH` を設定として追加
- ✅ Node: Unity応答に含まれるversionを受信し、不一致を検出して通知
- ✅ Unity: 応答に含めるpackage version取得の安定性を向上
- ✅ Tests: 代表ケース（warn/error/off相当）のユニットテスト追加
- ✅ Docs: 設定項目の追加

---
*本ドキュメントは実装完了後に作成されました*
