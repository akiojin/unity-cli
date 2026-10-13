use std::fs;
use std::path::{Path, PathBuf};
use std::thread::{self, JoinHandle};
use std::time::{Duration, SystemTime};

use crate::core::managed_binaries::{
    auto_update_disabled, binary_path_for, detect_rid, download_latest_binary,
    fetch_latest_release, install_dir_for, read_local_version_for, ManagedBinary,
};

const THROTTLE_SECS: u64 = 4 * 60 * 60; // 4 hours

/// Spawn a background thread that checks for updates and installs the latest
/// binary if a newer version is available.  Returns `None` when the check is
/// skipped (opt-out env, throttle, or error resolving paths).
pub fn maybe_self_update() -> Option<JoinHandle<()>> {
    if auto_update_disabled() {
        tracing::debug!("self-update skipped: UNITY_CLI_NO_AUTO_UPDATE=1");
        return None;
    }

    let marker = last_check_path().ok()?;
    if is_recent(&marker) {
        tracing::debug!("self-update skipped: last check within throttle window");
        return None;
    }

    Some(thread::spawn(move || match run_update(&marker) {
        Ok(()) => tracing::debug!("self-update check completed"),
        Err(e) => tracing::debug!("self-update failed: {e:#}"),
    }))
}

/// Print a one-time warning if `~/.cargo/bin/unity-cli` exists, which could
/// shadow the managed binary.
pub fn warn_cargo_conflict() {
    if let Some(home) = dirs::home_dir() {
        let cargo_bin = if cfg!(target_os = "windows") {
            home.join(".cargo/bin/unity-cli.exe")
        } else {
            home.join(".cargo/bin/unity-cli")
        };
        if cargo_bin.exists() {
            eprintln!(
                "warning: {} exists and may shadow the managed binary. \
                 Consider running `cargo uninstall unity-cli`.",
                cargo_bin.display()
            );
        }
    }
}

fn last_check_path() -> anyhow::Result<PathBuf> {
    Ok(install_dir_for(ManagedBinary::UnityCli)?.join("LAST_UPDATE_CHECK"))
}

fn is_recent(path: &Path) -> bool {
    fs::metadata(path)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| SystemTime::now().duration_since(t).ok())
        .is_some_and(|age| age < Duration::from_secs(THROTTLE_SECS))
}

fn touch(path: &Path) {
    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }
    let _ = fs::write(path, b"");
}

fn run_update(marker: &Path) -> anyhow::Result<()> {
    let latest = fetch_latest_release(ManagedBinary::UnityCli);

    // Always touch the marker, even on failure, to prevent hammering the API.
    touch(marker);

    let latest = latest?;
    let local = read_local_version_for(ManagedBinary::UnityCli);
    if local.as_deref() == Some(latest.version.as_str()) {
        tracing::debug!(
            "self-update: already at latest version {} (rid={})",
            latest.version,
            detect_rid()
        );
        return Ok(());
    }

    tracing::debug!(
        "self-update: upgrading from {:?} to {} (rid={})",
        local,
        latest.version,
        detect_rid()
    );
    let dest = binary_path_for(ManagedBinary::UnityCli)?;
    download_latest_binary(ManagedBinary::UnityCli, &latest, &dest)?;
    tracing::debug!("self-update: successfully installed {}", latest.version);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn env_lock() -> &'static std::sync::Mutex<()> {
        crate::test_env::env_lock()
    }

    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = std::env::var(key).ok();
            std::env::set_var(key, value);
            Self { key, previous }
        }
        fn unset(key: &'static str) -> Self {
            let previous = std::env::var(key).ok();
            std::env::remove_var(key);
            Self { key, previous }
        }
    }

    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(value) = &self.previous {
                std::env::set_var(self.key, value);
            } else {
                std::env::remove_var(self.key);
            }
        }
    }

    #[test]
    fn maybe_self_update_skipped_when_opt_out_env_set() {
        let _guard = env_lock().lock().unwrap_or_else(|p| p.into_inner());
        let _env = EnvVarGuard::set("UNITY_CLI_NO_AUTO_UPDATE", "1");
        assert!(maybe_self_update().is_none());
    }

    #[test]
    fn warn_cargo_conflict_runs_without_panic() {
        warn_cargo_conflict();
    }

    #[test]
    fn touch_creates_parent_and_writes_marker() {
        let tmp = tempfile::TempDir::new().unwrap();
        let marker = tmp.path().join("nested/dir/LAST_CHECK");
        touch(&marker);
        assert!(marker.exists());
        assert!(marker.parent().unwrap().is_dir());
    }

    #[test]
    fn is_recent_returns_false_for_missing_path() {
        let tmp = tempfile::TempDir::new().unwrap();
        assert!(!is_recent(&tmp.path().join("nonexistent")));
    }

    #[test]
    fn is_recent_returns_true_for_freshly_touched_file() {
        let tmp = tempfile::TempDir::new().unwrap();
        let path = tmp.path().join("recent");
        touch(&path);
        assert!(is_recent(&path));
    }

    #[test]
    fn last_check_path_uses_unity_cli_tools_root_env() {
        let _guard = env_lock().lock().unwrap_or_else(|p| p.into_inner());
        let tmp = tempfile::TempDir::new().unwrap();
        let _env = EnvVarGuard::set("UNITY_CLI_TOOLS_ROOT", tmp.path().to_str().unwrap());
        let path = last_check_path().unwrap();
        assert!(path.starts_with(tmp.path()));
        assert!(path.ends_with("LAST_UPDATE_CHECK"));
    }

    #[test]
    fn maybe_self_update_skips_when_recent_check_exists() {
        let _guard = env_lock().lock().unwrap_or_else(|p| p.into_inner());
        let _no_auto = EnvVarGuard::unset("UNITY_CLI_NO_AUTO_UPDATE");
        let tmp = tempfile::TempDir::new().unwrap();
        let _tools_env = EnvVarGuard::set("UNITY_CLI_TOOLS_ROOT", tmp.path().to_str().unwrap());
        let marker = last_check_path().unwrap();
        if let Some(parent) = marker.parent() {
            std::fs::create_dir_all(parent).unwrap();
        }
        std::fs::write(&marker, b"").unwrap();
        assert!(maybe_self_update().is_none());
    }
}
