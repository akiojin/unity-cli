//! Windows bind failures must remain actionable at the installed CLI boundary.
#![cfg(windows)]

use serde_json::Value;
use std::net::TcpListener;
use std::process::{Command, Output};

#[test]
fn status_and_doctor_explain_bind_failure_and_clear_it_after_restart() {
    let tools = tempfile::tempdir().unwrap();
    let rid = if cfg!(target_arch = "aarch64") {
        "win-arm64"
    } else {
        "win-x64"
    };
    let lsp = tools.path().join("csharp-lsp").join(rid);
    std::fs::create_dir_all(&lsp).unwrap();
    // Serving ping/status does not execute the LSP. Keep this test offline.
    std::fs::write(lsp.join("server.exe"), b"unused by this test").unwrap();
    let occupied = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = occupied.local_addr().unwrap().port().to_string();
    let proxy = TcpListener::bind("127.0.0.1:0").unwrap();
    let offline_proxy = format!("http://{}", proxy.local_addr().unwrap());
    drop(proxy);

    let invoke = |args: &[&str]| -> Output {
        Command::new(env!("CARGO_BIN_EXE_unity-cli"))
            .env("UNITY_CLI_TOOLS_ROOT", tools.path())
            .env("UNITY_CLI_LSPD_PORT", &port)
            .env("UNITY_CLI_LSPD_IDLE_TIMEOUT", "1")
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env("HTTPS_PROXY", &offline_proxy)
            .env("HTTP_PROXY", &offline_proxy)
            .env("ALL_PROXY", &offline_proxy)
            .env("NO_PROXY", "")
            .env_remove("GH_TOKEN")
            .env_remove("GITHUB_TOKEN")
            .args(args)
            .args(["--output", "json"])
            .output()
            .unwrap()
    };
    let data = |output: Output| -> Value {
        assert!(output.status.success(), "{output:?}");
        let envelope: Value = serde_json::from_slice(&output.stdout).unwrap();
        assert_eq!(envelope["success"], true);
        envelope["data"].clone()
    };

    let failed = invoke(&["lspd", "serve"]);
    assert!(!failed.status.success(), "{failed:?}");
    drop(occupied);
    for args in [["lspd", "status"], ["lsp", "doctor"]] {
        let value = data(invoke(&args));
        let diagnostic = &value["startupError"];
        assert_eq!(diagnostic["endpoint"], format!("127.0.0.1:{port}"));
        assert_eq!(diagnostic["osError"], 10048);
        assert!(diagnostic["recovery"]
            .as_str()
            .unwrap()
            .contains("UNITY_CLI_LSPD_PORT"));
        assert!(diagnostic["recovery"]
            .as_str()
            .unwrap()
            .contains("unity-cli lspd start"));
    }

    let restarted = invoke(&["lspd", "serve"]);
    assert!(restarted.status.success(), "{restarted:?}");
    for args in [["lspd", "status"], ["lsp", "doctor"]] {
        assert!(data(invoke(&args)).get("startupError").is_none());
    }
}
