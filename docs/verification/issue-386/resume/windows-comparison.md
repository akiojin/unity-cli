# Windows 公式CLI比較の計測値

Windows 11 x64 / Unity6000.4.4f1 / unity-cli・Bridge0.18.1 debug / official1.0.0-beta.11 / Pipeline0.8.0-exp.1。

各操作は3回のwarmup後に100回、両ツールの順を交互にした。背景はEditorの最小化条件であり、macOSの表示した背景とは異なる。
ほかのプロジェクトの正式検証がhostで稼働中だった。性能予算への合格や過去のbaselineとの同一条件比較ではない。

プロセス方式は呼出ごとに起動、常駐方式は同じunitydと同じ公式NDJSON shellを保持する。
全条件で同一プロジェクトとEditorを確認した。バイナリ・scriptのSHA256とfocusによる除外数は各JSONに保持する。

## process / 前面

[全サンプル・条件JSON](official-windows-process-frontmost.json)。単位: ms。

| 操作 | unity-cli p50 | unity-cli p95 | 公式 p50 | 公式 p95 |
| --- | ---: | ---: | ---: | ---: |
| `create_gameobject` | 152.56 | 772.00 | 566.91 | 1400.40 |
| `hierarchy` | 41.16 | 677.94 | 616.34 | 1109.08 |
| `eval` | 1749.75 | 3522.45 | 736.40 | 1401.74 |

## process / 最小化

[全サンプル・条件JSON](official-windows-process-background.json)。単位: ms。

| 操作 | unity-cli p50 | unity-cli p95 | 公式 p50 | 公式 p95 |
| --- | ---: | ---: | ---: | ---: |
| `create_gameobject` | 161.36 | 713.62 | 607.91 | 1141.02 |
| `hierarchy` | 270.32 | 1009.96 | 788.11 | 1180.21 |
| `eval` | 1708.23 | 3227.09 | 668.51 | 1310.37 |

## resident / 前面

[全サンプル・条件JSON](official-windows-resident-frontmost.json)。単位: ms。

| 操作 | unity-cli p50 | unity-cli p95 | 公式 p50 | 公式 p95 |
| --- | ---: | ---: | ---: | ---: |
| `editor_state` | 223.44 | 715.67 | 735.68 | 1118.54 |
| `hierarchy` | 40.66 | 828.94 | 741.80 | 1446.67 |
| `find_gameobject` | 38.06 | 862.07 | 711.20 | 1209.85 |
| `transform` | 40.79 | 602.45 | 851.60 | 1595.39 |
| `position` | 38.34 | 598.93 | 571.22 | 1069.88 |
| `create_cube` | 39.69 | 898.57 | 631.00 | 1186.84 |
| `add_component` | 51.85 | 878.84 | 839.05 | 1353.01 |
| `remove_component` | 46.29 | 592.21 | 865.47 | 1811.90 |
| `delete_gameobject` | 40.93 | 328.88 | 618.58 | 1333.86 |
| `scene_info` | 37.65 | 334.26 | 626.42 | 1164.28 |
| `scene_save` | 131.45 | 647.33 | 731.03 | 1118.06 |
| `console` | 194.53 | 319.23 | 608.51 | 883.48 |
| `screenshot` | 96.07 | 404.13 | 696.99 | 1092.53 |
| `material_search` | 43.35 | 548.99 | 672.49 | 1200.97 |
| `asset_copy` | 100.90 | 408.95 | 773.65 | 1283.73 |
| `asset_move` | 289.69 | 593.52 | 745.94 | 1191.12 |
| `asset_delete` | 89.09 | 625.86 | 727.89 | 1127.38 |
| `import_settings` | 248.28 | 578.64 | 677.56 | 1136.85 |
| `material_modify` | 63.10 | 620.20 | 670.51 | 1219.39 |
| `time_settings` | 39.11 | 581.13 | 675.52 | 1140.79 |
| `read_csharp` | 33.51 | 596.27 | 657.45 | 1245.12 |
| `play_until_ready` | 785.33 | 1403.39 | 1372.78 | 2092.59 |
| `stop_until_ready` | 672.11 | 1207.18 | 1356.79 | 2300.94 |

## resident / 最小化

[全サンプル・条件JSON](official-windows-resident-background.json)。単位: ms。

| 操作 | unity-cli p50 | unity-cli p95 | 公式 p50 | 公式 p95 |
| --- | ---: | ---: | ---: | ---: |
| `editor_state` | 201.96 | 706.68 | 705.11 | 1094.77 |
| `hierarchy` | 103.11 | 899.01 | 691.88 | 1272.59 |
| `find_gameobject` | 106.43 | 707.95 | 692.45 | 1292.24 |
| `transform` | 107.25 | 701.11 | 895.84 | 1391.78 |
| `position` | 103.13 | 1102.66 | 694.65 | 1316.50 |
| `create_cube` | 103.87 | 902.30 | 701.23 | 1121.95 |
| `add_component` | 102.21 | 702.83 | 890.33 | 1204.97 |
| `remove_component` | 102.97 | 903.02 | 909.00 | 1511.48 |
| `delete_gameobject` | 101.15 | 602.33 | 702.73 | 1195.75 |
| `scene_info` | 100.75 | 598.80 | 686.22 | 1287.26 |
| `scene_save` | 193.74 | 506.45 | 813.59 | 1310.85 |
| `console` | 219.77 | 423.74 | 618.18 | 1146.31 |
| `screenshot` | 206.83 | 831.21 | 734.02 | 1355.40 |
| `material_search` | 200.25 | 563.19 | 715.92 | 1396.92 |
| `asset_copy` | 162.34 | 959.58 | 843.09 | 1356.51 |
| `asset_move` | 263.80 | 667.39 | 682.49 | 1277.85 |
| `asset_delete` | 335.01 | 662.07 | 684.14 | 1192.27 |
| `import_settings` | 118.93 | 914.90 | 642.20 | 1133.44 |
| `material_modify` | 139.28 | 932.20 | 653.47 | 1244.54 |
| `time_settings` | 117.84 | 914.90 | 623.08 | 1197.13 |
| `read_csharp` | 37.44 | 580.05 | 694.92 | 1302.50 |
| `play_until_ready` | 822.01 | 1466.44 | 1435.69 | 2099.78 |
| `stop_until_ready` | 737.03 | 1428.65 | 1441.33 | 2158.01 |
