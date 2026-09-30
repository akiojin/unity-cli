//! `unity-cli doctor`: explains why the Unity bridge cannot be reached.
//!
//! A failed connection usually does not mean "no Editor". The Editor may be in
//! Safe Mode because of compile errors (the bridge assembly is not loaded), the
//! bridge package may be missing from `Packages/manifest.json`, another process
//! may hold the configured port, or a sandbox may deny local sockets. Each case
//! maps to one `diagnosis` code so agents can pick the right recovery.

use std::collections::BTreeSet;
use std::io::ErrorKind;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use anyhow::Result;
use regex::Regex;
use serde::Serialize;
use serde_json::{json, Value};

use crate::config::RuntimeConfig;
use crate::transport::UnityClient;

pub const BRIDGE_PACKAGE: &str = "com.akiojin.unity-cli-bridge";
const DEFAULT_PROBE_TIMEOUT: Duration = Duration::from_secs(3);
const SAFE_MODE_ENTER_MARKERS: [&str; 2] = [
    "Safe Mode: Only loading a subset of assemblies",
    "Loading Safe Mode Domain",
];
const SAFE_MODE_EXIT_MARKER: &str = "Exiting safe mode";

/// Hint appended to connection failures so agents run the diagnosis instead of
/// assuming the Editor is gone.
pub const DOCTOR_HINT: &str = "Run `unity-cli doctor --output json` to diagnose (Safe Mode / \
compile errors, bridge not installed, port in use, Editor not running, sandbox)";

pub fn connect_failure_message(host: &str, port: u16) -> String {
    format!("Failed to connect to Unity at {host}:{port}. {DOCTOR_HINT}")
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
pub enum Diagnosis {
    Ok,
    SandboxBlocked,
    PortInUse,
    EditorNotRunning,
    BridgeNotInstalled,
    SafeMode,
    CompileErrors,
    BridgeNotResponding,
}

impl Diagnosis {
    fn recovery(self) -> &'static str {
        match self {
            Diagnosis::Ok => "The bridge answered ping. No action needed.",
            Diagnosis::SandboxBlocked => {
                "The OS denied the local socket (sandbox). Re-run unity-cli outside the sandbox \
                 or allow loopback network access; do not assume the Editor is closed."
            }
            Diagnosis::PortInUse => {
                "Another process holds the bridge port. Stop that process or change the port in \
                 Project Settings > Unity CLI Bridge and pass the same --port."
            }
            Diagnosis::EditorNotRunning => {
                "Start the Unity Editor for this project, wait for import to finish, then retry."
            }
            Diagnosis::BridgeNotInstalled => {
                "Run `unity-cli bridge install` (or `unity-cli setup`) to add \
                 com.akiojin.unity-cli-bridge to Packages/manifest.json, then let Unity resolve \
                 packages."
            }
            Diagnosis::SafeMode => {
                "The Editor is in Safe Mode, so the bridge is not loaded. Fix the compile errors \
                 listed in editorLog.compileErrors (edit the files directly), then exit Safe Mode \
                 or restart the Editor."
            }
            Diagnosis::CompileErrors => {
                "Script compilation failed, so the bridge may not be loaded. Fix the files in \
                 editorLog.compileErrors, then wait for recompilation."
            }
            Diagnosis::BridgeNotResponding => {
                "The Editor is running with the bridge installed but nothing answered on the \
                 port. Wait for import/compilation to finish, and check the port in Project \
                 Settings > Unity CLI Bridge matches --port."
            }
        }
    }
}

#[derive(Debug, Clone)]
pub struct DoctorOptions {
    pub project_path: Option<PathBuf>,
    pub host: String,
    pub port: u16,
    pub port_source: &'static str,
    pub probe_timeout: Duration,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CompileError {
    pub file: String,
    pub line: u32,
    pub column: u32,
    pub code: String,
    pub message: String,
}

#[derive(Debug, Default, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorLogFindings {
    pub safe_mode: bool,
    pub compile_errors: Vec<CompileError>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorProcess {
    pub pid: u32,
    pub project_path: Option<String>,
    pub log_file: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgeInstall {
    pub installed: bool,
    pub source: Option<String>,
    pub version: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PortProbe {
    pub listening: bool,
    pub bridge_responded: bool,
    pub permission_denied: bool,
    pub error: Option<String>,
    pub listener_pid: Option<u32>,
    pub listener_process: Option<String>,
}

/// Inputs to the pure classification step.
#[derive(Debug, Default)]
pub struct Facts {
    pub port: PortProbe,
    pub editors: Vec<EditorProcess>,
    pub target_editor_pid: Option<u32>,
    pub bridge: Option<BridgeInstall>,
    pub log: Option<EditorLogFindings>,
}

pub fn classify(facts: &Facts) -> Diagnosis {
    if facts.port.bridge_responded {
        return Diagnosis::Ok;
    }
    if facts.port.permission_denied {
        return Diagnosis::SandboxBlocked;
    }
    let listener_is_editor = facts
        .port
        .listener_pid
        .is_some_and(|pid| facts.editors.iter().any(|editor| editor.pid == pid));
    if facts.port.listening && !listener_is_editor {
        return Diagnosis::PortInUse;
    }
    if facts.target_editor_pid.is_none() {
        return Diagnosis::EditorNotRunning;
    }
    if facts
        .bridge
        .as_ref()
        .is_some_and(|bridge| !bridge.installed)
    {
        return Diagnosis::BridgeNotInstalled;
    }
    if let Some(log) = &facts.log {
        if log.safe_mode {
            return Diagnosis::SafeMode;
        }
        if !log.compile_errors.is_empty() {
            return Diagnosis::CompileErrors;
        }
    }
    Diagnosis::BridgeNotResponding
}

pub async fn run(options: &DoctorOptions) -> Result<Value> {
    let project = options.project_path.as_deref();
    let bridge = project.map(read_bridge_install);
    let editors = list_editor_processes();
    let target_editor = match project {
        Some(path) => editors
            .iter()
            .find(|editor| editor_matches_project(editor, path)),
        None => editors.first(),
    };
    let log_path = target_editor
        .and_then(|editor| editor.log_file.as_deref())
        .filter(|path| *path != "-")
        .map(PathBuf::from)
        .or_else(default_editor_log_path);
    let log = target_editor
        .and(log_path.as_deref())
        .and_then(|path| std::fs::read_to_string(path).ok())
        .map(|content| parse_editor_log(&content));
    let port = probe_port(&options.host, options.port, options.probe_timeout).await;

    let facts = Facts {
        target_editor_pid: target_editor.map(|editor| editor.pid),
        port,
        editors: editors.clone(),
        bridge,
        log,
    };
    let diagnosis = classify(&facts);

    Ok(json!({
        "diagnosis": diagnosis,
        "healthy": diagnosis == Diagnosis::Ok,
        "recovery": diagnosis.recovery(),
        "project": project.map(|path| json!({
            "path": path.display().to_string(),
            "unityVersion": read_project_unity_version(path),
        })),
        "endpoint": {
            "host": options.host,
            "port": options.port,
            "portSource": options.port_source,
        },
        "port": facts.port,
        "bridge": facts.bridge,
        "editor": {
            "running": facts.target_editor_pid.is_some(),
            "pid": facts.target_editor_pid,
            "processes": facts.editors,
        },
        "editorLog": {
            "path": log_path.map(|path| path.display().to_string()),
            "safeMode": facts.log.as_ref().is_some_and(|log| log.safe_mode),
            "compileErrors": facts.log.map(|log| log.compile_errors).unwrap_or_default(),
        },
    }))
}

/// Resolves the project from `--project-path`, `UNITY_PROJECT_ROOT`, or the
/// nearest ancestor of the current directory that looks like a Unity project.
pub fn resolve_project_path(explicit: Option<&Path>) -> Option<PathBuf> {
    if let Some(path) = explicit {
        return Some(absolutize(path));
    }
    if let Some(root) = crate::config::read_env(&["UNITY_PROJECT_ROOT"]) {
        return Some(absolutize(Path::new(&root)));
    }
    let cwd = std::env::current_dir().ok()?;
    cwd.ancestors()
        .find(|dir| is_unity_project(dir))
        .map(Path::to_path_buf)
}

fn absolutize(path: &Path) -> PathBuf {
    std::fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf())
}

fn is_unity_project(dir: &Path) -> bool {
    dir.join("ProjectSettings/ProjectVersion.txt").is_file() && dir.join("Assets").is_dir()
}

/// Port configured in `ProjectSettings/UnityCliBridgeSettings.asset`, if any.
pub fn read_project_bridge_port(project: &Path) -> Option<u16> {
    let content =
        std::fs::read_to_string(project.join("ProjectSettings/UnityCliBridgeSettings.asset"))
            .ok()?;
    content.lines().find_map(|line| {
        line.trim()
            .strip_prefix("port:")
            .and_then(|value| value.trim().parse::<u16>().ok())
            .filter(|port| *port > 0)
    })
}

fn read_project_unity_version(project: &Path) -> Option<String> {
    let content =
        std::fs::read_to_string(project.join("ProjectSettings/ProjectVersion.txt")).ok()?;
    content.lines().find_map(|line| {
        line.strip_prefix("m_EditorVersion:")
            .map(|value| value.trim().to_string())
    })
}

pub fn read_bridge_install(project: &Path) -> BridgeInstall {
    let packages = project.join("Packages");
    let source = std::fs::read_to_string(packages.join("manifest.json"))
        .ok()
        .and_then(|content| serde_json::from_str::<Value>(&content).ok())
        .and_then(|manifest| {
            manifest
                .get("dependencies")?
                .get(BRIDGE_PACKAGE)?
                .as_str()
                .map(str::to_string)
        });
    let version = source.as_deref().and_then(|source| {
        if let Some(local) = source.strip_prefix("file:") {
            let dir = packages.join(local);
            let package: Value =
                serde_json::from_str(&std::fs::read_to_string(dir.join("package.json")).ok()?)
                    .ok()?;
            package.get("version")?.as_str().map(str::to_string)
        } else if let Some((_, fragment)) = source.rsplit_once('#') {
            Some(fragment.trim_start_matches('v').to_string())
        } else if source.contains("://") || source.starts_with("git") {
            None
        } else {
            Some(source.to_string())
        }
    });
    BridgeInstall {
        installed: source.is_some(),
        source,
        version,
    }
}

/// Extracts Safe Mode state and the compile errors of the latest compilation.
pub fn parse_editor_log(content: &str) -> EditorLogFindings {
    let error_pattern = Regex::new(r"^(.+?\.cs)\((\d+),(\d+)\): error (CS\d+): (.*)$")
        .expect("compile error regex is valid");
    let mut safe_mode = false;
    let mut seen = BTreeSet::new();
    let mut errors = Vec::new();
    for raw in content.lines() {
        let line = raw.trim_end();
        if SAFE_MODE_ENTER_MARKERS
            .iter()
            .any(|marker| line.contains(marker))
        {
            safe_mode = true;
        } else if line.contains(SAFE_MODE_EXIT_MARKER) {
            safe_mode = false;
        } else if line.starts_with("*** Tundra build success") {
            seen.clear();
            errors.clear();
        } else if let Some(captures) = error_pattern.captures(line) {
            let error = CompileError {
                file: captures[1].to_string(),
                line: captures[2].parse().unwrap_or_default(),
                column: captures[3].parse().unwrap_or_default(),
                code: captures[4].to_string(),
                message: captures[5].to_string(),
            };
            let key = (
                error.file.clone(),
                error.line,
                error.column,
                error.code.clone(),
            );
            if seen.insert(key) {
                errors.push(error);
            }
        }
    }
    EditorLogFindings {
        safe_mode,
        compile_errors: errors,
    }
}

fn default_editor_log_path() -> Option<PathBuf> {
    if cfg!(target_os = "macos") {
        dirs::home_dir().map(|home| home.join("Library/Logs/Unity/Editor.log"))
    } else if cfg!(windows) {
        dirs::data_local_dir().map(|dir| dir.join("Unity/Editor/Editor.log"))
    } else {
        dirs::config_dir().map(|dir| dir.join("unity3d/Editor.log"))
    }
}

fn editor_matches_project(editor: &EditorProcess, project: &Path) -> bool {
    let Some(path) = editor.project_path.as_deref() else {
        return false;
    };
    let normalize = |path: &Path| {
        let resolved = absolutize(path);
        let text = resolved
            .to_string_lossy()
            .trim_end_matches(['/', '\\'])
            .to_string();
        if cfg!(any(windows, target_os = "macos")) {
            text.to_lowercase()
        } else {
            text
        }
    };
    normalize(Path::new(path)) == normalize(project)
}

fn list_editor_processes() -> Vec<EditorProcess> {
    process_command_lines()
        .into_iter()
        .filter_map(|(pid, command_line)| parse_editor_command_line(pid, &command_line))
        .collect()
}

/// Recognizes a Unity Editor command line (not Unity Hub or helper processes).
pub fn parse_editor_command_line(pid: u32, command_line: &str) -> Option<EditorProcess> {
    let executable = Regex::new(
        r#"(?i)(/Unity\.app/Contents/MacOS/Unity|[\\/]Editor[\\/]Unity(\.exe)?|^Unity\.exe|^Unity)(\s|"|$)"#,
    )
    .expect("editor regex is valid");
    let head = command_line.trim_start_matches('"');
    let found = executable.find(head)?;
    // Hub helpers live under paths such as ".../Unity Hub.app/...": reject when
    // the match is followed by more of the executable name.
    if head[found.end()..].starts_with("Hub") {
        return None;
    }
    Some(EditorProcess {
        pid,
        project_path: argument_value(command_line, "-projectPath"),
        log_file: argument_value(command_line, "-logFile"),
    })
}

/// Reads the value after `flag` (case-insensitive) from a flattened command
/// line. Values may contain spaces, so the value ends at the next ` -option`.
fn argument_value(command_line: &str, flag: &str) -> Option<String> {
    let pattern = format!(
        r#"(?i)(?:^|\s){}\s+(?:"([^"]*)"|(.+?))(?:\s+-[A-Za-z]|$)"#,
        regex::escape(flag)
    );
    let captures = Regex::new(&pattern).ok()?.captures(command_line)?;
    captures
        .get(1)
        .or_else(|| captures.get(2))
        .map(|value| value.as_str().trim().to_string())
        .filter(|value| !value.is_empty())
}

fn process_command_lines() -> Vec<(u32, String)> {
    let output = if cfg!(windows) {
        Command::new("powershell")
            .args([
                "-NoProfile",
                "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | \
                 ForEach-Object { \"$($_.ProcessId) $($_.CommandLine)\" }",
            ])
            .output()
    } else {
        Command::new("ps")
            .args(["-axww", "-o", "pid=,command="])
            .output()
    };
    let Ok(output) = output else {
        return Vec::new();
    };
    String::from_utf8_lossy(&output.stdout)
        .lines()
        .filter_map(|line| {
            let (pid, rest) = line.trim().split_once(char::is_whitespace)?;
            Some((pid.parse().ok()?, rest.trim().to_string()))
        })
        .collect()
}

async fn probe_port(host: &str, port: u16, probe_timeout: Duration) -> PortProbe {
    let mut probe = PortProbe::default();
    let connect =
        tokio::time::timeout(probe_timeout, tokio::net::TcpStream::connect((host, port))).await;
    match connect {
        Ok(Ok(stream)) => {
            drop(stream);
            probe.listening = true;
        }
        Ok(Err(error)) => {
            probe.permission_denied = error.kind() == ErrorKind::PermissionDenied;
            probe.error = Some(error.to_string());
        }
        Err(_) => probe.error = Some(format!("connect timed out after {probe_timeout:?}")),
    }
    if probe.listening {
        let config = RuntimeConfig {
            host: host.to_string(),
            port,
            timeout: probe_timeout,
        };
        match ping(&config).await {
            Ok(()) => probe.bridge_responded = true,
            Err(error) => probe.error = Some(format!("{error:#}")),
        }
    }
    if !probe.bridge_responded {
        probe.listener_pid = listener_pid(port);
        probe.listener_process = probe.listener_pid.and_then(process_name);
    }
    probe
}

async fn ping(config: &RuntimeConfig) -> Result<()> {
    let mut client = UnityClient::connect(config).await?;
    tokio::time::timeout(config.timeout, client.call_tool("ping", json!({})))
        .await
        .map_err(|_| anyhow::anyhow!("ping timed out after {:?}", config.timeout))??;
    Ok(())
}

pub fn default_probe_timeout() -> Duration {
    DEFAULT_PROBE_TIMEOUT
}

fn listener_pid(port: u16) -> Option<u32> {
    if cfg!(windows) {
        let output = Command::new("netstat")
            .args(["-ano", "-p", "TCP"])
            .output()
            .ok()?;
        let suffix = format!(":{port}");
        return String::from_utf8_lossy(&output.stdout)
            .lines()
            .find_map(|line| {
                let columns: Vec<&str> = line.split_whitespace().collect();
                (columns.len() >= 5
                    && columns[1].ends_with(&suffix)
                    && columns[3].eq_ignore_ascii_case("LISTENING"))
                .then(|| columns[4].parse().ok())
                .flatten()
            });
    }
    let lsof = Command::new("lsof")
        .args(["-nP", &format!("-iTCP:{port}"), "-sTCP:LISTEN", "-Fp"])
        .output()
        .ok()
        .and_then(|output| {
            String::from_utf8_lossy(&output.stdout)
                .lines()
                .find_map(|line| line.strip_prefix('p')?.parse().ok())
        });
    lsof.or_else(|| {
        let output = Command::new("ss")
            .args(["-ltnpH", &format!("sport = :{port}")])
            .output()
            .ok()?;
        let text = String::from_utf8_lossy(&output.stdout).to_string();
        let pid = Regex::new(r"pid=(\d+)").ok()?;
        pid.captures(&text)?[1].parse().ok()
    })
}

fn process_name(pid: u32) -> Option<String> {
    let output = if cfg!(windows) {
        Command::new("tasklist")
            .args(["/FI", &format!("PID eq {pid}"), "/FO", "CSV", "/NH"])
            .output()
            .ok()?
    } else {
        Command::new("ps")
            .args(["-p", &pid.to_string(), "-o", "comm="])
            .output()
            .ok()?
    };
    let text = String::from_utf8_lossy(&output.stdout);
    let first = text.lines().next()?.trim();
    let name = if cfg!(windows) {
        first.split(',').next()?.trim_matches('"')
    } else {
        first
    };
    (!name.is_empty()).then(|| name.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn options(project: &Path, port: u16) -> DoctorOptions {
        DoctorOptions {
            project_path: Some(project.to_path_buf()),
            host: "127.0.0.1".to_string(),
            port,
            port_source: "test",
            probe_timeout: Duration::from_millis(500),
        }
    }

    fn free_port() -> u16 {
        std::net::TcpListener::bind("127.0.0.1:0")
            .expect("bind ephemeral port")
            .local_addr()
            .expect("local addr")
            .port()
    }

    fn write_project(root: &Path, manifest: &str) {
        std::fs::create_dir_all(root.join("Assets")).unwrap();
        std::fs::create_dir_all(root.join("Packages")).unwrap();
        std::fs::create_dir_all(root.join("ProjectSettings")).unwrap();
        std::fs::write(
            root.join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.3.25f1\n",
        )
        .unwrap();
        std::fs::write(root.join("Packages/manifest.json"), manifest).unwrap();
    }

    // AC-3: a non-Unity process holding the configured port.
    #[tokio::test(flavor = "current_thread")]
    async fn reports_port_in_use_with_owner_pid() {
        let project = tempfile::tempdir().unwrap();
        write_project(project.path(), r#"{"dependencies":{}}"#);
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();

        let report = run(&options(project.path(), port)).await.unwrap();

        assert_eq!(report["diagnosis"], "PORT_IN_USE");
        assert_eq!(report["port"]["listening"], true);
        assert_eq!(report["port"]["bridgeResponded"], false);
        assert_eq!(report["port"]["listenerPid"], json!(std::process::id()));
        drop(listener);
    }

    // AC-4: nothing listening and no Editor for the project.
    #[tokio::test(flavor = "current_thread")]
    async fn reports_editor_not_running() {
        let project = tempfile::tempdir().unwrap();
        write_project(
            project.path(),
            r#"{"dependencies":{"com.akiojin.unity-cli-bridge":"0.15.3"}}"#,
        );

        let report = run(&options(project.path(), free_port())).await.unwrap();

        assert_eq!(report["diagnosis"], "EDITOR_NOT_RUNNING");
        assert_eq!(report["healthy"], false);
        assert_eq!(report["editor"]["running"], false);
        assert_eq!(report["bridge"]["installed"], true);
        assert_eq!(report["bridge"]["version"], "0.15.3");
        assert_eq!(report["project"]["unityVersion"], "6000.3.25f1");
    }

    #[test]
    fn parses_safe_mode_and_latest_compile_errors() {
        let log = "\
Assets/Old.cs(1,1): error CS0001: stale\n\
*** Tundra build success (1.0 seconds)\n\
##### Output\n\
Assets/Broken.cs(3,24): error CS0029: Cannot implicitly convert type 'string' to 'int'\n\
*** Tundra build failed (15.53 seconds), 841 items updated, 844 evaluated\n\
Assets/Broken.cs(3,24): error CS0029: Cannot implicitly convert type 'string' to 'int'\n\
Packages/com.example/Editor/X.cs(10,5): error CS0103: The name 'y' does not exist in the current context\n\
/Users/me/pkg/Editor/W.cs(32,20): warning CS0618: obsolete\n\
Safe Mode: Only loading a subset of assemblies\n";
        let findings = parse_editor_log(log);
        assert!(findings.safe_mode);
        assert_eq!(
            findings.compile_errors,
            vec![
                CompileError {
                    file: "Assets/Broken.cs".into(),
                    line: 3,
                    column: 24,
                    code: "CS0029".into(),
                    message: "Cannot implicitly convert type 'string' to 'int'".into(),
                },
                CompileError {
                    file: "Packages/com.example/Editor/X.cs".into(),
                    line: 10,
                    column: 5,
                    code: "CS0103".into(),
                    message: "The name 'y' does not exist in the current context".into(),
                },
            ]
        );
    }

    #[test]
    fn safe_mode_exit_clears_flag() {
        let findings =
            parse_editor_log("Safe Mode: Only loading a subset of assemblies\nExiting safe mode\n");
        assert!(!findings.safe_mode);
    }

    #[test]
    fn recognizes_editor_command_lines_only() {
        let mac = parse_editor_command_line(
            7,
            "/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -projectPath /tmp/My Game -logFile /tmp/e.log",
        )
        .expect("macOS editor");
        assert_eq!(mac.project_path.as_deref(), Some("/tmp/My Game"));
        assert_eq!(mac.log_file.as_deref(), Some("/tmp/e.log"));

        let win = parse_editor_command_line(
            8,
            r#""C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -projectpath "C:\Work\Game""#,
        )
        .expect("windows editor");
        assert_eq!(win.project_path.as_deref(), Some(r"C:\Work\Game"));

        assert!(parse_editor_command_line(
            9,
            "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
        )
        .is_none());
        assert!(parse_editor_command_line(
            10,
            "/Applications/Unity Hub.app/Contents/Frameworks/Unity Hub Helper.app/Contents/MacOS/Unity Hub Helper --type=gpu-process"
        )
        .is_none());
    }

    #[test]
    fn reads_bridge_version_from_local_file_dependency() {
        let project = tempfile::tempdir().unwrap();
        let package = project.path().join("LocalBridge");
        std::fs::create_dir_all(&package).unwrap();
        std::fs::write(package.join("package.json"), r#"{"version":"1.2.3"}"#).unwrap();
        write_project(
            project.path(),
            r#"{"dependencies":{"com.akiojin.unity-cli-bridge":"file:../LocalBridge"}}"#,
        );
        let bridge = read_bridge_install(project.path());
        assert!(bridge.installed);
        assert_eq!(bridge.version.as_deref(), Some("1.2.3"));

        write_project(project.path(), r#"{"dependencies":{}}"#);
        assert!(!read_bridge_install(project.path()).installed);
    }

    #[test]
    fn classification_priority() {
        let editor = EditorProcess {
            pid: 42,
            project_path: None,
            log_file: None,
        };
        let running = |bridge: bool, log: EditorLogFindings| Facts {
            port: PortProbe::default(),
            editors: vec![editor.clone()],
            target_editor_pid: Some(42),
            bridge: Some(BridgeInstall {
                installed: bridge,
                source: None,
                version: None,
            }),
            log: Some(log),
        };
        let safe = EditorLogFindings {
            safe_mode: true,
            compile_errors: vec![],
        };
        assert_eq!(classify(&running(true, safe.clone())), Diagnosis::SafeMode);
        assert_eq!(
            classify(&running(false, safe)),
            Diagnosis::BridgeNotInstalled
        );
        assert_eq!(
            classify(&running(true, EditorLogFindings::default())),
            Diagnosis::BridgeNotResponding
        );

        let mut sandboxed = running(true, EditorLogFindings::default());
        sandboxed.port.permission_denied = true;
        assert_eq!(classify(&sandboxed), Diagnosis::SandboxBlocked);

        let mut editor_on_port = running(true, EditorLogFindings::default());
        editor_on_port.port.listening = true;
        editor_on_port.port.listener_pid = Some(42);
        assert_eq!(classify(&editor_on_port), Diagnosis::BridgeNotResponding);
    }

    #[test]
    fn reads_project_bridge_port() {
        let project = tempfile::tempdir().unwrap();
        write_project(project.path(), "{}");
        assert_eq!(read_project_bridge_port(project.path()), None);
        std::fs::write(
            project
                .path()
                .join("ProjectSettings/UnityCliBridgeSettings.asset"),
            "MonoBehaviour:\n  unityHost: localhost\n  port: 6491\n",
        )
        .unwrap();
        assert_eq!(read_project_bridge_port(project.path()), Some(6491));
    }
}
