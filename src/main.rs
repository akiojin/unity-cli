mod cli;
mod config;
mod instances;
mod local_tools;
mod lsp;
mod lsp_manager;
mod lspd;
#[cfg(test)]
mod test_env;
mod tool_catalog;
mod transport;
mod unityd;

use std::fs;
use std::path::PathBuf;

use anyhow::{anyhow, Context, Result};
use clap::Parser;
use serde_json::{json, Value};
use tracing_subscriber::EnvFilter;

use crate::cli::{
    Cli, Command, InstancesCommand, LspCommand, LspdCommand, OutputFormat, RawArgs, SceneCommand,
    SystemCommand, ToolCommand, UnitydCommand,
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
    run_with_cli(cli).await
}

async fn run_with_cli(cli: Cli) -> Result<()> {
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
        Command::Unityd { command } => match command {
            UnitydCommand::Start => {
                let value = unityd::start_background()?;
                print_value(&value, cli.output)?;
            }
            UnitydCommand::Stop => {
                let value = unityd::stop()?;
                print_value(&value, cli.output)?;
            }
            UnitydCommand::Status => {
                let value = unityd::status()?;
                print_value(&value, cli.output)?;
            }
            UnitydCommand::Serve => {
                unityd::serve_forever().await?;
            }
        },
        Command::Batch { json, stdin } => {
            let value = execute_batch(&cli, json.as_deref(), *stdin).await?;
            print_value(&value, cli.output)?;
        }
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

    // Try daemon first (fast path).
    match unityd::try_call_tool(tool_name, &params, &config).await {
        Ok(value) => return Ok(value),
        Err(error) if error.is_transport() => {}
        Err(error) => return Err(error.into()),
    }

    // Direct TCP fallback
    let mut client = UnityClient::connect(&config).await.with_context(|| {
        format!(
            "Failed to connect to Unity at {}:{}",
            config.host, config.port
        )
    })?;
    client.call_tool(tool_name, params).await
}

async fn execute_batch(cli: &Cli, json_str: Option<&str>, use_stdin: bool) -> Result<Value> {
    let raw = if use_stdin {
        let mut buf = String::new();
        std::io::Read::read_to_string(&mut std::io::stdin(), &mut buf)
            .context("Failed to read batch JSON from stdin")?;
        buf
    } else if let Some(inline) = json_str {
        inline.to_string()
    } else {
        return Err(anyhow!("Provide --json or --stdin for batch input"));
    };

    let commands: Vec<unityd::BatchItem> =
        serde_json::from_str(&raw).context("Batch input must be a JSON array of {tool, params}")?;

    if commands.is_empty() {
        return Ok(json!([]));
    }

    let config = RuntimeConfig::from_cli(cli)?;

    // Try daemon first.
    match unityd::try_batch(commands, &config).await {
        Ok(value) => Ok(value),
        Err(error) if error.is_transport() => {
            // Cannot retry easily since commands were moved; re-parse.
            let commands2: Vec<unityd::BatchItem> = serde_json::from_str(&raw)
                .context("Batch input must be a JSON array of {tool, params}")?;
            execute_batch_direct(&config, commands2).await
        }
        Err(error) => Err(error.into()),
    }
}

async fn execute_batch_direct(
    config: &RuntimeConfig,
    commands: Vec<unityd::BatchItem>,
) -> Result<Value> {
    let mut client = UnityClient::connect(config).await.with_context(|| {
        format!(
            "Failed to connect to Unity at {}:{}",
            config.host, config.port
        )
    })?;

    let mut results = Vec::with_capacity(commands.len());
    for item in commands {
        match client.call_tool(&item.tool, item.params).await {
            Ok(value) => results.push(json!({ "ok": true, "result": value })),
            Err(error) => results.push(json!({ "ok": false, "error": error.to_string() })),
        }
    }

    Ok(Value::Array(results))
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
    use super::{
        init_tracing, load_params, parse_external_tool_command, parse_json_object, parse_ports,
        print_value, run_with_cli,
    };
    use crate::cli::{
        Cli, Command, InstancesCommand, LspdCommand, OutputFormat, RawArgs, SceneCommand,
        SystemCommand, ToolCommand, UnitydCommand,
    };
    use tempfile::tempdir;

    fn cli_for(command: Command) -> Cli {
        cli_for_with_output(command, OutputFormat::Json)
    }

    fn cli_for_with_output(command: Command, output: OutputFormat) -> Cli {
        Cli {
            output,
            host: Some("127.0.0.1".to_string()),
            port: Some(9),
            timeout_ms: Some(20),
            verbose: 0,
            command,
        }
    }

    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = std::env::var(key).ok();
            std::env::set_var(key, value);
            Self { key, previous }
        }
    }

    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(previous) = &self.previous {
                std::env::set_var(self.key, previous);
            } else {
                std::env::remove_var(self.key);
            }
        }
    }

    #[test]
    fn env_var_guard_restores_previous_value() {
        std::env::set_var("UNITY_CLI_TEST_GUARD", "before");
        {
            let _guard = EnvVarGuard::set("UNITY_CLI_TEST_GUARD", "during");
            assert_eq!(
                std::env::var("UNITY_CLI_TEST_GUARD").ok().as_deref(),
                Some("during")
            );
        }
        assert_eq!(
            std::env::var("UNITY_CLI_TEST_GUARD").ok().as_deref(),
            Some("before")
        );
        std::env::remove_var("UNITY_CLI_TEST_GUARD");
    }

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
    fn parse_ports_returns_empty_for_none() {
        let parsed = parse_ports(&None).expect("none ports should parse");
        assert!(parsed.is_empty());
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

    #[test]
    fn parse_external_tool_command_supports_equals_forms() {
        let args = vec![
            "ping".to_string(),
            "--json={\"message\":\"hi\"}".to_string(),
            "--params-file=/tmp/params.json".to_string(),
        ];
        let parsed = parse_external_tool_command(&args).expect("external args should parse");
        assert_eq!(parsed.tool_name, "ping");
        assert_eq!(parsed.json.as_deref(), Some("{\"message\":\"hi\"}"));
        assert_eq!(
            parsed.params_file.as_deref().and_then(|p| p.to_str()),
            Some("/tmp/params.json")
        );
    }

    #[test]
    fn parse_external_tool_command_requires_values_for_flags() {
        let json_err = parse_external_tool_command(&["ping".to_string(), "--json".to_string()])
            .expect_err("missing json value should fail");
        assert!(format!("{json_err:#}").contains("requires a value"));

        let file_err =
            parse_external_tool_command(&["ping".to_string(), "--params-file".to_string()])
                .expect_err("missing params file value should fail");
        assert!(format!("{file_err:#}").contains("requires a value"));
    }

    #[test]
    fn parse_external_tool_command_requires_tool_name() {
        let err = parse_external_tool_command(&[]).expect_err("tool name should be required");
        assert!(format!("{err:#}").contains("Tool name is required"));
    }

    #[test]
    fn load_params_rejects_when_json_and_params_file_are_both_set() {
        let args = RawArgs {
            tool_name: "ping".to_string(),
            json: Some("{\"message\":\"hi\"}".to_string()),
            params_file: Some("/tmp/params.json".into()),
        };
        let err = load_params(&args).expect_err("both json and params file should fail");
        assert!(err.to_string().contains("either --json or --params-file"));
    }

    #[test]
    fn load_params_reads_json_from_file() {
        let dir = tempdir().expect("tempdir should succeed");
        let path = dir.path().join("params.json");
        std::fs::write(&path, "{\"message\":\"from-file\"}")
            .expect("params fixture should be writable");

        let args = RawArgs {
            tool_name: "ping".to_string(),
            json: None,
            params_file: Some(path.clone()),
        };
        let value = load_params(&args).expect("params file should parse");
        assert_eq!(value["message"], "from-file");
    }

    #[test]
    fn load_params_defaults_to_empty_object() {
        let args = RawArgs {
            tool_name: "ping".to_string(),
            json: None,
            params_file: None,
        };
        let value = load_params(&args).expect("default params should succeed");
        assert_eq!(value, serde_json::json!({}));
    }

    #[test]
    fn load_params_reads_inline_json() {
        let args = RawArgs {
            tool_name: "ping".to_string(),
            json: Some("{\"message\":\"inline\"}".to_string()),
            params_file: None,
        };
        let value = load_params(&args).expect("inline json should parse");
        assert_eq!(value["message"], "inline");
    }

    #[test]
    fn print_value_text_handles_string_and_object() {
        print_value(&serde_json::json!("hello"), OutputFormat::Text)
            .expect("string text output should succeed");
        print_value(&serde_json::json!({"ok": true}), OutputFormat::Text)
            .expect("object text output should succeed");
    }

    #[test]
    fn init_tracing_accepts_verbose_levels() {
        init_tracing(1).expect("debug tracing should initialize");
        init_tracing(2).expect("trace tracing should initialize");
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_handles_local_tool_and_batch_paths() {
        run_with_cli(cli_for(Command::Tool {
            command: ToolCommand::List,
        }))
        .await
        .expect("tool list should succeed");

        run_with_cli(cli_for(Command::Raw(RawArgs {
            tool_name: "list_packages".to_string(),
            json: None,
            params_file: None,
        })))
        .await
        .expect("raw local tool should succeed");

        run_with_cli(cli_for(Command::Tool {
            command: ToolCommand::Call(RawArgs {
                tool_name: "list_packages".to_string(),
                json: None,
                params_file: None,
            }),
        }))
        .await
        .expect("tool call should succeed");

        run_with_cli(cli_for(Command::Batch {
            json: Some("[]".to_string()),
            stdin: false,
        }))
        .await
        .expect("empty batch should succeed");

        run_with_cli(cli_for_with_output(
            Command::Tool {
                command: ToolCommand::List,
            },
            OutputFormat::Text,
        ))
        .await
        .expect("tool list text output should succeed");
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_rejects_unknown_external_tool() {
        let err = run_with_cli(cli_for(Command::Tool {
            command: ToolCommand::External(vec!["not_existing_tool".to_string()]),
        }))
        .await
        .expect_err("unknown tool should fail");
        assert!(format!("{err:#}").contains("Unknown tool"));
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_exercises_remote_command_error_paths() {
        let ping_err = run_with_cli(cli_for(Command::System {
            command: SystemCommand::Ping {
                message: Some("hello".to_string()),
            },
        }))
        .await
        .expect_err("system ping should fail when unity is unreachable");
        assert!(format!("{ping_err:#}").contains("Failed to connect to Unity"));

        let scene_err = run_with_cli(cli_for(Command::Scene {
            command: SceneCommand::Create {
                scene_name: "Main".to_string(),
                path: Some("Assets/Main.unity".to_string()),
                load_scene: true,
                add_to_build_settings: false,
            },
        }))
        .await
        .expect_err("scene create should fail when unity is unreachable");
        assert!(format!("{scene_err:#}").contains("Failed to connect to Unity"));

        let batch_err = run_with_cli(cli_for(Command::Batch {
            json: Some(r#"[{"tool":"ping","params":{}}]"#.to_string()),
            stdin: false,
        }))
        .await
        .expect_err("non-empty batch should fail when unity is unreachable");
        assert!(format!("{batch_err:#}").contains("Failed to connect to Unity"));
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_handles_instances_and_daemon_commands_without_server() {
        run_with_cli(cli_for(Command::Instances {
            command: InstancesCommand::List {
                ports: Some("9".to_string()),
                host: "127.0.0.1".to_string(),
                timeout_ms: 20,
            },
        }))
        .await
        .expect("instances list should succeed");

        let set_active_err = run_with_cli(cli_for(Command::Instances {
            command: InstancesCommand::SetActive {
                id: "127.0.0.1:9".to_string(),
                timeout_ms: 20,
            },
        }))
        .await
        .expect_err("set-active should fail for unreachable id");
        assert!(format!("{set_active_err:#}").contains("Instance unreachable"));

        run_with_cli(cli_for(Command::Lspd {
            command: LspdCommand::Status,
        }))
        .await
        .expect("lspd status should succeed");
        run_with_cli(cli_for(Command::Lspd {
            command: LspdCommand::Stop,
        }))
        .await
        .expect("lspd stop should succeed");

        run_with_cli(cli_for(Command::Unityd {
            command: UnitydCommand::Status,
        }))
        .await
        .expect("unityd status should succeed");
        run_with_cli(cli_for(Command::Unityd {
            command: UnitydCommand::Stop,
        }))
        .await
        .expect("unityd stop should succeed");

        run_with_cli(cli_for_with_output(
            Command::Instances {
                command: InstancesCommand::List {
                    ports: Some("9".to_string()),
                    host: "127.0.0.1".to_string(),
                    timeout_ms: 20,
                },
            },
            OutputFormat::Text,
        ))
        .await
        .expect("instances list text output should succeed");
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_requires_batch_input_source() {
        let err = run_with_cli(cli_for(Command::Batch {
            json: None,
            stdin: false,
        }))
        .await
        .expect_err("batch command must require input");
        assert!(format!("{err:#}").contains("Provide --json or --stdin"));
    }

    #[allow(clippy::await_holding_lock)]
    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_set_active_text_output_when_reachable() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let registry = tempdir().expect("tempdir should succeed");
        let registry_path = registry.path().join("instances.json");
        let _registry_env = EnvVarGuard::set(
            "UNITY_CLI_REGISTRY_PATH",
            registry_path
                .to_str()
                .expect("registry path should be valid UTF-8"),
        );

        let listener = tokio::net::TcpListener::bind(("127.0.0.1", 0))
            .await
            .expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should expose local addr")
            .port();
        let accept_task = tokio::spawn(async move {
            let _ = listener.accept().await;
        });

        run_with_cli(cli_for_with_output(
            Command::Instances {
                command: InstancesCommand::SetActive {
                    id: format!("127.0.0.1:{port}"),
                    timeout_ms: 200,
                },
            },
            OutputFormat::Text,
        ))
        .await
        .expect("set-active text output should succeed for reachable instance");

        tokio::time::timeout(std::time::Duration::from_secs(1), accept_task)
            .await
            .expect("listener task should finish")
            .expect("listener task should succeed");
    }
}
