use std::path::PathBuf;

use clap::{ArgAction, Args, Parser, Subcommand, ValueEnum};

#[derive(Debug, Clone, Copy, ValueEnum, Default)]
pub enum OutputFormat {
    #[default]
    Text,
    Json,
}

#[derive(Debug, Parser)]
#[command(
    name = "unity-cli",
    version,
    about = "Rust CLI for Unity Editor automation over Unity TCP protocol",
    arg_required_else_help = true
)]
pub struct Cli {
    #[arg(long, global = true, value_enum, default_value_t = OutputFormat::Text)]
    pub output: OutputFormat,

    #[arg(long, global = true)]
    pub host: Option<String>,

    #[arg(long, global = true)]
    pub port: Option<u16>,

    /// Target the Unity Editor that has this project open (default: the
    /// project containing the current directory). Also read from UNITY_PROJECT_ROOT.
    #[arg(long, global = true, value_name = "PATH")]
    pub project_path: Option<PathBuf>,

    #[arg(long, global = true, value_name = "MS")]
    pub timeout_ms: Option<u64>,

    #[arg(short, long, global = true, action = ArgAction::Count)]
    pub verbose: u8,

    #[arg(long, global = true)]
    pub dry_run: bool,

    #[command(subcommand)]
    pub command: Command,
}

#[derive(Debug, Subcommand)]
pub enum Command {
    Raw(RawArgs),
    Editor {
        #[command(subcommand)]
        command: EditorCommand,
    },
    Tool {
        #[command(subcommand)]
        command: ToolCommand,
    },
    System {
        #[command(subcommand)]
        command: SystemCommand,
    },
    Scene {
        #[command(subcommand)]
        command: SceneCommand,
    },
    Instances {
        #[command(subcommand)]
        command: InstancesCommand,
    },
    Lsp {
        #[command(subcommand)]
        command: LspCommand,
    },
    Cli {
        #[command(subcommand)]
        command: CliCommand,
    },
    Lspd {
        #[command(subcommand)]
        command: LspdCommand,
    },
    Unityd {
        #[command(subcommand)]
        command: UnitydCommand,
    },
    Skills {
        #[command(subcommand)]
        command: SkillsCommand,
    },
    Reference {
        #[command(subcommand)]
        command: ReferenceCommand,
    },
    /// Install the bridge into a Unity project and confirm the Editor connection.
    Setup {
        /// Unity project root (default: nearest ancestor with Packages/manifest.json).
        #[arg(long, value_name = "PATH")]
        project_path: Option<PathBuf>,
        /// Launch the project's Unity Editor (Unity Hub install path) when it is not reachable.
        #[arg(long, default_value_t = false)]
        launch_editor: bool,
        /// Seconds to wait for the Editor bridge (default: 0, or 900 with --launch-editor).
        #[arg(long, value_name = "SECS")]
        wait_secs: Option<u64>,
    },
    /// Manage the com.akiojin.unity-cli-bridge package in a Unity project.
    Bridge {
        #[command(subcommand)]
        command: BridgeCommand,
    },
    /// Diagnose why the Unity bridge is unreachable (Safe Mode, missing bridge, port conflict, ...).
    Doctor(DoctorArgs),
    Batch {
        #[arg(long, value_name = "JSON")]
        json: Option<String>,
        #[arg(long)]
        stdin: bool,
    },
}

#[derive(Debug, Args)]
pub struct DoctorArgs {
    /// Unity project root. Defaults to UNITY_PROJECT_ROOT or the nearest Unity project above the current directory.
    #[arg(long, value_name = "DIR")]
    pub project_path: Option<PathBuf>,
}

#[derive(Debug, Subcommand)]
pub enum ToolCommand {
    List(ToolListArgs),
    Schema {
        tool_name: Option<String>,
    },
    Call(RawArgs),
    #[command(external_subcommand)]
    External(Vec<String>),
}

#[derive(Debug, Args, Default)]
pub struct ToolListArgs {
    /// Case-insensitive substring matched against tool names and one-line descriptions.
    #[arg(long, value_name = "TEXT")]
    pub query: Option<String>,

    /// docs/tools.md category heading or slug (e.g. `scenes`, `playback-testing`).
    #[arg(long, value_name = "NAME")]
    pub category: Option<String>,

    /// Emit `{name, description}` entries instead of bare tool names.
    #[arg(long)]
    pub compact: bool,

    /// Maximum number of tools to return after filtering.
    #[arg(long, value_name = "N")]
    pub limit: Option<usize>,

    /// Number of filtered tools to skip before `--limit` applies.
    #[arg(long, value_name = "N", default_value_t = 0)]
    pub offset: usize,
}

#[derive(Debug, Args)]
pub struct RawArgs {
    pub tool_name: String,

    #[arg(long, value_name = "JSON")]
    pub json: Option<String>,

    #[arg(long, value_name = "FILE")]
    pub params_file: Option<PathBuf>,
}

#[derive(Debug, Subcommand)]
pub enum SystemCommand {
    Ping {
        #[arg(long)]
        message: Option<String>,
    },
}

#[derive(Debug, Clone, Copy, ValueEnum, Default)]
pub enum EvalMode {
    #[default]
    Expression,
    Statements,
}

#[derive(Debug, Subcommand)]
pub enum EditorCommand {
    /// Evaluate synchronous C# in the Editor. Timeout does not cancel execution.
    Eval {
        code: String,
        #[arg(long, value_enum, default_value_t = EvalMode::Expression)]
        mode: EvalMode,
        /// Reuse this ID to query the result after a timeout. Never retry with a new ID.
        #[arg(long)]
        request_id: Option<String>,
    },
    /// Retrieve a result from the current Editor domain (unknown after reload).
    EvalStatus { request_id: String },
    /// Show eval counters of the current Editor domain: loaded assemblies, caches and memory.
    EvalStats {
        /// Run a managed garbage collection before reading memory.
        #[arg(long)]
        collect: bool,
    },
}

#[derive(Debug, Subcommand)]
pub enum SceneCommand {
    Create {
        scene_name: String,

        #[arg(long)]
        path: Option<String>,

        #[arg(long, default_value_t = true)]
        load_scene: bool,

        #[arg(long, default_value_t = false)]
        add_to_build_settings: bool,
    },
}

#[derive(Debug, Subcommand)]
pub enum InstancesCommand {
    List {
        #[arg(long, value_name = "CSV")]
        ports: Option<String>,

        #[arg(long, default_value = crate::config::DEFAULT_HOST)]
        host: String,

        #[arg(long, value_name = "MS", default_value_t = 1000)]
        timeout_ms: u64,
    },
    SetActive {
        id: String,

        #[arg(long, value_name = "MS", default_value_t = 1000)]
        timeout_ms: u64,
    },
}

#[derive(Debug, Subcommand)]
pub enum BridgeCommand {
    /// Add the OpenUPM registry and the bridge (CLI version) to Packages/manifest.json. Idempotent.
    Install {
        #[arg(long, value_name = "PATH")]
        project_path: Option<PathBuf>,
    },
    /// Pin the bridge dependency to the CLI version.
    Upgrade {
        #[arg(long, value_name = "PATH")]
        project_path: Option<PathBuf>,
    },
    /// Show the declared / resolved bridge version and the CLI version check.
    Status {
        #[arg(long, value_name = "PATH")]
        project_path: Option<PathBuf>,
    },
}

#[derive(Debug, Subcommand)]
pub enum LspCommand {
    Install,
    Doctor,
}

#[derive(Debug, Subcommand)]
pub enum CliCommand {
    Install {
        #[arg(long, default_value_t = false)]
        force: bool,
    },
    Doctor,
}

#[derive(Debug, Subcommand)]
pub enum LspdCommand {
    Start,
    Stop,
    Status,
    #[command(hide = true)]
    Serve,
}

#[derive(Debug, Subcommand)]
pub enum UnitydCommand {
    Start,
    Stop,
    Status,
    #[command(hide = true)]
    Serve,
}

#[derive(Debug, Clone, Copy, ValueEnum, Default)]
pub enum SkillSeverity {
    #[default]
    Warning,
    Error,
}

#[derive(Debug, Clone, Copy, ValueEnum, Default)]
pub enum SkillFormat {
    #[default]
    Text,
    Json,
}

#[derive(Debug, Subcommand)]
pub enum SkillsCommand {
    /// Validate Claude Code / Codex skill directories against Skill Contract v1.
    Lint {
        /// Skills root (default: auto-detect `.claude-plugin/plugins/unity-cli/skills`).
        #[arg(long, value_name = "PATH")]
        root: Option<PathBuf>,

        /// Output format.
        #[arg(long, value_enum, default_value_t = SkillFormat::Text)]
        format: SkillFormat,

        /// Severity gate; `error` exits non-zero on any violation.
        #[arg(long, value_enum, default_value_t = SkillSeverity::Warning)]
        severity: SkillSeverity,
    },
}

#[derive(Debug, Subcommand)]
pub enum ReferenceCommand {
    /// Fetch UnityCsReference for the active Unity version into the local cache.
    Fetch {
        #[arg(long)]
        version: Option<String>,
        #[arg(long)]
        branch: Option<String>,
        #[arg(long, default_value_t = false)]
        force: bool,
        #[arg(long, default_value_t = false)]
        accept_license: bool,
    },
    /// Show cached UnityCsReference versions and disk usage.
    Status {
        #[arg(long)]
        version: Option<String>,
    },
    /// Search the cached reference source for a pattern (file-level hits).
    Search {
        pattern: String,
        #[arg(long)]
        version: Option<String>,
        #[arg(long)]
        path: Option<String>,
        #[arg(long)]
        max_results: Option<u64>,
        #[arg(long, default_value_t = false)]
        regex: bool,
    },
    /// Grep the cached reference source line-by-line with optional context.
    Grep {
        pattern: String,
        #[arg(long)]
        version: Option<String>,
        #[arg(long)]
        file_glob: Option<String>,
        #[arg(long, default_value_t = 0)]
        context: u32,
    },
    /// View a file from the cached reference source with an optional line range.
    View {
        path: String,
        #[arg(long)]
        version: Option<String>,
        #[arg(long)]
        start_line: Option<u32>,
        #[arg(long)]
        max_lines: Option<u32>,
    },
    /// Find a symbol (class/interface/struct/enum) in the cached reference index.
    FindSymbol {
        #[arg(long)]
        name: String,
        #[arg(long)]
        kind: Option<String>,
        #[arg(long)]
        namespace: Option<String>,
        #[arg(long)]
        version: Option<String>,
    },
    /// Diff a symbol or path range between two cached Unity versions.
    Diff {
        #[arg(long)]
        from: String,
        #[arg(long)]
        to: String,
        #[arg(long)]
        symbol: Option<String>,
        #[arg(long)]
        path: Option<String>,
        #[arg(long)]
        max_symbols: Option<u64>,
    },
    /// Resolve the C# token at a cursor position to candidate reference cache entries.
    ResolveSymbolAt {
        path: String,
        #[arg(long)]
        line: u32,
        #[arg(long)]
        column: u32,
        #[arg(long)]
        version: Option<String>,
    },
    /// Build the embedding index for a cached Unity version (uses fastembed).
    EmbedBuild {
        #[arg(long)]
        version: Option<String>,
    },
    /// Semantic search over the embedding index for a cached Unity version.
    EmbedSearch {
        #[arg(long)]
        query: String,
        #[arg(long)]
        version: Option<String>,
        #[arg(long)]
        top_k: Option<u64>,
    },
    /// Remove old UnityCsReference snapshots, keeping the newest entries.
    Clean {
        #[arg(long, default_value_t = 1)]
        keep: u64,
        #[arg(long)]
        version: Option<String>,
        #[arg(long, default_value_t = false)]
        dry_run: bool,
    },
}
