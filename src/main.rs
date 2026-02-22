mod cli;
mod config;
mod instances;
mod local_tools;
mod lsp;
mod lsp_manager;
mod lspd;
mod tool_catalog;
mod transport;

use std::fs;
use std::path::PathBuf;

use anyhow::{anyhow, Context, Result};
use clap::Parser;
use serde_json::{json, Value};
use tracing_subscriber::EnvFilter;

use crate::cli::{
    Cli, Command, InstancesCommand, LspCommand, LspdCommand, OutputFormat, RawArgs, SceneCommand,
    SystemCommand, ToolCommand,
};
use crate::config::RuntimeConfig;
use crate::instances::{list_instances, set_active_instance};
use crate::tool_catalog::{is_known_tool, TOOL_NAMES};
use crate::transport::UnityClient;

#[tokio::main]
async fn main() {
    if let Err(error) = run().await {
        eprintln!("Error: {error:#}");
        std::process::exit(1);
    }
}

async fn run() -> Result<()> {
    let cli = Cli::parse();
    init_tracing(cli.verbose)?;

    match &cli.command {
        Command::Raw(args) => {
            let value = execute_raw(&cli, args).await?;
            print_value(&value, cli.output)?;
        }
        Command::Tool { command } => match command {
            ToolCommand::List => {
                if matches!(cli.output, OutputFormat::Json) {
                    print_value(&serde_json::to_value(TOOL_NAMES)?, cli.output)?;
                } else {
                    for name in TOOL_NAMES {
                        println!("{name}");
                    }
                }
            }
            ToolCommand::Call(args) => {
                let value = execute_raw(&cli, args).await?;
                print_value(&value, cli.output)?;
            }
            ToolCommand::External(args) => {
                let raw = parse_external_tool_command(args)?;
                if !is_known_tool(&raw.tool_name) {
                    return Err(anyhow!(
                        "Unknown tool `{}`. Use `unity-cli tool list` to see supported names.",
                        raw.tool_name
                    ));
                }
                let value = execute_raw(&cli, &raw).await?;
                print_value(&value, cli.output)?;
            }
        },
        Command::System { command } => match command {
            SystemCommand::Ping { message } => {
                let mut params = serde_json::Map::new();
                if let Some(msg) = message {
                    params.insert("message".to_string(), Value::String(msg.clone()));
                }
                let value = execute_tool(&cli, "ping", Value::Object(params)).await?;
                print_value(&value, cli.output)?;
            }
        },
        Command::Scene { command } => match command {
            SceneCommand::Create {
                scene_name,
                path,
                load_scene,
                add_to_build_settings,
            } => {
                let mut params = serde_json::Map::new();
                params.insert("sceneName".to_string(), Value::String(scene_name.clone()));
                params.insert("loadScene".to_string(), Value::Bool(*load_scene));
                params.insert(
                    "addToBuildSettings".to_string(),
                    Value::Bool(*add_to_build_settings),
                );
                if let Some(scene_path) = path {
                    params.insert("path".to_string(), Value::String(scene_path.clone()));
                }

                let value = execute_tool(&cli, "create_scene", Value::Object(params)).await?;
                print_value(&value, cli.output)?;
            }
        },
        Command::Instances { command } => match command {
            InstancesCommand::List {
                ports,
                host,
                timeout_ms,
            } => {
                let parsed_ports = parse_ports(ports)?;
                let statuses = list_instances(host, &parsed_ports, *timeout_ms).await?;

                if matches!(cli.output, OutputFormat::Json) {
                    print_value(&serde_json::to_value(&statuses)?, cli.output)?;
                } else {
                    for status in statuses {
                        let marker = if status.active { "*" } else { " " };
                        println!(
                            "{} {:<21} {:<5} checked_at={}",
                            marker, status.id, status.status, status.last_checked_at
                        );
                    }
                }
            }
            InstancesCommand::SetActive { id, timeout_ms } => {
                let result = set_active_instance(id, *timeout_ms).await?;
                let value = serde_json::to_value(&result)?;
                if matches!(cli.output, OutputFormat::Json) {
                    print_value(&value, cli.output)?;
                } else {
                    println!(
                        "active instance changed: {} -> {}",
                        result.previous_id.as_deref().unwrap_or("(none)"),
                        result.active_id
                    );
                }
            }
        },
        Command::Lsp { command } => match command {
            LspCommand::Install => {
                let value = lsp_manager::install_latest()?;
                print_value(&value, cli.output)?;
            }
            LspCommand::Doctor => {
                let value = lsp_manager::doctor()?;
                print_value(&value, cli.output)?;
            }
        },
        Command::Lspd { command } => match command {
            LspdCommand::Start => {
                let value = lspd::start_background()?;
                print_value(&value, cli.output)?;
            }
            LspdCommand::Stop => {
                let value = lspd::stop()?;
                print_value(&value, cli.output)?;
            }
            LspdCommand::Status => {
                let value = lspd::status()?;
                print_value(&value, cli.output)?;
            }
            LspdCommand::Serve => {
                lspd::serve_forever()?;
            }
        },
    }

    Ok(())
}

async fn execute_raw(cli: &Cli, args: &RawArgs) -> Result<Value> {
    let params = load_params(args)?;
    execute_tool(cli, &args.tool_name, params).await
}

async fn execute_tool(cli: &Cli, tool_name: &str, params: Value) -> Result<Value> {
    if let Some(local_result) = local_tools::maybe_execute_local_tool(tool_name, &params) {
        return local_result;
    }

    let config = RuntimeConfig::from_cli(cli)?;
    let mut client = UnityClient::connect(&config).await.with_context(|| {
        format!(
            "Failed to connect to Unity at {}:{}",
            config.host, config.port
        )
    })?;
    client.call_tool(tool_name, params).await
}

fn load_params(args: &RawArgs) -> Result<Value> {
    if args.json.is_some() && args.params_file.is_some() {
        return Err(anyhow!("Use either --json or --params-file, not both"));
    }

    if let Some(file) = &args.params_file {
        let content = fs::read_to_string(file)
            .with_context(|| format!("Failed to read params file: {}", file.display()))?;
        return parse_json_object(&content);
    }

    if let Some(inline) = &args.json {
        return parse_json_object(inline);
    }

    Ok(json!({}))
}

fn parse_external_tool_command(args: &[String]) -> Result<RawArgs> {
    if args.is_empty() {
        return Err(anyhow!(
            "Tool name is required. Use `unity-cli tool list` to see available tools."
        ));
    }

    let tool_name = args[0].clone();
    let mut json = None;
    let mut params_file = None;

    let mut idx = 1;
    while idx < args.len() {
        let arg = &args[idx];
        if arg == "--json" {
            idx += 1;
            let value = args
                .get(idx)
                .ok_or_else(|| anyhow!("`--json` requires a value"))?;
            json = Some(value.clone());
        } else if let Some(value) = arg.strip_prefix("--json=") {
            json = Some(value.to_string());
        } else if arg == "--params-file" {
            idx += 1;
            let value = args
                .get(idx)
                .ok_or_else(|| anyhow!("`--params-file` requires a value"))?;
            params_file = Some(PathBuf::from(value));
        } else if let Some(value) = arg.strip_prefix("--params-file=") {
            params_file = Some(PathBuf::from(value));
        } else {
            return Err(anyhow!(
                "Unsupported argument `{arg}` for `unity-cli tool <tool>`. Use --json or --params-file."
            ));
        }
        idx += 1;
    }

    Ok(RawArgs {
        tool_name,
        json,
        params_file,
    })
}

fn parse_json_object(raw: &str) -> Result<Value> {
    let value: Value = serde_json::from_str(raw).context("Failed to parse JSON parameters")?;
    if !value.is_object() {
        return Err(anyhow!("Tool parameters must be a JSON object"));
    }
    Ok(value)
}

fn parse_ports(raw: &Option<String>) -> Result<Vec<u16>> {
    let Some(csv) = raw else {
        return Ok(Vec::new());
    };

    let mut ports = Vec::new();
    for token in csv
        .split(',')
        .map(str::trim)
        .filter(|value| !value.is_empty())
    {
        let port = token
            .parse::<u16>()
            .with_context(|| format!("Invalid port in --ports: {token}"))?;
        if !ports.contains(&port) {
            ports.push(port);
        }
    }

    Ok(ports)
}

fn print_value(value: &Value, format: OutputFormat) -> Result<()> {
    match format {
        OutputFormat::Json => {
            println!("{}", serde_json::to_string_pretty(value)?);
        }
        OutputFormat::Text => {
            if let Some(text) = value.as_str() {
                println!("{text}");
            } else {
                println!("{}", serde_json::to_string_pretty(value)?);
            }
        }
    }
    Ok(())
}

fn init_tracing(verbose: u8) -> Result<()> {
    let level = match verbose {
        0 => "info",
        1 => "debug",
        _ => "trace",
    };

    let env_filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(level));
    tracing_subscriber::fmt()
        .with_env_filter(env_filter)
        .with_target(false)
        .compact()
        .try_init()
        .ok();

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{parse_external_tool_command, parse_json_object, parse_ports};

    #[test]
    fn parse_ports_deduplicates_values() {
        let parsed = parse_ports(&Some("6400, 6401,6400".to_string())).expect("ports should parse");
        assert_eq!(parsed, vec![6400, 6401]);
    }

    #[test]
    fn parse_ports_rejects_invalid_values() {
        let err = parse_ports(&Some("6400,abc".to_string())).expect_err("invalid port should fail");
        assert!(format!("{err:#}").contains("Invalid port"));
    }

    #[test]
    fn parse_json_object_accepts_object() {
        let value = parse_json_object("{\"foo\":\"bar\"}").expect("object should parse");
        assert!(value.is_object());
    }

    #[test]
    fn parse_json_object_rejects_non_object() {
        let err = parse_json_object("[1,2,3]").expect_err("array should be rejected");
        assert!(format!("{err:#}").contains("JSON object"));
    }

    #[test]
    fn parse_external_tool_command_accepts_json_flag() {
        let args = vec![
            "ping".to_string(),
            "--json".to_string(),
            "{\"message\":\"hi\"}".to_string(),
        ];
        let parsed = parse_external_tool_command(&args).expect("external args should parse");
        assert_eq!(parsed.tool_name, "ping");
        assert_eq!(parsed.json.as_deref(), Some("{\"message\":\"hi\"}"));
        assert!(parsed.params_file.is_none());
    }

    #[test]
    fn parse_external_tool_command_rejects_unknown_flag() {
        let args = vec!["ping".to_string(), "--unknown".to_string()];
        let err =
            parse_external_tool_command(&args).expect_err("unsupported option should be rejected");
        assert!(format!("{err:#}").contains("Unsupported argument"));
    }
}
