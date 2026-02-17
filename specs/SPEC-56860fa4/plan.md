# 実装計画: UI自動化・操作機能

**機能ID**: `SPEC-56860fa4` | **ステータス**: 実装完了

## 概要

uGUI / UI Toolkit / IMGUI を横断して、既存のUI操作ツール（検索/クリック/値設定/状態取得/複合シーケンス）で扱えるようにする。

## 実装状況

- ✅ UI識別（`elementPath`）の共通化
  - uGUI: GameObject階層パス
  - UI Toolkit: `uitk:<UIDocument path>#<VisualElement.name>`
  - IMGUI: `imgui:<controlId>`（登録制）
- ✅ UIルーティング実装（既存ツール名は維持）
- ✅ IMGUI制御対象のレジストリ追加（OnGUI側で登録）
- ✅ 検証用シーン（3系統同居）追加
- ✅ 自動テスト追加（EditorテストでIMGUI操作・ルーティング確認）
- ✅ Claude Code Skills 更新（`elementPath`の新形式を追記）

## 参考実装

実装詳細については `spec.md` の「参考実装」セクションを参照してください。

---
*本ドキュメントは要点の更新を含みます*
