//! Resolve credentials separately from discovery output so tokens never appear
//! in instance listings, target errors, or Debug/Serialize implementations.
use anyhow::{Context, Result};
use std::path::Path;

use crate::config::RuntimeConfig;
use crate::core::editor_discovery;

pub(crate) fn allow_unauthenticated() -> bool {
    std::env::var("UNITY_CLI_ALLOW_UNAUTHENTICATED").as_deref() == Ok("1")
}

pub(crate) fn warn_unauthenticated() {
    if allow_unauthenticated() {
        eprintln!("Warning: UNITY_CLI_ALLOW_UNAUTHENTICATED=1 is deprecated and will be removed in the next minor release; authentication will be required.");
    }
}

fn read_token(path: &Path) -> Result<Option<String>> {
    // Do not attach the file contents to any error or diagnostic.
    let bytes = std::fs::read(path).context("Unable to read Editor authentication file")?;
    let value: serde_json::Value =
        serde_json::from_slice(&bytes).context("Editor authentication file is not valid JSON")?;
    Ok(value
        .get("authToken")
        .and_then(|value| value.as_str())
        .filter(|token| !token.is_empty())
        .map(str::to_owned))
}

pub(crate) fn token_for(config: &RuntimeConfig) -> Result<Option<String>> {
    if let Some(path) = std::env::var_os("UNITY_CLI_AUTH_TOKEN_FILE") {
        return read_token(Path::new(&path));
    }
    let host = if config.host == "localhost" {
        "127.0.0.1"
    } else {
        &config.host
    };
    let editors = editor_discovery::discover();
    let matches: Vec<_> = editors
        .iter()
        .filter(|editor| {
            editor.is_live() && editor.lock.port == config.port && editor.lock.host == host
        })
        .collect();
    if matches.len() == 1 {
        return read_token(&matches[0].lockfile);
    }
    // The Bridge rejects a missing credential. Keeping this on the wire also
    // preserves its structured UNAUTHORIZED response and the opt-out handshake.
    Ok(None)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_exact_one_enables_legacy_opt_out() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        for (value, expected) in [
            (None, false),
            (Some("0"), false),
            (Some("true"), false),
            (Some(" 1"), false),
            (Some("1"), true),
        ] {
            match value {
                Some(value) => std::env::set_var("UNITY_CLI_ALLOW_UNAUTHENTICATED", value),
                None => std::env::remove_var("UNITY_CLI_ALLOW_UNAUTHENTICATED"),
            }
            assert_eq!(allow_unauthenticated(), expected);
        }
    }

    #[test]
    fn explicit_mounted_lockfile_and_invalid_file_errors_do_not_expose_token() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let directory = tempfile::tempdir().unwrap();
        let file = directory.path().join("editor.json");
        std::env::set_var("UNITY_CLI_AUTH_TOKEN_FILE", &file);
        let config = RuntimeConfig {
            host: "remote-host".into(),
            port: 6400,
            timeout: std::time::Duration::from_secs(1),
        };
        std::fs::write(&file, r#"{"authToken":"secret"}"#).unwrap();
        assert_eq!(token_for(&config).unwrap().as_deref(), Some("secret"));
        std::fs::write(&file, "secret malformed JSON").unwrap();
        assert!(!format!("{:#}", token_for(&config).unwrap_err()).contains("secret"));
        std::fs::remove_file(&file).unwrap();
        assert!(token_for(&config).is_err());
    }

    #[test]
    fn credentials_are_endpoint_scoped_and_absent_from_discovery_output() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        let directory = editor_discovery::editors_dir().unwrap();
        std::fs::create_dir_all(&directory).unwrap();
        let mut value = serde_json::json!({
            "pid": std::process::id(), "projectPath": "/test", "host": "127.0.0.1",
            "port": 6540, "authToken": "private-secret",
            "heartbeatAt": std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH).unwrap().as_secs_f64()
        });
        let file = directory.join("editor.json");
        std::fs::write(&file, value.to_string()).unwrap();
        let mut config = RuntimeConfig {
            host: "other-host".into(),
            port: 6540,
            timeout: std::time::Duration::from_secs(1),
        };
        assert!(token_for(&config).unwrap().is_none());
        config.host = "localhost".into();
        config.port = 6541;
        assert!(token_for(&config).unwrap().is_none());
        config.port = 6540;
        assert_eq!(
            token_for(&config).unwrap().as_deref(),
            Some("private-secret")
        );
        let editors = editor_discovery::discover();
        assert!(!format!("{editors:?}").contains("private-secret"));
        assert!(!editors[0]
            .candidate_json()
            .to_string()
            .contains("private-secret"));
        assert!(!serde_json::to_string(&editors[0].lock)
            .unwrap()
            .contains("private-secret"));
        std::fs::write(directory.join("duplicate.json"), value.to_string()).unwrap();
        assert!(token_for(&config).unwrap().is_none());
        std::fs::remove_file(directory.join("duplicate.json")).unwrap();
        value["pid"] = serde_json::json!(0);
        std::fs::write(&file, value.to_string()).unwrap();
        assert!(token_for(&config).unwrap().is_none());
    }
}
