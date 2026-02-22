use std::env;
use std::time::Duration;

use anyhow::{bail, Result};

use crate::cli::Cli;

const DEFAULT_HOST: &str = "localhost";
const DEFAULT_PORT: u16 = 6400;
const DEFAULT_TIMEOUT_MS: u64 = 30_000;
const LEGACY_ENV_PREFIX: &str = concat!("UNITY_", "M", "CP_");

#[derive(Debug, Clone)]
pub struct RuntimeConfig {
    pub host: String,
    pub port: u16,
    pub timeout: Duration,
}

impl RuntimeConfig {
    pub fn from_cli(cli: &Cli) -> Result<Self> {
        fail_if_legacy_env_set()?;

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
    read_env(&["UNITY_CLI_HOST"]).unwrap_or_else(|| DEFAULT_HOST.to_string())
}

fn default_port() -> u16 {
    read_env_u16("UNITY_CLI_PORT").unwrap_or(DEFAULT_PORT)
}

fn default_timeout_ms() -> u64 {
    read_env_u64("UNITY_CLI_TIMEOUT_MS").unwrap_or(DEFAULT_TIMEOUT_MS)
}

fn fail_if_legacy_env_set() -> Result<()> {
    for (key, _) in env::vars_os() {
        if let Some(key_str) = key.to_str() {
            if key_str.starts_with(LEGACY_ENV_PREFIX) {
                bail!(
                    "Environment variable '{}' is no longer supported. Use UNITY_CLI_* variables only.",
                    key_str,
                );
            }
        }
    }

    Ok(())
}
fn read_env(keys: &[&str]) -> Option<String> {
    for key in keys {
        if let Ok(value) = env::var(key) {
            let trimmed = value.trim().to_string();
            if !trimmed.is_empty() {
                return Some(trimmed);
            }
        }
    }
    None
}

fn read_env_u16(key: &str) -> Option<u16> {
    read_env(&[key])
        .and_then(|value| value.parse::<u16>().ok())
        .filter(|port| *port > 0)
}

fn read_env_u64(key: &str) -> Option<u64> {
    read_env(&[key])
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
    fn returns_none_when_no_keys_set() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_HOST");
        let value = read_env(&["UNITY_CLI_HOST"]);
        assert!(value.is_none());
    }

    #[test]
    fn empty_value_is_ignored() {
        with_env_vars(&[("UNITY_CLI_HOST", "  ")], || {
            let value = read_env(&["UNITY_CLI_HOST"]);
            assert!(value.is_none());
        });
    }

    #[test]
    fn u16_env_parses_correctly() {
        with_env_vars(&[("UNITY_CLI_PORT", "7000")], || {
            let value = read_env_u16("UNITY_CLI_PORT");
            assert_eq!(value, Some(7000));
        });
    }

    #[test]
    fn u16_env_rejects_zero() {
        with_env_vars(&[("UNITY_CLI_PORT", "0")], || {
            let value = read_env_u16("UNITY_CLI_PORT");
            assert!(value.is_none());
        });
    }

    #[test]
    fn u64_env_parses_correctly() {
        with_env_vars(&[("UNITY_CLI_TIMEOUT_MS", "5000")], || {
            let value = read_env_u64("UNITY_CLI_TIMEOUT_MS");
            assert_eq!(value, Some(5000));
        });
    }

    #[test]
    fn u64_env_rejects_zero() {
        with_env_vars(&[("UNITY_CLI_TIMEOUT_MS", "0")], || {
            let value = read_env_u64("UNITY_CLI_TIMEOUT_MS");
            assert!(value.is_none());
        });
    }

    #[test]
    fn default_host_returns_localhost_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_HOST");
        assert_eq!(default_host(), "localhost");
    }

    #[test]
    fn default_port_returns_6400_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_PORT");
        assert_eq!(default_port(), 6400);
    }

    #[test]
    fn default_timeout_returns_30000_without_env() {
        let _lock = ENV_LOCK.lock().unwrap();
        env::remove_var("UNITY_CLI_TIMEOUT_MS");
        assert_eq!(default_timeout_ms(), 30_000);
    }

    #[test]
    fn fails_when_legacy_alias_is_set() {
        for legacy_key in [concat!("UNITY_", "M", "CP_", "TEST_KEY")] {
            let _lock = ENV_LOCK.lock().unwrap();
            env::remove_var("UNITY_CLI_HOST");
            env::remove_var("UNITY_CLI_PORT");
            env::remove_var("UNITY_CLI_TIMEOUT_MS");
            env::set_var(legacy_key, "legacy-value");

            let err = fail_if_legacy_env_set().expect_err("legacy env should be rejected");
            assert!(
                err.to_string().contains(legacy_key),
                "error should mention legacy key"
            );
            assert!(err.to_string().contains("UNITY_CLI_*"));

            env::remove_var(legacy_key);
        }
    }
}
