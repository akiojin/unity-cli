# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Italiano](README.it.md)

`unity-cli` es una CLI en Rust que permite a Claude Code controlar Unity Editor por TCP directo.
Es el sucesor de [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), redisenado desde Node.js + MCP hacia un flujo nativo con binario unico.

## Por que unity-cli

- Controla Unity desde Claude Code con skills por dominio y comandos typed.
- Usa 143 runtime Unity Tool APIs y 11 herramientas Reference Cache para escenas, assets, codigo, pruebas, UI y editor.
- Binario unico con arranque rapido y bajo overhead.

## Funciones anadidas desde v0.13

Los [ejemplos ejecutables de herramientas](docs/tools.md#v013v015-executable-examples) cubren:

- Resultados de pruebas PlayMode a traves de Domain Reload; mantener teclado/gamepad, simular raton/toque, secuencias de entrada y comprobaciones de notificacion InputAction.
- Grabacion MP4, WebM y secuencia de fotogramas PNG, mas vistas previas de hot reload de metodos.
- Edicion de assets/pistas/clips de Timeline, trabajos de bake de Lighting/NavMesh/NavMeshSurface/Occlusion y builds de Player independientes con consulta de estado.

Empiece con `unity-playmode-testing` para runtime/entrada/captura/hot reload, con `unity-asset-management` para Timeline y con `unity-editor-tools` para trabajos de build/bake. Los requisitos de paquetes y escenas figuran en cada ejemplo.

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
Las comprobaciones de linea base y de todas las herramientas en Windows pasaron en 6000.3.28f1, 6000.6.8f1 y Unity 7.0 Alpha 7000.0.0a16 contra el codigo actualizado el 2026-11-10.
Este ultimo ejecuta el Editor en CoreCLR; los resultados de Windows enlazados indican la build alpha exacta, los paquetes y el alcance probado.

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
`UNITY_CLI_VERSION=v0.19.1` para fijar una release. Tras la instalacion inicial, el CLI
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
```

Las instalaciones con Homebrew se actualizan con `brew upgrade`.
El paquete de Windows para winget aun no esta publicado (#537); hasta entonces use
el comando de instalacion rapida de PowerShell anterior.
En macOS Intel, los embeddings de reference (`unity-cli reference embed-build` / `embed-search`) cargan ONNX Runtime
de forma dinamica; la formula de Homebrew instala `onnxruntime`; en otro caso ejecute
`brew install onnxruntime` o defina `ORT_DYLIB_PATH`.

#### Releases o cargo install

Instale el crate publicado desde crates.io. El flujo de release lo publica con `cargo publish`:

```bash
cargo install unity-cli
```

Tambien puede descargar el binario mas reciente desde [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) o instalar desde un checkout local:

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

## Skills (23)

Estos skills son guias de flujo de trabajo. La lista coincide con [docs/skills.md](docs/skills.md) y con los directorios del plugin en `.claude-plugin/plugins/unity-cli/skills/`.

| Categoria | Skills |
| --- | --- |
| Inicio | `unity-project-bootstrap`, `unity-cli-usage` |
| Escenas y objetos | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-2d-sprite-tilemap`, `unity-asset-management`, `unity-audio-setup`, `unity-addressables` |
| Codigo | `unity-csharp-navigate`, `unity-csharp-edit`, `unity-csharp-reference` |
| Runtime y pruebas | `unity-playmode-testing`, `unity-input-system`, `unity-ui-toolkit-build`, `unity-localization`, `unity-ui-automation`, `unity-development-loop`, `unity-vfx-graph` |
| Editor | `unity-editor-tools`, `unity-package-management`, `unity-urp-setup` |

`gh-skills-sync` vive en `dev-skills/` y es un ayudante solo de desarrollo. No forma parte del plugin.

## Ejemplos rapidos

Importe archivos `.unitypackage` locales o paquetes descargados de Asset Store sin dialogo, esperando la compilacion y Domain Reload.

```bash
unity-cli package import /absolute/path/Small.unitypackage --output json
unity-cli package cache-list --output json
unity-cli package cache-import "Small Package" --output json
# Abra My Assets, pulse Download a mano y luego importe automaticamente:
unity-cli package asset-store-import 12345 --download-timeout-ms 600000 --output json
```

Use `--cache-path` o `UNITY_CLI_ASSET_STORE_CACHE_PATH` para una cache propia.
Los ID de seleccion, los tiempos de espera y la recuperacion estan en [package import commands](docs/tools.md#unitypackage-and-asset-store-cache). `package asset-store-open [name-or-id]` y `package asset-store-wait <name-or-id>` separan la visualizacion y la espera. La descarga, el inicio de sesion y la comprobacion de propiedad siguen siendo manuales en Unity; el CLI nunca pulsa Download ni lee tokens de la cuenta de Unity.

```bash
# Conectividad
unity-cli system ping

# Crear escena
unity-cli scene create MainScene

# Crear GameObject con llamada raw
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Buscar codigo C# (herramienta local)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Inspeccionar el schema legible por maquina
unity-cli tool schema create_scene --output json

# Dry-run de llamadas que mutan estado (sin efectos secundarios)
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# Ejecutar pruebas EditMode
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Flujo GWT Spec

Las especificaciones de funciones se gestionan en GitHub Issues con la etiqueta `gwt-spec`.
El cuerpo del Issue es la fuente de `Spec`, `Plan`, `Tasks` y `TDD`.

## Contribuir

Las contribuciones externas deben apuntar a la rama `develop`. Lea
[CONTRIBUTING.md](CONTRIBUTING.md) antes de abrir un PR, en especial la politica de ramas, el CI requerido y los cambios de documentacion de skills.

## Configuracion

| Variable | Descripcion | Valor por defecto |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directorio con `Assets/` y `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Host de Unity Editor | `localhost` |
| `UNITY_CLI_PORT` | Puerto de Unity Editor | `6400` |
| `UNITY_CLI_AUTH_TOKEN_FILE` | JSON de lockfile del Editor con `authToken` para credenciales remotas o montadas | *(unset)* |
| `UNITY_CLI_ALLOW_UNAUTHENTICATED` | Opt-out temporal de migracion. Solo `1` lo activa | *(unset)* |
| `UNITY_CLI_TIMEOUT_MS` | Timeout del comando (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | Modo LSP (`off` / `auto` / `required`). `auto` prefiere el LSP de C# para buscar simbolos y vuelve al indice local (`backend: "index"`, `fallbackReason`) | `auto` |
| `UNITY_CLI_LSP_AUTO_TIMEOUT_MS` | Limite (ms) que una busqueda de simbolos en `auto` espera al LSP antes de volver al indice local | `10000` |
| `UNITY_CLI_TOOLS_ROOT` | Directorio raiz de herramientas descargadas | OS default |
| `UNITY_CLI_UNITYD_PORT` | Puerto TCP loopback de unityd en Windows (cliente y daemon) | `6422` |
| `UNITY_CLI_LSPD_PORT` | Puerto TCP loopback del daemon LSP en Windows (cliente y daemon) | `6421` |
| `UNITY_CLI_NO_AUTO_UPDATE` | Desactiva la actualizacion automatica en segundo plano (`1` para desactivar) | *(unset)* |

No se admiten variables legacy con prefijo MCP. Usa solo `UNITY_CLI_*`.
Los puertos de daemon en Windows aceptan enteros de 1 a 65535; dejarlos sin definir restaura los valores por defecto. Pase los mismos valores al daemon y a los clientes CLI.
Autenticacion, Docker y WSL2 estan en la [guia de configuracion](docs/configuration.md).

## Documentacion

- Catalogo completo de comandos y herramientas: [docs/tools.md](docs/tools.md)
- Contrato e inventario de skills: [docs/skills.md](docs/skills.md)
- Guia de configuracion: [docs/configuration.md](docs/configuration.md)
- Arquitectura: [docs/architecture.md](docs/architecture.md)
- Hot reload: [docs/hot-reload.md](docs/hot-reload.md)
- Notas de migracion: [docs/migration-notes.md](docs/migration-notes.md)
- Adaptador MCP fino y configuracion de clientes: [docs/mcp.md](docs/mcp.md)
- Comparacion con el Unity CLI oficial (funciones, medicion de latencia, uso conjunto): [docs/comparison.md](docs/comparison.md)
- Flujo de desarrollo y CI: [docs/development.md](docs/development.md)
- Viabilidad y limites de la descarga de Asset Store: [investigacion](docs/asset-store-download-research.md)
- Guia de contribucion: [CONTRIBUTING.md](CONTRIBUTING.md)
- Proceso de release: [RELEASE.md](RELEASE.md)
- Plantillas de atribucion: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licencia

MIT. Consulta [ATTRIBUTION.md](ATTRIBUTION.md) para plantillas de atribucion en redistribucion.
