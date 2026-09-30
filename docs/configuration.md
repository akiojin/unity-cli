# Configuration Guide

This guide covers connection settings for local, Docker, WSL2, and
multi-instance workflows.

## Core Variables

| Variable                  |                Default | Use                                                                    |
| ------------------------- | ---------------------: | ---------------------------------------------------------------------- |
| `UNITY_PROJECT_ROOT`      |            auto-detect | Unity project directory; same as `--project-path` (selects the Editor) |
| `UNITY_CLI_HOST`          |            `127.0.0.1` | Hostname used by the CLI to reach the Unity TCP listener               |
| `UNITY_CLI_PORT`          |                 `6400` | Unity TCP listener port                                                |
| `UNITY_CLI_TIMEOUT_MS`    |                `30000` | Command timeout in milliseconds                                        |
| `UNITY_CLI_REGISTRY_PATH` |          OS config dir | Optional path for the instance registry                                |
| `UNITY_CLI_EDITORS_DIR`   | `~/.unity-cli/editors` | Editor lockfile directory (set the same value for Unity and the CLI)   |

Unity-side listener settings live at `Edit -> Project Settings -> Unity CLI Bridge`.
Locally you do not need to match ports: the CLI finds the Editor through its
lockfile (see [Multiple Unity Instances](#multiple-unity-instances)). Set
`UNITY_CLI_PORT` only when the CLI cannot read the lockfiles (Docker, WSL2).

The default host is the IPv4 loopback literal `127.0.0.1`, not `localhost`. The
Unity listener binds IPv4, while `localhost` resolves to `::1` first on Windows,
which adds about 2 seconds to every new connection. If you set the Unity-side
host to `::1` or `::`, set `UNITY_CLI_HOST=::1`.

## Docker To Host Unity

When `unity-cli` runs inside Docker and Unity Editor runs on the host, the default
`127.0.0.1` inside the container is the container itself. Point `UNITY_CLI_HOST` at the host:

```bash
docker run --rm \
  -e UNITY_PROJECT_ROOT=/workspace/UnityCliBridge \
  -e UNITY_CLI_HOST=host.docker.internal \
  -e UNITY_CLI_PORT=6400 \
  -v "$PWD":/workspace \
  unity-cli-dev unity-cli system ping
```

On Linux Docker engines that do not provide `host.docker.internal`, add a host
gateway mapping:

```bash
docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -e UNITY_PROJECT_ROOT=/workspace/UnityCliBridge \
  -e UNITY_CLI_HOST=host.docker.internal \
  -e UNITY_CLI_PORT=6400 \
  -v "$PWD":/workspace \
  unity-cli-dev unity-cli system ping
```

If Unity is bound only to loopback, keep the default `UNITY_CLI_HOST` for local CLI
calls and use `host.docker.internal` only from containers. If your network policy
allows external container access, set the Unity-side host to `0.0.0.0` or a
specific LAN address, then restart the listener with `Apply & Restart`.

## WSL2 To Windows Unity

For WSL2 shells connecting to Unity Editor on Windows, use:

```bash
export UNITY_PROJECT_ROOT=/mnt/c/path/to/UnityCliBridge
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
unity-cli system ping
```

If DNS for `host.docker.internal` is unavailable, use the Windows host IP from
`/etc/resolv.conf` or your WSL network configuration.

## Multiple Unity Instances

Each Unity CLI Bridge writes a lockfile to `~/.unity-cli/editors/<pid>.json`
(`%USERPROFILE%\.unity-cli\editors` on Windows). The lockfile holds the pid,
project path, host, port, Unity version, state, and a heartbeat that is
refreshed every 5 seconds. It is kept, with state `reloading`, during a domain
reload and deleted when the Editor quits. When the configured port is already in
use (for example, two projects with the default 6400), the Bridge listens on the
next free port (up to +20) and records that port in the lockfile.

Commands pick the target Editor in this order:

1. `--port` / `UNITY_CLI_PORT` (with `--host` / `UNITY_CLI_HOST`), or `--host` alone
2. `--project-path <path>` / `UNITY_PROJECT_ROOT`: the Editor whose project contains the path
3. the Editor chosen with `instances set-active`
4. the deepest project containing the current directory
5. the only running Editor
6. `127.0.0.1:6400` when no lockfile exists (Bridge versions without lockfiles)

If several Editors are running and none of these selects one, the command fails
before contacting any Editor with `AMBIGUOUS_EDITOR` (exit code 6) and lists the
candidates:

```json
{
  "success": false,
  "error": { "code": "AMBIGUOUS_EDITOR", "message": "..." },
  "data": {
    "candidates": [
      {
        "projectPath": "/work/A",
        "host": "127.0.0.1",
        "port": 6400,
        "pid": 4242,
        "unityVersion": "6000.3.25f1",
        "state": "ready"
      }
    ]
  }
}
```

`--project-path` pointing at a project with no running Editor fails with
`EDITOR_NOT_FOUND`.

```bash
unity-cli --project-path ~/work/ProjectA raw get_hierarchy
unity-cli instances list                      # lockfile Editors, with project paths
unity-cli instances list --host 127.0.0.1 --ports 6400,6401,6402
unity-cli instances set-active 127.0.0.1:6401
```

`instances list` shows every lockfile Editor with `projectPath` and `pid`. An
Editor whose process is gone or whose heartbeat is older than 120 seconds is
shown as `unreachable`. `--ports` still adds endpoints manually, for example
for Bridge versions without lockfiles. Duplicate `--ports` values are ignored
and reported as a warning. Instance health checks require a Unity Bridge `ping`
response, so unrelated processes that only keep a TCP socket open are reported
as `down`.

## 設定ガイド

このガイドは、ローカル、Docker、WSL2、複数 Unity インスタンス運用の接続設定をまとめます。

### 基本環境変数

| 環境変数                  |             デフォルト | 用途                                                               |
| ------------------------- | ---------------------: | ------------------------------------------------------------------ |
| `UNITY_PROJECT_ROOT`      |               自動検出 | Unity プロジェクト。`--project-path` と同じ（Editor 選択にも使用） |
| `UNITY_CLI_HOST`          |            `127.0.0.1` | CLI から Unity TCP リスナーへ接続するホスト名                      |
| `UNITY_CLI_PORT`          |                 `6400` | Unity TCP リスナーのポート                                         |
| `UNITY_CLI_TIMEOUT_MS`    |                `30000` | コマンドタイムアウト（ミリ秒）                                     |
| `UNITY_CLI_REGISTRY_PATH` |    OS 設定ディレクトリ | インスタンスレジストリの任意パス                                   |
| `UNITY_CLI_EDITORS_DIR`   | `~/.unity-cli/editors` | Editor lockfile の配置先（Unity と CLI に同じ値を設定）            |

Unity 側の待受設定は `Edit -> Project Settings -> Unity CLI Bridge` にあります。
ローカルでは CLI が lockfile から Editor を見つけるため、ポートを合わせる必要は
ありません（[複数 Unity インスタンス](#複数-unity-インスタンス) 参照）。lockfile を
読めない環境（Docker、WSL2）でのみ `UNITY_CLI_PORT` を指定してください。

デフォルトのホストは `localhost` ではなく IPv4 ループバックのリテラル `127.0.0.1`
です。Unity 側リスナーは IPv4 で待ち受けますが、Windows では `localhost` が先に
`::1` へ解決され、新しい接続ごとに約2秒の遅延が発生するためです。Unity 側ホストを
`::1` または `::` に設定した場合は `UNITY_CLI_HOST=::1` を指定してください。

### Docker からホスト Unity へ接続する

Docker コンテナ内のデフォルト `127.0.0.1` はコンテナ自身です。Unity Editor がホスト側で
起動している場合は、CLI 側の接続先をホストへ向けます。

```bash
docker run --rm \
  -e UNITY_PROJECT_ROOT=/workspace/UnityCliBridge \
  -e UNITY_CLI_HOST=host.docker.internal \
  -e UNITY_CLI_PORT=6400 \
  -v "$PWD":/workspace \
  unity-cli-dev unity-cli system ping
```

Linux Docker で `host.docker.internal` が使えない場合は host gateway を追加します。

```bash
docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -e UNITY_PROJECT_ROOT=/workspace/UnityCliBridge \
  -e UNITY_CLI_HOST=host.docker.internal \
  -e UNITY_CLI_PORT=6400 \
  -v "$PWD":/workspace \
  unity-cli-dev unity-cli system ping
```

Unity 側が loopback のみに bind している場合、ローカルCLIでは
デフォルトの `UNITY_CLI_HOST` を使い、コンテナ内だけ `host.docker.internal` を使います。
外部コンテナからの接続を許可する場合は、Unity 側ホストを `0.0.0.0` または
特定のLANアドレスに設定し、`Apply & Restart` でリスナーを再起動してください。

### WSL2 から Windows Unity へ接続する

WSL2 シェルから Windows 側の Unity Editor へ接続する場合:

```bash
export UNITY_PROJECT_ROOT=/mnt/c/path/to/UnityCliBridge
export UNITY_CLI_HOST=host.docker.internal
export UNITY_CLI_PORT=6400
unity-cli system ping
```

`host.docker.internal` が解決できない場合は、`/etc/resolv.conf` または WSL の
ネットワーク設定から Windows ホストIPを確認して指定してください。

### 複数 Unity インスタンス

Unity CLI Bridge は Editor ごとに `~/.unity-cli/editors/<pid>.json`
（Windows は `%USERPROFILE%\.unity-cli\editors`）へ lockfile を書きます。lockfile には
pid、プロジェクトパス、ホスト、ポート、Unity バージョン、状態、5 秒ごとに更新する
heartbeat が入ります。ドメインリロード中は状態 `reloading` で残り、Editor 終了時に
削除されます。設定ポートが使用中の場合（例: 2 プロジェクトとも既定の 6400）、Bridge は
次の空きポート（最大 +20）で待ち受け、そのポートを lockfile に記録します。

コマンドの接続先は次の順に決まります。

1. `--port` / `UNITY_CLI_PORT`（`--host` / `UNITY_CLI_HOST` と併用可）、または `--host` のみ
2. `--project-path <path>` / `UNITY_PROJECT_ROOT`: そのパスを含むプロジェクトの Editor
3. `instances set-active` で選択した Editor
4. カレントディレクトリを含む最も深いプロジェクト
5. 起動中の Editor が 1 つだけならその Editor
6. lockfile が 1 つも無い場合は `127.0.0.1:6400`（lockfile 非対応の Bridge 向け）

複数の Editor が起動していてどれにも決まらない場合、どの Editor にも接続せずに
`AMBIGUOUS_EDITOR`（終了コード 6）で失敗し、候補（`projectPath`、`port`、`pid` など）を
`data.candidates` に返します。`--project-path` のプロジェクトで Editor が起動していない
場合は `EDITOR_NOT_FOUND` になります。

```bash
unity-cli --project-path ~/work/ProjectA raw get_hierarchy
unity-cli instances list                      # lockfile の Editor をプロジェクトパス付きで表示
unity-cli instances list --host 127.0.0.1 --ports 6400,6401,6402
unity-cli instances set-active 127.0.0.1:6401
```

`instances list` は lockfile の Editor を `projectPath` と `pid` 付きで表示します。
プロセスが存在しない、または heartbeat が 120 秒以上古い Editor は `unreachable` と
表示されます。`--ports` は lockfile 非対応の Bridge などのために引き続き手動で
エンドポイントを追加できます。重複した `--ports` 値は無視され、警告として報告されます。
インスタンスのヘルスチェックは Unity Bridge の `ping` 応答を要求するため、TCPソケットだけを
開いている無関係なプロセスは `down` と表示されます。
