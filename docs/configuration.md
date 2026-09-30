# Configuration Guide

This guide covers connection settings for local, Docker, WSL2, and
multi-instance workflows.

## Core Variables

| Variable                  |       Default | Use                                                          |
| ------------------------- | ------------: | ------------------------------------------------------------ |
| `UNITY_PROJECT_ROOT`      |   auto-detect | Unity project directory containing `Assets/` and `Packages/` |
| `UNITY_CLI_HOST`          |   `127.0.0.1` | Hostname used by the CLI to reach the Unity TCP listener     |
| `UNITY_CLI_PORT`          |        `6400` | Unity TCP listener port                                      |
| `UNITY_CLI_TIMEOUT_MS`    |       `30000` | Command timeout in milliseconds                              |
| `UNITY_CLI_REGISTRY_PATH` | OS config dir | Optional path for the instance registry                      |

Unity-side listener settings live at `Edit -> Project Settings -> Unity CLI Bridge`.
The Unity-side `Port` must match `UNITY_CLI_PORT`.

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

Use explicit port lists when several Unity Editors are running:

```bash
unity-cli instances list --host 127.0.0.1 --ports 6400,6401,6402
unity-cli instances set-active 127.0.0.1:6401
```

Duplicate `--ports` values are ignored and reported as a warning. Instance
health checks require a Unity Bridge `ping` response, so unrelated processes that
only keep a TCP socket open are reported as `down`.

## 設定ガイド

このガイドは、ローカル、Docker、WSL2、複数 Unity インスタンス運用の接続設定をまとめます。

### 基本環境変数

| 環境変数                  |          デフォルト | 用途                                               |
| ------------------------- | ------------------: | -------------------------------------------------- |
| `UNITY_PROJECT_ROOT`      |            自動検出 | `Assets/` と `Packages/` を含む Unity プロジェクト |
| `UNITY_CLI_HOST`          |         `127.0.0.1` | CLI から Unity TCP リスナーへ接続するホスト名      |
| `UNITY_CLI_PORT`          |              `6400` | Unity TCP リスナーのポート                         |
| `UNITY_CLI_TIMEOUT_MS`    |             `30000` | コマンドタイムアウト（ミリ秒）                     |
| `UNITY_CLI_REGISTRY_PATH` | OS 設定ディレクトリ | インスタンスレジストリの任意パス                   |

Unity 側の待受設定は `Edit -> Project Settings -> Unity CLI Bridge` にあります。
Unity 側の `Port` は `UNITY_CLI_PORT` と一致させてください。

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

複数の Unity Editor を起動している場合は、ポートリストを明示します。

```bash
unity-cli instances list --host 127.0.0.1 --ports 6400,6401,6402
unity-cli instances set-active 127.0.0.1:6401
```

重複した `--ports` 値は無視され、警告として報告されます。インスタンスの
ヘルスチェックは Unity Bridge の `ping` 応答を要求するため、TCPソケットだけを
開いている無関係なプロセスは `down` と表示されます。
