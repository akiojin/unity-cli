# Quickstart: マルチインスタンス安全切替

## 起動準備
- Unity 側で 2 つのプロジェクトをポート 6400, 6401 で待受。

## 一覧表示
```
npx @akiojin/unity-cli list-instances --ports 6400,6401 --host localhost
```
期待: 両方表示、片方停止なら status=down。

## 接続先切替
```
npx @akiojin/unity-cli set-active localhost:6401
```
期待: "active: localhost:6401" と確認 ping 成功。

## 失敗ケース確認
```
npx @akiojin/unity-cli set-active localhost:6500
```
期待: 1s 以内に疎通不可エラーと再選択ガイド。
