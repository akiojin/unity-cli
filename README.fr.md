# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` est une CLI Rust qui permet a Claude Code de piloter Unity Editor via une connexion TCP directe.
C est le successeur de [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), repense de Node.js + MCP vers un workflow natif en binaire unique.

## Pourquoi unity-cli

- Piloter Unity depuis Claude Code avec des skills cibles et des commandes typed.
- Utiliser `101` Unity Tool APIs pour scene, assets, code, tests, UI et editor.
- Executable unique avec demarrage rapide et faible overhead.

## Fonctionnement

```text
Claude Code
  -> Skills (charges a la demande)
  -> unity-cli
  -> Unity Editor (TCP bridge)
```

Certains outils code (`read`, `search`, `find_symbol`, `find_refs`, etc.) s executent en local sans connexion Unity.

## Demarrage

### Recommande: plugin Claude Code

Installez le plugin `unity-cli` depuis la Marketplace de Claude Code:

```bash
/plugin marketplace add akiojin/unity-cli
```

Si `cargo` est disponible, l installation du plugin peut installer ou mettre a jour automatiquement `unity-cli`.

### Codex Skills

Lorsque vous utilisez ce depot avec Codex, les skills sont disponibles via `.codex/skills/` (liens symboliques vers la source du plugin).
Aucune configuration supplementaire n est necessaire — il suffit de cloner le depot.

### Installation manuelle

```bash
cargo install unity-cli
```

URL du package UPM cote Unity:

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

Verification de connexion:

```bash
unity-cli system ping
```

## Skills (13)

| Categorie | Skills |
| --- | --- |
| Prise en main | `unity-cli-usage` |
| Scenes et objets | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-asset-management`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit` |
| Runtime et tests | `unity-playmode-testing`, `unity-input-system`, `unity-ui-automation` |
| Editeur | `unity-editor-tools` |

## Exemples rapides

```bash
# Connectivite
unity-cli system ping

# Creer une scene
unity-cli scene create MainScene

# Creer un GameObject via raw
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Rechercher dans le code C# (outil local)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Lancer les tests EditMode
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Configuration

| Variable | Description | Valeur par defaut |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Repertoire contenant `Assets/` et `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Hote Unity Editor | `localhost` |
| `UNITY_CLI_PORT` | Port Unity Editor | `6400` |
| `UNITY_CLI_TIMEOUT_MS` | Timeout de commande (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | Mode LSP (`off` / `auto` / `required`) | `off` |
| `UNITY_CLI_TOOLS_ROOT` | Repertoire racine des outils telecharges | OS default |

Les variables legacy prefixees MCP ne sont pas supportees. Utilisez uniquement `UNITY_CLI_*`.

## Documentation

- Catalogue complet des commandes et des outils: [docs/tools.md](docs/tools.md)
- Workflow de developpement et CI: [docs/development.md](docs/development.md)
- Guide de contribution: [CONTRIBUTING.md](CONTRIBUTING.md)
- Processus de release: [RELEASE.md](RELEASE.md)
- Modeles d attribution: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licence

MIT. Voir [ATTRIBUTION.md](ATTRIBUTION.md) pour les modeles d attribution en redistribution.
