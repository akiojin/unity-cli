#![cfg(unix)]

use serde_json::{json, Value};
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::os::unix::fs::PermissionsExt;
use std::process::Command;
use std::time::{Duration, Instant};

fn fixture() -> tempfile::TempDir {
    let dir = tempfile::tempdir().unwrap();
    std::fs::create_dir(dir.path().join("ProjectSettings")).unwrap();
    std::fs::write(
        dir.path().join("ProjectSettings/ProjectVersion.txt"),
        "m_EditorVersion: 6000.3.25f1\n",
    )
    .unwrap();
    std::fs::write(
        dir.path().join("editor"),
        "#!/bin/sh\necho $$ > \"$PID_FILE\"\nexec sleep 60\n",
    )
    .unwrap();
    std::fs::set_permissions(
        dir.path().join("editor"),
        std::fs::Permissions::from_mode(0o755),
    )
    .unwrap();
    dir
}

fn cli(dir: &tempfile::TempDir, port: u16) -> Command {
    let mut cmd = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
    cmd.env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_CLI_EDITORS_DIR", dir.path().join("editors"))
        .env("UNITY_EDITOR_PATH", dir.path().join("editor"))
        .env("PID_FILE", dir.path().join("pid"))
        .env_remove("UNITY_CLI_AUTH_TOKEN_FILE")
        .args([
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "--project-path",
        ])
        .arg(dir.path())
        .args(["test", "--output"])
        .arg(dir.path().join("results.xml"));
    cmd
}

fn accept(listener: &TcpListener) -> TcpStream {
    listener.set_nonblocking(true).unwrap();
    let deadline = Instant::now() + Duration::from_secs(10);
    loop {
        if let Ok((socket, _)) = listener.accept() {
            socket.set_nonblocking(false).unwrap();
            socket
                .set_read_timeout(Some(Duration::from_secs(5)))
                .unwrap();
            return socket;
        }
        assert!(Instant::now() < deadline, "CLI never connected");
        std::thread::sleep(Duration::from_millis(10));
    }
}

fn reply(socket: &mut TcpStream, tool: &str, result: Value) {
    let mut header = [0; 4];
    socket.read_exact(&mut header).unwrap();
    let mut bytes = vec![0; u32::from_be_bytes(header) as usize];
    socket.read_exact(&mut bytes).unwrap();
    let request: Value = serde_json::from_slice(&bytes).unwrap();
    assert_eq!(request["type"], tool);
    let payload =
        serde_json::to_vec(&json!({"id":request["id"],"status":"success","result":result}))
            .unwrap();
    socket
        .write_all(&(payload.len() as u32).to_be_bytes())
        .unwrap();
    socket.write_all(&payload).unwrap();
}

fn assert_child_stopped(output: &std::process::Output) {
    let stderr = String::from_utf8_lossy(&output.stderr);
    let pid: i32 = regex::Regex::new(r"Started headless Editor \(pid (\d+)\)")
        .unwrap()
        .captures(&stderr)
        .unwrap()[1]
        .parse()
        .unwrap();
    assert_ne!(unsafe { libc::kill(pid, 0) }, 0, "owned Editor leaked");
}

#[test]
fn failed_tests_reconnect_export_and_annotate_before_exit_eight() {
    let dir = fixture();
    let root = dir.path().canonicalize().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        reply(
            &mut socket,
            "get_compilation_state",
            json!({"isCompiling":false,"errorCount":0}),
        );
        reply(
            &mut socket,
            "run_tests",
            json!({"status":"running","runId":"run-445"}),
        );
        drop(socket);
        // A dropped poll simulates Domain Reload; run_tests must not repeat.
        drop(accept(&listener));
        let mut socket = accept(&listener);
        reply(
            &mut socket,
            "get_test_status",
            json!({"status":"completed","runId":"run-445","success":false,"totalTests":1,"failedTests":1,
            "tests":[{"name":"Bad","fullName":"Suite.Bad","status":"Failed","message":"intentional"}],
            "failures":[{"testName":"Suite.Bad","message":"intentional","stackTrace":"at Suite.Bad () (at Assets/Test.cs:12)"}]}),
        );
    });
    let output = cli(&dir, port)
        .args(["--output-format", "github", "--timeout", "10"])
        .output()
        .unwrap();
    server.join().unwrap();
    assert_eq!(output.status.code(), Some(8), "{output:?}");
    assert!(
        String::from_utf8_lossy(&output.stdout).contains("::error file=Assets/Test.cs,line=12::")
    );
    let xml = std::fs::read_to_string(dir.path().join("results.xml")).unwrap();
    assert_eq!(xml.matches("<failure ").count(), 1);
    assert_child_stopped(&output);
}

#[test]
fn unreachable_editor_times_out_and_cleans_up() {
    let dir = fixture();
    std::fs::create_dir(dir.path().join("Logs")).unwrap();
    std::fs::write(
        dir.path().join("Logs/unity-cli-editor.log"),
        "Previous run: error CS1002",
    )
    .unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    drop(listener);
    let output = cli(&dir, port).args(["--timeout", "1"]).output().unwrap();
    assert_eq!(output.status.code(), Some(7), "{output:?}");
    assert_child_stopped(&output);
}

#[test]
fn compilation_failure_is_exit_six_and_cleans_up() {
    let dir = fixture();
    let root = dir.path().canonicalize().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        reply(
            &mut socket,
            "get_compilation_state",
            json!({"isCompiling":false,"errorCount":1}),
        );
    });
    let output = cli(&dir, port).args(["--timeout", "10"]).output().unwrap();
    server.join().unwrap();
    assert_eq!(output.status.code(), Some(6), "{output:?}");
    assert_child_stopped(&output);
}

#[test]
fn timeout_after_starting_a_test_is_execution_failure() {
    let dir = fixture();
    let root = dir.path().canonicalize().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        reply(
            &mut socket,
            "get_compilation_state",
            json!({"isCompiling":false,"errorCount":0}),
        );
        reply(
            &mut socket,
            "run_tests",
            json!({"status":"running","runId":"run-445"}),
        );
        let mut socket = accept(&listener);
        reply(
            &mut socket,
            "get_test_status",
            json!({"status":"running","runId":"run-445"}),
        );
    });
    let output = cli(&dir, port).args(["--timeout", "1"]).output().unwrap();
    server.join().unwrap();
    assert_eq!(output.status.code(), Some(6), "{output:?}");
    assert_child_stopped(&output);
}

#[test]
fn starting_tests_uses_the_command_deadline_not_the_poll_timeout() {
    let dir = fixture();
    let root = dir.path().canonicalize().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        reply(
            &mut socket,
            "get_compilation_state",
            json!({"isCompiling":false,"errorCount":0}),
        );
        std::thread::sleep(Duration::from_millis(2300));
        reply(
            &mut socket,
            "run_tests",
            json!({"status":"running","runId":"run-445"}),
        );
        let mut socket = accept(&listener);
        reply(
            &mut socket,
            "get_test_status",
            json!({"status":"completed","runId":"run-445","success":true,"totalTests":1,"failedTests":0,"tests":[{"name":"Pass","fullName":"Suite.Pass","status":"Passed"}]}),
        );
    });
    let output = cli(&dir, port).args(["--timeout", "10"]).output().unwrap();
    // Assert the CLI failure first so RED does not hang joining a server that
    // legitimately never receives the next request after an early timeout.
    assert_eq!(output.status.code(), Some(0), "{output:?}");
    server.join().unwrap();
    assert_child_stopped(&output);
}

#[test]
fn readiness_retries_a_disconnect_before_compilation_state() {
    let dir = fixture();
    let root = dir.path().canonicalize().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = std::thread::spawn(move || {
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        drop(socket);
        let mut socket = accept(&listener);
        reply(&mut socket, "ping", json!({"projectPath":root}));
        reply(
            &mut socket,
            "get_compilation_state",
            json!({"isCompiling":false,"errorCount":0}),
        );
        reply(
            &mut socket,
            "run_tests",
            json!({"status":"running","runId":"run-445"}),
        );
        let mut socket = accept(&listener);
        reply(
            &mut socket,
            "get_test_status",
            json!({"status":"completed","runId":"run-445","success":true,"totalTests":1,"failedTests":0,"tests":[{"name":"Pass","fullName":"Suite.Pass","status":"Passed"}]}),
        );
    });
    let output = cli(&dir, port).args(["--timeout", "10"]).output().unwrap();
    assert_eq!(output.status.code(), Some(0), "{output:?}");
    server.join().unwrap();
    assert_child_stopped(&output);
}
