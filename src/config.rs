use std::env;
use std::time::Duration;

use anyhow::Result;

use crate::cli::Cli;

const DEFAULT_HOST: &str = "localhost";
const DEFAULT_PORT: u16 = 6400;
const DEFAULT_TIMEOUT_MS: u64 = 30_000;

#[derive(Debug, Clone)]
pub struct RuntimeConfig {
    pub host: String,
    pub port: u16,
    pub timeout: Duration,
}

impl RuntimeConfig {
    pub fn from_cli(cli: &Cli) -> Result<Self> {
        let host = cli.host.clone().unwrap_or_else(default_host);
        let port = cli.port.unwrap_or_else(default_port);
        let timeout_ms = cli.timeout_ms.unwrap_or_else(default_timeout_ms);

        Ok(Self {
            host,
            port,
            timeout: Duration::from_millis(timeout_ms),
        })
    }
}

fn default_host() -> String {
    read_env_with_deprecation(
        &["UNITY_CLI_HOST"],
        &[("UNITY_MCP_MCP_HOST", "UNITY_CLI_HOST"), ("UNITY_MCP_UNITY_HOST", "UNITY_CLI_HOST")],
    )
    .unwrap_or_else(|| DEFAULT_HOST.to_string())
}

fn default_port() -> u16 {
    read_env_u16_with_deprecation(
        &["UNITY_CLI_PORT"],
        &[("UNITY_MCP_PORT", "UNITY_CLI_PORT")],
    )
    .unwrap_or(DEFAULT_PORT)
}

fn default_timeout_ms() -> u64 {
    read_env_u64_with_deprecation(
        &["UNITY_CLI_TIMEOUT_MS"],
        &[
            ("UNITY_MCP_COMMAND_TIMEOUT", "UNITY_CLI_TIMEOUT_MS"),
            ("UNITY_MCP_CONNECT_TIMEOUT", "UNITY_CLI_TIMEOUT_MS"),
        ],
    )
    .unwrap_or(DEFAULT_TIMEOUT_MS)
}

/// Read an environment variable from primary keys first, then deprecated keys.
/// If a deprecated key is found, emit a warning via `tracing::warn!`.
fn read_env_with_deprecation(
    primary_keys: &[&str],
    deprecated_keys: &[(&str, &str)],
) -> Option<String> {
    for key in primary_keys {
        if let Ok(value) = env::var(key) {
            let trimmed = value.trim().to_string();
            if !trimmed.is_empty() {
                return Some(trimmed);
            }
        }
    }

    for (deprecated_key, recommended_key) in deprecated_keys {
        if let Ok(value) = env::var(deprecated_key) {
            let trimmed = value.trim().to_string();
            if !trimmed.is_empty() {
                tracing::warn!(
                    "Environment variable '{}' is deprecated and will be removed in v1.0.0. Use '{}' instead.",
                    deprecated_key,
                    recommended_key,
                );
                return Some(trimmed);
            }
        }
    }

    None
}

/// Read a `u16` environment variable with deprecation warning support.
fn read_env_u16_with_deprecation(
    primary_keys: &[&str],
    deprecated_keys: &[(&str, &str)],
) -> Option<u16> {
    read_env_with_deprecation(primary_keys, deprecated_keys)
        .and_then(|value| value.parse::<u16>().ok())
        .filter(|port| *port > 0)
}

/// Read a `u64` environment variable with deprecation warning support.
fn read_env_u64_with_deprecation(
    primary_keys: &[&str],
    deprecated_keys: &[(&str, &str)],
) -> Option<u64> {
    read_env_with_deprecation(primary_keys, deprecated_keys)
        .and_then(|value| value.parse::<u64>().ok())
        .filter(|timeout| *timeout > 0)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    // Serialize env-var tests so they don't interfere with each other.
    static ENV_LOCK: Mutex<()> = Mutex::new(());

    /// Helper to temporarily set env vars and clean them up after the closure runs.
    fn with_env_vars<F, R>(vars: &[(&str, &str)], f: F) -> R
    where
        F: FnOnce() -> R,
    {
        let _lock = ENV_LOCK.lock().unwrap();
        for (key, value) in vars {
            env::set_var(key, value);
        }
        let result = f();
        for (key, _) in vars {
            env::remove_var(key);
        }
        result
    }

    #[test]
    fn primary_key_takes_precedence_over_deprecated() {
        with_env_vars(
            &[("UNITY_CLI_HOST", "primary-host"), ("UNITY_MCP_MCP_HOST", "deprecated-host")],
            || {
                let value = read_env_with_deprecation(
                    &["UNITY_CLI_HOST"],
                    &[("UNITY_MCP_MCP_HOST", "UNITY_CLI_HOST")],
                );
                assert_eq!(value.as_deref(), Some("primary-host"));
            },
        );
    }

    #[test]
    fn deprecated_key_returns_value_when_primary_absent() {
        with_env_vars(&[("UNITY_MCP_MCP_HOST", "deprecated-host")], || {
            let value = read_env_with_deprecation(
                &["UNITY_CLI_HOST"],
                &[("UNITY_MCP_MCP_HOST", "UNITY_CLI_HOST")],
            );
            assert_eq!(value.as_deref(), Some("deprecated-host"));
        });
    }

    #[test]
    fn returns_none_when_no_keys_set() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_HOST");
        env::remove_var("UNITY_MCP_MCP_HOST");
        let value = read_env_with_deprecation(
            &["UNITY_CLI_HOST"],
            &[("UNITY_MCP_MCP_HOST", "UNITY_CLI_HOST")],
        );
        assert!(value.is_none());
    }

    #[test]
    fn empty_value_is_ignored() {
        with_env_vars(&[("UNITY_CLI_HOST", "  ")], || {
            let value = read_env_with_deprecation(
                &["UNITY_CLI_HOST"],
                &[("UNITY_MCP_MCP_HOST", "UNITY_CLI_HOST")],
            );
            assert!(value.is_none());
        });
    }

    #[test]
    fn u16_with_deprecation_parses_correctly() {
        with_env_vars(&[("UNITY_MCP_PORT", "7000")], || {
            let value = read_env_u16_with_deprecation(
                &["UNITY_CLI_PORT"],
                &[("UNITY_MCP_PORT", "UNITY_CLI_PORT")],
            );
            assert_eq!(value, Some(7000));
        });
    }

    #[test]
    fn u16_with_deprecation_rejects_zero() {
        with_env_vars(&[("UNITY_CLI_PORT", "0")], || {
            let value = read_env_u16_with_deprecation(
                &["UNITY_CLI_PORT"],
                &[("UNITY_MCP_PORT", "UNITY_CLI_PORT")],
            );
            assert!(value.is_none());
        });
    }

    #[test]
    fn u64_with_deprecation_parses_correctly() {
        with_env_vars(&[("UNITY_MCP_COMMAND_TIMEOUT", "5000")], || {
            let value = read_env_u64_with_deprecation(
                &["UNITY_CLI_TIMEOUT_MS"],
                &[("UNITY_MCP_COMMAND_TIMEOUT", "UNITY_CLI_TIMEOUT_MS")],
            );
            assert_eq!(value, Some(5000));
        });
    }

    #[test]
    fn u64_with_deprecation_rejects_zero() {
        with_env_vars(&[("UNITY_CLI_TIMEOUT_MS", "0")], || {
            let value = read_env_u64_with_deprecation(
                &["UNITY_CLI_TIMEOUT_MS"],
                &[("UNITY_MCP_COMMAND_TIMEOUT", "UNITY_CLI_TIMEOUT_MS")],
            );
            assert!(value.is_none());
        });
    }

    #[test]
    fn default_host_returns_localhost_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_HOST");
        env::remove_var("UNITY_MCP_MCP_HOST");
        env::remove_var("UNITY_MCP_UNITY_HOST");
        assert_eq!(default_host(), "localhost");
    }

    #[test]
    fn default_port_returns_6400_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_PORT");
        env::remove_var("UNITY_MCP_PORT");
        assert_eq!(default_port(), 6400);
    }

    #[test]
    fn default_timeout_returns_30000_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_TIMEOUT_MS");
        env::remove_var("UNITY_MCP_COMMAND_TIMEOUT");
        env::remove_var("UNITY_MCP_CONNECT_TIMEOUT");
        assert_eq!(default_timeout_ms(), 30_000);
    }
}
