use serde_json::{json, Value};
use std::io::{Read, Write};
use std::net::TcpListener;
use std::process::Command;

struct DaemonProcess(std::process::Child);

impl Drop for DaemonProcess {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

#[test]
fn failed_build_status_keeps_json_report_and_nonzero_exit() {
    check_failed_build_status(false);
}

#[test]
#[cfg(unix)]
fn daemon_failed_build_status_keeps_json_report_and_nonzero_exit() {
    check_failed_build_status(true);
}

fn check_failed_build_status(daemon: bool) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let response = json!({
        "status": "error", "error": "Build failed", "code": "BUILD_FAILED",
        "details": {"buildId": "build-123", "state": "failed", "reportResult": "Failed",
            "totalErrors": 1, "errors": ["Compiler error"], "artifacts": []}
    });
    let expected = response.clone();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket
            .set_read_timeout(Some(std::time::Duration::from_secs(10)))
            .unwrap();
        let mut header = [0; 4];
        socket.read_exact(&mut header).unwrap();
        let mut payload = vec![0; i32::from_be_bytes(header) as usize];
        socket.read_exact(&mut payload).unwrap();
        let request: Value = serde_json::from_slice(&payload).unwrap();
        assert_eq!(request["type"], "get_build_status");
        let bytes = serde_json::to_vec(&response).unwrap();
        socket
            .write_all(&(bytes.len() as i32).to_be_bytes())
            .unwrap();
        socket.write_all(&bytes).unwrap();
    });
    let root = tempfile::tempdir().unwrap();
    let _daemon_process = if daemon {
        let child = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("UNITY_CLI_TOOLS_ROOT", root.path())
            .args(["unityd", "serve"])
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .spawn()
            .unwrap();
        let guard = DaemonProcess(child);
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while !root.path().join("run/unityd/unityd.pid").exists() {
            assert!(std::time::Instant::now() < deadline, "daemon did not start");
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
        Some(guard)
    } else {
        None
    };
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_CLI_TOOLS_ROOT", root.path())
        .args([
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "--output",
            "json",
            "raw",
            "get_build_status",
            "--json",
            r#"{"buildId":"build-123"}"#,
        ])
        .output()
        .unwrap();
    server.join().unwrap();
    assert_eq!(output.status.code(), Some(6));
    let actual: Value = serde_json::from_slice(&output.stdout).unwrap_or_else(|error| {
        panic!(
            "missing error JSON: {error}; stderr={}",
            String::from_utf8_lossy(&output.stderr)
        )
    });
    assert_eq!(actual["success"], false);
    assert_eq!(actual["errors"][0]["code"], "BUILD_FAILED");
    assert_eq!(actual["data"], expected);
}
