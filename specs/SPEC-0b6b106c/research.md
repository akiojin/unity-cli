# Research: SerializeField 値更新ツール

## 1. SerializedPropertyパス表記
- **決定**: MCP入力では人が読みやすい `foo.bar[0].baz` を受け取り、Unity側では `.Array.data[n]` を含む正規化パスを使用する。
- **理由**: LLMや人間にとって `[n]` 記法が直感的であり、Unityの `SerializedProperty` APIのみが `.Array.data[n]` を要求するため。
- **検討した代替案**: 入力段階から `.Array.data[n]` を強制 → 可読性が下がり、ユーザーエラーが増えるため却下。

## 2. Prefab保存フロー
- **決定**: Prefab資産を直接編集した場合は `PrefabUtility.SaveAsPrefabAsset` を実行し、Prefab Stageやシーン上のインスタンスには `PrefabUtility.RecordPrefabInstancePropertyModifications` を適用する。
- **理由**: SaveAsPrefabAssetはアセット書き込みを保証し、Stage/シーンでは記録のみで十分。
- **検討した代替案**: すべてApplyPrefabInstanceで統一 → シーンインスタンスに不要なApplyが走り履歴が汚れるため却下。

## 3. Play Mode制約
- **決定**: Play Mode中は `runtime:true` が明示されていない限り、SerializeField更新を拒否し、構造変更 (親子変更/レイヤー) は常に禁止する。
- **理由**: Runtime用の変更は一時的であり、Undo/ドメインリロードに依存する。意図確認が必須。
- **検討した代替案**: Play Modeでも常に許可 → 予期せぬゲーム状態破壊リスク大。

## 4. dry-run出力
- **決定**: dry-runは `dryRun: true`, `previewValue`, `previousValue`, `resolvedPath`, `notes[]` を返し、実オブジェクトを変更しない。
- **理由**: 安全性のため preview を提供し、後続の本適用とレスポンス形式を揃えられる。
- **検討した代替案**: dry-run専用レスポンスにする → クライアント実装が複雑になるため却下。

## 5. 参照型の指定方法
- **決定**: `objectReference.assetPath` または `objectReference.guid` を受け取り、なければ `value` に直接パスを受け付ける。GUID優先で解決に失敗した場合は詳細なエラーを返す。
- **理由**: GUID経由が最も安定しており、既存のアセット参照ワークフローとも整合する。
- **検討した代替案**: InstanceIDを直接受け取る → セッションを跨ぐと無効になる。

## 6. ログポリシー
- **決定**: `set_component_field` カテゴリで JSON構造ログ (target, scope, dryRun, result/ error) を出力し、Undo登録状況も記録する。
- **理由**: 監査性とトラブルシュート性を確保し、遠隔操作を追跡可能にするため。
- **検討した代替案**: 既存ログに文字列連結で追記 → 解析しづらく目的を達成できない。
