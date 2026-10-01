//! Test execution owns only Editors it starts. Polling never resubmits a run.
use std::time::Duration;

use anyhow::{anyhow, bail, Context, Result};
use serde_json::json;

use crate::cli::{Cli, TestArgs, TestMode, TestOutputFormat, TestReport};
use crate::config::{RuntimeConfig, RuntimeOverrides};
use crate::core::failure::{self, FailureKind, UnityCommandError};
use crate::transport::UnityClient;

pub async fn run(cli: &Cli, args: &TestArgs) -> Result<()> {
    if cli.dry_run {
        println!(
            "{}",
            json!({"dryRun":true,"command":"test","output":args.report_output})
        );
        return Ok(());
    }
    let root = crate::core::doctor::resolve_project_path(cli.project_path.as_deref())
        .context("Specify --project-path")?
        .canonicalize()?;
    let existing = super::editor::running(&root);
    let mut child = if let Some(pid) = existing {
        eprintln!("Reusing Editor (pid {pid})");
        None
    } else {
        let version = super::setup::project_unity_version(&root)?;
        let editor = std::env::var_os("UNITY_EDITOR_PATH")
            .map(std::path::PathBuf::from)
            .unwrap_or_else(|| super::setup::hub_editor_path(&version));
        std::fs::create_dir_all(root.join("Logs"))?;
        // Do not interpret compiler errors from a previous invocation while
        // the new Editor is still starting and has not opened its log yet.
        std::fs::File::create(root.join("Logs/unity-cli-editor.log"))?;
        let mut command = super::editor::launch_command(&editor, &root, true);
        if let Some(port) = cli.port {
            command.env("UNITY_CLI_PORT_OVERRIDE", port.to_string());
        }
        let child = command.spawn().context(FailureKind::Unreachable)?;
        eprintln!("Started headless Editor (pid {})", child.id());
        Some(child)
    };
    let mut test_started = false;
    // Preserve cleanup even if readiness, compilation, polling or report writing fails.
    let execution = tokio::time::timeout(Duration::from_secs(args.timeout), async {
        let mut client = loop {
            if let Some(child) = child.as_mut() {
                if child.try_wait()?.is_some() {
                    bail!("Editor exited before tests could run; inspect Logs/unity-cli-editor.log");
                }
            }
            if let Ok(config) = configuration(cli, &root) {
                if let Ok(mut client) = UnityClient::connect(&config).await {
                    if let Ok(pong) = client.call_tool("ping", json!({})).await {
                        if pong["projectPath"].as_str().and_then(|p| std::path::Path::new(p).canonicalize().ok()).as_ref() == Some(&root) {
                            let state = match client.call_tool("get_compilation_state", json!({})).await {
                                Ok(state) => state,
                                Err(error) if error.downcast_ref::<UnityCommandError>().is_some() => return Err(error),
                                Err(_) => {
                                    // Asset imports/domain initialization can stall or
                                    // replace the listener after a successful ping.
                                    tokio::time::sleep(Duration::from_millis(500)).await;
                                    continue;
                                }
                            };
                            if state["errorCount"].as_u64().unwrap_or(0) > 0 {
                                bail!("Unity project has compilation errors");
                            }
                            if state["isCompiling"] != true { break client; }
                        }
                    }
                }
            }
            // Batch Editors with syntax errors can never initialize the Bridge.
            if child.is_some() {
                let log = std::fs::read_to_string(root.join("Logs/unity-cli-editor.log")).unwrap_or_default();
                if log.contains("error CS") || log.contains("Scripts have compiler errors") {
                    bail!("Unity project has compilation errors; inspect Logs/unity-cli-editor.log");
                }
            }
            tokio::time::sleep(Duration::from_millis(500)).await;
        };
        let mode = match args.mode { TestMode::Editmode => "EditMode", TestMode::Playmode => "PlayMode" };
        client.set_timeout(Duration::from_secs(args.timeout));
        let started = client.call_tool("run_tests", json!({"testMode":mode,"filter":args.filter,"includeDetails":true,"disableDomainReload":args.disable_domain_reload})).await?;
        let run_id = started["runId"].as_str().context("Test runner did not return a run ID")?.to_owned();
        test_started = true;
        loop {
            // Domain Reload can replace the listener and token. Resolve each poll.
            tokio::time::sleep(Duration::from_millis(500)).await;
            let response = async {
                let config = configuration(cli, &root)?;
                let mut client = UnityClient::connect(&config).await?;
                client.call_tool("get_test_status", json!({})).await
            }.await;
            let value = match response {
                Ok(value) => value,
                Err(error) => {
                    if let Some(failure) = error.downcast_ref::<UnityCommandError>() {
                        if failure::failed_tests(&failure.response) { failure.response.clone() }
                        else { return Err(error); }
                    } else { continue; }
                }
            };
            if value["runId"].as_str() != Some(&run_id) {
                bail!("Test run ID changed while polling; refusing unrelated results");
            }
            if value["status"] == "completed" { return Ok(value); }
        }
    }).await;
    if let Some(mut child) = child {
        // Only our child is terminated; an existing GUI Editor is never closed.
        #[cfg(unix)]
        unsafe {
            libc::kill(child.id() as i32, libc::SIGTERM);
        }
        for _ in 0..100 {
            if child.try_wait()?.is_some() {
                break;
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
        if child.try_wait()?.is_none() {
            child.kill()?;
        }
        child.wait()?;
    }
    let result = execution
        .map_err(|_| {
            anyhow!("Test execution exceeded {} seconds", args.timeout).context(if test_started {
                FailureKind::Operation
            } else {
                FailureKind::Unreachable
            })
        })?
        .context(FailureKind::Operation)?;
    let report = match args.report {
        TestReport::Junit => super::test_report::junit(&result),
        TestReport::Nunit => super::test_report::nunit(&result),
    };
    if let Some(parent) = args
        .report_output
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
    {
        std::fs::create_dir_all(parent).context(FailureKind::Operation)?;
    }
    std::fs::write(&args.report_output, report).context(FailureKind::Operation)?;
    if matches!(args.output_format, TestOutputFormat::Github) {
        for line in super::test_report::annotations(&result) {
            println!("{line}");
        }
    }
    println!(
        "{} tests, {} failed; report: {}",
        result["totalTests"],
        result["failedTests"],
        args.report_output.display()
    );
    failure::check_response(&result)
}

fn configuration(cli: &Cli, root: &std::path::Path) -> Result<RuntimeConfig> {
    RuntimeConfig::from_overrides(&RuntimeOverrides {
        host: cli.host.clone(),
        port: cli.port,
        project_root: Some(root.to_path_buf()),
        timeout_ms: Some(2000),
        ..Default::default()
    })
}
