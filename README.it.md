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

### Compatibilita dell'Editor

Il Bridge richiede Unity 2022.3 o successivo. La matrice macOS copre
2022.3.62f3, 6000.0.84f1, 6000.3.25f1, 6000.4.11f1, 6000.5.3f1,
6000.6.3f1, 6000.7.0a2 e 6000.7.0b2 (E2E di accettazione: PASS il 2026-09-30). Consultare le
[date e i risultati di verifica](docs/editor-compatibility.md) prima di scegliere una versione.
L'Editor Unity 6000.7 gira su Mono; le build Player CoreCLR sono escluse da questa matrice.

### Installare con un agente LLM

Copiare questo prompt in Claude Code, Codex o un altro agente di programmazione aperto
nel proprio progetto Unity:

```text
Installa unity-cli per questo progetto Unity e collegalo allo Unity Editor.

1. Rileva il mio sistema operativo (macOS, Linux o Windows).
2. Se `unity-cli` non e ancora nel PATH, esegui lo script ufficiale di Quick Install:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. Verifica che `unity-cli --version` funzioni dal PATH. Su macOS / Linux, se non viene
   trovato, aggiungi ~/.local/bin al PATH di questa shell e indicami la riga da aggiungere al
   mio profilo della shell. Su Windows, apri una nuova shell per applicare il PATH utente aggiornato.
4. Prima di modificare il mio progetto, dimmi che setup modifica Packages/manifest.json
   (aggiunge lo scoped registry OpenUPM e com.akiojin.unity-cli-bridge fissato alla versione
   della CLI) e, per i progetti che usano solo il vecchio Input Manager, imposta
   activeInputHandler su Both in ProjectSettings/ProjectSettings.asset. Attendi la mia conferma.
5. Dalla radice del progetto Unity, esegui: unity-cli --output json setup --launch-editor
   Se il JSON contiene ok: false, segui editor.hint (la prima importazione puo richiedere
   alcuni minuti; riesegui setup per continuare ad attendere).
6. Verifica la connessione con: unity-cli system ping
7. Se un passaggio fallisce, esegui: unity-cli doctor --output json
   Riporta il codice diagnosis e segui il suo suggerimento prima di riprovare.
```

**Plugin Claude Code**: installare il plugin `unity-cli` da Claude Code
Marketplace per permettere all'agente di usare gli skill dei workflow Unity:

```bash
/plugin marketplace add akiojin/unity-cli
```

Il plugin fornisce gli skill; il binario e il bridge Unity vengono preparati quando servono.
Quando un'attivita richiede Unity e `unity-cli` manca, lo skill `unity-cli-usage` fa eseguire
all'agente lo script di Quick Install e poi `unity-cli setup` nel progetto Unity
(avvisa prima di modificare `Packages/manifest.json`).

**Codex Skills**: usando questo repository con Codex, gli skill sono disponibili tramite
`.agents/skills/` (symlink alla sorgente del plugin, esposti anche tramite `.claude/skills/`).
Non serve alcuna configurazione aggiuntiva — basta clonare il repository.

### Installazione manuale

#### Quick Install (consigliato)

macOS (Apple silicon / Intel) e Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

Entrambi gli installer scaricano il binario della release nella struttura gestita
`~/.unity/tools/unity-cli/{rid}/` e lo verificano con i `SHA256SUMS` della release prima
dell'installazione; se il checksum non corrisponde, l'installazione viene interrotta e il
binario esistente non viene toccato. `install.sh` crea un symlink in `~/.local/bin/unity-cli`;
`install.ps1` aggiunge la directory gestita al `PATH` utente. Impostare
`UNITY_CLI_VERSION=v0.16.0` per fissare una release. Dopo l'installazione iniziale la CLI
controlla automaticamente gli aggiornamenti in background, e ogni aggiornamento viene
verificato con gli stessi checksum.

Se `~/.local/bin` non e nel PATH, aggiungere quanto segue al profilo della shell
(ad es. `~/.zshrc` o `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Impostare `UNITY_CLI_NO_AUTO_UPDATE=1` per disattivare l'aggiornamento automatico.

#### Gestori di pacchetti

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux (Homebrew)
winget install akiojin.unity-cli       # Windows (winget)
```

Le installazioni tramite gestore di pacchetti si aggiornano con `brew upgrade` / `winget upgrade`.
Su macOS Intel, gli embedding reference (`unity-cli reference embed-build` / `embed-search`) caricano ONNX Runtime
dinamicamente; la formula Homebrew installa `onnxruntime`, altrimenti eseguire
`brew install onnxruntime` o impostare `ORT_DYLIB_PATH`.

#### Releases o cargo install

Scaricare il binario piu recente da [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) oppure installarlo da un checkout locale:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### Configurazione del progetto

Configurazione del progetto con un solo comando (controllo del binario → installazione del bridge → connessione all'Editor):

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` aggiunge lo scoped registry OpenUPM e `com.akiojin.unity-cli-bridge`
(fissato alla versione della CLI) a `Packages/manifest.json`, avvia facoltativamente
l'Editor Unity Hub del progetto, attende il bridge e restituisce un report JSON.
`unity-cli bridge install | upgrade | status` gestiscono il pacchetto separatamente;
`install` e idempotente, e `setup` / `system ping` segnalano un `versionCheck` quando le
versioni di CLI e bridge differiscono. Se il progetto usa ancora solo il vecchio Input
Manager e l'Editor e chiuso, `install` imposta anche `activeInputHandler` su Both in
`ProjectSettings/ProjectSettings.asset`, cosi la dipendenza Input System viene importata
senza un prompt di riavvio bloccante.

Pacchetto bridge lato Unity, alternative manuali (sceglierne una):

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### Verifica della connessione

```bash
unity-cli system ping
unity-cli doctor --output json   # se ping fallisce: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### Manutenzione del binario gestito

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` e `lspd` aggiornano automaticamente i propri binari gestiti `unity-cli` / C# LSP all'avvio del daemon.
Le copie gestite si trovano in `UNITY_CLI_TOOLS_ROOT` (o nella directory tools predefinita del sistema operativo) e vengono aggiornate senza conferma interattiva.

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
