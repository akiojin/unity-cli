# unity-cli

[English](README.md) | [日本語](README.ja.md) | [中文](README.zh.md) | [Deutsch](README.de.md) | [Italiano](README.it.md) | [Español](README.es.md)

`unity-cli` est une CLI Rust qui permet a Claude Code de piloter Unity Editor via une connexion TCP directe.
C est le successeur de [`unity-mcp-server`](https://github.com/akiojin/unity-mcp-server), repense de Node.js + MCP vers un workflow natif en binaire unique.

## Pourquoi unity-cli

- Piloter Unity depuis Claude Code avec des skills cibles et des commandes typed.
- Utiliser 143 runtime Unity Tool APIs et 11 outils Reference Cache pour scene, assets, code, tests, UI et editor.
- Executable unique avec demarrage rapide et faible overhead.

## Fonctions ajoutees depuis v0.13

Les [exemples d outils executables](docs/tools.md#v013v015-executable-examples) couvrent:

- Resultats de tests PlayMode a travers Domain Reload; maintien clavier/gamepad, simulation souris/tactile, sequences d entree et verifications de notification InputAction.
- Enregistrement MP4, WebM et sequence d images PNG, plus des apercus de hot reload de methodes.
- Edition d assets/pistes/clips Timeline, travaux de bake Lighting/NavMesh/NavMeshSurface/Occlusion et builds Player autonomes avec interrogation du statut.

Commencez par `unity-playmode-testing` pour le runtime/l entree/la capture/le hot reload, par `unity-asset-management` pour Timeline et par `unity-editor-tools` pour les travaux de build/bake. Les prerequis de packages et de scenes sont indiques avec chaque exemple.

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
Les controles de base et de tous les outils sous Windows ont reussi sur 6000.3.28f1, 6000.6.8f1 et Unity 7.0 Alpha 7000.0.0a16 contre les sources mises a jour le 2026-11-10.
Ce dernier execute l Editor sur CoreCLR; les resultats Windows lies indiquent la build alpha exacte, les paquets et le perimetre teste.

### Installer ou mettre a jour avec un agent LLM

Copiez ce prompt dans Claude Code, Codex ou un autre agent de code ouvert dans
votre projet Unity. Relancez le meme prompt plus tard pour mettre a jour ensemble le CLI
et le package bridge Unity:

```text
Installe ou mets a jour unity-cli pour ce projet Unity, garde le package bridge Unity a la
meme version que le CLI et connecte-le au Unity Editor.

1. Detecte mon systeme d'exploitation (macOS, Linux ou Windows).
2. Si `unity-cli` n'est pas encore dans le PATH, execute le script officiel de Quick Install:
   - macOS / Linux: curl -fsSL https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.sh | sh
   - Windows (PowerShell): irm https://raw.githubusercontent.com/akiojin/unity-cli/main/scripts/install.ps1 | iex
3. Si `unity-cli` est deja dans le PATH, mets-le plutot a jour vers la derniere version.
   Si UNITY_CLI_NO_AUTO_UPDATE=1 ou UNITY_CLI_VERSION est defini, j'ai fixe la version
   volontairement: ne le mets pas a jour; previens-moi et demande d'abord. Sinon, determine
   ou se trouve reellement le binaire (en suivant les liens symboliques) et mets-le a jour
   selon sa methode d'installation:
   - Quick Install (copie geree sous ~/.unity/tools): unity-cli cli install
   - Homebrew: brew upgrade akiojin/tap/unity-cli
   - Tout autre cas (cargo install, telechargement manuel): dis-moi comment il a ete
     installe et demande avant de le modifier.
4. Verifie que `unity-cli --version` fonctionne depuis le PATH. Sous macOS / Linux, s'il est
   introuvable, ajoute ~/.local/bin au PATH de ce shell et indique-moi la ligne a ajouter a
   mon profil de shell. Sous Windows, ouvre un nouveau shell pour appliquer le PATH utilisateur mis a jour.
5. Avant de modifier mon projet, dis-moi que setup modifie Packages/manifest.json (ajoute le
   scoped registry OpenUPM et com.akiojin.unity-cli-bridge fixe a la version du CLI) et, pour
   les projets qui n'utilisent que l'ancien Input Manager, met activeInputHandler a Both dans
   ProjectSettings/ProjectSettings.asset. Attends ma confirmation.
6. A la racine du projet Unity, execute: unity-cli --output json setup --launch-editor
   Si le JSON contient ok: false, suis editor.hint (le premier import peut prendre
   plusieurs minutes; relance setup pour continuer a attendre).
7. Verifie la version du bridge avec: unity-cli --output json bridge status
   - Si source vaut registry et que versionCheck.status (ici ou dans le rapport de setup) vaut
     mismatch: dis-moi que cela remplace la ligne com.akiojin.unity-cli-bridge de
     Packages/manifest.json par la version du CLI et attends ma confirmation. Execute ensuite
     unity-cli bridge upgrade et laisse le Unity Editor reimporter le package.
   - Si source vaut custom ou embedded (URL Git, chemin local ou copie sous Packages/), ou si
     versionCheck.status vaut unknown: ne le reecris pas. Indique-moi plutot le source et
     les deux versions.
8. Verifie la connexion avec: unity-cli system ping
   Confirme que versionCheck.status vaut match (le CLI et le bridge ont la meme version).
9. Si une etape echoue, execute: unity-cli doctor --output json
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
`UNITY_CLI_VERSION=v0.19.1` pour fixer une release. Apres l'installation initiale, le CLI
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
```

Les installations Homebrew se mettent a jour avec `brew upgrade`.
Le paquet Windows winget n'est pas encore publie (#537) ; utilisez jusque-la
la commande Quick Install PowerShell ci-dessus.
Sous macOS Intel, les embeddings reference (`unity-cli reference embed-build` / `embed-search`) chargent ONNX Runtime
dynamiquement; la formule Homebrew installe `onnxruntime`, sinon executez
`brew install onnxruntime` ou definissez `ORT_DYLIB_PATH`.

#### Releases ou cargo install

Installez le crate publie depuis crates.io. Le workflow de release le publie avec `cargo publish`:

```bash
cargo install unity-cli
```

Vous pouvez aussi telecharger le dernier binaire depuis [GitHub
Releases](https://github.com/akiojin/unity-cli/releases) ou installer depuis un checkout local:

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

## Skills (23)

Ces skills sont des guides de workflow. La liste correspond a [docs/skills.md](docs/skills.md) et aux repertoires du plugin sous `.claude-plugin/plugins/unity-cli/skills/`.

| Categorie | Skills |
| --- | --- |
| Prise en main | `unity-project-bootstrap`, `unity-cli-usage` |
| Scenes et objets | `unity-scene-create`, `unity-scene-inspect`, `unity-gameobject-edit`, `unity-prefab-workflow` |
| Assets | `unity-2d-sprite-tilemap`, `unity-asset-management`, `unity-audio-setup`, `unity-addressables` |
| Code | `unity-csharp-navigate`, `unity-csharp-edit`, `unity-csharp-reference` |
| Runtime et tests | `unity-playmode-testing`, `unity-input-system`, `unity-ui-toolkit-build`, `unity-localization`, `unity-ui-automation`, `unity-development-loop`, `unity-vfx-graph` |
| Editeur | `unity-editor-tools`, `unity-package-management`, `unity-urp-setup` |

`gh-skills-sync` se trouve dans `dev-skills/` et sert uniquement au developpement. Il ne fait pas partie du plugin.

## Exemples rapides

Importez des fichiers `.unitypackage` locaux ou des paquets Asset Store telecharges sans dialogue, en attendant la compilation et Domain Reload.

```bash
unity-cli package import /absolute/path/Small.unitypackage --output json
unity-cli package cache-list --output json
unity-cli package cache-import "Small Package" --output json
# Ouvrir My Assets, appuyer sur Download a la main, puis importer automatiquement:
unity-cli package asset-store-import 12345 --download-timeout-ms 600000 --output json
```

Utilisez `--cache-path` ou `UNITY_CLI_ASSET_STORE_CACHE_PATH` pour un cache personnalise.
Les ID de selection, les delais et la recuperation sont dans [package import commands](docs/tools.md#unitypackage-and-asset-store-cache). `package asset-store-open [name-or-id]` et `package asset-store-wait <name-or-id>` separent l affichage et l attente. Le telechargement, la connexion et la verification de propriete restent manuels dans Unity; le CLI n appuie jamais sur Download et ne lit pas les jetons de compte Unity.

```bash
# Connectivite
unity-cli system ping

# Creer une scene
unity-cli scene create MainScene

# Creer un GameObject via raw
unity-cli raw create_gameobject --json '{"name":"Player"}'

# Rechercher dans le code C# (outil local)
unity-cli tool call search --json '{"pattern":"PlayerController"}'

# Inspecter le schema d outil lisible par machine
unity-cli tool schema create_scene --output json

# Dry-run des appels qui mutent l etat (sans effet de bord)
unity-cli --dry-run tool call create_scene --json '{"sceneName":"PreviewScene"}'

# Lancer les tests EditMode
unity-cli tool call run_tests --json '{"mode":"editmode"}'
```

## Workflow GWT Spec

Les specifications de fonctionnalites sont gerees dans des GitHub Issues etiquetes `gwt-spec`.
Le corps de l Issue est la source pour `Spec`, `Plan`, `Tasks` et `TDD`.

## Contribuer

Les contributions externes doivent viser la branche `develop`. Lisez
[CONTRIBUTING.md](CONTRIBUTING.md) avant d ouvrir une PR, en particulier la politique de branches, la CI requise et les changements de documentation des skills.

## Configuration

| Variable | Description | Valeur par defaut |
| --- | --- | --- |
| `UNITY_PROJECT_ROOT` | Repertoire contenant `Assets/` et `Packages/` | auto-detect |
| `UNITY_CLI_HOST` | Hote Unity Editor | `localhost` |
| `UNITY_CLI_PORT` | Port Unity Editor | `6400` |
| `UNITY_CLI_AUTH_TOKEN_FILE` | JSON de lockfile Editor contenant `authToken` pour des identifiants distants ou montes | *(unset)* |
| `UNITY_CLI_ALLOW_UNAUTHENTICATED` | Opt-out temporaire de migration. Seul `1` l active | *(unset)* |
| `UNITY_CLI_TIMEOUT_MS` | Timeout de commande (ms) | `30000` |
| `UNITY_CLI_LSP_MODE` | Mode LSP (`off` / `auto` / `required`). `auto` privilegie le LSP C# pour la recherche de symboles et revient a l'index local (`backend: "index"`, `fallbackReason`) | `auto` |
| `UNITY_CLI_LSP_AUTO_TIMEOUT_MS` | Limite (ms) d'attente du LSP pour une recherche de symboles en `auto` avant le retour a l'index local | `10000` |
| `UNITY_CLI_TOOLS_ROOT` | Repertoire racine des outils telecharges | OS default |
| `UNITY_CLI_UNITYD_PORT` | Port TCP loopback unityd sous Windows (client et daemon) | `6422` |
| `UNITY_CLI_LSPD_PORT` | Port TCP loopback du daemon LSP sous Windows (client et daemon) | `6421` |
| `UNITY_CLI_NO_AUTO_UPDATE` | Desactive la mise a jour automatique en arriere plan (`1` pour desactiver) | *(unset)* |

Les variables legacy prefixees MCP ne sont pas supportees. Utilisez uniquement `UNITY_CLI_*`.
Les ports de daemon Windows acceptent les entiers de 1 a 65535; les retirer retablit les valeurs par defaut. Passez les memes valeurs au daemon et aux clients CLI.
L authentification, Docker et WSL2 sont dans le [guide de configuration](docs/configuration.md).

## Documentation

- Catalogue complet des commandes et des outils: [docs/tools.md](docs/tools.md)
- Contrat et inventaire des skills: [docs/skills.md](docs/skills.md)
- Guide de configuration: [docs/configuration.md](docs/configuration.md)
- Architecture: [docs/architecture.md](docs/architecture.md)
- Hot reload: [docs/hot-reload.md](docs/hot-reload.md)
- Notes de migration: [docs/migration-notes.md](docs/migration-notes.md)
- Adaptateur MCP fin et configuration des clients: [docs/mcp.md](docs/mcp.md)
- Comparaison avec le Unity CLI officiel (fonctionnalites, mesures de latence, utilisation conjointe): [docs/comparison.md](docs/comparison.md)
- Workflow de developpement et CI: [docs/development.md](docs/development.md)
- Faisabilite et contraintes du telechargement Asset Store: [recherche](docs/asset-store-download-research.md)
- Guide de contribution: [CONTRIBUTING.md](CONTRIBUTING.md)
- Processus de release: [RELEASE.md](RELEASE.md)
- Modeles d attribution: [ATTRIBUTION.md](ATTRIBUTION.md)

## Licence

MIT. Voir [ATTRIBUTION.md](ATTRIBUTION.md) pour les modeles d attribution en redistribution.
