//! `unity-cli setup`: verify the binary, install the bridge package, then
//! confirm the Editor answers `ping` for this project with a matching bridge.

use anyhow::{bail, Context, Result};
use serde_json::{json, Value};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use crate::config::RuntimeConfig;
use crate::core::bridge::{self, BridgeAction};
use crate::transport::UnityClient;

const LAUNCH_WAIT_SECS: u64 = 900;
const POLL_INTERVAL: Duration = Duration::from_secs(2);

pub struct SetupOptions {
    pub project_path: Option<PathBuf>,
    pub launch_editor: bool,
    pub wait_secs: Option<u64>,
    pub dry_run: bool,
}

pub async fn run(options: &SetupOptions, config: &RuntimeConfig, cwd: &Path) -> Result<Value> {
    let binary = binary_report();
    let root = bridge::resolve_project_root(options.project_path.as_deref(), cwd)?;
    let bridge_report = bridge::apply(&root, BridgeAction::Install, options.dry_run)?;

    let mut launch = Value::Null;
    let mut ping = ping_once(config).await;
    let wait_secs = options.wait_secs.unwrap_or(if options.launch_editor {
        LAUNCH_WAIT_SECS
    } else {
        0
    });
    if ping.is_err() && options.launch_editor {
        launch = if options.dry_run {
            json!({ "launched": false, "reason": "dry_run" })
        } else {
            launch_editor(&root, config.port)?
        };
    }
    if ping.is_err() && wait_secs > 0 {
        let deadline = Instant::now() + Duration::from_secs(wait_secs);
        while ping.is_err() && Instant::now() < deadline {
            tokio::time::sleep(POLL_INTERVAL).await;
            ping = ping_once(config).await;
        }
    }

    let mut warnings = Vec::new();
    let editor = match &ping {
        Ok(pong) => {
            let version_check =
                bridge::version_check(pong.get("bridgeVersion").and_then(Value::as_str));
            if version_check["status"] != "match" {
                warnings.push(version_check["message"].clone());
            }
            let project_matches = pong
                .get("projectPath")
                .and_then(Value::as_str)
                .map(|path| same_path(Path::new(path), &root));
            json!({
                "reachable": true,
                "endpoint": format!("{}:{}", config.host, config.port),
                "bridgeVersion": pong.get("bridgeVersion"),
                "projectPath": pong.get("projectPath"),
                "projectMatches": project_matches,
                "versionCheck": version_check,
            })
        }
        Err(error) => json!({
            "reachable": false,
            "endpoint": format!("{}:{}", config.host, config.port),
            "error": format!("{error:#}"),
            "hint": "Open this project in the Unity Editor (or rerun with --launch-editor) and wait for the bridge to compile; first import can take several minutes. If the Editor opened in Safe Mode, fix the compile errors in its Console",
        }),
    };
    let project_ok = editor["projectMatches"] != json!(false);
    if !project_ok {
        warnings.push(json!(format!(
            "The Editor at {}:{} serves a different project; pass --port for this project's Editor",
            config.host, config.port
        )));
    }

    Ok(json!({
        "ok": ping.is_ok() && project_ok,
        "binary": binary,
        "bridge": bridge_report,
        "launch": launch,
        "editor": editor,
        "warnings": warnings,
    }))
}

async fn ping_once(config: &RuntimeConfig) -> Result<Value> {
    let mut client = UnityClient::connect(config).await?;
    client.call_tool("ping", json!({})).await
}

fn binary_report() -> Value {
    let exe = std::env::current_exe().ok();
    let on_path = find_on_path("unity-cli");
    json!({
        "version": bridge::cli_version(),
        "path": exe.as_ref().map(|p| p.display().to_string()),
        "onPath": on_path.as_ref().map(|p| p.display().to_string()),
        "ok": true,
    })
}

fn find_on_path(name: &str) -> Option<PathBuf> {
    let file = if cfg!(windows) {
        format!("{name}.exe")
    } else {
        name.to_string()
    };
    std::env::split_paths(&std::env::var_os("PATH")?)
        .map(|dir| dir.join(&file))
        .find(|candidate| candidate.is_file())
}

fn same_path(a: &Path, b: &Path) -> bool {
    match (a.canonicalize(), b.canonicalize()) {
        (Ok(a), Ok(b)) => a == b,
        _ => a == b,
    }
}

/// Unity version recorded by the project (`ProjectSettings/ProjectVersion.txt`).
pub fn project_unity_version(root: &Path) -> Result<String> {
    let path = root.join("ProjectSettings").join("ProjectVersion.txt");
    let text = std::fs::read_to_string(&path)
        .with_context(|| format!("Failed to read {}", path.display()))?;
    text.lines()
        .find_map(|line| line.strip_prefix("m_EditorVersion:"))
        .map(|v| v.trim().to_string())
        .filter(|v| !v.is_empty())
        .with_context(|| format!("m_EditorVersion not found in {}", path.display()))
}

/// Default Unity Hub install location of an Editor version.
pub fn hub_editor_path(version: &str) -> PathBuf {
    if cfg!(target_os = "macos") {
        PathBuf::from(format!(
            "/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        ))
    } else if cfg!(windows) {
        PathBuf::from(format!(
            r"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe"
        ))
    } else {
        dirs::home_dir()
            .unwrap_or_default()
            .join(format!("Unity/Hub/Editor/{version}/Editor/Unity"))
    }
}

fn launch_editor(root: &Path, port: u16) -> Result<Value> {
    if bridge::editor_is_open(root) {
        return Ok(json!({
            "launched": false,
            "reason": "Editor appears to be open already (Temp/UnityLockfile); waiting for the bridge",
        }));
    }
    let version = project_unity_version(root)?;
    let editor = std::env::var_os("UNITY_EDITOR_PATH")
        .map(PathBuf::from)
        .unwrap_or_else(|| hub_editor_path(&version));
    if !editor.is_file() {
        bail!(
            "Unity {version} Editor not found at {}. Install it with Unity Hub or set UNITY_EDITOR_PATH",
            editor.display()
        );
    }
    let mut command = std::process::Command::new(&editor);
    command
        .arg("-projectPath")
        .arg(root)
        .env("UNITY_CLI_PORT", port.to_string())
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null());
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        // Detach from the caller's process group so the Editor outlives it.
        command.process_group(0);
    }
    let child = command
        .spawn()
        .with_context(|| format!("Failed to launch {}", editor.display()))?;
    Ok(json!({
        "launched": true,
        "editorPath": editor.display().to_string(),
        "unityVersion": version,
        "pid": child.id(),
        "port": port,
    }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};

    fn project() -> TempDir {
        let dir = TempDir::new().unwrap();
        std::fs::create_dir_all(dir.path().join("Packages")).unwrap();
        std::fs::create_dir_all(dir.path().join("ProjectSettings")).unwrap();
        std::fs::write(
            dir.path().join("Packages/manifest.json"),
            "{\n  \"dependencies\": {}\n}\n",
        )
        .unwrap();
        std::fs::write(
            dir.path().join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.3.25f1\nm_EditorVersionWithRevision: 6000.3.25f1 (abc)\n",
        )
        .unwrap();
        dir
    }

    async fn mock_editor(result: Value) -> u16 {
        let listener = tokio::net::TcpListener::bind(("127.0.0.1", 0))
            .await
            .unwrap();
        let port = listener.local_addr().unwrap().port();
        tokio::spawn(async move {
            while let Ok((mut socket, _)) = listener.accept().await {
                let mut len = [0_u8; 4];
                if socket.read_exact(&mut len).await.is_err() {
                    continue;
                }
                let mut payload = vec![0_u8; i32::from_be_bytes(len) as usize];
                socket.read_exact(&mut payload).await.unwrap();
                let body =
                    serde_json::to_vec(&json!({"status": "success", "result": result})).unwrap();
                socket
                    .write_all(&(body.len() as i32).to_be_bytes())
                    .await
                    .unwrap();
                socket.write_all(&body).await.unwrap();
            }
        });
        port
    }

    fn config(port: u16) -> RuntimeConfig {
        RuntimeConfig {
            host: "127.0.0.1".to_string(),
            port,
            timeout: Duration::from_millis(500),
        }
    }

    fn options() -> SetupOptions {
        SetupOptions {
            project_path: None,
            launch_editor: false,
            wait_secs: None,
            dry_run: false,
        }
    }

    #[tokio::test]
    async fn setup_installs_bridge_and_confirms_matching_editor() {
        let dir = project();
        let port = mock_editor(json!({
            "message": "pong",
            "bridgeVersion": bridge::cli_version(),
            "projectPath": dir.path().display().to_string(),
        }))
        .await;

        let report = run(&options(), &config(port), dir.path()).await.unwrap();

        assert_eq!(report["ok"], true);
        assert_eq!(report["binary"]["version"], bridge::cli_version());
        assert_eq!(report["bridge"]["manifestChanged"], true);
        assert_eq!(report["editor"]["projectMatches"], true);
        assert_eq!(report["editor"]["versionCheck"]["status"], "match");
        assert_eq!(report["warnings"], json!([]));
    }

    #[tokio::test]
    async fn setup_reports_version_mismatch_as_json() {
        let dir = project();
        let port = mock_editor(json!({"message": "pong", "bridgeVersion": "0.0.1"})).await;

        let report = run(&options(), &config(port), dir.path()).await.unwrap();

        assert_eq!(report["ok"], true);
        assert_eq!(report["editor"]["versionCheck"]["status"], "mismatch");
        assert_eq!(report["editor"]["versionCheck"]["bridgeVersion"], "0.0.1");
        assert_eq!(report["warnings"].as_array().unwrap().len(), 1);
    }

    #[tokio::test]
    async fn setup_flags_editor_serving_another_project() {
        let dir = project();
        let other = TempDir::new().unwrap();
        let port = mock_editor(json!({
            "bridgeVersion": bridge::cli_version(),
            "projectPath": other.path().display().to_string(),
        }))
        .await;

        let report = run(&options(), &config(port), dir.path()).await.unwrap();

        assert_eq!(report["ok"], false);
        assert_eq!(report["editor"]["projectMatches"], false);
    }

    #[tokio::test]
    async fn setup_reports_unreachable_editor_and_dry_run_skips_writes() {
        let dir = project();
        let listener = std::net::TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = listener.local_addr().unwrap().port();
        drop(listener);
        let mut opts = options();
        opts.dry_run = true;
        opts.launch_editor = true;
        opts.wait_secs = Some(0);

        let report = run(&opts, &config(port), dir.path()).await.unwrap();

        assert_eq!(report["ok"], false);
        assert_eq!(report["editor"]["reachable"], false);
        assert_eq!(report["launch"]["reason"], "dry_run");
        assert_eq!(report["bridge"]["manifestChanged"], true);
        assert_eq!(
            std::fs::read_to_string(dir.path().join("Packages/manifest.json")).unwrap(),
            "{\n  \"dependencies\": {}\n}\n"
        );
    }

    #[test]
    fn launch_editor_skips_when_project_is_locked_and_errors_without_editor() {
        let dir = project();
        std::fs::create_dir_all(dir.path().join("Temp")).unwrap();
        std::fs::write(dir.path().join("Temp/UnityLockfile"), "").unwrap();
        let locked = launch_editor(dir.path(), 6400).unwrap();
        assert_eq!(locked["launched"], false);

        std::fs::remove_file(dir.path().join("Temp/UnityLockfile")).unwrap();
        let _guard = crate::test_env::env_lock().lock().unwrap();
        std::env::set_var("UNITY_EDITOR_PATH", dir.path().join("missing-unity"));
        let error = launch_editor(dir.path(), 6400).unwrap_err();
        std::env::remove_var("UNITY_EDITOR_PATH");
        assert!(format!("{error:#}").contains("6000.3.25f1"));
    }

    #[test]
    fn project_unity_version_and_hub_path() {
        let dir = project();
        assert_eq!(project_unity_version(dir.path()).unwrap(), "6000.3.25f1");
        assert!(hub_editor_path("6000.3.25f1")
            .display()
            .to_string()
            .contains("6000.3.25f1"));
        let empty = TempDir::new().unwrap();
        assert!(project_unity_version(empty.path()).is_err());
    }
}
