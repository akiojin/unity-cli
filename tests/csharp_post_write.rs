#![cfg(unix)]

use serde_json::{json, Value};
use std::fs;
use std::io::{Read, Write};
use std::net::TcpListener;
use std::os::unix::fs::PermissionsExt;
use std::process::Command;
use std::thread;
use std::time::{Duration, Instant};

struct Fixture {
    root: tempfile::TempDir,
}

impl Fixture {
    fn new() -> Self {
        let root = tempfile::tempdir().unwrap();
        let rid = match (std::env::consts::OS, std::env::consts::ARCH) {
            ("macos", "aarch64") => "osx-arm64",
            ("macos", _) => "osx-x64",
            (_, "aarch64") => "linux-arm64",
            _ => "linux-x64",
        };
        let install = root.path().join("tools/csharp-lsp").join(rid);
        fs::create_dir_all(&install).unwrap();
        let server = install.join("server");
        fs::write(&server, include_str!("fixtures/csharp-write-server.py")).unwrap();
        fs::set_permissions(server, fs::Permissions::from_mode(0o755)).unwrap();
        fs::create_dir_all(root.path().join("project/Assets")).unwrap();
        fs::write(
            root.path().join("project/Assets/Test.cs"),
            "class Test {}\n",
        )
        .unwrap();
        Self { root }
    }

    fn call(&self, tool: &str, params: &Value, port: u16) -> Value {
        let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .args([
                "--host",
                "127.0.0.1",
                "--port",
                &port.to_string(),
                "--timeout-ms",
                "10000",
                "--project-path",
            ])
            .arg(self.root.path().join("project"))
            .args(["raw", tool, "--json", &params.to_string()])
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("UNITY_CLI_TOOLS_ROOT", self.root.path().join("tools"))
            .env("UNITY_CLI_CACHE_ROOT", self.root.path().join("cache"))
            .env(
                "UNITY_CLI_REGISTRY_PATH",
                self.root.path().join("registry.json"),
            )
            .env("UNITY_CLI_EDITORS_DIR", self.root.path().join("editors"))
            .env("UNITY_PROJECT_ROOT", self.root.path().join("wrong-project"))
            .env("UNITY_CLI_PORT", "1")
            .env_remove("UNITY_CLI_LSP_MODE")
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
        serde_json::from_slice(&output.stdout).unwrap()
    }

    fn writes(&self) -> usize {
        fs::read_to_string(self.root.path().join("project/Assets/Test.cs"))
            .unwrap()
            .matches("class Test {}")
            .count()
    }
}

fn bridge(responses: Vec<Value>) -> (u16, thread::JoinHandle<Vec<Value>>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    listener.set_nonblocking(true).unwrap();
    let task = thread::spawn(move || {
        let deadline = Instant::now() + Duration::from_secs(30);
        let mut requests = Vec::new();
        for response in responses {
            let mut socket = loop {
                match listener.accept() {
                    Ok((socket, _)) => break socket,
                    Err(error)
                        if error.kind() == std::io::ErrorKind::WouldBlock
                            && Instant::now() < deadline =>
                    {
                        thread::sleep(Duration::from_millis(10))
                    }
                    Err(error) => panic!("missing post-write call: {error}"),
                }
            };
            // BSD accept can inherit the listener's nonblocking mode.
            socket.set_nonblocking(false).unwrap();
            socket
                .set_read_timeout(Some(Duration::from_secs(15)))
                .unwrap();
            let mut size = [0; 4];
            socket.read_exact(&mut size).unwrap();
            let mut payload = vec![0; i32::from_be_bytes(size) as usize];
            socket.read_exact(&mut payload).unwrap();
            requests.push(serde_json::from_slice(&payload).unwrap());
            if response.is_null() {
                continue; // Domain Reload closes an in-flight read connection.
            }
            let data = serde_json::to_vec(&response).unwrap();
            socket
                .write_all(&(data.len() as i32).to_be_bytes())
                .unwrap();
            socket.write_all(&data).unwrap();
        }
        requests
    });
    (port, task)
}

fn write_params() -> Value {
    json!({"relative":"Assets/Test.cs","newText":"class Test {}\n","apply":true,"refresh":true,"waitForCompile":true})
}

#[test]
fn retry_after_refresh_failure_preserves_files_and_content() {
    let fixture = Fixture::new();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    drop(listener);
    for _ in 0..2 {
        let response = fixture.call("write_csharp_file", &write_params(), port);
        assert_eq!(response["success"], false);
        assert_eq!(response["applied"], true);
        assert_eq!(response["changedFiles"], json!(["Assets/Test.cs"]));
        assert_eq!(response["error"]["code"], "refresh_failed");
        assert!(response["error"]["message"]
            .as_str()
            .unwrap()
            .contains(&port.to_string()));
    }
    assert_eq!(
        fixture.writes(),
        1,
        "retry must not duplicate file contents"
    );
}

#[test]
fn compile_failure_returns_structured_error_and_saved_files() {
    let fixture = Fixture::new();
    let (port, server) = bridge(vec![
        json!({"status":"success","result":{}}),
        json!({"status":"error","error":"compilation state unavailable"}),
    ]);
    let response = fixture.call("write_csharp_file", &write_params(), port);
    assert_eq!(response["success"], false);
    assert_eq!(response["applied"], true);
    assert_eq!(response["changedFiles"], json!(["Assets/Test.cs"]));
    assert_eq!(response["error"]["code"], "compile_wait_failed");
    assert!(response["error"]["message"]
        .as_str()
        .unwrap()
        .contains("compilation state unavailable"));
    assert_eq!(fixture.writes(), 1);
    assert_eq!(server.join().unwrap().len(), 2);
}

#[test]
fn preview_does_not_refresh_or_compile() {
    let fixture = Fixture::new();
    let mut params = write_params();
    params["apply"] = json!(false);
    let response = fixture.call("write_csharp_file", &params, 1);
    assert_eq!(response["success"], true);
    assert_eq!(response["applied"], false);
    assert!(response.get("refresh").is_none());
    assert!(response.get("compileState").is_none());
}

#[test]
fn compilation_read_reconnects_after_domain_reload_without_rewriting() {
    let fixture = Fixture::new();
    let (port, server) = bridge(vec![
        json!({"status":"success","result":{}}),
        Value::Null,
        json!({"status":"success","result":{"isCompiling":false,"isUpdating":false,"errorCount":0}}),
    ]);
    let response = fixture.call("write_csharp_file", &write_params(), port);
    assert_eq!(response["success"], true, "{response}");
    assert_eq!(response["compileState"]["errorCount"], 0);
    let requests = server.join().unwrap();
    assert_eq!(requests.len(), 3);
    assert_eq!(requests[1]["type"], "get_compilation_state");
    assert_eq!(requests[2]["type"], "get_compilation_state");
}

#[test]
fn post_write_tools_use_explicit_endpoint_and_include_compile_results() {
    for (tool, mut params) in [
        (
            "create_csharp_file",
            json!({"relative":"Assets/Test.cs","text":"class Test {}"}),
        ),
        (
            "write_csharp_file",
            json!({"relative":"Assets/Test.cs","newText":"class Test {}"}),
        ),
        (
            "apply_csharp_edits",
            json!({"files":[{"relative":"Assets/Test.cs","newText":"class Test {}"}]}),
        ),
    ] {
        let fixture = Fixture::new();
        params["apply"] = json!(true);
        params["waitForCompile"] = json!(true);
        let (port, server) = bridge(vec![
            json!({"status":"success","result":{"message":"refreshed"}}),
            json!({"status":"success","result":{"isCompiling":true,"isUpdating":false}}),
            json!({"status":"success","result":{"isCompiling":false,"isUpdating":false,"errorCount":0,"messages":[{"type":"warning","message":"example"}]}}),
        ]);
        let response = fixture.call(tool, &params, port);
        assert_eq!(response["success"], true, "{tool}: {response}");
        assert_eq!(response["compileState"]["errorCount"], 0, "{tool}");
        assert_eq!(response["diagnostics"][0]["message"], "example");
        assert_eq!(fixture.writes(), 1);
        let requests = server.join().unwrap();
        assert_eq!(requests[0]["type"], "refresh_assets");
        assert_eq!(requests[1]["type"], "get_compilation_state");
        assert_eq!(requests[2]["params"]["includeMessages"], true);
    }
}
