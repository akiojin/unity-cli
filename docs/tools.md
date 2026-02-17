# Tools & Discovery

English | [日本語](#日本語)

`unity-cli` can invoke Unity TCP commands in two ways:

1. Typed subcommands (for common workflows)
2. Raw tool calls (for full command coverage)

## Typed Subcommands

- `system ping`
- `scene create`
- `instances list`
- `instances set-active`
- `tool list`
- `tool <name> --json '{...}'`

## Raw Tool Calls

Use `raw` when no typed subcommand exists:

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

## Local (Rust-side) Tools

The following tools run locally without Unity TCP roundtrip:

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

## Index Workflow

```bash
unity-cli --output json tool build_index --json '{"excludePackageCache":true}'
unity-cli --output json tool find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
unity-cli --output json tool find_refs --json '{"name":"MyClass","pageSize":20}'
```

## Catalog Sources (Maintainers)

- Rust catalog: `src/tool_catalog.rs`
- Local tool implementation: `src/local_tools.rs`

---

## 日本語

`unity-cli` のツール呼び出しは次の2系統です。

1. よく使う操作向けのサブコマンド
2. すべてのUnity TCPコマンドを叩ける `raw`

## サブコマンド

- `system ping`
- `scene create`
- `instances list`
- `instances set-active`
- `tool list`
- `tool <name> --json '{...}'`

## raw 呼び出し

未定義のサブコマンドは `raw` を使います。

```bash
unity-cli raw create_gameobject --json '{"name":"Player"}'
```

## ローカル実行ツール（Rust側）

以下は Unity TCP を介さずローカルで実行されます。

- `read`
- `search`
- `list_packages`
- `get_symbols`
- `build_index`
- `update_index`
- `find_symbol`
- `find_refs`

## インデックス運用

```bash
unity-cli --output json tool build_index --json '{"excludePackageCache":true}'
unity-cli --output json tool find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
unity-cli --output json tool find_refs --json '{"name":"MyClass","pageSize":20}'
```

## メンテナ向け参照先

- Rustツールカタログ: `src/tool_catalog.rs`
- ローカルツール実装: `src/local_tools.rs`
