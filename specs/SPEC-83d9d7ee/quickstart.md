# Quickstart: SPEC-83d9d7ee

## 1. Build

```bash
cargo build --manifest-path unity-cli/Cargo.toml
```

## 1.1 Cargo install

```bash
cargo install --path unity-cli
```

## 2. Connectivity check

```bash
cargo run --manifest-path unity-cli/Cargo.toml -- system ping
```

## 3. Generic Unity command call

```bash
cargo run --manifest-path unity-cli/Cargo.toml -- raw create_scene --json '{"sceneName":"MainScene"}'
```

## 3.1 Tool catalog and direct invocation

```bash
cargo run --manifest-path unity-cli/Cargo.toml -- tool list
cargo run --manifest-path unity-cli/Cargo.toml -- tool ping --json '{}'
```

## 3.2 Local script/index workflow (Rust)

```bash
export UNITY_PROJECT_ROOT=/path/to/UnityProject
cargo run --manifest-path unity-cli/Cargo.toml -- --output json tool build_index --json '{"excludePackageCache":true}'
cargo run --manifest-path unity-cli/Cargo.toml -- --output json tool find_symbol --json '{"name":"MyClass","kind":"class","exact":true}'
cargo run --manifest-path unity-cli/Cargo.toml -- --output json tool find_refs --json '{"name":"MyClass","pageSize":20}'
cargo run --manifest-path unity-cli/Cargo.toml -- --output json tool update_index --json '{"paths":["Assets/Scripts/MyClass.cs"]}'
```

## 3.3 LSP path and mode

```bash
export UNITY_CLI_LSP_MODE=auto
export UNITY_CLI_LSP_COMMAND=\"dotnet run --project lsp/Server.csproj --configuration Release\"
```

LSP source is bundled at `unity-cli/lsp/`.

## 4. Instance safety workflow

```bash
cargo run --manifest-path unity-cli/Cargo.toml -- instances list --ports 6400,6401
cargo run --manifest-path unity-cli/Cargo.toml -- instances set-active localhost:6401
```

## 5. JSON output mode

```bash
cargo run --manifest-path unity-cli/Cargo.toml -- --output json system ping
```

## 6. TDD verification

```bash
cargo test --manifest-path unity-cli/Cargo.toml
```

LSP tests:

```bash
dotnet test unity-cli/lsp/Server.Tests.csproj
```

## 7. Dedicated repository export (T018)

```bash
./scripts/export-unity-cli-subtree.sh git@github.com:akiojin/unity-cli.git main unity-cli-v0.1.0
```
