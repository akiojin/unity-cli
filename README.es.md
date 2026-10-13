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

### Compatibilidad del Editor

El Bridge requiere Unity 2022.3 o posterior. La matriz de macOS cubre
2022.3.62f3, 6000.0.84f1, 6000.3.25f1, 6000.4.11f1, 6000.5.3f1,
6000.6.3f1, 6000.7.0a2 y 6000.7.0b2 (E2E de aceptacion: PASS el 2026-09-30). Consulte las
[fechas y resultados de verificacion](docs/editor-compatibility.md) antes de elegir una version.
El Editor de Unity 6000.7 se ejecuta sobre Mono; los builds de Player con CoreCLR quedan fuera de esta matriz.

### Instalar o actualizar con un agente LLM

Copie este prompt en Claude Code, Codex u otro agente de programacion abierto en
su proyecto de Unity. Ejecute el mismo prompt mas adelante para actualizar juntos el CLI
y el paquete bridge de Unity:

```text
Instala o actualiza unity-cli para este proyecto de Unity, manten el paquete bridge de
Unity en la misma version que el CLI y conectalo al Unity Editor.

1. Detecta mi sistema operativo (macOS, Linux o Windows).
2. Si `unity-cli` todavia no esta en el PATH, ejecuta el script oficial de Quick Install:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. Si `unity-cli` ya esta en el PATH, actualizalo a la ultima version en su lugar.
   Si UNITY_CLI_NO_AUTO_UPDATE=1 o UNITY_CLI_VERSION estan definidos, fije la version a
   proposito: no lo actualices; dimelo y preguntame primero. En otro caso, averigua donde esta
   realmente el binario (siguiendo enlaces simbolicos) y actualizalo segun como se instalo:
   - Quick Install (copia gestionada en ~/.unity/tools): unity-cli cli install
   - Homebrew: brew upgrade akiojin/tap/unity-cli
   - winget: winget upgrade akiojin.unity-cli
   - Cualquier otro caso (cargo install, descarga manual): dime como se instalo y
     preguntame antes de cambiarlo.
4. Comprueba que `unity-cli --version` funciona desde el PATH. En macOS / Linux, si no se
   encuentra, agrega ~/.local/bin al PATH de esta shell y dime la linea que debo agregar a
   mi perfil de shell. En Windows, abre una shell nueva para aplicar el PATH de usuario actualizado.
5. Antes de modificar mi proyecto, dime que setup edita Packages/manifest.json (agrega el
   scoped registry de OpenUPM y com.akiojin.unity-cli-bridge fijado a la version del CLI) y,
   en proyectos que solo usan el Input Manager antiguo, establece activeInputHandler en Both
   en ProjectSettings/ProjectSettings.asset. Espera mi confirmacion.
6. Desde la raiz del proyecto de Unity, ejecuta: unity-cli --output json setup --launch-editor
   Si el JSON tiene ok: false, sigue editor.hint (la primera importacion puede tardar
   varios minutos; vuelve a ejecutar setup para seguir esperando).
7. Comprueba la version del bridge con: unity-cli --output json bridge status
   - Si source es registry y versionCheck.status (aqui o en el informe de setup) es mismatch:
     dime que esto cambia la linea com.akiojin.unity-cli-bridge de Packages/manifest.json a la
     version del CLI y espera mi confirmacion. Luego ejecuta unity-cli bridge upgrade y deja
     que el Unity Editor vuelva a importar el paquete.
   - Si source es custom o embedded (URL de Git, ruta local o una copia en Packages/), o
     versionCheck.status es unknown: no lo reescribas. En su lugar, informame del source y
     de ambas versiones.
8. Verifica la conexion con: unity-cli system ping
   Confirma que versionCheck.status es match (el CLI y el bridge usan la misma version).
9. Si algun paso falla, ejecuta: unity-cli doctor --output json
   Informa el codigo diagnosis y sigue su indicacion antes de reintentar.
```

**Plugin de Claude Code**: instale el plugin `unity-cli` desde Claude Code
Marketplace para que el agente pueda usar los skills de flujos de trabajo de Unity:

```bash
/plugin marketplace add akiojin/unity-cli
```

El plugin incluye skills; el binario y el bridge de Unity se preparan cuando hacen falta.
Cuando una tarea necesita Unity y falta `unity-cli`, el skill `unity-cli-usage` hace que
el agente ejecute el script de Quick Install y luego `unity-cli setup` en su proyecto de
Unity (le avisa antes de editar `Packages/manifest.json`).

**Codex Skills**: si usa este repositorio con Codex, los skills estan disponibles en
`.agents/skills/` (symlinks a la fuente del plugin, tambien expuestos en `.claude/skills/`).
No requiere configuracion adicional — solo clone el repositorio.

### Instalacion manual

#### Quick Install (recomendado)

macOS (Apple silicon / Intel) y Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

Ambos instaladores descargan el binario de la release en la estructura administrada
`~/.unity/tools/unity-cli/{rid}/` y lo verifican con los `SHA256SUMS` de la release antes
de instalar; si la suma de comprobacion no coincide, la instalacion se cancela y el binario
existente no se toca. `install.sh` crea un symlink en `~/.local/bin/unity-cli`;
`install.ps1` agrega el directorio administrado al `PATH` del usuario. Defina
`UNITY_CLI_VERSION=v0.16.0` para fijar una release. Tras la instalacion inicial, el CLI
busca actualizaciones automaticamente en segundo plano, y cada actualizacion se verifica
con las mismas sumas de comprobacion.

Si `~/.local/bin` no esta en su PATH, agregue lo siguiente a su perfil de shell
(por ejemplo `~/.zshrc` o `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Defina `UNITY_CLI_NO_AUTO_UPDATE=1` para desactivar la actualizacion automatica.

#### Gestores de paquetes

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux (Homebrew)
winget install akiojin.unity-cli       # Windows (winget)
```

Las instalaciones con gestores de paquetes se actualizan con `brew upgrade` / `winget upgrade`.
En macOS Intel, los embeddings de reference (`unity-cli reference embed-build` / `embed-search`) cargan ONNX Runtime
de forma dinamica; la formula de Homebrew instala `onnxruntime`; en otro caso ejecute
`brew install onnxruntime` o defina `ORT_DYLIB_PATH`.

#### Releases o cargo install

Descargue el binario mas reciente desde [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) o instale desde un checkout local:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### Configuracion del proyecto

Configuracion del proyecto en un solo comando (comprobacion del binario → instalacion del bridge → conexion con el Editor):

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` agrega el scoped registry de OpenUPM y `com.akiojin.unity-cli-bridge`
(fijado a la version del CLI) a `Packages/manifest.json`, opcionalmente inicia el Editor
de Unity Hub del proyecto, espera al bridge y devuelve un informe JSON.
`unity-cli bridge install | upgrade | status` administran el paquete por separado;
`install` es idempotente, y `setup` / `system ping` informan un `versionCheck` cuando las
versiones del CLI y del bridge difieren. Si el proyecto aun usa solo el Input Manager
antiguo y el Editor esta cerrado, `install` tambien establece `activeInputHandler` en Both
en `ProjectSettings/ProjectSettings.asset`, para que la dependencia de Input System se
importe sin un aviso de reinicio bloqueante.

Paquete bridge del lado de Unity, alternativas manuales (elija una):

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### Comprobacion de conexion

```bash
unity-cli system ping
unity-cli doctor --output json   # si ping falla: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### Mantenimiento del binario administrado

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` y `lspd` actualizan automaticamente sus binarios administrados de `unity-cli` / C# LSP al iniciar el daemon.
Las copias administradas residen en `UNITY_CLI_TOOLS_ROOT` (o en el directorio de tools predeterminado del SO) y se actualizan sin confirmacion interactiva.

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
- Comparacion con el Unity CLI oficial (funciones, medicion de latencia, uso conjunto): [docs/comparison.md](docs/comparison.md)
- Flujo de desarrollo y CI: [docs/development.md](docs/development.md)
- Guia de contribucion: [CONTRIBUTING.md](CONTRIBUTING.md)
- Proceso de release: [RELEASE.md](RELEASE.md)
- Plantillas de atribucion: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licencia

MIT. Consulta [ATTRIBUTION.md](ATTRIBUTION.md) para plantillas de atribucion en redistribucion.
