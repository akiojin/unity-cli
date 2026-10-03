use serde_json::Value;
use std::collections::BTreeMap;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

fn snapshot(root: &Path) -> BTreeMap<PathBuf, Vec<u8>> {
    let mut files = BTreeMap::new();
    for entry in std::fs::read_dir(root).unwrap() {
        let path = entry.unwrap().path();
        if path.is_dir() {
            files.insert(path.clone(), Vec::new());
            files.extend(snapshot(&path));
        } else {
            files.insert(path.clone(), std::fs::read(path).unwrap());
        }
    }
    files
}

fn drain(mut pipe: impl Read + Send + 'static) -> std::thread::JoinHandle<Vec<u8>> {
    std::thread::spawn(move || {
        let mut bytes = Vec::new();
        pipe.read_to_end(&mut bytes).unwrap();
        bytes
    })
}

#[cfg(windows)]
#[test]
fn setup_launch_returns_output_while_editor_is_still_running() {
    let dir = tempfile::tempdir().unwrap();
    let project = dir.path().join("Project with spaces");
    std::fs::create_dir_all(project.join("Packages")).unwrap();
    std::fs::create_dir_all(project.join("ProjectSettings")).unwrap();
    std::fs::write(
        project.join("Packages/manifest.json"),
        "{\"dependencies\":{}}",
    )
    .unwrap();
    std::fs::write(
        project.join("ProjectSettings/ProjectVersion.txt"),
        "m_EditorVersion: 6000.4.4f1\n",
    )
    .unwrap();
    let source = dir.path().join("fake_editor.rs");
    let editor = dir.path().join("fake_editor.exe");
    let started = dir.path().join("started");
    let stop = dir.path().join("stop");
    std::fs::write(
        &source,
        r#"fn main() {
            std::fs::write(std::env::var_os("FAKE_EDITOR_STARTED").unwrap(), "started").unwrap();
            let stop = std::path::PathBuf::from(std::env::var_os("FAKE_EDITOR_STOP").unwrap());
            let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
            while !stop.exists() && std::time::Instant::now() < deadline {
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
        }"#,
    )
    .unwrap();
    let compiled = Command::new("rustc")
        .arg(&source)
        .arg("-o")
        .arg(&editor)
        .output()
        .unwrap();
    assert!(compiled.status.success(), "{compiled:?}");
    let port = std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port();
    let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
    command
        .args(["--output", "json", "--host", "127.0.0.1", "--port"])
        .arg(port.to_string())
        .args(["--timeout-ms", "100", "--project-path"])
        .arg(&project)
        .args(["setup", "--launch-editor", "--wait-secs", "0"])
        .env("UNITY_EDITOR_PATH", &editor)
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_CLI_TOOLS_ROOT", dir.path().join("tools"))
        .env("UNITY_CLI_REGISTRY_PATH", dir.path().join("instances.json"))
        .env("UNITY_CLI_EDITORS_DIR", dir.path().join("editors"))
        .env("FAKE_EDITOR_STARTED", &started)
        .env("FAKE_EDITOR_STOP", &stop);
    let (send, receive) = std::sync::mpsc::channel();
    let reader = std::thread::spawn(move || send.send(command.output().unwrap()).unwrap());
    let output = receive.recv_timeout(Duration::from_secs(5));
    let deadline = Instant::now() + Duration::from_secs(3);
    while !started.exists() && Instant::now() < deadline {
        std::thread::sleep(Duration::from_millis(10));
    }
    let editor_started = started.exists();
    // Stop only the fixture launched by this test, even on the failing path.
    std::fs::write(&stop, "stop").unwrap();
    let completed = output.is_ok();
    let output = output.unwrap_or_else(|_| receive.recv_timeout(Duration::from_secs(5)).unwrap());
    reader.join().unwrap();
    assert!(
        editor_started,
        "setup did not launch the fixture: {output:?}"
    );
    assert!(
        completed,
        "setup output waited for the Editor to exit: {output:?}"
    );
    assert_eq!(output.status.code(), Some(4), "{output:?}");
    let report: Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(report["data"]["launch"]["launched"], true, "{report}");
}

#[test]
fn setup_dry_run_prints_plan_without_connections_writes_or_launches() {
    for (launch, wait) in [(false, None), (true, None), (false, Some("900"))] {
        for json in [false, true] {
            let dir = tempfile::tempdir().unwrap();
            let project = dir.path().join("Project with spaces");
            std::fs::create_dir_all(project.join("Packages")).unwrap();
            std::fs::create_dir_all(project.join("ProjectSettings")).unwrap();
            std::fs::write(
                project.join("Packages/manifest.json"),
                "{\"dependencies\":{}}",
            )
            .unwrap();
            std::fs::write(
                project.join("ProjectSettings/ProjectVersion.txt"),
                "m_EditorVersion: 6000.3.25f1\n",
            )
            .unwrap();
            std::fs::write(
                project.join("ProjectSettings/ProjectSettings.asset"),
                "PlayerSettings:\n  activeInputHandler: 0\n",
            )
            .unwrap();
            let before = snapshot(dir.path());
            let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
            listener.set_nonblocking(true).unwrap();
            let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
            command
                .args(["setup", "--dry-run", "--project-path"])
                .arg(&project)
                .args([
                    "--host",
                    "127.0.0.1",
                    "--port",
                    &listener.local_addr().unwrap().port().to_string(),
                ])
                // A missing executable would make any attempted launch fail.
                .env("UNITY_EDITOR_PATH", dir.path().join("missing-editor"))
                .env("UNITY_CLI_TOOLS_ROOT", dir.path().join("tools"))
                .env_remove("UNITY_CLI_NO_AUTO_UPDATE")
                .stdout(Stdio::piped())
                .stderr(Stdio::piped());
            if launch {
                command.arg("--launch-editor");
            }
            if json {
                command.arg("--json");
            }
            if let Some(wait) = wait {
                command.args(["--wait-secs", wait]);
            }
            let start = Instant::now();
            let mut child = command.spawn().unwrap();
            // Drain concurrently: a long project path can fill a small OS pipe.
            let stdout = drain(child.stdout.take().unwrap());
            let stderr = drain(child.stderr.take().unwrap());
            let mut timed_out = false;
            loop {
                if child.try_wait().unwrap().is_some() {
                    break;
                }
                if start.elapsed() > Duration::from_secs(3) {
                    child.kill().unwrap();
                    timed_out = true;
                    break;
                }
                std::thread::sleep(Duration::from_millis(10));
            }
            let output = std::process::Output {
                status: child.wait().unwrap(),
                stdout: stdout.join().unwrap(),
                stderr: stderr.join().unwrap(),
            };
            assert!(
                !timed_out,
                "dry-run waited instead of returning a plan: {output:?}"
            );
            assert!(output.status.success(), "{output:?}");
            assert_eq!(
                listener.accept().unwrap_err().kind(),
                std::io::ErrorKind::WouldBlock
            );
            assert_eq!(snapshot(dir.path()), before);
            let text = String::from_utf8(output.stdout).unwrap();
            for step in ["binary", "bridge", "launch", "wait"] {
                assert!(text.contains(step), "{text}");
            }
            if json {
                let report: Value = serde_json::from_str(&text).unwrap();
                assert_eq!(report["success"], true);
                let report = &report["data"];
                assert_eq!(report["dryRun"], true);
                assert_eq!(report["steps"].as_array().unwrap().len(), 4);
                assert_eq!(report["steps"][2]["enabled"], launch);
                if launch {
                    assert_eq!(report["steps"][2]["args"][1], project.to_str().unwrap());
                    assert_eq!(
                        report["steps"][2]["executable"],
                        dir.path().join("missing-editor").to_str().unwrap()
                    );
                }
                assert_eq!(
                    report["steps"][3]["waitSecs"],
                    if launch || wait.is_some() { 900 } else { 0 }
                );
            }
        }
    }
}
