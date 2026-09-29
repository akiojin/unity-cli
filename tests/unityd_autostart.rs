//! Exercise the installed CLI boundary, using Unity's framed TCP protocol.
//! Unix sockets isolate daemons per test; Windows uses a shared fixed port.
#![cfg(unix)]

use serde_json::{json, Value};
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::process::{Command, Output};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::Arc;
use std::thread::{self, JoinHandle};
use std::time::{Duration, Instant};
use tempfile::TempDir;

struct MockUnity {
    port: u16,
    connections: Arc<AtomicUsize>,
    stopped: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
}

impl MockUnity {
    fn new(label: &'static str) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        listener.set_nonblocking(true).unwrap();
        let connections = Arc::new(AtomicUsize::new(0));
        let stopped = Arc::new(AtomicBool::new(false));
        let count = connections.clone();
        let stop = stopped.clone();
        let worker = thread::spawn(move || {
            let mut workers = Vec::new();
            while !stop.load(Ordering::SeqCst) {
                match listener.accept() {
                    Ok((stream, _)) => {
                        count.fetch_add(1, Ordering::SeqCst);
                        let stop = stop.clone();
                        workers.push(thread::spawn(move || serve_unity(stream, label, stop)));
                    }
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(Duration::from_millis(5));
                    }
                    Err(error) => panic!("mock accept failed: {error}"),
                }
            }
            for worker in workers {
                worker.join().unwrap();
            }
        });
        Self {
            port,
            connections,
            stopped,
            worker: Some(worker),
        }
    }
}

fn serve_unity(mut stream: TcpStream, label: &str, stop: Arc<AtomicBool>) {
    stream
        .set_read_timeout(Some(Duration::from_millis(100)))
        .unwrap();
    stream
        .set_write_timeout(Some(Duration::from_secs(2)))
        .unwrap();
    while !stop.load(Ordering::SeqCst) {
        let mut header = [0; 4];
        match stream.read_exact(&mut header) {
            Ok(()) => {}
            Err(error)
                if matches!(
                    error.kind(),
                    std::io::ErrorKind::WouldBlock | std::io::ErrorKind::TimedOut
                ) =>
            {
                continue
            }
            Err(_) => break,
        }
        let length = u32::from_be_bytes(header) as usize;
        assert!(length < 1024 * 1024);
        let mut body = vec![0; length];
        stream.read_exact(&mut body).unwrap();
        let request: Value = serde_json::from_slice(&body).unwrap();
        let response = serde_json::to_vec(&json!({
            "id": request["id"], "status": "success",
            "result": { "endpoint": label, "tool": request["type"], "params": request["params"] }
        }))
        .unwrap();
        if stream
            .write_all(&(response.len() as u32).to_be_bytes())
            .is_err()
            || stream.write_all(&response).is_err()
        {
            break;
        }
    }
}

impl Drop for MockUnity {
    fn drop(&mut self) {
        self.stopped.store(true, Ordering::SeqCst);
        self.worker.take().unwrap().join().unwrap();
    }
}

struct CliTest {
    root: TempDir,
    port: u16,
}

impl CliTest {
    fn new(port: u16) -> Self {
        // Keep the socket path below sockaddr_un's limit on macOS.
        Self {
            root: tempfile::Builder::new()
                .prefix("uc-")
                .tempdir_in("/tmp")
                .unwrap(),
            port,
        }
    }

    fn command(&self, args: &[&str]) -> Command {
        let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
        command
            .env("UNITY_CLI_TOOLS_ROOT", self.root.path())
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("UNITY_CLI_UNITYD_IDLE_TIMEOUT", "2")
            .env("NO_COLOR", "1")
            .env_remove("RUST_LOG")
            .args([
                "--output",
                "json",
                "--host",
                "127.0.0.1",
                "--port",
                &self.port.to_string(),
                "--timeout-ms",
                "2000",
            ])
            .args(args);
        command
    }

    fn run(&self, args: &[&str]) -> Value {
        parse_success(self.command(args).output().unwrap())
    }

    fn status(&self) -> Value {
        self.run(&["unityd", "status"])
    }
}

impl Drop for CliTest {
    fn drop(&mut self) {
        let _ = self.command(&["unityd", "stop"]).output();
        // Give the server its graceful cleanup window before TempDir removes runtime files.
        thread::sleep(Duration::from_millis(100));
    }
}

fn parse_success(output: Output) -> Value {
    assert!(
        output.status.success(),
        "CLI failed: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    serde_json::from_slice(&output.stdout).unwrap_or_else(|error| {
        panic!(
            "stdout is not JSON: {error}: {}",
            String::from_utf8_lossy(&output.stdout)
        )
    })
}

#[test]
fn cold_typed_call_starts_daemon_and_warm_call_reuses_connection() {
    let unity = MockUnity::new("first");
    let cli = CliTest::new(unity.port);
    assert_eq!(cli.status()["running"], false);
    assert_eq!(cli.run(&["system", "ping"])["endpoint"], "first");
    let first = cli.status();
    assert_eq!(
        first["running"], true,
        "cold Unity command must start unityd"
    );
    assert!(first["pid"].as_u64().is_some());
    assert_eq!(cli.run(&["raw", "ping"])["endpoint"], "first");
    let warm = cli.status();
    assert_eq!(warm["pid"], first["pid"]);
    assert_eq!(warm["connections"], 1);
    assert_eq!(unity.connections.load(Ordering::SeqCst), 1);
}

#[test]
fn idle_daemon_exits_and_next_call_restarts_it() {
    let unity = MockUnity::new("idle");
    let cli = CliTest::new(unity.port);
    cli.run(&["system", "ping"]);
    let first = cli.status();
    assert_eq!(first["running"], true);
    // Status requests themselves refresh idle activity, so wait without polling IPC.
    thread::sleep(Duration::from_secs(3));
    assert_eq!(cli.status()["running"], false);
    cli.run(&["system", "ping"]);
    let restarted = cli.status();
    assert_eq!(restarted["running"], true);
    assert_ne!(restarted["pid"], first["pid"]);
    assert_eq!(unity.connections.load(Ordering::SeqCst), 2);
}

#[test]
fn concurrent_cold_raw_typed_and_batch_calls_share_one_daemon() {
    let unity = MockUnity::new("concurrent");
    let cli = CliTest::new(unity.port);
    let batch = r#"[{"tool":"ping","params":{"message":"batch"}}]"#;
    let commands: [&[&str]; 3] = [
        &["raw", "ping"],
        &["system", "ping"],
        &["batch", "--json", batch],
    ];
    let workers: Vec<_> = (0..12)
        .map(|i| {
            let mut command = cli.command(commands[i % 3]);
            thread::spawn(move || parse_success(command.output().unwrap()))
        })
        .collect();
    for worker in workers {
        let response = worker.join().unwrap();
        let result = if response.is_array() {
            &response[0]["result"]
        } else {
            &response
        };
        assert_eq!(result["endpoint"], "concurrent");
    }
    let first = cli.status();
    assert_eq!(first["running"], true);
    assert!(first["pid"].as_u64().is_some());
    assert_eq!(
        unity.connections.load(Ordering::SeqCst),
        1,
        "all requests must reach one pooled connection"
    );
    cli.run(&["system", "ping"]);
    assert_eq!(cli.status()["pid"], first["pid"]);
}

#[test]
fn daemon_routes_each_endpoint_to_its_own_pooled_connection() {
    let first = MockUnity::new("first");
    let second = MockUnity::new("second");
    let mut cli = CliTest::new(first.port);
    assert_eq!(cli.run(&["system", "ping"])["endpoint"], "first");
    let pid = cli.status()["pid"].clone();
    cli.port = second.port;
    assert_eq!(cli.run(&["system", "ping"])["endpoint"], "second");
    cli.port = first.port;
    assert_eq!(cli.run(&["system", "ping"])["endpoint"], "first");
    assert_eq!(cli.status()["pid"], pid);
    assert_eq!(cli.status()["connections"], 2);
    assert_eq!(first.connections.load(Ordering::SeqCst), 1);
    assert_eq!(second.connections.load(Ordering::SeqCst), 1);
}

#[test]
fn local_invalid_status_and_skipped_dry_run_commands_do_not_start_daemon() {
    let unity = MockUnity::new("unused");
    let cli = CliTest::new(unity.port);
    for args in [
        vec!["tool", "list"],
        vec!["tool", "schema", "ping"],
        vec!["unityd", "status"],
        vec!["--dry-run", "scene", "create", "unused"],
    ] {
        cli.run(&args);
        assert_eq!(cli.status()["running"], false, "spawned for {args:?}");
    }
    for args in [
        vec!["tool", "not_a_tool"],
        vec!["raw", "ping", "--json", "[]"],
        vec!["batch", "--json", r#"[{"tool":"ping","params":[]}]"#],
    ] {
        assert!(
            !cli.command(&args).output().unwrap().status.success(),
            "accepted invalid {args:?}"
        );
        assert_eq!(
            cli.status()["running"],
            false,
            "spawned for invalid {args:?}"
        );
    }
    assert_eq!(unity.connections.load(Ordering::SeqCst), 0);
}

#[test]
fn startup_failure_falls_back_to_direct_tcp_with_diagnostic() {
    let unity = MockUnity::new("fallback");
    let cli = CliTest::new(unity.port);
    std::fs::write(cli.root.path().join("run"), "block runtime directory").unwrap();
    let started = Instant::now();
    let output = cli.command(&["system", "ping"]).output().unwrap();
    let diagnostic = String::from_utf8_lossy(&output.stderr).into_owned();
    assert_eq!(parse_success(output)["endpoint"], "fallback");
    assert!(
        diagnostic.contains("direct TCP"),
        "missing fallback diagnostic: {diagnostic}"
    );
    assert!(started.elapsed() < Duration::from_secs(10));
    assert_eq!(unity.connections.load(Ordering::SeqCst), 1);
}

#[test]
fn verbose_route_and_startup_timing_use_stderr_preserving_json_stdout() {
    let unity = MockUnity::new("verbose");
    let cli = CliTest::new(unity.port);
    let output = cli.command(&["-v", "system", "ping"]).output().unwrap();
    let diagnostic = String::from_utf8_lossy(&output.stderr).into_owned();
    assert_eq!(parse_success(output)["endpoint"], "verbose");
    assert!(diagnostic.contains("daemon"), "missing route: {diagnostic}");
    assert!(
        diagnostic.contains("startup_ms"),
        "missing startup timing: {diagnostic}"
    );
    assert!(
        diagnostic.contains("operation_ms"),
        "missing operation timing: {diagnostic}"
    );
    let output = cli.command(&["-v", "system", "ping"]).output().unwrap();
    let diagnostic = String::from_utf8_lossy(&output.stderr).into_owned();
    assert_eq!(parse_success(output)["endpoint"], "verbose");
    assert!(
        diagnostic.contains("startup_ms=None"),
        "warm call should not start a daemon: {diagnostic}"
    );
}

#[test]
fn verbose_batch_reports_cold_and_warm_route_timing_on_stderr() {
    let unity = MockUnity::new("verbose-batch");
    let cli = CliTest::new(unity.port);
    let batch = r#"[{"tool":"ping","params":{}}]"#;
    for cold in [true, false] {
        let output = cli
            .command(&["-v", "batch", "--json", batch])
            .output()
            .unwrap();
        let diagnostic = String::from_utf8_lossy(&output.stderr).into_owned();
        assert_eq!(
            parse_success(output)[0]["result"]["endpoint"],
            "verbose-batch"
        );
        assert!(
            diagnostic.contains("route=\"daemon\""),
            "missing daemon route: {diagnostic}"
        );
        assert!(
            diagnostic.contains("operation_ms"),
            "missing batch operation timing: {diagnostic}"
        );
        let expected = if cold {
            "startup_ms=Some("
        } else {
            "startup_ms=None"
        };
        assert!(
            diagnostic.contains(expected),
            "missing {expected}: {diagnostic}"
        );
    }
    assert_eq!(unity.connections.load(Ordering::SeqCst), 1);
}
