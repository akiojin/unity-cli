# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Deutsch](README.de.md) | [Español](README.es.md)

`unity-cli` e una CLI Rust che permette a Claude Code di controllare Unity Editor tramite TCP diretto.
E il successore di [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), riprogettato da Node.js + MCP a un workflow nativo con binario unico.

## Perche unity-cli

- Controlla Unity da Claude Code con skill mirati e comandi typed.
- Usa `101` Unity Tool APIs per scene, asset, codice, test, UI ed editor.
- Binario unico con avvio rapido e overhead ridotto.

## Come funziona

```text
Claude Code
  -> Skills (caricati on demand)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Alcuni strumenti di codice (`read`, `search`, `find_symbol`, `find_refs`, ecc.) funzionano in locale senza connessione Unity.

## Inizio rapido

### Consigliato: plugin Claude Code

Installa il plugin `unity-cli` dal Claude Code Marketplace:

```bash
/plugin marketplace add akiojin/unity-cli
```

Il plugin del Marketplace installa solo gli skill. Installa il binario
`unity-cli` separatamente con una delle opzioni manuali qui sotto.

### Codex Skills

Quando si utilizza questo repository con Codex, le skill sono disponibili tramite `.codex/skills/` (link simbolici alla sorgente del plugin).
Non e necessaria alcuna configurazione aggiuntiva — basta clonare il repository.

### Installazione manuale

Scarica il binary piu recente da [GitHub
Releases](https://github.com/akiojin/unity-cli/releases), oppure installalo da
un clone locale:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

Pacchetto bridge lato Unity (scegli un'opzione):

**OpenUPM** (consigliato):

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Verifica connessione:

```bash
unity-cli system ping
```

## Skills (13)

| Categoria | Skills |
| --- | --- |
| Introduzione | `unity-cli-usage` |
| Scene e oggetti | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Asset | `unity-asset-management`, `unity-addressables` |
| Codice | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime e test | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |

## Esempi rapidi

```bash
# Connettivita
unity-cli system ping

# Crea una scena
unity-cli scene create MainScene

# Crea un GameObject tramite raw
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Cerca nel codice C# (tool locale)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Esegui test EditMode
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Configurazione

| Variabile | Descrizione | Default |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Directory con `Assets/` e `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Host Unity Editor | `localhost` |
| `UNITY_CLI_PORT` | Porta Unity Editor | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Timeout comando (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | Modalita LSP (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Directory root dei tool scaricati | OS default |

Le variabili legacy con prefisso MCP non sono supportate. Usa solo `UNITY_CLI_*`.

## Documentazione

- Catalogo completo di comandi e tool: [docs/tools.md](docs/tools.md)
- Confronto con il Unity CLI ufficiale (funzionalita, benchmark di latenza, uso congiunto): [docs/comparison.md](docs/comparison.md)
- Workflow di sviluppo e CI: [docs/development.md](docs/development.md)
- Guida ai contributi: [CONTRIBUTING.md](CONTRIBUTING.md)
- Processo di release: [RELEASE.md](RELEASE.md)
- Template di attribuzione: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licenza

MIT. Vedi [ATTRIBUTION.md](ATTRIBUTION.md) per i template di attribuzione in caso di redistribuzione.
