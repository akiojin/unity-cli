use std::io::{Read, Write};
use std::process::Command;

#[test]
fn editor_lifecycle_commands_are_available() {
    for name in ["open", "close", "status"] {
        let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .args(["editor", name, "--help"])
            .output()
            .unwrap();
        assert!(output.status.success(), "{name}: {output:?}");
    }
}

#[test]
fn missing_editor_explains_official_install_and_returns_precondition() {
    let dir = tempfile::tempdir().unwrap();
    std::fs::create_dir(dir.path().join("ProjectSettings")).unwrap();
    std::fs::write(
        dir.path().join("ProjectSettings/ProjectVersion.txt"),
        "m_EditorVersion: 9999.1.1f1\n",
    )
    .unwrap();
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args(["editor", "open", "--project-path"])
        .arg(dir.path())
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_EDITOR_PATH", dir.path().join("missing"))
        .output()
        .unwrap();
    assert_eq!(output.status.code(), Some(4), "{output:?}");
    let error = String::from_utf8_lossy(&output.stderr);
    assert!(error.contains("9999.1.1f1"), "{error}");
    assert!(error.contains("unity install 9999.1.1f1"), "{error}");
}

#[test]
fn close_requires_bridge_scene_protection_before_sending_quit() {
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket
            .set_read_timeout(Some(std::time::Duration::from_secs(5)))
            .unwrap();
        let mut header = [0; 4];
        socket.read_exact(&mut header).unwrap();
        let mut bytes = vec![0; u32::from_be_bytes(header) as usize];
        socket.read_exact(&mut bytes).unwrap();
        let request: serde_json::Value = serde_json::from_slice(&bytes).unwrap();
        assert_eq!(request["type"], "ping");
        let reply = serde_json::to_vec(&serde_json::json!({"id": request["id"],
            "status": "success", "result": {"projectPath": "/nonexistent", "message": "pong"}}))
        .unwrap();
        socket
            .write_all(&(reply.len() as u32).to_be_bytes())
            .unwrap();
        socket.write_all(&reply).unwrap();
        // A Bridge without the capability must never receive quit_editor.
        let result = socket.read(&mut header);
        assert!(matches!(result, Ok(0)), "unexpected follow-up: {result:?}");
    });
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args([
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "editor",
            "close",
        ])
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .output()
        .unwrap();
    server.join().unwrap();
    assert!(!output.status.success());
    assert!(
        String::from_utf8_lossy(&output.stderr).contains("scene protection"),
        "{output:?}"
    );
}

#[test]
fn status_reports_compilation_from_the_selected_project() {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().canonicalize().unwrap();
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server_root = root.clone();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket
            .set_read_timeout(Some(std::time::Duration::from_secs(5)))
            .unwrap();
        for (tool, result) in [
            ("ping", serde_json::json!({"projectPath": server_root})),
            (
                "get_editor_state",
                serde_json::json!({"state": {"isCompiling": true}}),
            ),
        ] {
            let mut header = [0; 4];
            socket.read_exact(&mut header).unwrap();
            let mut bytes = vec![0; u32::from_be_bytes(header) as usize];
            socket.read_exact(&mut bytes).unwrap();
            let request: serde_json::Value = serde_json::from_slice(&bytes).unwrap();
            assert_eq!(request["type"], tool);
            let reply = serde_json::to_vec(&serde_json::json!({"id": request["id"],
                "status": "success", "result": result}))
            .unwrap();
            socket
                .write_all(&(reply.len() as u32).to_be_bytes())
                .unwrap();
            socket.write_all(&reply).unwrap();
        }
    });
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args([
            "--output",
            "json",
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "--project-path",
        ])
        .arg(&root)
        .args(["editor", "status"])
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .output()
        .unwrap();
    server.join().unwrap();
    assert!(output.status.success(), "{output:?}");
    let value: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    let value = value.get("data").unwrap_or(&value);
    assert_eq!(value["state"], "compiling");
}

#[cfg(unix)]
#[test]
fn headless_launch_preserves_paths_and_keeps_batch_host_enabled_without_quit() {
    use std::os::unix::fs::PermissionsExt;
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().join("Project with spaces");
    std::fs::create_dir_all(root.join("ProjectSettings")).unwrap();
    std::fs::write(
        root.join("ProjectSettings/ProjectVersion.txt"),
        "m_EditorVersion: 6000.3.25f1\n",
    )
    .unwrap();
    let editor = dir.path().join("Fake Editor");
    std::fs::write(&editor, "#!/usr/bin/env python3\nimport json,os,sys\nwith open(os.environ['LIFECYCLE_CAPTURE'], 'w') as f: json.dump({'args':sys.argv[1:], 'batch':os.environ.get('UNITY_CLI_ALLOW_BATCH_HOST'), 'port':os.environ.get('UNITY_CLI_PORT_OVERRIDE')},f)\n").unwrap();
    std::fs::set_permissions(&editor, std::fs::Permissions::from_mode(0o755)).unwrap();
    let capture = dir.path().join("capture.json");
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args([
            "editor",
            "open",
            "--headless",
            "--port",
            "6499",
            "--project-path",
        ])
        .arg(&root)
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_EDITOR_PATH", &editor)
        .env("LIFECYCLE_CAPTURE", &capture)
        .output()
        .unwrap();
    assert!(output.status.success(), "{output:?}");
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
    let value: serde_json::Value = loop {
        if let Ok(bytes) = std::fs::read(&capture) {
            if let Ok(value) = serde_json::from_slice(&bytes) {
                break value;
            }
        }
        assert!(
            std::time::Instant::now() < deadline,
            "fake Editor was not invoked"
        );
        std::thread::sleep(std::time::Duration::from_millis(10));
    };
    assert_eq!(value["batch"], "1");
    assert_eq!(value["port"], "6499");
    assert_eq!(
        value["args"][1],
        root.canonicalize().unwrap().to_str().unwrap()
    );
    let args = value["args"].as_array().unwrap();
    assert!(args.contains(&serde_json::json!("-batchmode")));
    assert!(args.contains(&serde_json::json!("-nographics")));
    assert!(!args.contains(&serde_json::json!("-quit")));
}

#[cfg(unix)]
struct FakeEditor {
    root: tempfile::TempDir,
    child: std::process::Child,
}

#[cfg(unix)]
impl FakeEditor {
    fn start(mode: &str) -> Self {
        use std::os::unix::fs::PermissionsExt;
        let root = tempfile::tempdir().unwrap();
        let editor = root.path().join("Editor/Unity");
        std::fs::create_dir(editor.parent().unwrap()).unwrap();
        std::fs::create_dir(root.path().join("ProjectSettings")).unwrap();
        std::fs::write(
            root.path().join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.3.25f1\n",
        )
        .unwrap();
        std::fs::write(&editor, include_str!("fixtures/editor-lifecycle-host.py")).unwrap();
        std::fs::set_permissions(&editor, std::fs::Permissions::from_mode(0o755)).unwrap();
        let child = Command::new(&editor)
            .arg("-projectPath")
            .arg(root.path().canonicalize().unwrap())
            .arg("-logFile")
            .arg(root.path().join("Editor.log"))
            .env("UNITY_CLI_EDITORS_DIR", root.path().join("editors"))
            .env("LIFECYCLE_MODE", mode)
            .spawn()
            .unwrap();
        let server = Self { root, child };
        let lock = server
            .root
            .path()
            .join(format!("editors/{}.json", server.child.id()));
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while !lock.is_file() {
            assert!(std::time::Instant::now() < deadline, "fake Editor startup");
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
        server
    }

    fn cli(&self, args: &[&str]) -> Command {
        let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
        command
            .args(["--output", "json", "--project-path"])
            .arg(self.root.path())
            .args(args)
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("UNITY_EDITOR_PATH", self.root.path().join("Editor/Unity"))
            .env("UNITY_CLI_EDITORS_DIR", self.root.path().join("editors"))
            .env_remove("UNITY_CLI_PORT")
            .env_remove("UNITY_CLI_HOST");
        command
    }
}

#[cfg(unix)]
impl Drop for FakeEditor {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[cfg(unix)]
fn payload(output: &std::process::Output) -> serde_json::Value {
    assert!(output.status.success(), "{output:?}");
    let value: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    value.get("data").unwrap_or(&value).clone()
}

#[cfg(unix)]
#[test]
fn open_reuses_the_project_process_and_waits_for_its_lockfile_ping() {
    let server = FakeEditor::start("ready");
    let output = server
        .cli(&["editor", "open", "--wait-ready", "5"])
        .output()
        .unwrap();
    let value = payload(&output);
    assert_eq!(value["pid"], server.child.id());
    assert_eq!(value["launched"], false);
    assert_eq!(value["ready"], true);
}

#[cfg(unix)]
#[test]
fn close_waits_until_process_exit_after_the_bridge_reply() {
    let mut server = FakeEditor::start("ready");
    let mut cli = server.cli(&["editor", "close"]);
    // Reap the child while close waits, including on Unix hosts where a zombie
    // remains visible to kill(pid, 0) until its parent calls wait().
    let child = &mut server.child;
    let output = std::thread::scope(|scope| {
        let reaper = scope.spawn(|| child.wait().unwrap());
        let output = cli.output().unwrap();
        assert!(reaper.join().unwrap().success());
        output
    });
    assert_eq!(payload(&output)["closed"], true);
    assert!(server.child.try_wait().unwrap().is_some());
}

#[cfg(unix)]
#[test]
fn startup_deadline_leaves_editor_running_and_status_reports_safe_mode_from_log() {
    let mut server = FakeEditor::start("silent");
    std::fs::write(
        server.root.path().join("Editor.log"),
        "Safe Mode: Only loading a subset of assemblies\n",
    )
    .unwrap();
    let output = server
        .cli(&["editor", "open", "--wait-ready", "1"])
        .output()
        .unwrap();
    assert!(!output.status.success());
    let diagnostic = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(diagnostic.contains("Editor remains running"), "{output:?}");
    assert!(server.child.try_wait().unwrap().is_none());
    let output = server.cli(&["editor", "status"]).output().unwrap();
    assert_eq!(payload(&output)["state"], "safe_mode");
}
