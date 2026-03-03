# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md)

`unity-cli` es una CLI en Rust que permite a Claude Code controlar Unity Editor por TCP directo.
Es el sucesor de [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), redisenado desde Node.js + MCP hacia un flujo nativo con binario unico.

## Por que unity-cli

- Controla Unity desde Claude Code con skills por dominio y comandos typed.
- Usa `101` Unity Tool APIs para escenas, assets, codigo, pruebas, UI y editor.
- Binario unico con arranque rapido y bajo overhead.

## Como funciona

```text
Claude Code
  -> Skills (carga bajo demanda)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Algunas herramientas de codigo (`read`, `search`, `find_symbol`, `find_refs`, etc.) se ejecutan en local sin conexion a Unity.

## Inicio rapido

### Recomendado: plugin de Claude Code

Instala el plugin `unity-cli` desde Claude Code Marketplace:

```bash
/plugin marketplace add akiojin/unity-cli
```

Si `cargo` esta disponible, la configuracion del plugin puede instalar o actualizar `unity-cli` automaticamente.

### Codex Skills

Al usar este repositorio con Codex, los skills estan disponibles a traves de `.codex/skills/` (enlaces simbolicos a la fuente del plugin).
No se requiere configuracion adicional — solo clona el repositorio.

### Instalacion manual

```bash
cargo install unity-cli
```

URL del paquete UPM para Unity:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Comprobacion de conexion:

```bash
unity-cli system ping
```

## Skills (13)

| Categoria | Skills |
| --- | --- |
| Inicio | `unity-cli-usage` |
| Escenas y objetos | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Codigo | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime y pruebas | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |

## Ejemplos rapidos

```bash
# Conectividad
unity-cli system ping

# Crear escena
unity-cli scene create MainScene

# Crear GameObject con llamada raw
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Buscar codigo C# (herramienta local)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Ejecutar pruebas EditMode
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Configuracion

| Variable | Descripcion | Valor por defecto |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directorio con `Assets/` y `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Host de Unity Editor | `localhost` |
| `UNITY_CLI_PORT` | Puerto de Unity Editor | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Timeout del comando (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | Modo LSP (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Directorio raiz de herramientas descargadas | OS default |

No se admiten variables legacy con prefijo MCP. Usa solo `UNITY_CLI_*`.

## Documentacion

- Catalogo completo de comandos y herramientas: [docs/tools.md](docs/tools.md)
- Flujo de desarrollo y CI: [docs/development.md](docs/development.md)
- Guia de contribucion: [CONTRIBUTING.md](CONTRIBUTING.md)
- Proceso de release: [RELEASE.md](RELEASE.md)
- Plantillas de atribucion: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licencia

MIT. Consulta [ATTRIBUTION.md](ATTRIBUTION.md) para plantillas de atribucion en redistribucion.
