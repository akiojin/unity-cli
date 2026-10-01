//! Local Editor process lifecycle. Readiness is tied to the project's lockfile,
//! never to an arbitrary listener on the default port.
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;

use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Value};

use crate::cli::Cli;
use crate::config::{RuntimeConfig, RuntimeOverrides};
use crate::core::failure::UnityCommandError;
use crate::core::{doctor, editor_discovery, endpoint};
use crate::transport::UnityClient;

fn project(cli: &Cli) -> Result<PathBuf> {
    doctor::resolve_project_path(cli.project_path.as_deref())
        .context("Specify a Unity project with --project-path")?
        .canonicalize()
        .context("Unity project path does not exist")
}

fn config(cli: &Cli, root: &Path) -> Result<RuntimeConfig> {
    let endpoint = endpoint::resolve_endpoint_for_project(cli.host.clone(), cli.port, root)?;
    Ok(RuntimeConfig {
        host: endpoint.host,
        port: endpoint.port,
        timeout: Duration::from_millis(cli.timeout_ms.unwrap_or(30_000)),
    })
}

pub(super) fn running(root: &Path) -> Option<u32> {
    doctor::list_editor_processes()
        .iter()
        .find(|p| doctor::editor_matches_project(p, root))
        .map(|p| p.pid)
}

pub(super) fn launch_command(editor: &Path, root: &Path, headless: bool) -> Command {
    let mut command = Command::new(editor);
    command
        .arg("-projectPath")
        .arg(root)
        .arg("-logFile")
        .arg(root.join("Logs/unity-cli-editor.log"))
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    if headless {
        command
            .args(["-batchmode", "-nographics"])
            .env("UNITY_CLI_ALLOW_BATCH_HOST", "1");
    }
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        command.process_group(0);
    }
    command
}

pub async fn open(cli: &Cli, headless: bool, wait_ready: u64) -> Result<Value> {
    let root = project(cli)?;
    let version = super::setup::project_unity_version(&root)?;
    let editor = std::env::var_os("UNITY_EDITOR_PATH")
        .map(PathBuf::from)
        .unwrap_or_else(|| super::setup::hub_editor_path(&version));
    if !editor.is_file() {
        return Err(UnityCommandError::new(json!({
            "status": "error", "code": "PRECONDITION_FAILED",
            "error": format!("Unity {version} Editor not found at {}. Run `unity install {version}` or set UNITY_EDITOR_PATH to an installed Editor", editor.display()),
        })).into());
    }
    if cli.dry_run {
        return Ok(
            json!({"dryRun": true, "editorPath": editor, "projectPath": root,
            "headless": headless, "waitReady": wait_ready}),
        );
    }
    let existing = running(&root);
    let mut child = if existing.is_none() {
        std::fs::create_dir_all(root.join("Logs"))?;
        let mut command = launch_command(&editor, &root, headless);
        if let Some(port) = cli.port {
            command.env("UNITY_CLI_PORT_OVERRIDE", port.to_string());
        }
        Some(
            command
                .spawn()
                .with_context(|| format!("Failed to launch {}", editor.display()))?,
        )
    } else {
        None
    };
    let pid = child
        .as_ref()
        .map(std::process::Child::id)
        .or(existing)
        .unwrap();
    let mut report = json!({"pid": pid, "projectPath": root, "unityVersion": version,
        "editorPath": editor, "launched": existing.is_none(), "headless": headless, "ready": false});
    if wait_ready == 0 {
        return Ok(report);
    }
    let waiting = async {
        loop {
            if let Some(child) = &mut child {
                if let Some(exit) = child.try_wait()? {
                    bail!(
                        "Unity Editor exited before ready ({exit}); see {}",
                        root.join("Logs/unity-cli-editor.log").display()
                    );
                }
            }
            let editors = editor_discovery::discover();
            if let Some(found) = editors.iter().find(|e| e.is_live() && e.lock.pid == pid) {
                let probe = RuntimeConfig {
                    host: found.lock.host.clone(),
                    port: found.lock.port,
                    timeout: Duration::from_secs(1),
                };
                if let Ok(mut client) = UnityClient::connect(&probe).await {
                    if let Ok(pong) = client.call_tool("ping", json!({})).await {
                        let matches = pong["projectPath"]
                            .as_str()
                            .and_then(|p| Path::new(p).canonicalize().ok())
                            == Some(root.clone());
                        if matches && found.lock.state.as_deref() != Some("compiling") {
                            report["ready"] = json!(true);
                            report["host"] = json!(probe.host);
                            report["port"] = json!(probe.port);
                            return Ok(report.clone());
                        }
                    }
                }
            }
            tokio::time::sleep(Duration::from_millis(250)).await;
        }
    };
    tokio::time::timeout(Duration::from_secs(wait_ready), waiting).await
        .map_err(|_| anyhow!("Editor did not become ready within {wait_ready}s; run `unity-cli editor status --project-path {}` (Editor remains running)", root.display()))?
}

pub async fn close(cli: &Cli, force: bool) -> Result<Value> {
    let config = RuntimeConfig::from_overrides(&RuntimeOverrides {
        host: cli.host.clone(),
        port: cli.port,
        project_root: cli.project_path.clone(),
        timeout_ms: cli.timeout_ms,
        ..Default::default()
    })?;
    if cli.dry_run {
        return Ok(json!({"dryRun": true, "tool": "quit_editor", "force": force}));
    }
    let mut client = UnityClient::connect(&config).await?;
    let pong = client.call_tool("ping", json!({})).await?;
    if !force && pong["supportsSafeClose"] != true {
        bail!("This Bridge does not support scene protection; upgrade the Bridge before using editor close, or explicitly use --force to discard changes");
    }
    let root = pong["projectPath"]
        .as_str()
        .context("Bridge ping did not identify the project")?;
    let pid = running(Path::new(root))
        .context("Cannot identify the local Editor process; close requires a local Editor")?;
    client
        .call_tool("quit_editor", json!({"force": force}))
        .await?;
    tokio::time::timeout(config.timeout, async {
        while editor_discovery::pid_alive(pid) {
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
    })
    .await
    .context("Editor accepted quit but its process did not exit before the timeout")?;
    Ok(json!({"closed": true, "pid": pid, "projectPath": root}))
}

pub async fn status(cli: &Cli) -> Result<Value> {
    let root = project(cli)?;
    let mut config = config(cli, &root)?;
    config.timeout = config.timeout.min(Duration::from_secs(1));
    if let Ok(mut client) = UnityClient::connect(&config).await {
        if let Ok(pong) = client.call_tool("ping", json!({})).await {
            if pong["projectPath"]
                .as_str()
                .and_then(|p| Path::new(p).canonicalize().ok())
                == Some(root.clone())
            {
                let state = client.call_tool("get_editor_state", json!({})).await?;
                let state = state.get("state").unwrap_or(&state);
                let label = if state["isCompiling"] == true {
                    "compiling"
                } else if state["isUpdating"] == true {
                    "starting"
                } else {
                    "ready"
                };
                return Ok(
                    json!({"state": label, "projectPath": root, "pid": running(&root), "editor": state}),
                );
            }
        }
    }
    let diagnostic = doctor::run(&doctor::DoctorOptions {
        project_path: Some(root.clone()),
        host: config.host,
        port: config.port,
        port_source: "project",
        probe_timeout: config.timeout,
    })
    .await?;
    let label = if diagnostic["editorLog"]["safeMode"] == true {
        "safe_mode"
    } else if diagnostic["editor"]["running"] != true {
        "stopped"
    } else {
        let editors = editor_discovery::discover();
        if editor_discovery::select_for_path(&editors, &root)
            .iter()
            .any(|e| e.lock.state.as_deref() == Some("compiling"))
        {
            "compiling"
        } else {
            "starting"
        }
    };
    Ok(json!({"state": label, "projectPath": root, "diagnostic": diagnostic}))
}
