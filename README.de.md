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

### Empfohlen: Claude Code Plugin

Installieren Sie das `unity-cli` Plugin aus dem Claude Code Marketplace.
Wenn `cargo` verfugbar ist, kann das Plugin Setup `unity-cli` automatisch installieren oder aktualisieren.

### Manuelle Installation

```bash
cargo install unity-cli
```

UPM Paket URL auf Unity Seite:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Verbindungstest:

```bash
unity-cli system ping
```

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
- Entwicklungsworkflow und CI: [docs/development.md](docs/development.md)
- Beitragshandbuch: [CONTRIBUTING.md](CONTRIBUTING.md)
- Release Prozess: [RELEASE.md](RELEASE.md)
- Attribution Vorlagen: [ATTRIBUTION.md](ATTRIBUTION.md)

## Lizenz

MIT. Siehe [ATTRIBUTION.md](ATTRIBUTION.md) fur Attribution Vorlagen bei Redistribution.
