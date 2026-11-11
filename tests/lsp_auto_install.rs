//! Exercise the production CLI and detached installer with an isolated Unicode tools root.
use serde_json::{json, Value};
use std::fs;
use std::net::TcpListener;
use std::path::PathBuf;
use std::process::Command;
use std::time::{Duration, Instant};

struct Fixture {
    _temp: tempfile::TempDir,
    home: PathBuf,
    install: PathBuf,
    proxy: Option<String>,
}

impl Fixture {
    fn new(offline: bool) -> Self {
        let temp = tempfile::tempdir().unwrap();
        let home = temp.path().join("日本語 ユーザー");
        fs::create_dir_all(home.join("Assets")).unwrap();
        fs::write(home.join("Assets/Player.cs"), "class Player {}\n").unwrap();
        let rid = match (std::env::consts::OS, std::env::consts::ARCH) {
            ("windows", "aarch64") => "win-arm64",
            ("windows", _) => "win-x64",
            ("macos", "aarch64") => "osx-arm64",
            ("macos", _) => "osx-x64",
            (_, "aarch64") => "linux-arm64",
            _ => "linux-x64",
        };
        let install = home.join(".unity/tools/csharp-lsp").join(rid);
        let proxy = offline.then(|| {
            let listener = TcpListener::bind("127.0.0.1:0").unwrap();
            format!("http://{}", listener.local_addr().unwrap())
        });
        Self {
            _temp: temp,
            home,
            install,
            proxy,
        }
    }

    fn call(&self, args: &[&str]) -> Value {
        let mut command = Command::new(env!("CARGO_BIN_EXE_unity-cli"));
        command
            .current_dir(&self.home)
            .env("UNITY_CLI_TOOLS_ROOT", self.home.join(".unity/tools"))
            .env("UNITY_CLI_CACHE_ROOT", self.home.join(".unity/cache"))
            .env("UNITY_CLI_REGISTRY_PATH", self.home.join("registry.json"))
            .env("UNITY_CLI_NO_AUTO_UPDATE", "1")
            .env_remove("UNITY_CLI_TEST_SKIP_MANAGED_UPDATE")
            .env_remove("UNITY_CLI_LSP_MODE")
            .args(["--output", "json", "--project-path"])
            .arg(&self.home)
            .args(args);
        if let Some(proxy) = &self.proxy {
            command
                .env("HTTP_PROXY", proxy)
                .env("HTTPS_PROXY", proxy)
                .env("ALL_PROXY", proxy)
                .env("NO_PROXY", "")
                .env_remove("GH_TOKEN")
                .env_remove("GITHUB_TOKEN");
        }
        let output = command.output().unwrap();
        serde_json::from_slice(&output.stdout).unwrap_or_else(|error| panic!("{error}: {output:?}"))
    }

    fn search(&self) -> Value {
        self.call(&[
            "raw",
            "get_symbols",
            "--json",
            r#"{"path":"Assets/Player.cs"}"#,
        ])
    }

    fn wait_for(&self, name: &str) {
        let deadline =
            Instant::now() + Duration::from_secs(if self.proxy.is_some() { 8 } else { 180 });
        while !self.install.join(name).exists() {
            assert!(
                Instant::now() < deadline,
                "installer did not produce {name}: {:?}",
                self.call(&["lsp", "doctor"])
            );
            std::thread::sleep(Duration::from_millis(100));
        }
    }
}

#[test]
fn failed_background_install_is_diagnosed_and_writes_offer_recovery() {
    let fixture = Fixture::new(true);
    let search = fixture.search();
    assert_eq!(
        search["data"]["fallbackReason"], "lsp_not_installed",
        "{search}"
    );
    fixture.wait_for(".auto-install-error");
    let doctor = fixture.call(&["lsp", "doctor"]);
    assert_eq!(doctor["data"]["autoInstall"]["state"], "failed", "{doctor}");
    assert!(doctor["data"]["autoInstall"]["error"]
        .as_str()
        .unwrap()
        .contains("Failed to fetch"));
    assert!(doctor["data"]["autoInstall"]["recovery"]
        .as_str()
        .unwrap()
        .contains("unity-cli lsp install"));
    assert!(!fixture.install.join(".auto-install-requested").exists());

    let write = fixture.call(&[
        "raw",
        "write_csharp_file",
        "--json",
        &json!({"relative":"Assets/Player.cs", "newText":"class Updated {}", "apply":true})
            .to_string(),
    ]);
    assert_eq!(write["success"], false, "{write}");
    assert!(
        write["errors"][0]["message"]
            .as_str()
            .unwrap()
            .contains("unity-cli lsp install"),
        "{write}"
    );
    assert_eq!(
        fs::read_to_string(fixture.home.join("Assets/Player.cs")).unwrap(),
        "class Player {}\n"
    );
}

#[test]
#[ignore = "downloads the real release; run explicitly for the acceptance matrix"]
fn release_background_install_survives_cli_exit_in_unicode_directory() {
    let fixture = Fixture::new(false);
    let start = Instant::now();
    let search = fixture.search();
    assert_eq!(
        search["data"]["fallbackReason"], "lsp_not_installed",
        "{search}"
    );
    assert!(
        start.elapsed() < Duration::from_secs(10),
        "initial search blocked on download"
    );
    fixture.wait_for("VERSION");
    let binary = fixture.install.join(if cfg!(windows) {
        "server.exe"
    } else {
        "server"
    });
    assert!(fs::metadata(binary).unwrap().len() > 1_000_000);
    let deadline = Instant::now() + Duration::from_secs(5);
    while fixture.install.join(".auto-install-requested").exists() {
        assert!(
            Instant::now() < deadline,
            "completed install retained marker"
        );
        std::thread::sleep(Duration::from_millis(50));
    }
    let doctor = fixture.call(&["lsp", "doctor"]);
    assert_eq!(doctor["data"]["binaryExists"], true, "{doctor}");
    assert_eq!(
        doctor["data"]["autoInstall"]["state"], "installed",
        "{doctor}"
    );
    assert!(!fixture.install.join(".auto-install-error").exists());
    println!(
        "RID/path/version: {} / {}",
        fixture.install.display(),
        doctor["data"]["localVersion"]
    );
}
