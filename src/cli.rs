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
}

#[derive(Debug, Subcommand)]
pub enum ToolCommand {
    List,
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
