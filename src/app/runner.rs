use anyhow::{anyhow, Context, Result};
use clap::Parser;
use serde_json::{json, Value};
use std::fs;
use std::path::PathBuf;
use tracing_subscriber::EnvFilter;

use crate::cli::{
    Cli, CliCommand, Command, InstancesCommand, LspCommand, LspdCommand, OutputFormat, RawArgs,
    ReferenceCommand, SceneCommand, SkillFormat, SkillSeverity, SkillsCommand, SystemCommand,
    ToolCommand, UnitydCommand,
};
use crate::config::{RuntimeConfig, RuntimeOverrides};
use crate::core::command_stats::{self, CliCommandTiming};
use crate::core::contracts::BatchItem;
use crate::instances::{list_instances, set_active_instance};
use crate::tool_catalog::{get_tool_spec, is_known_tool, list_tool_specs, TOOL_NAMES};
use crate::transport::UnityClient;
use crate::{local_tools, lsp_manager, lspd, unityd};

pub async fn run() -> Result<()> {
    let cli = Cli::parse();
    run_with_cli(cli).await
}

pub async fn run_with_cli(cli: Cli) -> Result<()> {
    init_tracing(cli.verbose)?;

    // Background self-update (non-blocking). Skipped for `cli` subcommands
    // which manage the binary themselves.
    let mut update_handle = if !matches!(&cli.command, Command::Cli { .. }) {
        crate::core::self_update::maybe_self_update()
    } else {
        None
    };

    crate::core::self_update::warn_cargo_conflict();

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
            ToolCommand::Schema { tool_name } => {
                let value = if let Some(name) = tool_name {
                    let spec = get_tool_spec(name).ok_or_else(|| {
                        anyhow!(
                            "Unknown tool `{}`. Use `unity-cli tool list` to see supported names.",
                            name
                        )
                    })?;
                    serde_json::to_value(spec)?
                } else {
                    json!({
                        "tools": list_tool_specs()
                    })
                };
                print_value(&value, cli.output)?;
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
                let parsed = parse_ports_with_diagnostics(ports)?;
                if !parsed.duplicate_ports.is_empty() {
                    eprintln!(
                        "Warning: duplicate --ports values ignored: {}",
                        parsed
                            .duplicate_ports
                            .iter()
                            .map(u16::to_string)
                            .collect::<Vec<_>>()
                            .join(",")
                    );
                }
                let parsed_ports = parsed.ports;
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
        Command::Cli { command } => match command {
            CliCommand::Install { force } => {
                let value = lsp_manager::cli_install_latest(*force)?;
                print_value(&value, cli.output)?;
            }
            CliCommand::Doctor => {
                let value = lsp_manager::cli_doctor()?;
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
                // Wait for the self-update to finish before starting the daemon
                // so the daemon process uses the latest binary.
                if let Some(handle) = update_handle.take() {
                    let _ = handle.join();
                }
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
        Command::Skills { command } => match command {
            SkillsCommand::Lint {
                root,
                format,
                severity,
            } => {
                run_skills_lint(root.as_deref(), *format, *severity)?;
            }
        },
        Command::Reference { command } => {
            let (tool, params) = build_reference_call(command);
            let value = execute_tool(&cli, tool, params).await?;
            print_value(&value, cli.output)?;
        }
        Command::Batch { json, stdin } => {
            let value = execute_batch(&cli, json.as_deref(), *stdin).await?;
            print_value(&value, cli.output)?;
        }
    }

    // Wait for background self-update to complete before process exit so the
    // downloaded binary is fully written to disk.  Command output has already
    // been printed, so the user sees results immediately.
    if let Some(handle) = update_handle {
        let _ = handle.join();
    }

    Ok(())
}

fn build_reference_call(command: &ReferenceCommand) -> (&'static str, Value) {
    let mut params = serde_json::Map::new();
    let tool: &'static str = match command {
        ReferenceCommand::Fetch {
            version,
            branch,
            force,
            accept_license,
        } => {
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            if let Some(b) = branch {
                params.insert("branch".to_string(), Value::String(b.clone()));
            }
            params.insert("force".to_string(), Value::Bool(*force));
            params.insert("acceptLicense".to_string(), Value::Bool(*accept_license));
            "reference_fetch"
        }
        ReferenceCommand::Status { version } => {
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            "reference_status"
        }
        ReferenceCommand::Search {
            pattern,
            version,
            path,
            max_results,
            regex,
        } => {
            params.insert("pattern".to_string(), Value::String(pattern.clone()));
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            if let Some(p) = path {
                params.insert("path".to_string(), Value::String(p.clone()));
            }
            if let Some(n) = max_results {
                params.insert("maxResults".to_string(), Value::Number((*n).into()));
            }
            params.insert("regex".to_string(), Value::Bool(*regex));
            "reference_search"
        }
        ReferenceCommand::Grep {
            pattern,
            version,
            file_glob,
            context,
        } => {
            params.insert("pattern".to_string(), Value::String(pattern.clone()));
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            if let Some(g) = file_glob {
                params.insert("fileGlob".to_string(), Value::String(g.clone()));
            }
            params.insert(
                "context".to_string(),
                Value::Number((u64::from(*context)).into()),
            );
            "reference_grep"
        }
        ReferenceCommand::View {
            path,
            version,
            start_line,
            max_lines,
        } => {
            params.insert("path".to_string(), Value::String(path.clone()));
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            if let Some(n) = start_line {
                params.insert(
                    "startLine".to_string(),
                    Value::Number((u64::from(*n)).into()),
                );
            }
            if let Some(n) = max_lines {
                params.insert(
                    "maxLines".to_string(),
                    Value::Number((u64::from(*n)).into()),
                );
            }
            "reference_view"
        }
        ReferenceCommand::FindSymbol {
            name,
            kind,
            namespace,
            version,
        } => {
            params.insert("name".to_string(), Value::String(name.clone()));
            if let Some(k) = kind {
                params.insert("kind".to_string(), Value::String(k.clone()));
            }
            if let Some(ns) = namespace {
                params.insert("namespace".to_string(), Value::String(ns.clone()));
            }
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            return ("reference_find_symbol", Value::Object(params));
        }
        ReferenceCommand::Diff {
            from,
            to,
            symbol,
            path,
            max_symbols,
        } => {
            params.insert("from".to_string(), Value::String(from.clone()));
            params.insert("to".to_string(), Value::String(to.clone()));
            if let Some(s) = symbol {
                params.insert("symbol".to_string(), Value::String(s.clone()));
            }
            if let Some(p) = path {
                params.insert("path".to_string(), Value::String(p.clone()));
            }
            if let Some(n) = max_symbols {
                params.insert("maxSymbols".to_string(), Value::Number((*n).into()));
            }
            return ("reference_diff", Value::Object(params));
        }
        ReferenceCommand::ResolveSymbolAt {
            path,
            line,
            column,
            version,
        } => {
            params.insert("path".to_string(), Value::String(path.clone()));
            params.insert("line".to_string(), Value::Number((u64::from(*line)).into()));
            params.insert(
                "column".to_string(),
                Value::Number((u64::from(*column)).into()),
            );
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            return ("reference_resolve_symbol_at", Value::Object(params));
        }
        ReferenceCommand::EmbedBuild { version } => {
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            return ("reference_embed_build", Value::Object(params));
        }
        ReferenceCommand::EmbedSearch {
            query,
            version,
            top_k,
        } => {
            params.insert("query".to_string(), Value::String(query.clone()));
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            if let Some(k) = top_k {
                params.insert("topK".to_string(), Value::Number((*k).into()));
            }
            return ("reference_embed_search", Value::Object(params));
        }
        ReferenceCommand::Clean {
            keep,
            version,
            dry_run,
        } => {
            params.insert("keep".to_string(), Value::Number((*keep).into()));
            if let Some(v) = version {
                params.insert("version".to_string(), Value::String(v.clone()));
            }
            params.insert("dryRun".to_string(), Value::Bool(*dry_run));
            "reference_clean"
        }
    };
    (tool, Value::Object(params))
}

async fn execute_raw(cli: &Cli, args: &RawArgs) -> Result<Value> {
    let params = load_params(args)?;
    execute_tool(cli, &args.tool_name, params).await
}

async fn execute_tool(cli: &Cli, tool_name: &str, params: Value) -> Result<Value> {
    validate_tool_params(tool_name, &params)?;

    if should_skip_for_dry_run(cli, tool_name) {
        return Ok(json!({
            "dryRun": true,
            "executed": false,
            "tool": tool_name,
            "reason": "mutating_tool_blocked_by_dry_run",
            "params": params
        }));
    }

    if let Some(local_result) = local_tools::maybe_execute_local_tool(tool_name, &params) {
        return local_result;
    }

    let config = RuntimeConfig::from_overrides(&runtime_overrides_from_cli(cli))?;
    let (mut value, timing) = call_remote_tool_with_timing(&config, tool_name, params).await?;
    if tool_name == "get_command_stats" {
        augment_command_stats(&mut value);
    }
    if let Some(timing) = timing {
        command_stats::record_cli_tool_call(tool_name, timing);
    }
    Ok(value)
}

async fn call_remote_tool_with_timing(
    config: &RuntimeConfig,
    tool_name: &str,
    params: Value,
) -> Result<(Value, Option<CliCommandTiming>)> {
    // Try daemon first (fast path).
    match unityd::try_call_tool_with_timing(tool_name, &params, config).await {
        Ok(call) => {
            let remote_timing = call.timing;
            let connect_ms = remote_timing.as_ref().and_then(|timing| timing.connect_ms);
            let unity_roundtrip_ms = remote_timing
                .as_ref()
                .map(|timing| timing.transport.total_ms);
            let daemon_ipc_ms = Some(
                (call.daemon_roundtrip_ms
                    - connect_ms.unwrap_or_default()
                    - unity_roundtrip_ms.unwrap_or_default())
                .max(0.0),
            );
            return Ok((
                call.value,
                Some(CliCommandTiming {
                    route: "daemon",
                    success: true,
                    total_ms: call.daemon_roundtrip_ms,
                    daemon_ipc_ms,
                    connect_ms,
                    unity_roundtrip_ms,
                    send_ms: remote_timing
                        .as_ref()
                        .map(|timing| timing.transport.send_ms),
                    read_ms: remote_timing
                        .as_ref()
                        .map(|timing| timing.transport.read_ms),
                    normalize_ms: remote_timing
                        .as_ref()
                        .map(|timing| timing.transport.normalize_ms),
                }),
            ));
        }
        Err(error) if error.is_transport() => {}
        Err(error) => return Err(error.into()),
    }

    // Direct TCP fallback
    let connect_started_at = std::time::Instant::now();
    let mut client = UnityClient::connect(config).await.with_context(|| {
        format!(
            "Failed to connect to Unity at {}:{}",
            config.host, config.port
        )
    })?;
    let connect_ms = connect_started_at.elapsed().as_secs_f64() * 1000.0;
    let outcome = client.call_tool_with_timing(tool_name, params).await?;
    let unity_roundtrip_ms = outcome.timing.total_ms;
    Ok((
        outcome.value,
        Some(CliCommandTiming {
            route: "direct",
            success: true,
            total_ms: connect_ms + unity_roundtrip_ms,
            daemon_ipc_ms: None,
            connect_ms: Some(connect_ms),
            unity_roundtrip_ms: Some(unity_roundtrip_ms),
            send_ms: Some(outcome.timing.send_ms),
            read_ms: Some(outcome.timing.read_ms),
            normalize_ms: Some(outcome.timing.normalize_ms),
        }),
    ))
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

    let commands: Vec<BatchItem> =
        serde_json::from_str(&raw).context("Batch input must be a JSON array of {tool, params}")?;

    if commands.is_empty() {
        return Ok(json!([]));
    }

    for item in &commands {
        validate_tool_params(&item.tool, &item.params)
            .with_context(|| format!("Batch command validation failed for tool `{}`", item.tool))?;
    }

    if !cli.dry_run {
        let config = RuntimeConfig::from_overrides(&runtime_overrides_from_cli(cli))?;
        match unityd::try_batch(commands, &config).await {
            Ok(value) => return Ok(value),
            Err(error) if error.is_transport() => {
                let commands2: Vec<BatchItem> = serde_json::from_str(&raw)
                    .context("Batch input must be a JSON array of {tool, params}")?;
                return execute_batch_direct(&config, commands2).await;
            }
            Err(error) => return Err(error.into()),
        }
    }

    let mut results = Vec::with_capacity(commands.len());
    for item in commands {
        if should_skip_for_dry_run(cli, &item.tool) {
            results.push(json!({
                "ok": true,
                "skipped": true,
                "result": {
                    "dryRun": true,
                    "executed": false,
                    "tool": item.tool,
                    "reason": "mutating_tool_blocked_by_dry_run",
                    "params": item.params
                }
            }));
            continue;
        }

        match execute_tool(cli, &item.tool, item.params).await {
            Ok(value) => results.push(json!({ "ok": true, "result": value })),
            Err(error) => results.push(json!({ "ok": false, "error": error.to_string() })),
        }
    }

    Ok(Value::Array(results))
}

async fn execute_batch_direct(config: &RuntimeConfig, commands: Vec<BatchItem>) -> Result<Value> {
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

fn should_skip_for_dry_run(cli: &Cli, tool_name: &str) -> bool {
    if !cli.dry_run {
        return false;
    }
    get_tool_spec(tool_name)
        .map(|spec| spec.mutating)
        .unwrap_or(false)
}

fn runtime_overrides_from_cli(cli: &Cli) -> RuntimeOverrides {
    RuntimeOverrides {
        host: cli.host.clone(),
        port: cli.port,
        timeout_ms: cli.timeout_ms,
        dry_run: cli.dry_run,
        project_root: None,
    }
}

fn augment_command_stats(value: &mut Value) {
    let cli_stats = command_stats::snapshot_value();
    if let Some(object) = value.as_object_mut() {
        object.insert("cli".to_string(), cli_stats);
    }
}

fn validate_tool_params(tool_name: &str, params: &Value) -> Result<()> {
    let Some(spec) = get_tool_spec(tool_name) else {
        return Ok(());
    };

    validate_value_against_schema(params, &spec.params_schema, "$")
        .with_context(|| format!("Invalid parameters for tool `{tool_name}`"))
}

fn validate_value_against_schema(value: &Value, schema: &Value, path: &str) -> Result<()> {
    if let Some(expected_type) = schema.get("type").and_then(Value::as_str) {
        match expected_type {
            "object" => validate_object(value, schema, path)?,
            "array" => validate_array(value, schema, path)?,
            "string" if !value.is_string() => {
                return Err(anyhow!("{path} must be a string"));
            }
            "boolean" if !value.is_boolean() => {
                return Err(anyhow!("{path} must be a boolean"));
            }
            "integer" => {
                let is_integer = value.as_i64().is_some()
                    || value.as_u64().is_some()
                    || matches!(value, Value::Number(n) if n.as_i64().is_some() || n.as_u64().is_some());
                if !is_integer {
                    return Err(anyhow!("{path} must be an integer"));
                }
            }
            "number" if value.as_f64().is_none() => {
                return Err(anyhow!("{path} must be a number"));
            }
            _ => {}
        }
    }

    if let Some(enum_values) = schema.get("enum").and_then(Value::as_array) {
        if !enum_values.iter().any(|candidate| candidate == value) {
            return Err(anyhow!("{path} must be one of the allowed enum values"));
        }
    }

    if let Some(one_of) = schema.get("oneOf").and_then(Value::as_array) {
        let mut matches = 0usize;
        let mut failures: Vec<(bool, String)> = Vec::new();
        let action = value
            .as_object()
            .and_then(|obj| obj.get("action"))
            .and_then(Value::as_str);
        for variant in one_of {
            match validate_value_against_schema(value, variant, path) {
                Ok(()) => matches += 1,
                Err(err) => {
                    let action_matched = action
                        .map(|a| schema_variant_matches_action(variant, a))
                        .unwrap_or(false);
                    failures.push((action_matched, err.to_string()));
                }
            }
        }
        if matches != 1 {
            let detail = failures
                .iter()
                .find(|(action_matched, _)| *action_matched)
                .or_else(|| failures.first())
                .map(|(_, msg)| msg.as_str());
            return Err(anyhow!(
                "{path} must satisfy exactly one schema variant (matched {matches}){}",
                detail
                    .map(|msg| format!("; details: {msg}"))
                    .unwrap_or_default()
            ));
        }
    }

    if let Some(any_of) = schema.get("anyOf").and_then(Value::as_array) {
        let mut matches = 0usize;
        let mut failures: Vec<(bool, String)> = Vec::new();
        let action = value
            .as_object()
            .and_then(|obj| obj.get("action"))
            .and_then(Value::as_str);
        for variant in any_of {
            match validate_value_against_schema(value, variant, path) {
                Ok(()) => matches += 1,
                Err(err) => {
                    let action_matched = action
                        .map(|a| schema_variant_matches_action(variant, a))
                        .unwrap_or(false);
                    failures.push((action_matched, err.to_string()));
                }
            }
        }
        if matches == 0 {
            let detail = failures
                .iter()
                .find(|(action_matched, _)| *action_matched)
                .or_else(|| failures.first())
                .map(|(_, msg)| msg.as_str());
            return Err(anyhow!(
                "{path} must satisfy at least one schema variant{}",
                detail
                    .map(|msg| format!("; details: {msg}"))
                    .unwrap_or_default()
            ));
        }
    }

    Ok(())
}

fn validate_object(value: &Value, schema: &Value, path: &str) -> Result<()> {
    let obj = value
        .as_object()
        .ok_or_else(|| anyhow!("{path} must be an object"))?;

    let required = schema
        .get("required")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    for key in required {
        let Some(key_name) = key.as_str() else {
            continue;
        };
        if !obj.contains_key(key_name) {
            return Err(anyhow!("{path}.{key_name} is required"));
        }
    }

    let properties = schema.get("properties").and_then(Value::as_object);
    let allow_additional = schema
        .get("additionalProperties")
        .and_then(Value::as_bool)
        .unwrap_or(true);

    for (key, child_value) in obj {
        if let Some(prop_schema) = properties.and_then(|props| props.get(key)) {
            let child_path = format!("{path}.{key}");
            validate_value_against_schema(child_value, prop_schema, &child_path)?;
        } else if !allow_additional {
            return Err(anyhow!("{path}.{key} is not allowed"));
        }
    }

    Ok(())
}

fn validate_array(value: &Value, schema: &Value, path: &str) -> Result<()> {
    let items = value
        .as_array()
        .ok_or_else(|| anyhow!("{path} must be an array"))?;
    if let Some(item_schema) = schema.get("items") {
        for (idx, item) in items.iter().enumerate() {
            let item_path = format!("{path}[{idx}]");
            validate_value_against_schema(item, item_schema, &item_path)?;
        }
    }
    Ok(())
}

fn schema_variant_matches_action(variant: &Value, action: &str) -> bool {
    variant
        .get("properties")
        .and_then(Value::as_object)
        .and_then(|props| props.get("action"))
        .and_then(|action_schema| action_schema.get("enum"))
        .and_then(Value::as_array)
        .map(|candidates| {
            candidates
                .iter()
                .any(|candidate| candidate.as_str() == Some(action))
        })
        .unwrap_or(false)
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

#[derive(Debug, PartialEq, Eq)]
struct ParsedPorts {
    ports: Vec<u16>,
    duplicate_ports: Vec<u16>,
}

#[cfg(test)]
fn parse_ports(raw: &Option<String>) -> Result<Vec<u16>> {
    Ok(parse_ports_with_diagnostics(raw)?.ports)
}

fn parse_ports_with_diagnostics(raw: &Option<String>) -> Result<ParsedPorts> {
    let Some(csv) = raw else {
        return Ok(ParsedPorts {
            ports: Vec::new(),
            duplicate_ports: Vec::new(),
        });
    };

    let mut ports = Vec::new();
    let mut duplicate_ports = Vec::new();
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
        } else if !duplicate_ports.contains(&port) {
            duplicate_ports.push(port);
        }
    }

    Ok(ParsedPorts {
        ports,
        duplicate_ports,
    })
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

fn run_skills_lint(
    root: Option<&std::path::Path>,
    format: SkillFormat,
    severity: SkillSeverity,
) -> Result<()> {
    use crate::skills::model::Severity;
    use crate::skills::report::{render, ReportFormat};
    use crate::skills::{lint, LintOptions};

    let cwd = std::env::current_dir().context("get current_dir")?;
    let (skills_root, repo_root) = match root {
        Some(p) => {
            let abs = if p.is_absolute() {
                p.to_path_buf()
            } else {
                cwd.join(p)
            };
            let repo = abs
                .ancestors()
                .find(|a| a.join(".claude-plugin").is_dir() || a.join(".git").is_dir())
                .map(|p| p.to_path_buf())
                .unwrap_or_else(|| cwd.clone());
            (abs, repo)
        }
        None => match crate::skills::runner::discover_root(&cwd) {
            Some(pair) => pair,
            None => {
                return Err(anyhow!(
                    "could not auto-detect skills root; pass `--root <path>`"
                ));
            }
        },
    };

    let sev = match severity {
        SkillSeverity::Warning => Severity::Warning,
        SkillSeverity::Error => Severity::Error,
    };
    let outcome = lint(&LintOptions {
        root: skills_root,
        repo_root,
        severity: sev,
    })?;

    let fmt = match format {
        SkillFormat::Text => ReportFormat::Text,
        SkillFormat::Json => ReportFormat::Json,
    };
    let rendered = render(&outcome, fmt, sev);
    print!("{rendered}");
    if let SkillFormat::Json = format {
        if !rendered.ends_with('\n') {
            println!();
        }
    }

    if outcome.has_errors(sev) {
        std::process::exit(1);
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
        build_reference_call, execute_tool, init_tracing, load_params, parse_external_tool_command,
        parse_json_object, parse_ports, parse_ports_with_diagnostics, print_value, run_with_cli,
        runtime_overrides_from_cli, validate_tool_params,
    };
    use crate::cli::{
        Cli, Command, InstancesCommand, LspdCommand, OutputFormat, RawArgs, ReferenceCommand,
        SceneCommand, SystemCommand, ToolCommand, UnitydCommand,
    };
    use serde_json::json;
    use tempfile::tempdir;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};

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
            dry_run: false,
            command,
        }
    }

    fn cli_for_dry_run(command: Command) -> Cli {
        let mut cli = cli_for(command);
        cli.dry_run = true;
        cli
    }

    async fn spawn_mock_bridge(accept_count: usize) -> (u16, tokio::task::JoinHandle<()>) {
        let listener = tokio::net::TcpListener::bind(("127.0.0.1", 0))
            .await
            .expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should expose local addr")
            .port();

        let task = tokio::spawn(async move {
            for _ in 0..accept_count {
                let (mut socket, _) = listener.accept().await.expect("accept should succeed");
                let mut len_buf = [0_u8; 4];
                socket
                    .read_exact(&mut len_buf)
                    .await
                    .expect("request header should be readable");
                let payload_len = i32::from_be_bytes(len_buf);
                let mut payload = vec![0_u8; payload_len as usize];
                socket
                    .read_exact(&mut payload)
                    .await
                    .expect("request payload should be readable");

                let response = json!({
                    "status": "success",
                    "result": {"pong": true}
                });
                let response_bytes =
                    serde_json::to_vec(&response).expect("response should serialize");
                socket
                    .write_all(&(response_bytes.len() as i32).to_be_bytes())
                    .await
                    .expect("response header should write");
                socket
                    .write_all(&response_bytes)
                    .await
                    .expect("response payload should write");
            }
        });

        (port, task)
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
    fn runtime_overrides_are_derived_from_cli_at_app_boundary() {
        let cli = cli_for_dry_run(Command::Tool {
            command: ToolCommand::List,
        });

        let overrides = runtime_overrides_from_cli(&cli);

        assert_eq!(overrides.host.as_deref(), Some("127.0.0.1"));
        assert_eq!(overrides.port, Some(9));
        assert_eq!(overrides.timeout_ms, Some(20));
        assert!(overrides.dry_run);
        assert!(overrides.project_root.is_none());
    }

    #[test]
    fn parse_ports_deduplicates_values() {
        let parsed = parse_ports(&Some("6400, 6401,6400".to_string())).expect("ports should parse");
        assert_eq!(parsed, vec![6400, 6401]);
    }

    #[test]
    fn parse_ports_with_diagnostics_reports_duplicate_values() {
        let parsed = parse_ports_with_diagnostics(&Some("6400, 6401,6400, 6401".to_string()))
            .expect("ports should parse");
        assert_eq!(parsed.ports, vec![6400, 6401]);
        assert_eq!(parsed.duplicate_ports, vec![6400, 6401]);
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
    fn validate_tool_params_rejects_unknown_property_when_schema_is_strict() {
        let err = validate_tool_params("ping", &json!({ "unknown": true }))
            .expect_err("unknown key should fail for strict schema");
        assert!(format!("{err:#}").contains("$.unknown"));
    }

    #[test]
    fn augment_command_stats_inserts_cli_snapshot() {
        crate::core::command_stats::reset_for_tests();
        crate::core::command_stats::record_cli_tool_call(
            "ping",
            crate::core::command_stats::CliCommandTiming {
                route: "direct",
                success: true,
                total_ms: 5.0,
                daemon_ipc_ms: None,
                connect_ms: Some(1.0),
                unity_roundtrip_ms: Some(4.0),
                send_ms: Some(1.0),
                read_ms: Some(2.0),
                normalize_ms: Some(1.0),
            },
        );

        let mut value = json!({ "counts": {} });
        super::augment_command_stats(&mut value);

        assert_eq!(value["cli"]["perTool"]["ping"]["count"], 1);
        assert_eq!(value["cli"]["perTool"]["ping"]["routeCounts"]["direct"], 1);
    }

    #[test]
    fn validate_tool_params_rejects_missing_required_property() {
        let err = validate_tool_params("create_scene", &json!({}))
            .expect_err("missing required property should fail");
        assert!(format!("{err:#}").contains("$.sceneName is required"));
    }

    #[test]
    fn validate_tool_params_accepts_valid_payload() {
        validate_tool_params(
            "create_scene",
            &json!({
                "sceneName": "Main",
                "loadScene": true,
                "addToBuildSettings": false
            }),
        )
        .expect("valid payload should pass");
    }

    #[test]
    fn validate_tool_params_accepts_create_animator_controller_payload() {
        validate_tool_params(
            "create_animator_controller",
            &json!({
                "controllerPath": "Assets/Animations/Hero.controller",
                "parameters": [
                    {
                        "name": "isMoving",
                        "type": "Bool",
                        "defaultBool": false
                    }
                ],
                "states": [
                    {
                        "name": "Idle",
                        "motionPath": "Assets/Animations/Hero/Idle.anim"
                    },
                    {
                        "name": "Run"
                    }
                ],
                "defaultState": "Idle",
                "transitions": [
                    {
                        "from": "Idle",
                        "to": "Run",
                        "conditions": [
                            {
                                "parameter": "isMoving",
                                "mode": "If"
                            }
                        ]
                    }
                ]
            }),
        )
        .expect("create_animator_controller payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_invalid_animator_condition_mode() {
        let err = validate_tool_params(
            "create_animator_controller",
            &json!({
                "controllerPath": "Assets/Animations/Hero.controller",
                "transitions": [
                    {
                        "from": "Idle",
                        "to": "Run",
                        "conditions": [
                            {
                                "parameter": "isMoving",
                                "mode": "Always"
                            }
                        ]
                    }
                ]
            }),
        )
        .expect_err("invalid animator condition mode should fail");
        assert!(format!("{err:#}").contains("$.transitions[0].conditions[0].mode"));
    }

    #[test]
    fn validate_tool_params_accepts_create_animation_clip_payload() {
        validate_tool_params(
            "create_animation_clip",
            &json!({
                "clipPath": "Assets/Animations/Hero.anim",
                "spritePaths": [
                    "Assets/Sprites/Hero/idle_0.png",
                    "Assets/Sprites/Hero/idle_1.png"
                ],
                "frameRate": 12.0,
                "loopTime": true,
                "bindingPath": "Root/Hero"
            }),
        )
        .expect("create_animation_clip payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_create_animation_clip_without_sprite_paths() {
        let err = validate_tool_params(
            "create_animation_clip",
            &json!({
                "clipPath": "Assets/Animations/Hero.anim"
            }),
        )
        .expect_err("missing spritePaths should fail");
        assert!(format!("{err:#}").contains("$.spritePaths is required"));
    }

    #[test]
    fn validate_tool_params_accepts_create_sprite_atlas_payload() {
        validate_tool_params(
            "create_sprite_atlas",
            &json!({
                "atlasPath": "Assets/Atlases/UI.spriteatlas",
                "overwrite": true,
                "packables": ["Assets/Sprites/UI"],
                "packingSettings": {
                    "padding": 8,
                    "allowRotation": false,
                    "tightPacking": true
                },
                "textureSettings": {
                    "filterMode": "Bilinear",
                    "generateMipMaps": false
                }
            }),
        )
        .expect("create_sprite_atlas payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_invalid_sprite_atlas_filter_mode() {
        let err = validate_tool_params(
            "create_sprite_atlas",
            &json!({
                "atlasPath": "Assets/Atlases/UI.spriteatlas",
                "textureSettings": {
                    "filterMode": "Nearest"
                }
            }),
        )
        .expect_err("invalid sprite atlas filter mode should fail");
        assert!(format!("{err:#}").contains("$.textureSettings.filterMode"));
    }

    #[test]
    fn validate_tool_params_accepts_lsp_write_payload_with_path_alias() {
        validate_tool_params(
            "rename_symbol",
            &json!({
                "path": "Assets/Scripts/Player.cs",
                "namePath": "Player",
                "newName": "Hero",
                "apply": true
            }),
        )
        .expect("rename_symbol payload should pass");
    }

    #[test]
    fn validate_tool_params_accepts_build_index_output_path() {
        validate_tool_params(
            "build_index",
            &json!({
                "scope": "assets",
                "outputPath": "Library/unity-cli/index.json"
            }),
        )
        .expect("build_index outputPath should pass");
    }

    #[test]
    fn validate_tool_params_accepts_write_csharp_file_payload() {
        validate_tool_params(
            "write_csharp_file",
            &json!({
                "relative": "Assets/Scripts/Player.cs",
                "newText": "public class Player {}",
                "apply": true,
                "validate": true,
                "refresh": true,
                "waitForCompile": true,
                "updateIndex": true
            }),
        )
        .expect("write_csharp_file payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_create_package_setting_without_value() {
        let err = validate_tool_params(
            "set_package_setting",
            &json!({
                "package": "com.example.demo",
                "key": "coverage/enabled",
                "confirmChanges": true
            }),
        )
        .expect_err("missing value should fail");
        assert!(format!("{err:#}").contains("$.value is required"));
    }

    #[test]
    fn validate_tool_params_accepts_create_gameobject_with_vector_fields() {
        validate_tool_params(
            "create_gameobject",
            &json!({
                "name": "Player",
                "primitiveType": "cube",
                "position": { "x": 0.0, "y": 1.0, "z": 2.0 },
                "rotation": { "x": 0.0, "y": 90.0, "z": 0.0 },
                "scale": { "x": 1.0, "y": 1.0, "z": 1.0 }
            }),
        )
        .expect("create_gameobject payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_unknown_run_tests_property() {
        let err = validate_tool_params(
            "run_tests",
            &json!({
                "testMode": "EditMode",
                "unknown": true
            }),
        )
        .expect_err("unknown key should fail for run_tests schema");
        assert!(format!("{err:#}").contains("$.unknown is not allowed"));
    }

    #[test]
    fn validate_tool_params_accepts_load_scene_by_name() {
        validate_tool_params(
            "load_scene",
            &json!({
                "sceneName": "SampleScene",
                "loadMode": "Single"
            }),
        )
        .expect("load_scene sceneName payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_load_scene_with_both_identifiers() {
        let err = validate_tool_params(
            "load_scene",
            &json!({
                "scenePath": "Assets/Scenes/SampleScene.unity",
                "sceneName": "SampleScene"
            }),
        )
        .expect_err("load_scene should reject both scenePath and sceneName");
        assert!(format!("{err:#}").contains("exactly one schema variant"));
    }

    #[test]
    fn validate_tool_params_rejects_delete_gameobject_without_target() {
        let err = validate_tool_params("delete_gameobject", &json!({}))
            .expect_err("delete_gameobject should require path or paths");
        assert!(format!("{err:#}").contains("at least one schema variant"));
    }

    #[test]
    fn validate_tool_params_accepts_delete_gameobject_with_path_and_paths() {
        validate_tool_params(
            "delete_gameobject",
            &json!({
                "path": "/Root/Player",
                "paths": ["/Root/Enemy"]
            }),
        )
        .expect("delete_gameobject should allow path and paths together");
    }

    #[test]
    fn validate_tool_params_accepts_add_component_payload() {
        validate_tool_params(
            "add_component",
            &json!({
                "gameObjectPath": "/Main Camera",
                "componentType": "UnityEngine.Camera",
                "properties": {
                    "fieldOfView": 60.0
                }
            }),
        )
        .expect("add_component payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_set_component_field_missing_required() {
        let err = validate_tool_params(
            "set_component_field",
            &json!({
                "componentType": "UnityEngine.Camera"
            }),
        )
        .expect_err("set_component_field should require fieldPath");
        assert!(format!("{err:#}").contains("$.fieldPath is required"));
    }

    #[test]
    fn validate_tool_params_accepts_input_keyboard_batch_actions() {
        validate_tool_params(
            "input_keyboard",
            &json!({
                "actions": [
                    { "action": "press", "key": "space" },
                    { "action": "release", "key": "space" }
                ]
            }),
        )
        .expect("input_keyboard batched actions should pass");
    }

    #[test]
    fn validate_tool_params_rejects_analyze_screenshot_without_source() {
        let err = validate_tool_params("analyze_screenshot", &json!({}))
            .expect_err("analyze_screenshot should require imagePath or base64Data");
        assert!(format!("{err:#}").contains("at least one schema variant"));
    }

    #[test]
    fn validate_tool_params_rejects_unknown_field_for_clear_logs() {
        let err = validate_tool_params("clear_logs", &json!({ "unknown": true }))
            .expect_err("clear_logs should reject unknown keys");
        assert!(format!("{err:#}").contains("$.unknown is not allowed"));
    }

    #[test]
    fn validate_tool_params_accepts_profiler_start_payload() {
        validate_tool_params(
            "profiler_start",
            &json!({
                "mode": "normal",
                "recordToFile": true,
                "metrics": ["System Used Memory"],
                "maxDurationSec": 10.0
            }),
        )
        .expect("profiler_start payload should pass");
    }

    #[test]
    fn validate_tool_params_accepts_manage_layers_get_by_index() {
        validate_tool_params(
            "manage_layers",
            &json!({
                "action": "get_by_index",
                "layerIndex": 8
            }),
        )
        .expect("manage_layers get_by_index payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_manage_layers_invalid_action() {
        let err = validate_tool_params(
            "manage_layers",
            &json!({
                "action": "invalid"
            }),
        )
        .expect_err("invalid manage_layers action should fail");
        assert!(format!("{err:#}").contains("allowed enum"));
    }

    #[test]
    fn validate_tool_params_rejects_manage_layers_add_without_layer_name() {
        let err = validate_tool_params(
            "manage_layers",
            &json!({
                "action": "add"
            }),
        )
        .expect_err("manage_layers add should require layerName");
        assert!(format!("{err:#}").contains("$.layerName is required"));
    }

    #[test]
    fn validate_tool_params_accepts_addressables_manage_add_entry() {
        validate_tool_params(
            "addressables_manage",
            &json!({
                "action": "add_entry",
                "assetPath": "Assets/Prefabs/Player.prefab",
                "address": "player",
                "groupName": "Default"
            }),
        )
        .expect("addressables_manage add_entry payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_addressables_manage_move_entry_without_target_group() {
        let err = validate_tool_params(
            "addressables_manage",
            &json!({
                "action": "move_entry",
                "assetPath": "Assets/Prefabs/Player.prefab"
            }),
        )
        .expect_err("addressables_manage move_entry should require targetGroupName");
        assert!(format!("{err:#}").contains("$.targetGroupName is required"));
    }

    #[test]
    fn validate_tool_params_accepts_get_input_actions_state_with_asset_path() {
        validate_tool_params(
            "get_input_actions_state",
            &json!({
                "assetPath": "Assets/Input/Controls.inputactions",
                "includeBindings": true
            }),
        )
        .expect("get_input_actions_state payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_remove_input_binding_without_selector() {
        let err = validate_tool_params(
            "remove_input_binding",
            &json!({
                "assetPath": "Assets/Input/Controls.inputactions",
                "mapName": "Gameplay",
                "actionName": "Move"
            }),
        )
        .expect_err("remove_input_binding should require bindingIndex or bindingPath");
        assert!(format!("{err:#}").contains("at least one schema variant"));
    }

    #[test]
    fn validate_tool_params_accepts_package_manager_list_action() {
        validate_tool_params(
            "package_manager",
            &json!({
                "action": "list",
                "includeBuiltIn": false
            }),
        )
        .expect("package_manager list payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_package_manager_search_without_keyword() {
        let err = validate_tool_params(
            "package_manager",
            &json!({
                "action": "search"
            }),
        )
        .expect_err("package_manager search should require keyword");
        assert!(format!("{err:#}").contains("$.keyword is required"));
    }

    #[test]
    fn validate_tool_params_rejects_registry_config_add_scope_without_scope() {
        let err = validate_tool_params(
            "registry_config",
            &json!({
                "action": "add_scope",
                "registryName": "OpenUPM"
            }),
        )
        .expect_err("registry_config add_scope should require scope");
        assert!(format!("{err:#}").contains("$.scope is required"));
    }

    #[test]
    fn validate_tool_params_rejects_manage_selection_set_without_object_paths() {
        let err = validate_tool_params(
            "manage_selection",
            &json!({
                "action": "set"
            }),
        )
        .expect_err("manage_selection set should require objectPaths");
        assert!(format!("{err:#}").contains("$.objectPaths is required"));
    }

    #[test]
    fn validate_tool_params_rejects_manage_tools_activate_without_tool_name() {
        let err = validate_tool_params(
            "manage_tools",
            &json!({
                "action": "activate"
            }),
        )
        .expect_err("manage_tools activate should require toolName");
        assert!(format!("{err:#}").contains("$.toolName is required"));
    }

    #[test]
    fn validate_tool_params_rejects_manage_windows_focus_without_window_type() {
        let err = validate_tool_params(
            "manage_windows",
            &json!({
                "action": "focus"
            }),
        )
        .expect_err("manage_windows focus should require windowType");
        assert!(format!("{err:#}").contains("$.windowType is required"));
    }

    #[test]
    fn validate_tool_params_accepts_execute_menu_item_without_action() {
        validate_tool_params(
            "execute_menu_item",
            &json!({
                "menuPath": "Assets/Refresh",
                "safetyCheck": true
            }),
        )
        .expect("execute_menu_item should support omitted action (default execute)");
    }

    #[test]
    fn validate_tool_params_rejects_execute_menu_item_without_menu_path() {
        let err = validate_tool_params(
            "execute_menu_item",
            &json!({
                "action": "execute"
            }),
        )
        .expect_err("execute_menu_item execute should require menuPath");
        assert!(format!("{err:#}").contains("$.menuPath is required"));
    }

    #[test]
    fn validate_tool_params_accepts_execute_menu_item_get_available_menus() {
        validate_tool_params(
            "execute_menu_item",
            &json!({
                "action": "get_available_menus"
            }),
        )
        .expect("execute_menu_item get_available_menus payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_execute_menu_item_without_action_or_menu_path() {
        let err = validate_tool_params("execute_menu_item", &json!({}))
            .expect_err("execute_menu_item should require menuPath when action is omitted");
        assert!(format!("{err:#}").contains("$.menuPath is required"));
    }

    #[test]
    fn validate_tool_params_rejects_manage_asset_database_move_without_to_path() {
        let err = validate_tool_params(
            "manage_asset_database",
            &json!({
                "action": "move_asset",
                "fromPath": "Assets/A.prefab"
            }),
        )
        .expect_err("manage_asset_database move_asset should require toPath");
        assert!(format!("{err:#}").contains("$.toPath is required"));
    }

    #[test]
    fn validate_tool_params_rejects_addressables_analyze_dependencies_without_asset_path() {
        let err = validate_tool_params(
            "addressables_analyze",
            &json!({
                "action": "analyze_dependencies"
            }),
        )
        .expect_err("addressables_analyze analyze_dependencies should require assetPath");
        assert!(format!("{err:#}").contains("$.assetPath is required"));
    }

    #[test]
    fn validate_tool_params_accepts_analyze_asset_dependencies_get_dependencies() {
        validate_tool_params(
            "analyze_asset_dependencies",
            &json!({
                "action": "get_dependencies",
                "assetPath": "Assets/Prefabs/Player.prefab",
                "recursive": true
            }),
        )
        .expect("analyze_asset_dependencies get_dependencies payload should pass");
    }

    #[test]
    fn validate_tool_params_rejects_analyze_asset_dependencies_get_dependencies_without_asset_path()
    {
        let err = validate_tool_params(
            "analyze_asset_dependencies",
            &json!({
                "action": "get_dependencies"
            }),
        )
        .expect_err("get_dependencies should require assetPath");
        assert!(format!("{err:#}").contains("$.assetPath is required"));
    }

    #[test]
    fn validate_tool_params_rejects_analyze_asset_dependencies_invalid_action() {
        let err = validate_tool_params(
            "analyze_asset_dependencies",
            &json!({
                "action": "unknown"
            }),
        )
        .expect_err("invalid analyze_asset_dependencies action should fail");
        assert!(format!("{err:#}").contains("allowed enum"));
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
    async fn run_with_cli_supports_tool_schema_command() {
        run_with_cli(cli_for(Command::Tool {
            command: ToolCommand::Schema { tool_name: None },
        }))
        .await
        .expect("schema list should succeed");

        run_with_cli(cli_for(Command::Tool {
            command: ToolCommand::Schema {
                tool_name: Some("ping".to_string()),
            },
        }))
        .await
        .expect("single schema should succeed");
    }

    #[tokio::test(flavor = "current_thread")]
    async fn execute_tool_skips_mutating_tool_in_dry_run_mode() {
        let value = execute_tool(
            &cli_for_dry_run(Command::Tool {
                command: ToolCommand::List,
            }),
            "create_scene",
            json!({
                "sceneName": "Main"
            }),
        )
        .await
        .expect("dry-run mutating tool should not fail");

        assert_eq!(value["dryRun"], true);
        assert_eq!(value["executed"], false);
    }

    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_batch_marks_skipped_items_in_dry_run_mode() {
        run_with_cli(cli_for_dry_run(Command::Batch {
            json: Some(
                r#"[{"tool":"create_scene","params":{"sceneName":"Main"}},{"tool":"list_packages","params":{}}]"#
                    .to_string(),
            ),
            stdin: false,
        }))
        .await
        .expect("batch with dry-run should succeed");
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

    #[test]
    fn build_reference_call_status_includes_explicit_version() {
        let cmd = ReferenceCommand::Status {
            version: Some("2023.2.20f1".to_string()),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_status");
        assert_eq!(params["version"], "2023.2.20f1");
    }

    #[test]
    fn build_reference_call_status_omits_optional_version() {
        let cmd = ReferenceCommand::Status { version: None };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_status");
        assert!(params.get("version").is_none());
    }

    #[test]
    fn build_reference_call_fetch_carries_all_flags() {
        let cmd = ReferenceCommand::Fetch {
            version: Some("2023.2.20f1".to_string()),
            branch: Some("2023.2/staging".to_string()),
            force: true,
            accept_license: true,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_fetch");
        assert_eq!(params["version"], "2023.2.20f1");
        assert_eq!(params["branch"], "2023.2/staging");
        assert_eq!(params["force"], true);
        assert_eq!(params["acceptLicense"], true);
    }

    #[test]
    fn build_reference_call_fetch_defaults_without_overrides() {
        let cmd = ReferenceCommand::Fetch {
            version: None,
            branch: None,
            force: false,
            accept_license: false,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_fetch");
        assert!(params.get("version").is_none());
        assert!(params.get("branch").is_none());
        assert_eq!(params["force"], false);
        assert_eq!(params["acceptLicense"], false);
    }

    #[test]
    fn build_reference_call_search_with_all_options() {
        let cmd = ReferenceCommand::Search {
            pattern: "Animator".to_string(),
            version: Some("2023.2.20f1".to_string()),
            path: Some("Runtime/Animator*.cs".to_string()),
            max_results: Some(5),
            regex: true,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_search");
        assert_eq!(params["pattern"], "Animator");
        assert_eq!(params["version"], "2023.2.20f1");
        assert_eq!(params["path"], "Runtime/Animator*.cs");
        assert_eq!(params["maxResults"], 5);
        assert_eq!(params["regex"], true);
    }

    #[test]
    fn build_reference_call_search_minimal_pattern() {
        let cmd = ReferenceCommand::Search {
            pattern: "Foo".to_string(),
            version: None,
            path: None,
            max_results: None,
            regex: false,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_search");
        assert_eq!(params["pattern"], "Foo");
        assert_eq!(params["regex"], false);
        assert!(params.get("version").is_none());
        assert!(params.get("maxResults").is_none());
    }

    #[test]
    fn build_reference_call_grep_with_file_glob_and_context() {
        let cmd = ReferenceCommand::Grep {
            pattern: "class".to_string(),
            version: Some("2023.2.20f1".to_string()),
            file_glob: Some("*.cs".to_string()),
            context: 3,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_grep");
        assert_eq!(params["pattern"], "class");
        assert_eq!(params["fileGlob"], "*.cs");
        assert_eq!(params["context"], 3);
        assert_eq!(params["version"], "2023.2.20f1");
    }

    #[test]
    fn build_reference_call_grep_defaults_context_to_zero() {
        let cmd = ReferenceCommand::Grep {
            pattern: "class".to_string(),
            version: None,
            file_glob: None,
            context: 0,
        };
        let (_tool, params) = build_reference_call(&cmd);
        assert_eq!(params["context"], 0);
        assert!(params.get("fileGlob").is_none());
    }

    #[test]
    fn build_reference_call_view_with_range() {
        let cmd = ReferenceCommand::View {
            path: "Runtime/Export/Animation/Animator.bindings.cs".to_string(),
            version: Some("2023.2.20f1".to_string()),
            start_line: Some(100),
            max_lines: Some(60),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_view");
        assert_eq!(
            params["path"],
            "Runtime/Export/Animation/Animator.bindings.cs"
        );
        assert_eq!(params["startLine"], 100);
        assert_eq!(params["maxLines"], 60);
    }

    #[test]
    fn build_reference_call_view_omits_optional_lines() {
        let cmd = ReferenceCommand::View {
            path: "Editor/Foo.cs".to_string(),
            version: None,
            start_line: None,
            max_lines: None,
        };
        let (_tool, params) = build_reference_call(&cmd);
        assert_eq!(params["path"], "Editor/Foo.cs");
        assert!(params.get("startLine").is_none());
        assert!(params.get("maxLines").is_none());
    }

    #[test]
    fn build_reference_call_clean_with_version() {
        let cmd = ReferenceCommand::Clean {
            keep: 2,
            version: Some("legacy".to_string()),
            dry_run: true,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_clean");
        assert_eq!(params["keep"], 2);
        assert_eq!(params["version"], "legacy");
        assert_eq!(params["dryRun"], true);
    }

    #[test]
    fn build_reference_call_diff_symbol_mode() {
        let cmd = ReferenceCommand::Diff {
            from: "2022.3.10f1".to_string(),
            to: "2023.2.20f1".to_string(),
            symbol: Some("UnityEngine.Animator".to_string()),
            path: None,
            max_symbols: None,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_diff");
        assert_eq!(params["from"], "2022.3.10f1");
        assert_eq!(params["to"], "2023.2.20f1");
        assert_eq!(params["symbol"], "UnityEngine.Animator");
        assert!(params.get("path").is_none());
        assert!(params.get("maxSymbols").is_none());
    }

    #[test]
    fn build_reference_call_diff_path_mode_with_limit() {
        let cmd = ReferenceCommand::Diff {
            from: "v1".to_string(),
            to: "v2".to_string(),
            symbol: None,
            path: Some("Runtime/Export".to_string()),
            max_symbols: Some(20),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_diff");
        assert_eq!(params["path"], "Runtime/Export");
        assert_eq!(params["maxSymbols"], 20);
        assert!(params.get("symbol").is_none());
    }

    #[test]
    fn build_reference_call_resolve_symbol_at_with_version() {
        let cmd = ReferenceCommand::ResolveSymbolAt {
            path: "Assets/Scripts/Player.cs".to_string(),
            line: 42,
            column: 18,
            version: Some("2023.2.20f1".to_string()),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_resolve_symbol_at");
        assert_eq!(params["path"], "Assets/Scripts/Player.cs");
        assert_eq!(params["line"], 42);
        assert_eq!(params["column"], 18);
        assert_eq!(params["version"], "2023.2.20f1");
    }

    #[test]
    fn build_reference_call_resolve_symbol_at_minimal() {
        let cmd = ReferenceCommand::ResolveSymbolAt {
            path: "Packages/com.acme/Foo.cs".to_string(),
            line: 1,
            column: 1,
            version: None,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_resolve_symbol_at");
        assert!(params.get("version").is_none());
    }

    #[test]
    fn build_reference_call_embed_build_includes_version() {
        let cmd = ReferenceCommand::EmbedBuild {
            version: Some("2023.2.20f1".to_string()),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_embed_build");
        assert_eq!(params["version"], "2023.2.20f1");
    }

    #[test]
    fn build_reference_call_embed_build_omits_optional_version() {
        let cmd = ReferenceCommand::EmbedBuild { version: None };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_embed_build");
        assert!(params.get("version").is_none());
    }

    #[test]
    fn build_reference_call_embed_search_with_top_k() {
        let cmd = ReferenceCommand::EmbedSearch {
            query: "animator state callback".to_string(),
            version: Some("2023.2.20f1".to_string()),
            top_k: Some(5),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_embed_search");
        assert_eq!(params["query"], "animator state callback");
        assert_eq!(params["version"], "2023.2.20f1");
        assert_eq!(params["topK"], 5);
    }

    #[test]
    fn build_reference_call_embed_search_omits_optional_top_k() {
        let cmd = ReferenceCommand::EmbedSearch {
            query: "foo".to_string(),
            version: None,
            top_k: None,
        };
        let (_tool, params) = build_reference_call(&cmd);
        assert_eq!(params["query"], "foo");
        assert!(params.get("topK").is_none());
    }

    #[test]
    fn build_reference_call_find_symbol_with_filters() {
        let cmd = ReferenceCommand::FindSymbol {
            name: "Animator".to_string(),
            kind: Some("class".to_string()),
            namespace: Some("UnityEngine".to_string()),
            version: Some("2023.2.20f1".to_string()),
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_find_symbol");
        assert_eq!(params["name"], "Animator");
        assert_eq!(params["kind"], "class");
        assert_eq!(params["namespace"], "UnityEngine");
        assert_eq!(params["version"], "2023.2.20f1");
    }

    #[test]
    fn build_reference_call_find_symbol_minimal() {
        let cmd = ReferenceCommand::FindSymbol {
            name: "Foo".to_string(),
            kind: None,
            namespace: None,
            version: None,
        };
        let (tool, params) = build_reference_call(&cmd);
        assert_eq!(tool, "reference_find_symbol");
        assert_eq!(params["name"], "Foo");
        assert!(params.get("kind").is_none());
        assert!(params.get("namespace").is_none());
        assert!(params.get("version").is_none());
    }

    #[test]
    fn build_reference_call_clean_defaults() {
        let cmd = ReferenceCommand::Clean {
            keep: 1,
            version: None,
            dry_run: false,
        };
        let (_tool, params) = build_reference_call(&cmd);
        assert_eq!(params["keep"], 1);
        assert_eq!(params["dryRun"], false);
        assert!(params.get("version").is_none());
    }

    #[allow(clippy::await_holding_lock)]
    #[tokio::test(flavor = "current_thread")]
    async fn run_with_cli_set_active_text_output_when_reachable() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let registry = tempdir().expect("tempdir should succeed");
        let registry_path = registry.path().join("instances.json");
        std::fs::write(&registry_path, "{\n  \"entries\": []\n}\n")
            .expect("registry fixture should be initialized");
        let _registry_env = EnvVarGuard::set(
            "UNITY_CLI_REGISTRY_PATH",
            registry_path
                .to_str()
                .expect("registry path should be valid UTF-8"),
        );

        let (port, accept_task) = spawn_mock_bridge(1).await;

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
