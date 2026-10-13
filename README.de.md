# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` ist eine Rust CLI, mit der Claude Code den Unity Editor direkt uber TCP steuern kann.
Es ist der Nachfolger von [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server) und wurde von Node.js + MCP auf einen nativen Binary Workflow umgestellt.

## Warum unity-cli

- Unity aus Claude Code mit fokussierten Skills und typed Befehlen steuern.
- `101` Unity Tool APIs fur Szene, Assets, Code, Tests, UI und Editor nutzen.
- Einzelnes Binary mit schnellem Start und geringem Overhead.

## Architektur

```text
Claude Code
  -> Skills (on demand)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Einige Code Tools (`read`, `search`, `find_symbol`, `find_refs` usw.) laufen lokal ohne Unity Verbindung.

## Einstieg

### Editor Kompatibilitat

Die Bridge benotigt Unity 2022.3 oder neuer. Die macOS Matrix umfasst
2022.3.62f3, 6000.0.84f1, 6000.3.25f1, 6000.4.11f1, 6000.5.3f1,
6000.6.3f1, 6000.7.0a2 und 6000.7.0b2 (Abnahme E2E: PASS am 2026-09-30). Prufen Sie die
[Testdaten und Ergebnisse](docs/editor-compatibility.md), bevor Sie eine Version wahlen.
Der Unity 6000.7 Editor lauft auf Mono; CoreCLR Player Builds sind nicht Teil dieser Matrix.

### Installation mit einem LLM Agenten

Kopieren Sie diesen Prompt in Claude Code, Codex oder einen anderen Coding Agenten,
der in Ihrem Unity Projekt geoffnet ist:

```text
Installiere unity-cli fur dieses Unity Projekt und verbinde es mit dem Unity Editor.

1. Erkenne mein Betriebssystem (macOS, Linux oder Windows).
2. Wenn `unity-cli` noch nicht im PATH ist, fuhre das offizielle Quick Install Skript aus:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. Prufe, dass `unity-cli --version` uber den PATH funktioniert. Wenn es unter macOS / Linux
   nicht gefunden wird, fuge ~/.local/bin fur diese Shell zum PATH hinzu und nenne mir die
   Zeile fur mein Shell Profil. Unter Windows offne eine neue Shell, damit der aktualisierte
   Benutzer PATH gilt.
4. Bevor du mein Projekt anderst, sage mir, dass setup Packages/manifest.json bearbeitet
   (fugt die OpenUPM Scoped Registry und com.akiojin.unity-cli-bridge in der CLI Version hinzu)
   und bei Projekten nur mit dem alten Input Manager activeInputHandler in
   ProjectSettings/ProjectSettings.asset auf Both setzt. Warte auf meine Bestatigung.
5. Fuhre im Unity Projektstamm aus: unity-cli --output json setup --launch-editor
   Wenn das JSON ok: false enthalt, folge editor.hint (der erste Import kann einige
   Minuten dauern; fuhre setup erneut aus, um weiter zu warten).
6. Prufe die Verbindung mit: unity-cli system ping
7. Wenn ein Schritt fehlschlagt, fuhre aus: unity-cli doctor --output json
   Melde den diagnosis Code und folge dessen Hinweis, bevor du es erneut versuchst.
```

**Claude Code Plugin**: Installieren Sie das `unity-cli` Plugin aus dem Claude Code
Marketplace, damit der Agent die Unity Workflow Skills nutzen kann:

```bash
/plugin marketplace add akiojin/unity-cli
```

Das Plugin liefert Skills; Binary und Unity Bridge werden bei Bedarf eingerichtet.
Wenn eine Aufgabe Unity benotigt und `unity-cli` fehlt, lasst der Skill
`unity-cli-usage` den Agenten das Quick Install Skript und danach `unity-cli setup`
in Ihrem Unity Projekt ausfuhren (er informiert Sie, bevor `Packages/manifest.json`
geandert wird).

**Codex Skills**: Wenn Sie dieses Repository mit Codex verwenden, sind Skills uber
`.agents/skills/` verfugbar (Symlinks zur Plugin Quelle, auch uber `.claude/skills/`).
Es ist keine zusatzliche Einrichtung erforderlich — klonen Sie einfach das Repository.

### Manuelle Installation

#### Quick Install (empfohlen)

macOS (Apple silicon / Intel) und Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

Beide Installer laden das Release Binary in das verwaltete Layout
`~/.unity/tools/unity-cli/{rid}/` und prufen es vor der Installation gegen die
Release `SHA256SUMS`; bei abweichender Prufsumme wird die Installation abgebrochen und
das vorhandene Binary bleibt unverandert. `install.sh` legt einen Symlink unter
`~/.local/bin/unity-cli` an; `install.ps1` fugt das verwaltete Verzeichnis zum Benutzer
`PATH` hinzu. Mit `UNITY_CLI_VERSION=v0.16.0` fixieren Sie ein Release. Nach der
Erstinstallation pruft die CLI im Hintergrund automatisch auf Updates, und jedes Update
wird gegen dieselben Prufsummen verifiziert.

Wenn `~/.local/bin` nicht im PATH ist, fugen Sie Folgendes zu Ihrem Shell Profil hinzu
(z. B. `~/.zshrc` oder `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Setzen Sie `UNITY_CLI_NO_AUTO_UPDATE=1`, um Auto Updates zu deaktivieren.

#### Paketmanager

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux (Homebrew)
winget install akiojin.unity-cli       # Windows (winget)
```

Installationen uber Paketmanager werden mit `brew upgrade` / `winget upgrade` aktualisiert.
Unter Intel macOS laden Reference Embeddings (`unity-cli reference embed-build` / `embed-search`) ONNX Runtime
dynamisch; die Homebrew Formula installiert `onnxruntime`, andernfalls fuhren Sie
`brew install onnxruntime` aus oder setzen `ORT_DYLIB_PATH`.

#### Releases oder cargo install

Laden Sie das neueste Binary von [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) herunter oder
installieren Sie es aus einem lokalen Checkout:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### Projekt Einrichtung

Projekt Einrichtung mit einem Befehl (Binary Prufung → Bridge Installation → Editor Verbindung):

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` fugt die OpenUPM Scoped Registry und `com.akiojin.unity-cli-bridge`
(auf die CLI Version fixiert) zu `Packages/manifest.json` hinzu, startet optional den
Unity Hub Editor des Projekts, wartet auf die Bridge und gibt einen JSON Bericht zuruck.
`unity-cli bridge install | upgrade | status` verwalten das Paket einzeln; `install` ist
idempotent, und `setup` / `system ping` melden einen `versionCheck`, wenn sich CLI und
Bridge Version unterscheiden. Nutzt das Projekt nur den alten Input Manager und ist der
Editor geschlossen, setzt `install` zusatzlich `activeInputHandler` in
`ProjectSettings/ProjectSettings.asset` auf Both, damit die Input System Abhangigkeit
ohne blockierenden Neustart Dialog importiert wird.

Bridge Paket auf Unity Seite, manuelle Alternativen (eine Option wahlen):

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### Verbindungstest

```bash
unity-cli system ping
unity-cli doctor --output json   # wenn ping fehlschlagt: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### Wartung des verwalteten Binary

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` und `lspd` aktualisieren ihre verwalteten `unity-cli` / C# LSP Binaries beim Daemon Start automatisch.
Die verwalteten Kopien liegen unter `UNITY_CLI_TOOLS_ROOT` (oder dem OS Standard Tools Verzeichnis) und werden ohne interaktive Bestatigung aktualisiert.

## Skills (13)

| Kategorie | Skills |
| --- | --- |
| Einstieg | `unity-cli-usage` |
| Szenen und Objekte | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit` |
| Laufzeit und Tests | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editor | `unity-editor-tools` |

## Schnelle Beispiele

```bash
# Verbindung
unity-cli system ping

# Szene erstellen
unity-cli scene create MainScene

# GameObject uber raw Aufruf erstellen
unity-cli raw create_gameobject --json '{"name":"Player"}'

# C# Code durchsuchen (lokales Tool)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# EditMode Tests ausfuhren
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Konfiguration

| Variable | Beschreibung | Standard |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Verzeichnis mit `Assets/` und `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Unity Editor Host | `localhost` |
| `UNITY_CLI_PORT` | Unity Editor Port | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Command Timeout (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | LSP Modus (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Root Verzeichnis fur heruntergeladene Tools | OS default |

Legacy MCP Umgebungsvariablen werden nicht unterstutzt. Nutzen Sie nur `UNITY_CLI_*`.

## Dokumentation

- Vollstandiger Command und Tool Katalog: [docs/tools.md](docs/tools.md)
- Vergleich mit dem offiziellen Unity CLI (Funktionen, Latenzmessung, gemeinsame Nutzung): [docs/comparison.md](docs/comparison.md)
- Entwicklungsworkflow und CI: [docs/development.md](docs/development.md)
- Beitragshandbuch: [CONTRIBUTING.md](CONTRIBUTING.md)
- Release Prozess: [RELEASE.md](RELEASE.md)
- Attribution Vorlagen: [ATTRIBUTION.md](ATTRIBUTION.md)

## Lizenz

MIT. Siehe [ATTRIBUTION.md](ATTRIBUTION.md) fur Attribution Vorlagen bei Redistribution.
