use serde_json::{json, Value};
use std::io::Write;
use std::process::{Command, Stdio};

fn cli() -> Command {
    let mut cmd = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
    cmd.env("UNITY_CLI_NO_AUTO_UPDATE", "1");
    cmd
}

#[test]
fn malformed_requests_do_not_end_session_or_execute_tools_before_initialization() {
    let mut child = cli()
        .args(["--port", "1", "--dry-run", "--output", "json", "mcp"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let mut input = child.stdin.take().unwrap();
    let request =
        |id, method: &str, params| json!({"jsonrpc":"2.0","id":id,"method":method,"params":params});
    writeln!(input, "broken json").unwrap();
    for value in [
        json!([]),
        request(
            1,
            "tools/call",
            json!({"name":"create_gameobject","arguments":{"name":"never"}}),
        ),
        request(2, "initialize", json!({})),
        request(
            3,
            "initialize",
            json!({"protocolVersion":"future","capabilities":{},"clientInfo":{"name":"test","version":"1"}}),
        ),
        json!({"jsonrpc":"2.0","method":"notifications/initialized"}),
        request(4, "tools/call", json!({"name":"not_a_tool"})),
        request(
            5,
            "tools/call",
            json!({"name":"create_gameobject","arguments":[]}),
        ),
        request(6, "tools/call", json!({})),
        request(7, "tools/list", json!({"cursor":"invalid"})),
        request(8, "ping", json!([])),
        request(
            9,
            "tools/call",
            json!({"name":"create_gameobject","arguments":{"name":"not-created"}}),
        ),
    ] {
        writeln!(input, "{value}").unwrap();
    }
    drop(input);
    let output = child.wait_with_output().unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let values: Vec<Value> = String::from_utf8(output.stdout)
        .unwrap()
        .lines()
        .map(|s| serde_json::from_str(s).unwrap())
        .collect();
    assert_eq!(values[0]["error"]["code"], -32700);
    assert_eq!(values[1]["error"]["code"], -32600);
    let by_id = |id| values.iter().find(|v| v["id"] == id).unwrap();
    assert_eq!(by_id(1)["error"]["code"], -32000);
    assert_eq!(by_id(2)["error"]["code"], -32602);
    assert_eq!(by_id(3)["result"]["protocolVersion"], "2025-11-25");
    for id in 4..=8 {
        assert_eq!(by_id(id)["error"]["code"], -32602, "request {id}");
    }
    assert_eq!(by_id(9)["result"]["isError"], false);
    let value: Value =
        serde_json::from_str(by_id(9)["result"]["content"][0]["text"].as_str().unwrap()).unwrap();
    assert_eq!(value["data"]["executed"], false);
}

#[test]
fn stdio_catalog_and_failure_contract_without_editor() {
    let mut child = cli()
        .args(["--port", "1", "mcp"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let requests = [
        json!({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}),
        json!({"jsonrpc":"2.0","method":"notifications/initialized"}),
        json!({"jsonrpc":"2.0","id":2,"method":"tools/list"}),
        json!({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"create_gameobject","arguments":{}}}),
        json!({"jsonrpc":"2.0","id":4,"method":"unknown"}),
        json!({"jsonrpc":"2.0","id":5,"method":"ping"}),
    ];
    let mut input = child.stdin.take().unwrap();
    for request in requests {
        writeln!(input, "{request}").unwrap();
    }
    drop(input);
    let out = child.wait_with_output().unwrap();
    assert!(
        out.status.success(),
        "{}",
        String::from_utf8_lossy(&out.stderr)
    );
    let messages: Vec<Value> = String::from_utf8(out.stdout)
        .unwrap()
        .lines()
        .map(|s| serde_json::from_str(s).unwrap())
        .collect();
    assert_eq!(messages.len(), 5);
    assert_eq!(
        messages[0]["result"]["capabilities"]["tools"]["listChanged"],
        true
    );
    let mut names: Vec<_> = messages[1]["result"]["tools"]
        .as_array()
        .unwrap()
        .iter()
        .map(|t| t["name"].as_str().unwrap().to_string())
        .collect();
    let list = cli()
        .args(["--port", "1", "tool", "list"])
        .output()
        .unwrap();
    let mut expected: Vec<_> = String::from_utf8(list.stdout)
        .unwrap()
        .lines()
        .map(str::to_string)
        .collect();
    names.sort();
    expected.sort();
    assert_eq!(names, expected);
    assert_eq!(messages[2]["result"]["isError"], true);
    let envelope: Value = serde_json::from_str(
        messages[2]["result"]["content"][0]["text"]
            .as_str()
            .unwrap(),
    )
    .unwrap();
    assert_eq!(envelope["success"], false);
    assert!(!envelope["errors"][0]["code"].as_str().unwrap().is_empty());
    assert_eq!(messages[3]["error"]["code"], -32601);
    assert_eq!(messages[4]["result"], json!({}));
}

#[test]
fn configure_all_clients_preserves_other_servers_and_dry_run() {
    for (client, relative, toml) in [
        ("claude-code", ".mcp.json", false),
        ("cursor", ".cursor/mcp.json", false),
        ("vscode", ".vscode/mcp.json", false),
        ("windsurf", ".codeium/windsurf/mcp_config.json", false),
        ("codex", ".codex/config.toml", true),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        let before = if toml {
            "# keep comment\nmodel = \"keep-model\"\n[mcp_servers.other]\ncommand = \"keep\"\n[mcp_servers.unity-cli]\ncommand = \"old\"\n[mcp_servers.unity-cli.env]\nCUSTOM = \"retain-value\"\n"
        } else if client == "vscode" {
            "{\"servers\":{\"other\":{\"command\":\"keep\"},\"unity-cli\":{\"command\":\"old\",\"env\":{\"CUSTOM\":\"retain-value\"}}},\"inputs\":[]}"
        } else {
            "{\"mcpServers\":{\"other\":{\"command\":\"keep\"},\"unity-cli\":{\"command\":\"old\",\"env\":{\"CUSTOM\":\"retain-value\"}}},\"extra\":true}"
        };
        std::fs::write(&path, before).unwrap();
        let run = |dry: bool| {
            let mut cmd = cli();
            cmd.current_dir(dir.path())
                .env("HOME", dir.path())
                .env("USERPROFILE", dir.path())
                .env("CODEX_HOME", dir.path().join(".codex"))
                .args(["mcp", "configure", client]);
            if client != "windsurf" {
                cmd.arg("--local");
            }
            if dry {
                cmd.arg("--dry-run");
            }
            cmd.output().unwrap()
        };
        let preview = run(true);
        assert!(
            preview.status.success(),
            "{client}: {}",
            String::from_utf8_lossy(&preview.stderr)
        );
        assert!(
            String::from_utf8_lossy(&preview.stdout)
                .contains(dir.path().canonicalize().unwrap().to_str().unwrap()),
            "{client}: preview must target the isolated configuration directory"
        );
        assert!(String::from_utf8_lossy(&preview.stdout).contains("+"));
        assert_eq!(std::fs::read_to_string(&path).unwrap(), before);
        let write = run(false);
        assert!(
            write.status.success(),
            "{client}: {}",
            String::from_utf8_lossy(&write.stderr)
        );
        let after = std::fs::read_to_string(&path).unwrap();
        if !toml {
            let configured: Value = serde_json::from_str(&after).unwrap();
            let key = if client == "vscode" {
                "servers"
            } else {
                "mcpServers"
            };
            assert_eq!(configured[key]["unity-cli"]["command"], "unity-cli");
        }
        assert!(after.contains("keep"));
        assert!(after.contains("unity-cli"));
        assert!(
            after.contains("retain-value"),
            "{client} erased existing server options"
        );
        if toml {
            assert!(after.contains("# keep comment"));
        }
        assert!(run(false).status.success());
        assert_eq!(std::fs::read_to_string(&path).unwrap(), after);
        #[cfg(unix)]
        {
            std::fs::remove_file(&path).unwrap();
            let target = dir.path().join("target");
            std::fs::write(&target, before).unwrap();
            std::os::unix::fs::symlink(&target, &path).unwrap();
            assert!(!run(false).status.success(), "{client} followed symlink");
            assert!(
                !run(true).status.success(),
                "{client} preview followed symlink"
            );
            assert_eq!(std::fs::read_to_string(target).unwrap(), before);
        }
    }
}
