use serde_json::{json, Value};
use std::{
    io::{BufRead, BufReader, Read, Write},
    net::TcpListener,
    process::{Child, Command, Stdio},
    sync::{
        atomic::{AtomicBool, AtomicUsize, Ordering},
        mpsc, Arc,
    },
    time::{Duration, Instant},
};

struct Editor {
    port: u16,
    published: Arc<AtomicBool>,
    calls: Arc<AtomicUsize>,
    stop: Arc<AtomicBool>,
    worker: Option<std::thread::JoinHandle<()>>,
}

impl Editor {
    fn new() -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        listener.set_nonblocking(true).unwrap();
        let published = Arc::new(AtomicBool::new(true));
        let calls = Arc::new(AtomicUsize::new(0));
        let stop = Arc::new(AtomicBool::new(false));
        let (active, invocations, done) = (published.clone(), calls.clone(), stop.clone());
        let worker = std::thread::spawn(move || {
            while !done.load(Ordering::SeqCst) {
                let (mut socket, _) = match listener.accept() {
                    Ok(connection) => connection,
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        std::thread::sleep(Duration::from_millis(2));
                        continue;
                    }
                    Err(error) => panic!("accept: {error}"),
                };
                socket.set_nonblocking(false).unwrap();
                socket
                    .set_read_timeout(Some(Duration::from_secs(2)))
                    .unwrap();
                loop {
                    let mut header = [0; 4];
                    if socket.read_exact(&mut header).is_err() {
                        break;
                    }
                    let mut bytes = vec![0; u32::from_be_bytes(header) as usize];
                    socket.read_exact(&mut bytes).unwrap();
                    let request: Value = serde_json::from_slice(&bytes).unwrap();
                    let data = match request["type"].as_str().unwrap() {
                        "list_tools" => {
                            let tools = if active.load(Ordering::SeqCst) {
                                vec![json!({
                                    "name":"mcp_probe_444", "description":"Project tool", "mutating":true,
                                    "executor":"remote", "source":"custom", "response_schema":{},
                                    "params_schema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}
                                })]
                            } else {
                                vec![]
                            };
                            json!({"tools":tools})
                        }
                        "mcp_probe_444" => {
                            invocations.fetch_add(1, Ordering::SeqCst);
                            json!({"created":request["params"]["name"]})
                        }
                        "ping" => json!({"message":"pong"}),
                        name => panic!("unexpected tool {name}"),
                    };
                    let bytes =
                        serde_json::to_vec(&json!({"status":"success","result":data})).unwrap();
                    if socket
                        .write_all(&(bytes.len() as u32).to_be_bytes())
                        .is_err()
                        || socket.write_all(&bytes).is_err()
                    {
                        break;
                    }
                }
            }
        });
        Self {
            port,
            published,
            calls,
            stop,
            worker: Some(worker),
        }
    }
}

impl Drop for Editor {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::SeqCst);
        self.worker.take().unwrap().join().unwrap();
    }
}

struct Peer {
    child: Child,
    messages: mpsc::Receiver<Value>,
}

impl Peer {
    fn send(&mut self, value: Value) {
        writeln!(self.child.stdin.as_mut().unwrap(), "{value}").unwrap();
        self.child.stdin.as_mut().unwrap().flush().unwrap();
    }

    fn receive(&self, matches: impl Fn(&Value) -> bool) -> Value {
        let deadline = Instant::now() + Duration::from_secs(10);
        loop {
            let value = self
                .messages
                .recv_timeout(deadline.saturating_duration_since(Instant::now()))
                .unwrap();
            if matches(&value) {
                return value;
            }
        }
    }
}

impl Drop for Peer {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[test]
fn custom_catalog_calls_and_live_removal_share_cli_discovery_and_validation() {
    let editor = Editor::new();
    let root = tempfile::tempdir().unwrap();
    std::fs::write(root.path().join("disable-daemon"), "file").unwrap();
    let mut child = Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .args([
            "--host",
            "127.0.0.1",
            "--port",
            &editor.port.to_string(),
            "--timeout-ms",
            "500",
            "mcp",
        ])
        .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
        .env("UNITY_CLI_TOOLS_ROOT", root.path().join("disable-daemon"))
        .env("UNITY_CLI_EDITORS_DIR", root.path().join("editors"))
        .env_remove("UNITY_CLI_AUTH_TOKEN_FILE")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::inherit())
        .spawn()
        .unwrap();
    let stdout = child.stdout.take().unwrap();
    let (send, receive) = mpsc::channel();
    std::thread::spawn(move || {
        for line in BufReader::new(stdout).lines() {
            if send
                .send(serde_json::from_str(&line.unwrap()).unwrap())
                .is_err()
            {
                break;
            }
        }
    });
    let mut peer = Peer {
        child,
        messages: receive,
    };
    peer.send(json!({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}));
    peer.receive(|v| v["id"] == 1);
    peer.send(json!({"jsonrpc":"2.0","method":"notifications/initialized"}));
    peer.send(json!({"jsonrpc":"2.0","id":2,"method":"tools/list"}));
    let listed = peer.receive(|v| v["id"] == 2);
    let custom = listed["result"]["tools"]
        .as_array()
        .unwrap()
        .iter()
        .find(|v| v["name"] == "mcp_probe_444")
        .expect("project tool must be exposed");
    assert_eq!(custom["inputSchema"]["required"], json!(["name"]));
    peer.send(json!({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"mcp_probe_444","arguments":{"name":"Sun"}}}));
    let result = peer.receive(|v| v["id"] == 3);
    assert_eq!(result["result"]["isError"], false);
    peer.send(json!({"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"mcp_probe_444","arguments":{"name":5}}}));
    let result = peer.receive(|v| v["id"] == 4);
    assert_eq!(result["result"]["isError"], true);
    let envelope: Value =
        serde_json::from_str(result["result"]["content"][0]["text"].as_str().unwrap()).unwrap();
    assert_eq!(envelope["errors"][0]["code"], "INVALID_ARGUMENT");
    assert_eq!(editor.calls.load(Ordering::SeqCst), 1);
    // The Editor stays connected; only its custom registry changes.
    while peer.messages.try_recv().is_ok() {}
    editor.published.store(false, Ordering::SeqCst);
    peer.receive(|v| v["method"] == "notifications/tools/list_changed");
    peer.send(json!({"jsonrpc":"2.0","id":5,"method":"tools/list"}));
    let result = peer.receive(|v| v["id"] == 5);
    assert!(!result["result"]["tools"]
        .as_array()
        .unwrap()
        .iter()
        .any(|v| v["name"] == "mcp_probe_444"));
}
