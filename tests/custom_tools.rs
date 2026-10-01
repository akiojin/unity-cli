use serde_json::{json, Value};
use std::io::{Read, Write};
use std::net::TcpListener;
use std::process::{Command, Output};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Mutex,
};
use std::time::Duration;

fn custom_tool() -> Value {
    json!({
        "name":"spawn_light", "description":"Create a light", "source":"custom",
        "executor":"remote", "mutating":true,
        "params_schema": {"type":"object", "properties":{
            "name":{"type":"string"}, "intensity":{"type":"number", "default":1.0}
        }, "required":["name"], "additionalProperties":false},
        "response_schema":{}
    })
}

struct EditorStub {
    port: u16,
    root: tempfile::TempDir,
    requests: Arc<Mutex<Vec<Value>>>,
    stop: Arc<AtomicBool>,
    thread: Option<std::thread::JoinHandle<()>>,
}

impl EditorStub {
    fn new(discovery: Value, drop_custom_response: bool) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        listener.set_nonblocking(true).unwrap();
        let root = tempfile::tempdir().unwrap();
        std::fs::write(root.path().join("disable-daemon"), "not a directory").unwrap();
        let requests = Arc::new(Mutex::new(Vec::new()));
        let stop = Arc::new(AtomicBool::new(false));
        let calls = requests.clone();
        let done = stop.clone();
        let thread = std::thread::spawn(move || {
            while !done.load(Ordering::SeqCst) {
                let (mut socket, _) = match listener.accept() {
                    Ok(connection) => connection,
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        std::thread::sleep(Duration::from_millis(2));
                        continue;
                    }
                    Err(error) => panic!("accept: {error}"),
                };
                // macOS inherits the listener's nonblocking flag on accept.
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
                    calls.lock().unwrap().push(request.clone());
                    let response = if request["type"] == "list_tools" {
                        discovery.clone()
                    } else if drop_custom_response {
                        break;
                    } else {
                        json!({"status":"success", "result":{"created":request["params"]["name"]}})
                    };
                    let bytes = serde_json::to_vec(&response).unwrap();
                    socket
                        .write_all(&(bytes.len() as u32).to_be_bytes())
                        .unwrap();
                    socket.write_all(&bytes).unwrap();
                }
            }
        });
        Self {
            port,
            root,
            requests,
            stop,
            thread: Some(thread),
        }
    }

    fn standard() -> Self {
        Self::new(
            json!({"status":"success", "result":{"tools":[custom_tool()]}}),
            false,
        )
    }

    fn invoke(&self, args: &[&str]) -> Output {
        Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .args([
                "--host",
                "127.0.0.1",
                "--port",
                &self.port.to_string(),
                "--timeout-ms",
                "500",
                "--output",
                "json",
            ])
            .args(args)
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env(
                "UNITY_CLI_TOOLS_ROOT",
                self.root.path().join("disable-daemon"),
            )
            .env("UNITY_CLI_EDITORS_DIR", self.root.path().join("editors"))
            .env_remove("UNITY_CLI_AUTH_TOKEN_FILE")
            .output()
            .unwrap()
    }

    fn json(&self, args: &[&str], exit: i32) -> Value {
        let output = self.invoke(args);
        assert_eq!(
            output.status.code(),
            Some(exit),
            "stdout={} stderr={}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
        serde_json::from_slice(&output.stdout).unwrap()
    }

    fn invocations(&self) -> Vec<Value> {
        self.requests
            .lock()
            .unwrap()
            .iter()
            .filter(|r| r["type"] != "list_tools")
            .cloned()
            .collect()
    }
}

impl Drop for EditorStub {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::SeqCst);
        self.thread.take().unwrap().join().unwrap();
    }
}

#[test]
fn discovery_merges_schemas_and_sources_and_filters_custom_tools() {
    let editor = EditorStub::standard();
    let result = editor.json(&["tool", "list"], 0);
    let tools = result["data"].as_array().unwrap();
    let custom = tools
        .iter()
        .find(|tool| tool["name"] == "spawn_light")
        .expect("custom tool must be discovered");
    assert_eq!(custom["params_schema"], custom_tool()["params_schema"]);
    assert_eq!(custom["source"], "custom");
    assert_eq!(
        tools.iter().find(|tool| tool["name"] == "ping").unwrap()["source"],
        "builtin"
    );
    let filtered = editor.json(
        &[
            "tool",
            "list",
            "--query",
            "LIGHT",
            "--category",
            "custom",
            "--limit",
            "1",
        ],
        0,
    );
    assert_eq!(filtered["data"][0]["name"], "spawn_light");
    assert_eq!(filtered["data"].as_array().unwrap().len(), 1);
    assert!(editor.invocations().is_empty());
}

#[test]
fn names_only_and_compact_preserve_small_list_shapes() {
    let editor = EditorStub::standard();
    let names = editor.json(
        &["tool", "list", "--query", "spawn_light", "--names-only"],
        0,
    );
    assert_eq!(names["data"], json!(["spawn_light"]));
    let compact = editor.json(&["tool", "list", "--category", "custom", "--compact"], 0);
    assert_eq!(
        compact["data"],
        json!([{"name":"spawn_light", "description":"Create a light"}])
    );
    editor.json(&["tool", "list", "--names-only", "--compact"], 2);
}

#[test]
fn schema_and_all_call_spellings_support_custom_names() {
    let editor = EditorStub::standard();
    let schema = editor.json(&["tool", "schema", "spawn_light"], 0);
    assert_eq!(schema["data"], custom_tool());
    let all = editor.json(&["tool", "schema"], 0);
    assert!(all["data"]["tools"]
        .as_array()
        .unwrap()
        .iter()
        .any(|t| t["name"] == "spawn_light"));
    for prefix in [vec!["raw"], vec!["tool", "call"], vec!["tool"]] {
        let mut args = prefix;
        args.extend(["spawn_light", "--json", r#"{"name":"Sun"}"#]);
        let result = editor.json(&args, 0);
        assert_eq!(result["data"]["created"], "Sun");
    }
    assert_eq!(editor.invocations().len(), 3);
}

#[test]
fn invalid_custom_arguments_never_reach_the_method() {
    let editor = EditorStub::standard();
    for payload in [
        "{}",
        r#"{"name":42}"#,
        r#"{"name":"Sun","intensity":"bright"}"#,
        r#"{"name":"Sun","extra":true}"#,
    ] {
        let result = editor.json(&["raw", "spawn_light", "--json", payload], 2);
        assert_eq!(result["errors"][0]["code"], "INVALID_ARGUMENT");
    }
    assert!(editor.invocations().is_empty());
}

#[test]
fn dry_run_and_unknown_tools_never_invoke_a_custom_method() {
    let editor = EditorStub::standard();
    let result = editor.json(
        &[
            "--dry-run",
            "raw",
            "spawn_light",
            "--json",
            r#"{"name":"Sun"}"#,
        ],
        0,
    );
    assert_eq!(result["data"]["executed"], false);
    let unknown = editor.json(&["raw", "not_registered"], 2);
    assert_eq!(unknown["errors"][0]["code"], "INVALID_ARGUMENT");
    assert!(editor.invocations().is_empty());
}

#[test]
fn custom_method_is_not_retried_after_lost_response() {
    let editor = EditorStub::new(
        json!({"status":"success", "result":{"tools":[custom_tool()]}}),
        true,
    );
    editor.json(&["raw", "spawn_light", "--json", r#"{"name":"Sun"}"#], 6);
    assert_eq!(editor.invocations().len(), 1);
}

#[test]
fn old_bridges_keep_the_builtin_catalog_but_auth_errors_are_visible() {
    let old = EditorStub::new(
        json!({"status":"error", "error":"Unknown command", "code":"UNKNOWN_COMMAND"}),
        false,
    );
    let result = old.json(&["tool", "list", "--query", "ping"], 0);
    assert_eq!(result["data"][0]["name"], "ping");
    let unauthorized = EditorStub::new(
        json!({"status":"error", "error":"Missing token", "code":"UNAUTHORIZED"}),
        false,
    );
    let result = unauthorized.json(&["tool", "list"], 3);
    assert_eq!(result["errors"][0]["code"], "UNAUTHORIZED");
}

#[test]
fn custom_numeric_ranges_and_nullable_defaults_are_validated_before_execution() {
    let mut tool = custom_tool();
    tool["params_schema"] = json!({"type":"object", "properties":{
        "name":{"anyOf":[{"type":"string"},{"type":"null"}], "default":null},
        "count":{"type":"integer", "minimum":-2147483648i64, "maximum":2147483647i64},
        "longCount":{"type":"integer", "minimum":i64::MIN, "maximum":i64::MAX},
        "intensity":{"type":"number", "minimum":-3.4028234663852886e38, "maximum":3.4028234663852886e38}
    }, "additionalProperties":false});
    let editor = EditorStub::new(
        json!({"status":"success", "result":{"tools":[tool]}}),
        false,
    );
    for payload in [
        r#"{"name":12}"#,
        r#"{"count":2147483648}"#,
        r#"{"count":-2147483649}"#,
        r#"{"longCount":9223372036854775808}"#,
        r#"{"intensity":1e40}"#,
    ] {
        editor.json(&["raw", "spawn_light", "--json", payload], 2);
    }
    assert!(editor.invocations().is_empty());
    for payload in [
        "{}",
        r#"{"name":null}"#,
        r#"{"count":2147483647}"#,
        r#"{"longCount":9223372036854775807}"#,
    ] {
        editor.json(&["raw", "spawn_light", "--json", payload], 0);
    }
    assert_eq!(editor.invocations().len(), 4);
}

#[test]
fn batch_validates_all_custom_arguments_before_any_side_effect() {
    let editor = EditorStub::standard();
    let result = editor.json(&["batch", "--json",
        r#"[{"tool":"spawn_light","params":{"name":"First"}},{"tool":"spawn_light","params":{"name":42}}]"#], 2);
    assert_eq!(result["errors"][0]["code"], "INVALID_ARGUMENT");
    assert!(editor.invocations().is_empty());
    let result = editor.json(
        &[
            "--dry-run",
            "batch",
            "--json",
            r#"[{"tool":"spawn_light","params":{"name":"Sun"}}]"#,
        ],
        0,
    );
    assert_eq!(result["data"][0]["result"]["executed"], false);
    assert!(editor.invocations().is_empty());
}

#[test]
fn discovery_rejects_conflicting_or_malformed_editor_catalogs() {
    let mut conflict = custom_tool();
    conflict["name"] = json!("ping");
    let mut forged = custom_tool();
    forged["source"] = json!("builtin");
    let mut malformed = custom_tool();
    malformed["params_schema"] = json!({});
    for tools in [
        json!([conflict]),
        json!([forged]),
        json!([malformed]),
        json!([custom_tool(), custom_tool()]),
    ] {
        let editor = EditorStub::new(json!({"status":"success", "result":{"tools":tools}}), false);
        let result = editor.json(&["tool", "list"], 1);
        assert!(!result["success"].as_bool().unwrap());
        assert!(editor.invocations().is_empty());
    }
}
