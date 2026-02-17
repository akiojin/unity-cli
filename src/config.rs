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
    read_env_string(&[
        "UNITY_CLI_HOST",
        "UNITY_MCP_MCP_HOST",
        "UNITY_MCP_UNITY_HOST",
    ])
    .unwrap_or_else(|| DEFAULT_HOST.to_string())
}

fn default_port() -> u16 {
    read_env_u16(&["UNITY_CLI_PORT", "UNITY_MCP_PORT"]).unwrap_or(DEFAULT_PORT)
}

fn default_timeout_ms() -> u64 {
    read_env_u64(&[
        "UNITY_CLI_TIMEOUT_MS",
        "UNITY_MCP_COMMAND_TIMEOUT",
        "UNITY_MCP_CONNECT_TIMEOUT",
    ])
    .unwrap_or(DEFAULT_TIMEOUT_MS)
}

fn read_env_string(keys: &[&str]) -> Option<String> {
    keys.iter()
        .find_map(|key| env::var(key).ok())
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
}

fn read_env_u16(keys: &[&str]) -> Option<u16> {
    keys.iter()
        .find_map(|key| env::var(key).ok())
        .and_then(|value| value.trim().parse::<u16>().ok())
        .filter(|port| *port > 0)
}

fn read_env_u64(keys: &[&str]) -> Option<u64> {
    keys.iter()
        .find_map(|key| env::var(key).ok())
        .and_then(|value| value.trim().parse::<u64>().ok())
        .filter(|timeout| *timeout > 0)
}
