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

### Compatibilite de l'Editor

Le Bridge requiert Unity 2022.3 ou plus recent. La matrice macOS couvre
2022.3.62f3, 6000.0.84f1, 6000.3.25f1, 6000.4.11f1, 6000.5.3f1,
6000.6.3f1, 6000.7.0a2 et 6000.7.0b2 (E2E d'acceptation: PASS le 2026-09-30). Consultez les
[dates et resultats de verification](docs/editor-compatibility.md) avant de choisir une version.
L'Editor Unity 6000.7 s'execute sur Mono; les builds Player CoreCLR sont hors de cette matrice.

### Installer avec un agent LLM

Copiez ce prompt dans Claude Code, Codex ou un autre agent de code ouvert dans
votre projet Unity:

```text
Installe unity-cli pour ce projet Unity et connecte-le au Unity Editor.

1. Detecte mon systeme d'exploitation (macOS, Linux ou Windows).
2. Si `unity-cli` n'est pas encore dans le PATH, execute le script officiel de Quick Install:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. Verifie que `unity-cli --version` fonctionne depuis le PATH. Sous macOS / Linux, s'il est
   introuvable, ajoute ~/.local/bin au PATH de ce shell et indique-moi la ligne a ajouter a
   mon profil de shell. Sous Windows, ouvre un nouveau shell pour appliquer le PATH utilisateur mis a jour.
4. Avant de modifier mon projet, dis-moi que setup modifie Packages/manifest.json (ajoute le
   scoped registry OpenUPM et com.akiojin.unity-cli-bridge fixe a la version du CLI) et, pour
   les projets qui n'utilisent que l'ancien Input Manager, met activeInputHandler a Both dans
   ProjectSettings/ProjectSettings.asset. Attends ma confirmation.
5. A la racine du projet Unity, execute: unity-cli --output json setup --launch-editor
   Si le JSON contient ok: false, suis editor.hint (le premier import peut prendre
   plusieurs minutes; relance setup pour continuer a attendre).
6. Verifie la connexion avec: unity-cli system ping
7. Si une etape echoue, execute: unity-cli doctor --output json
   Indique le code diagnosis et suis son conseil avant de reessayer.
```

**Plugin Claude Code**: installez le plugin `unity-cli` depuis Claude Code
Marketplace pour que l'agent puisse utiliser les skills de workflow Unity:

```bash
/plugin marketplace add akiojin/unity-cli
```

Le plugin fournit les skills; le binaire et le bridge Unity sont installes a la demande.
Quand une tache necessite Unity et que `unity-cli` manque, le skill `unity-cli-usage` fait
executer par l'agent le script de Quick Install puis `unity-cli setup` dans votre projet
Unity (il vous previent avant de modifier `Packages/manifest.json`).

**Codex Skills**: si vous utilisez ce depot avec Codex, les skills sont disponibles via
`.agents/skills/` (liens symboliques vers la source du plugin, aussi exposes via
`.claude/skills/`). Aucune configuration supplementaire n'est requise — clonez simplement le depot.

### Installation manuelle

#### Quick Install (recommande)

macOS (Apple silicon / Intel) et Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
```

Les deux installeurs telechargent le binaire de la release dans l'arborescence geree
`~/.unity/tools/unity-cli/{rid}/` et le verifient avec les `SHA256SUMS` de la release avant
l'installation; en cas de somme de controle differente, l'installation est annulee et le
binaire existant reste intact. `install.sh` cree un lien symbolique `~/.local/bin/unity-cli`;
`install.ps1` ajoute le repertoire gere au `PATH` utilisateur. Definissez
`UNITY_CLI_VERSION=v0.16.0` pour fixer une release. Apres l'installation initiale, le CLI
verifie automatiquement les mises a jour en arriere-plan, et chaque mise a jour est
verifiee avec les memes sommes de controle.

Si `~/.local/bin` n'est pas dans votre PATH, ajoutez ceci a votre profil de shell
(par ex. `~/.zshrc` ou `~/.bashrc`):

```bash
export PATH="$HOME/.local/bin:$PATH"
```

Definissez `UNITY_CLI_NO_AUTO_UPDATE=1` pour desactiver la mise a jour automatique.

#### Gestionnaires de paquets

```bash
brew install akiojin/tap/unity-cli     # macOS / Linux (Homebrew)
winget install akiojin.unity-cli       # Windows (winget)
```

Les installations par gestionnaire de paquets se mettent a jour avec `brew upgrade` / `winget upgrade`.
Sous macOS Intel, les embeddings reference (`unity-cli reference embed-build` / `embed-search`) chargent ONNX Runtime
dynamiquement; la formule Homebrew installe `onnxruntime`, sinon executez
`brew install onnxruntime` ou definissez `ORT_DYLIB_PATH`.

#### Releases ou cargo install

Telechargez le dernier binaire depuis [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) ou installez depuis un checkout local:

```bash
git clone https://github.com/akiojin/unity-cli.git
cd unity-cli
cargo install --path .
```

#### Configuration du projet

Configuration du projet en une commande (verification du binaire → installation du bridge → connexion a l'Editor):

```bash
cd /path/to/YourUnityProject
unity-cli --output json setup --launch-editor
```

`setup` ajoute le scoped registry OpenUPM et `com.akiojin.unity-cli-bridge`
(fixe a la version du CLI) a `Packages/manifest.json`, lance eventuellement l'Editor Unity
Hub du projet, attend le bridge et renvoie un rapport JSON.
`unity-cli bridge install | upgrade | status` gerent le paquet separement; `install` est
idempotent, et `setup` / `system ping` signalent un `versionCheck` quand les versions du CLI
et du bridge different. Si le projet n'utilise encore que l'ancien Input Manager et que
l'Editor est ferme, `install` met aussi `activeInputHandler` a Both dans
`ProjectSettings/ProjectSettings.asset`, afin que la dependance Input System s'importe sans
demande de redemarrage bloquante.

Paquet bridge cote Unity, alternatives manuelles (choisissez une option):

**OpenUPM**:

```bash
openupm add com.akiojin.unity-cli-bridge
```

**Git URL** (Unity Package Manager):

```text
https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge
```

#### Verification de connexion

```bash
unity-cli system ping
unity-cli doctor --output json   # si ping echoue: SAFE_MODE / BRIDGE_NOT_INSTALLED / PORT_IN_USE / ...
```

#### Maintenance du binaire gere

```bash
unity-cli cli doctor
unity-cli cli install
```

`unityd` et `lspd` mettent automatiquement a jour leurs binaires geres `unity-cli` / C# LSP au demarrage du daemon.
Les copies gerees se trouvent sous `UNITY_CLI_TOOLS_ROOT` (ou le repertoire tools par defaut de l'OS) et sont mises a jour sans confirmation interactive.

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
- Comparaison avec le Unity CLI officiel (fonctionnalites, mesures de latence, utilisation conjointe): [docs/comparison.md](docs/comparison.md)
- Workflow de developpement et CI: [docs/development.md](docs/development.md)
- Guide de contribution: [CONTRIBUTING.md](CONTRIBUTING.md)
- Processus de release: [RELEASE.md](RELEASE.md)
- Modeles d attribution: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licence

MIT. Voir [ATTRIBUTION.md](ATTRIBUTION.md) pour les modeles d attribution en redistribution.
