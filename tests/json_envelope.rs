use serde_json::{json, Value};
use std::io::{Read, Write};
use std::net::TcpListener;
use std::process::{Command, Output};

fn invoke(args: &[&str]) -> Output {
    invoke_route(args, false)
}

fn invoke_route(args: &[&str], direct: bool) -> Output {
    let root = tempfile::tempdir().unwrap();
    let tools = if direct {
        let path = root.path().join("disable-daemon");
        std::fs::write(&path, "not a directory").unwrap();
        path
    } else {
        root.path().to_path_buf()
    };
    let output = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_CLI_TOOLS_ROOT", &tools)
        .env("UNITY_CLI_EDITORS_DIR", root.path().join("editors"))
        .args(args)
        .output()
        .unwrap();
    if !direct && tools.join("run/unityd/unityd.pid").is_file() {
        let _ = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("UNITY_CLI_TOOLS_ROOT", &tools)
            .args(["unityd", "stop"])
            .output();
    }
    output
}

fn check(output: Output, exit: i32, code: Option<&str>) -> Value {
    assert_eq!(
        output.status.code(),
        Some(exit),
        "stderr={}",
        String::from_utf8_lossy(&output.stderr)
    );
    let result: Value = serde_json::from_slice(&output.stdout).expect("one JSON envelope");
    assert_eq!(result["success"], exit == 0);
    assert!(result["command"].is_string());
    assert!(result.get("data").is_some());
    assert!(result["warnings"].is_array());
    if let Some(code) = code {
        assert_eq!(result["errors"][0]["code"], code);
        assert!(result["errors"][0]["message"].is_string());
    } else {
        assert_eq!(result["errors"], json!([]));
    }
    result
}

#[test]
fn unreachable_editor_is_exit_seven() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port().to_string();
    drop(listener);
    check(
        invoke(&[
            "--host",
            "127.0.0.1",
            "--port",
            &port,
            "--timeout-ms",
            "100",
            "--output",
            "json",
            "system",
            "ping",
        ]),
        7,
        Some("EDITOR_UNREACHABLE"),
    );
}

#[test]
fn invalid_json_is_usage_error() {
    check(
        invoke(&[
            "--output",
            "json",
            "raw",
            "create_gameobject",
            "--json",
            "{",
        ]),
        2,
        Some("INVALID_ARGUMENT"),
    );
    let output = invoke(&["raw", "create_gameobject", "--json", "{"]);
    assert_eq!(output.status.code(), Some(2));
    assert!(String::from_utf8_lossy(&output.stderr).contains("INVALID_ARGUMENT"));
}

#[test]
fn clap_usage_error_has_json_envelope() {
    check(
        invoke(&["--output=json", "--port", "invalid", "system", "ping"]),
        2,
        Some("INVALID_ARGUMENT"),
    );
}

fn mock_call(response: Value, tool: &str, exit: i32, code: Option<&str>) -> Value {
    mock_call_route(response, tool, exit, code, false)
}

fn mock_call_route(
    response: Value,
    tool: &str,
    exit: i32,
    code: Option<&str>,
    direct: bool,
) -> Value {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port().to_string();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket
            .set_read_timeout(Some(std::time::Duration::from_secs(10)))
            .unwrap();
        let mut header = [0; 4];
        socket.read_exact(&mut header).unwrap();
        let mut payload = vec![0; i32::from_be_bytes(header) as usize];
        socket.read_exact(&mut payload).unwrap();
        let bytes = serde_json::to_vec(&response).unwrap();
        socket
            .write_all(&(bytes.len() as i32).to_be_bytes())
            .unwrap();
        socket.write_all(&bytes).unwrap();
    });
    let result = invoke_route(
        &[
            "--host",
            "127.0.0.1",
            "--port",
            &port,
            "--output",
            "json",
            "raw",
            tool,
        ],
        direct,
    );
    server.join().unwrap();
    check(result, exit, code)
}

#[test]
fn direct_authentication_and_bridge_failures_match_daemon() {
    for (code, exit) in [("UNAUTHORIZED", 3), ("OBJECT_NOT_FOUND", 6)] {
        let payload = json!({"status":"error", "code":code, "error":"fixture failure"});
        let result = mock_call_route(payload.clone(), "ping", exit, Some(code), true);
        assert_eq!(result["data"], payload);
    }
}

#[test]
fn bridge_errors_preserve_codes_and_authentication_exit() {
    for (code, exit) in [
        ("UNAUTHORIZED", 3),
        ("GAMEOBJECT_NOT_FOUND", 6),
        ("TEST_RUNNER_DOMAIN_RELOAD_REQUIRED", 4),
    ] {
        let response =
            json!({"status":"error", "code":code, "error":"fixture failure", "details":{"id":42}});
        let result = mock_call(response.clone(), "ping", exit, Some(code));
        assert_eq!(result["data"], response);
    }
}

#[test]
fn test_completion_distinguishes_failed_and_passed() {
    for failed in [0, 1] {
        let payload = json!({"status":"completed", "success":failed == 0, "totalTests":2, "failedTests":failed});
        let result = mock_call(
            json!({"status":"success", "result":payload}),
            "get_test_status",
            if failed == 0 { 0 } else { 8 },
            if failed == 0 {
                None
            } else {
                Some("TEST_FAILED")
            },
        );
        assert_eq!(result["data"], payload);
    }
}

#[test]
fn local_success_uses_single_envelope() {
    let result = check(
        invoke(&["--output", "json", "tool", "list", "--limit", "2"]),
        0,
        None,
    );
    assert_eq!(result["data"].as_array().unwrap().len(), 2);
    assert_eq!(result["command"], "tool list");
}

#[test]
fn nested_bridge_failure_is_not_reported_as_success() {
    let payload = json!({"error":"Missing object", "code":"OBJECT_NOT_FOUND"});
    let result = mock_call(
        json!({"status":"success", "result":payload}),
        "ping",
        6,
        Some("OBJECT_NOT_FOUND"),
    );
    assert_eq!(result["data"], payload);
}

#[test]
fn invalid_port_list_is_usage_error() {
    check(
        invoke(&["--output", "json", "instances", "list", "--ports", "oops"]),
        2,
        Some("INVALID_ARGUMENT"),
    );
}

#[test]
fn invalid_tool_routes_are_usage_errors() {
    for args in [
        vec!["--output", "json", "tool", "schema", "does_not_exist"],
        vec!["--output", "json", "tool", "ping", "--bad"],
        vec![
            "--output",
            "json",
            "tool",
            "list",
            "--category",
            "not-a-category",
        ],
    ] {
        check(invoke(&args), 2, Some("INVALID_ARGUMENT"));
    }
}

#[test]
fn bridge_warnings_reach_the_public_envelope() {
    let warnings = json!(["Play Mode changes will not persist"]);
    let result = mock_call(
        json!({"status":"success", "result":{"value":42}, "warnings":warnings}),
        "ping",
        0,
        None,
    );
    assert_eq!(result["warnings"], warnings);
}

#[test]
fn response_timeout_is_retryable_operation_failure() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port().to_string();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        let mut header = [0; 4];
        socket.read_exact(&mut header).unwrap();
        std::thread::sleep(std::time::Duration::from_millis(200));
    });
    let result = invoke(&[
        "--host",
        "127.0.0.1",
        "--port",
        &port,
        "--timeout-ms",
        "20",
        "--output",
        "json",
        "raw",
        "ping",
    ]);
    server.join().unwrap();
    check(result, 6, Some("TIMEOUT"));
}

#[test]
fn batch_with_failed_operation_does_not_exit_zero() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port().to_string();
    let server = std::thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        let mut header = [0; 4];
        socket.read_exact(&mut header).unwrap();
        let mut payload = vec![0; i32::from_be_bytes(header) as usize];
        socket.read_exact(&mut payload).unwrap();
        let bytes = serde_json::to_vec(
            &json!({"status":"error", "code":"UNAUTHORIZED", "error":"Missing token"}),
        )
        .unwrap();
        socket
            .write_all(&(bytes.len() as i32).to_be_bytes())
            .unwrap();
        socket.write_all(&bytes).unwrap();
    });
    let result = invoke(&[
        "--host",
        "127.0.0.1",
        "--port",
        &port,
        "--output",
        "json",
        "batch",
        "--json",
        r#"[{"tool":"ping","params":{}}]"#,
    ]);
    server.join().unwrap();
    check(result, 3, Some("UNAUTHORIZED"));
}

#[test]
fn skills_show_and_lint_use_envelopes() {
    let show = check(
        invoke(&["--output", "json", "skills", "show", "unity-cli-usage"]),
        0,
        None,
    );
    assert!(show["data"].is_string());
    check(
        invoke(&["--output", "json", "skills", "lint", "--severity", "error"]),
        0,
        None,
    );
}

#[test]
fn invalid_batch_is_usage_error() {
    check(
        invoke(&["--output", "json", "batch", "--json", "{"]),
        2,
        Some("INVALID_ARGUMENT"),
    );
}
