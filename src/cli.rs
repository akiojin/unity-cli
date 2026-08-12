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
    Batch {
        #[arg(long, value_name = "JSON")]
        json: Option<String>,
        #[arg(long)]
        stdin: bool,
    },
    /// Run as a Model Context Protocol (MCP) stdio server, exposing all Unity
    /// CLI tools to any MCP-compatible agent (Cursor, Cherry Studio, Claude
    /// SDK, etc.).
    Mcp,
}

#[derive(Debug, Subcommand)]
pub enum ToolCommand {
    List,
    Schema {
        tool_name: Option<String>,
    },
    Call(RawArgs),
    #[command(external_subcommand)]
    External(Vec<String>),
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

        #[arg(long, default_value = "localhost")]
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
