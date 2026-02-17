use std::fs;
use std::path::PathBuf;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, Context, Result};
use serde::{Deserialize, Serialize};
use tokio::net::TcpStream;
use tokio::time::timeout;

#[derive(Debug, Clone, Serialize, Deserialize)]
struct InstanceRecord {
    id: String,
    host: String,
    port: u16,
}

#[derive(Debug, Default, Serialize, Deserialize)]
struct Registry {
    #[serde(default)]
    active_id: Option<String>,
    #[serde(default)]
    entries: Vec<InstanceRecord>,
}

#[derive(Debug, Serialize)]
pub struct InstanceStatus {
    pub id: String,
    pub host: String,
    pub port: u16,
    pub status: String,
    pub last_checked_at: String,
    pub active: bool,
}

#[derive(Debug, Serialize)]
pub struct SetActiveResult {
    pub active_id: String,
    pub previous_id: Option<String>,
}

pub async fn list_instances(
    host: &str,
    ports: &[u16],
    timeout_ms: u64,
) -> Result<Vec<InstanceStatus>> {
    let mut registry = load_registry()?;

    for port in ports {
        let id = format!("{host}:{port}");
        if registry.entries.iter().all(|entry| entry.id != id) {
            registry.entries.push(InstanceRecord {
                id,
                host: host.to_string(),
                port: *port,
            });
        }
    }

    if registry.entries.is_empty() {
        registry.entries.push(InstanceRecord {
            id: format!("{}:{}", host, 6400),
            host: host.to_string(),
            port: 6400,
        });
    }

    registry.entries.sort_by(|a, b| a.id.cmp(&b.id));

    let mut statuses = Vec::with_capacity(registry.entries.len());
    let timeout = Duration::from_millis(timeout_ms);
    let checked_at = unix_timestamp();

    for entry in &registry.entries {
        let up = can_connect(&entry.host, entry.port, timeout).await;
        statuses.push(InstanceStatus {
            id: entry.id.clone(),
            host: entry.host.clone(),
            port: entry.port,
            status: if up {
                "up".to_string()
            } else {
                "down".to_string()
            },
            last_checked_at: checked_at.clone(),
            active: registry.active_id.as_deref() == Some(&entry.id),
        });
    }

    save_registry(&registry)?;
    Ok(statuses)
}

pub async fn set_active_instance(id: &str, timeout_ms: u64) -> Result<SetActiveResult> {
    let mut registry = load_registry()?;

    if registry.entries.iter().all(|entry| entry.id != id) {
        let (host, port) = parse_id(id)?;
        registry.entries.push(InstanceRecord {
            id: id.to_string(),
            host,
            port,
        });
    }

    let target = registry
        .entries
        .iter()
        .find(|entry| entry.id == id)
        .ok_or_else(|| anyhow!("Instance not found: {id}"))?;

    let ok = can_connect(&target.host, target.port, Duration::from_millis(timeout_ms)).await;
    if !ok {
        return Err(anyhow!("Instance unreachable: {id}"));
    }

    let previous_id = registry.active_id.clone();
    registry.active_id = Some(id.to_string());
    save_registry(&registry)?;

    Ok(SetActiveResult {
        active_id: id.to_string(),
        previous_id,
    })
}

fn parse_id(id: &str) -> Result<(String, u16)> {
    let (host, port_str) = id
        .split_once(':')
        .ok_or_else(|| anyhow!("Invalid instance id: {id}. Expected host:port"))?;
    let port = port_str
        .parse::<u16>()
        .with_context(|| format!("Invalid port in instance id: {id}"))?;
    if host.trim().is_empty() {
        return Err(anyhow!("Invalid host in instance id: {id}"));
    }
    Ok((host.to_string(), port))
}

async fn can_connect(host: &str, port: u16, timeout_duration: Duration) -> bool {
    matches!(
        timeout(timeout_duration, TcpStream::connect((host, port))).await,
        Ok(Ok(_))
    )
}

fn registry_path() -> Result<PathBuf> {
    if let Ok(raw_path) = std::env::var("UNITY_CLI_REGISTRY_PATH") {
        let trimmed = raw_path.trim();
        if !trimmed.is_empty() {
            let path = PathBuf::from(trimmed);
            if let Some(parent) = path.parent() {
                fs::create_dir_all(parent).with_context(|| {
                    format!("Failed to create registry parent dir: {}", parent.display())
                })?;
            }
            return Ok(path);
        }
    }

    let base_dir = dirs::config_dir()
        .or_else(|| dirs::home_dir().map(|home| home.join(".config")))
        .ok_or_else(|| anyhow!("Unable to resolve config directory"))?;

    let dir = base_dir.join("unity-cli");
    fs::create_dir_all(&dir)
        .with_context(|| format!("Failed to create config dir: {}", dir.display()))?;
    Ok(dir.join("instances.json"))
}

fn load_registry() -> Result<Registry> {
    let path = registry_path()?;
    if !path.exists() {
        return Ok(Registry::default());
    }

    let raw = fs::read_to_string(&path)
        .with_context(|| format!("Failed to read registry file: {}", path.display()))?;

    let mut registry: Registry = serde_json::from_str(&raw)
        .with_context(|| format!("Failed to parse registry file: {}", path.display()))?;

    for entry in &mut registry.entries {
        if entry.id.trim().is_empty() {
            entry.id = format!("{}:{}", entry.host, entry.port);
        }
    }

    Ok(registry)
}

fn save_registry(registry: &Registry) -> Result<()> {
    let path = registry_path()?;
    let content = serde_json::to_string_pretty(registry)?;
    fs::write(&path, content)
        .with_context(|| format!("Failed to write registry file: {}", path.display()))
}

fn unix_timestamp() -> String {
    match SystemTime::now().duration_since(UNIX_EPOCH) {
        Ok(duration) => duration.as_secs().to_string(),
        Err(_) => "0".to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::{list_instances, parse_id, set_active_instance};
    use std::path::PathBuf;
    use std::sync::{Mutex, OnceLock};
    use std::time::{Duration, SystemTime, UNIX_EPOCH};
    use tokio::net::TcpListener;

    fn env_lock() -> &'static Mutex<()> {
        static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
        LOCK.get_or_init(|| Mutex::new(()))
    }

    fn temp_registry_path(label: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or(Duration::from_secs(0))
            .as_nanos();
        let mut path = std::env::temp_dir();
        path.push(format!("unity-cli-{label}-{nanos}.json"));
        path
    }

    #[test]
    fn parse_id_validates_shape() {
        let (host, port) = parse_id("localhost:6400").expect("host:port should parse");
        assert_eq!(host, "localhost");
        assert_eq!(port, 6400);

        let err = parse_id("invalid").expect_err("missing separator should fail");
        assert!(format!("{err:#}").contains("Expected host:port"));
    }

    #[tokio::test]
    async fn list_instances_reports_up_for_reachable_port() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let registry_path = temp_registry_path("instances-up");
        std::env::set_var("UNITY_CLI_REGISTRY_PATH", &registry_path);

        let listener = TcpListener::bind(("127.0.0.1", 0))
            .await
            .expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should have local addr")
            .port();
        let accept_task = tokio::spawn(async move {
            let _ = listener.accept().await;
        });

        let statuses = list_instances("127.0.0.1", &[port], 300)
            .await
            .expect("list should succeed");
        assert_eq!(statuses.len(), 1);
        assert_eq!(statuses[0].status, "up");

        accept_task.abort();
        std::env::remove_var("UNITY_CLI_REGISTRY_PATH");
        let _ = std::fs::remove_file(&registry_path);
    }

    #[tokio::test]
    async fn set_active_fails_for_unreachable_instance() {
        let _guard = env_lock().lock().expect("lock should succeed");
        let registry_path = temp_registry_path("instances-down");
        std::env::set_var("UNITY_CLI_REGISTRY_PATH", &registry_path);

        let listener = TcpListener::bind(("127.0.0.1", 0))
            .await
            .expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should have local addr")
            .port();
        drop(listener);

        let id = format!("127.0.0.1:{port}");
        let err = set_active_instance(&id, 100)
            .await
            .expect_err("set-active should fail for down port");
        assert!(format!("{err:#}").contains("unreachable"));

        std::env::remove_var("UNITY_CLI_REGISTRY_PATH");
        let _ = std::fs::remove_file(&registry_path);
    }
}
