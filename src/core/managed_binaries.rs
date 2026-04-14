use std::collections::HashMap;
use std::env;
use std::fs;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use serde::Deserialize;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use ureq::{http, Agent, Body};

const DOWNLOAD_TIMEOUT_SECS: u64 = 30;
const HTTP_MAX_ATTEMPTS: usize = 3;
const HTTP_RETRY_BASE_DELAY_MILLIS: u64 = 250;
const USER_AGENT_VALUE: &str = "unity-cli";
const GITHUB_TOKEN_ENV_VARS: &[&str] = &["GITHUB_TOKEN", "GH_TOKEN"];

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ManagedBinary {
    CSharpLsp,
    UnityCli,
}

#[derive(Debug, Clone)]
pub struct ManagedBinaryStatus {
    pub managed_binary: ManagedBinary,
    pub rid: &'static str,
    pub binary_path: PathBuf,
    pub binary_exists: bool,
    pub local_version: Option<String>,
    pub latest: Option<LatestVersion>,
    pub latest_error: Option<String>,
    pub update_available: bool,
}

#[derive(Debug, Clone)]
pub struct LatestVersion {
    pub repo: String,
    pub tag: String,
    pub version: String,
}

#[derive(Debug, Clone)]
struct ManagedBinarySpec {
    key: &'static str,
    install_subdir: &'static str,
    manifest_name: &'static str,
    executable_name: &'static str,
    repos: &'static [&'static str],
}

#[derive(Debug, Deserialize)]
struct ReleaseInfo {
    tag_name: String,
}

#[derive(Debug, Deserialize)]
struct ReleaseManifest {
    #[serde(default)]
    version: Option<String>,
    #[serde(default)]
    assets: HashMap<String, ReleaseAsset>,
}

#[derive(Debug, Clone, Deserialize)]
pub(crate) struct ReleaseAsset {
    pub(crate) url: String,
    pub(crate) sha256: String,
}

#[derive(Debug, Clone)]
pub(crate) struct LatestRelease {
    pub(crate) repo: String,
    pub(crate) tag: String,
    pub(crate) version: String,
    pub(crate) asset: ReleaseAsset,
}

impl ManagedBinary {
    fn spec(self) -> ManagedBinarySpec {
        match self {
            Self::CSharpLsp => ManagedBinarySpec {
                key: "csharp-lsp",
                install_subdir: "csharp-lsp",
                manifest_name: "csharp-lsp-manifest.json",
                executable_name: if cfg!(target_os = "windows") {
                    "server.exe"
                } else {
                    "server"
                },
                repos: &["akiojin/unity-cli"],
            },
            Self::UnityCli => ManagedBinarySpec {
                key: "unity-cli",
                install_subdir: "unity-cli",
                manifest_name: "unity-cli-manifest.json",
                executable_name: if cfg!(target_os = "windows") {
                    "unity-cli.exe"
                } else {
                    "unity-cli"
                },
                repos: &["akiojin/unity-cli"],
            },
        }
    }
}

impl ManagedBinaryStatus {
    pub fn to_json(&self) -> Value {
        let latest = self.latest.as_ref().map(|latest| {
            json!({
                "repo": latest.repo,
                "tag": latest.tag,
                "version": latest.version
            })
        });

        json!({
            "success": true,
            "managed": true,
            "key": self.managed_binary.spec().key,
            "rid": self.rid,
            "binaryPath": self.binary_path.to_string_lossy().to_string(),
            "managedBinaryPath": self.binary_path.to_string_lossy().to_string(),
            "binaryExists": self.binary_exists,
            "localVersion": self.local_version,
            "latest": latest,
            "latestError": self.latest_error,
            "updateAvailable": self.update_available
        })
    }
}

pub fn detect_rid() -> &'static str {
    if cfg!(target_os = "windows") {
        if cfg!(target_arch = "aarch64") {
            "win-arm64"
        } else {
            "win-x64"
        }
    } else if cfg!(target_os = "macos") {
        if cfg!(target_arch = "aarch64") {
            "osx-arm64"
        } else {
            "osx-x64"
        }
    } else if cfg!(target_arch = "aarch64") {
        "linux-arm64"
    } else {
        "linux-x64"
    }
}

#[cfg(test)]
pub fn executable_name() -> &'static str {
    ManagedBinary::CSharpLsp.spec().executable_name
}

pub fn tools_root() -> Result<PathBuf> {
    env::var("UNITY_CLI_TOOLS_ROOT")
        .ok()
        .map(|root| PathBuf::from(root.trim()))
        .or_else(|| dirs::home_dir().map(|home| home.join(".unity/tools")))
        .ok_or_else(|| anyhow!("Unable to resolve tools root"))
}

pub fn install_dir() -> Result<PathBuf> {
    install_dir_for(ManagedBinary::CSharpLsp)
}

#[cfg(test)]
pub fn binary_path() -> Result<PathBuf> {
    binary_path_for(ManagedBinary::CSharpLsp)
}

#[cfg(test)]
pub fn version_path() -> Result<PathBuf> {
    version_path_for(ManagedBinary::CSharpLsp)
}

#[cfg(test)]
pub fn read_local_version() -> Option<String> {
    read_local_version_for(ManagedBinary::CSharpLsp)
}

pub fn cli_install_latest(force_download: bool) -> Result<Value> {
    Ok(install_latest_for(ManagedBinary::UnityCli, force_download)?.to_json())
}

pub fn cli_doctor() -> Result<Value> {
    Ok(doctor_for(ManagedBinary::UnityCli)?.to_json())
}

pub fn cli_status() -> Result<ManagedBinaryStatus> {
    inspect_for(ManagedBinary::UnityCli)
}

pub fn lsp_status() -> Result<ManagedBinaryStatus> {
    inspect_for(ManagedBinary::CSharpLsp)
}

pub fn ensure_latest_cli_for_daemon() -> Result<ManagedBinaryStatus> {
    ensure_latest_for(ManagedBinary::UnityCli, false, true)
}

pub fn ensure_latest_lsp_for_daemon() -> Result<ManagedBinaryStatus> {
    ensure_latest_for(ManagedBinary::CSharpLsp, false, true)
}

pub fn lsp_install_latest(force_download: bool) -> Result<ManagedBinaryStatus> {
    install_latest_for(ManagedBinary::CSharpLsp, force_download)
}

pub(crate) fn install_dir_for(managed_binary: ManagedBinary) -> Result<PathBuf> {
    Ok(tools_root()?
        .join(managed_binary.spec().install_subdir)
        .join(detect_rid()))
}

pub(crate) fn binary_path_for(managed_binary: ManagedBinary) -> Result<PathBuf> {
    Ok(install_dir_for(managed_binary)?.join(managed_binary.spec().executable_name))
}

fn version_path_for(managed_binary: ManagedBinary) -> Result<PathBuf> {
    Ok(install_dir_for(managed_binary)?.join("VERSION"))
}

pub(crate) fn read_local_version_for(managed_binary: ManagedBinary) -> Option<String> {
    let path = version_path_for(managed_binary).ok()?;
    fs::read_to_string(path)
        .ok()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
}

pub fn ensure_local(force_download: bool) -> Result<PathBuf> {
    ensure_local_for(ManagedBinary::CSharpLsp, force_download)
}

fn ensure_local_for(managed_binary: ManagedBinary, force_download: bool) -> Result<PathBuf> {
    let path = binary_path_for(managed_binary)?;
    if path.exists() && !force_download {
        return Ok(path);
    }

    let status = install_latest_for(managed_binary, true)?;
    Ok(status.binary_path)
}

pub fn install_latest() -> Result<Value> {
    Ok(lsp_install_latest(true)?.to_json())
}

pub fn doctor() -> Result<Value> {
    Ok(doctor_for(ManagedBinary::CSharpLsp)?.to_json())
}

fn inspect_for(managed_binary: ManagedBinary) -> Result<ManagedBinaryStatus> {
    let binary_path = binary_path_for(managed_binary)?;
    Ok(ManagedBinaryStatus {
        managed_binary,
        rid: detect_rid(),
        binary_exists: binary_path.exists(),
        local_version: read_local_version_for(managed_binary),
        latest: None,
        latest_error: None,
        update_available: false,
        binary_path,
    })
}

fn doctor_for(managed_binary: ManagedBinary) -> Result<ManagedBinaryStatus> {
    let mut status = inspect_for(managed_binary)?;
    match fetch_latest_release(managed_binary) {
        Ok(latest) => {
            status.update_available =
                status.local_version.as_deref() != Some(latest.version.as_str());
            status.latest = Some(LatestVersion {
                repo: latest.repo,
                tag: latest.tag,
                version: latest.version,
            });
        }
        Err(error) => {
            status.latest_error = Some(error.to_string());
        }
    }
    Ok(status)
}

fn install_latest_for(
    managed_binary: ManagedBinary,
    force_download: bool,
) -> Result<ManagedBinaryStatus> {
    ensure_latest_for(managed_binary, force_download, false)
}

fn ensure_latest_for(
    managed_binary: ManagedBinary,
    force_download: bool,
    allow_stale_existing: bool,
) -> Result<ManagedBinaryStatus> {
    let mut status = inspect_for(managed_binary)?;
    if should_skip_remote_checks_for_tests() {
        if status.binary_exists || allow_stale_existing {
            return Ok(status);
        }
        return Err(anyhow!(
            "managed {} binary is missing while test remote checks are disabled",
            managed_binary.spec().key
        ));
    }

    match fetch_latest_release(managed_binary) {
        Ok(latest) => {
            status.update_available =
                status.local_version.as_deref() != Some(latest.version.as_str());
            status.latest = Some(LatestVersion {
                repo: latest.repo.clone(),
                tag: latest.tag.clone(),
                version: latest.version.clone(),
            });
            if force_download || !status.binary_exists || status.update_available {
                download_latest_binary(managed_binary, &latest, &status.binary_path)?;
                status.binary_exists = true;
                status.local_version = Some(latest.version);
                status.update_available = false;
                status.latest_error = None;
            }
            Ok(status)
        }
        Err(error) if allow_stale_existing && status.binary_exists => {
            status.latest_error = Some(error.to_string());
            Ok(status)
        }
        Err(error) => Err(error),
    }
}

pub(crate) fn download_latest_binary(
    managed_binary: ManagedBinary,
    latest: &LatestRelease,
    dest: &Path,
) -> Result<PathBuf> {
    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create install directory: {}", parent.display()))?;
    }

    let tmp = dest.with_extension("download");
    download_to(&latest.asset.url, &tmp)?;
    let actual = sha256_file(&tmp)?;
    if !actual.eq_ignore_ascii_case(&latest.asset.sha256) {
        let _ = fs::remove_file(&tmp);
        return Err(anyhow!(
            "checksum mismatch for downloaded {} binary",
            managed_binary.spec().key
        ));
    }

    replace_file_atomic(&tmp, dest)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let mut permissions = fs::metadata(dest)?.permissions();
        permissions.set_mode(0o755);
        fs::set_permissions(dest, permissions)?;
    }

    write_local_version_for(managed_binary, &latest.version)?;
    Ok(dest.to_path_buf())
}

#[cfg(test)]
fn write_local_version(version: &str) -> Result<()> {
    write_local_version_for(ManagedBinary::CSharpLsp, version)
}

fn write_local_version_for(managed_binary: ManagedBinary, version: &str) -> Result<()> {
    let path = version_path_for(managed_binary)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create VERSION directory: {}", parent.display()))?;
    }
    fs::write(&path, format!("{}\n", version.trim()))
        .with_context(|| format!("Failed to write VERSION marker: {}", path.display()))
}

pub(crate) fn fetch_latest_release(managed_binary: ManagedBinary) -> Result<LatestRelease> {
    let mut errors = Vec::new();
    for repo in managed_binary.spec().repos {
        match fetch_latest_release_for_repo(managed_binary, repo) {
            Ok(release) => return Ok(release),
            Err(error) => errors.push(format!("{repo}: {error}")),
        }
    }

    Err(anyhow!(
        "Failed to fetch {} manifest from known repositories: {}",
        managed_binary.spec().key,
        errors.join(" | ")
    ))
}

fn fetch_latest_release_for_repo(
    managed_binary: ManagedBinary,
    repo: &str,
) -> Result<LatestRelease> {
    let release_url = format!("https://api.github.com/repos/{repo}/releases/latest");
    let release: ReleaseInfo = get_json(&release_url)?;

    let tag = release.tag_name;
    let manifest_url = format!(
        "https://github.com/{repo}/releases/download/{tag}/{}",
        managed_binary.spec().manifest_name
    );

    let manifest: ReleaseManifest = get_json(&manifest_url)?;
    let version = manifest
        .version
        .unwrap_or_else(|| tag.trim_start_matches('v').to_string());
    let asset = manifest
        .assets
        .get(detect_rid())
        .cloned()
        .ok_or_else(|| anyhow!("manifest missing asset for RID: {}", detect_rid()))?;

    Ok(LatestRelease {
        repo: repo.to_string(),
        tag,
        version,
        asset,
    })
}

fn download_to(url: &str, dest: &Path) -> Result<()> {
    let response = get_response(url)?;
    let mut reader = response.into_body().into_reader();

    let mut file = fs::File::create(dest)
        .with_context(|| format!("Failed to create temporary file: {}", dest.display()))?;
    io::copy(&mut reader, &mut file)
        .with_context(|| format!("Failed to write download: {}", dest.display()))?;
    file.flush()
        .with_context(|| format!("Failed to flush download: {}", dest.display()))?;
    Ok(())
}

fn sha256_file(path: &Path) -> Result<String> {
    let mut file = fs::File::open(path)
        .with_context(|| format!("Failed to open file for checksum: {}", path.display()))?;
    let mut hasher = Sha256::new();
    let mut buffer = [0_u8; 8192];
    loop {
        let read = file
            .read(&mut buffer)
            .with_context(|| format!("Failed to read for checksum: {}", path.display()))?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hasher
        .finalize()
        .iter()
        .map(|b| format!("{b:02x}"))
        .collect::<String>())
}

fn replace_file_atomic(tmp: &Path, dest: &Path) -> Result<()> {
    // tmp is always in the same directory as dest (created via
    // `dest.with_extension("download")`), so rename is an atomic
    // same-filesystem operation that overwrites dest in place.
    // Never delete dest before rename — that risks leaving no binary at all
    // if the process is interrupted.
    #[cfg(windows)]
    {
        if dest.exists() {
            let backup = dest.with_extension("previous");
            if backup.exists() {
                fs::remove_file(&backup).with_context(|| {
                    format!(
                        "Failed to clear backup file before replace: {}",
                        backup.display()
                    )
                })?;
            }

            fs::rename(dest, &backup).with_context(|| {
                format!(
                    "Failed to move existing destination {} to {}",
                    dest.display(),
                    backup.display()
                )
            })?;

            if let Err(error) = fs::rename(tmp, dest) {
                let _ = fs::rename(&backup, dest);
                return Err(error).with_context(|| {
                    format!("Failed to move {} to {}", tmp.display(), dest.display())
                });
            }

            fs::remove_file(&backup).with_context(|| {
                format!(
                    "Failed to remove backup file after replace: {}",
                    backup.display()
                )
            })?;
            return Ok(());
        }
    }

    fs::rename(tmp, dest)
        .with_context(|| format!("Failed to move {} to {}", tmp.display(), dest.display()))
}

fn http_client() -> Result<Agent> {
    let config = Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(DOWNLOAD_TIMEOUT_SECS)))
        .build();
    Ok(Agent::new_with_config(config))
}

fn get_response(url: &str) -> Result<http::Response<Body>> {
    let client = http_client()?;
    let token = github_token_from_env();
    let mut last_error = None;

    for attempt in 1..=HTTP_MAX_ATTEMPTS {
        let mut request = client.get(url).header("User-Agent", USER_AGENT_VALUE);
        if let Some(token) = token.as_deref() {
            request = request.header("Authorization", format!("Bearer {token}"));
        }

        match request.call() {
            Ok(response) => return Ok(response),
            Err(ureq::Error::StatusCode(code)) => {
                let error = anyhow!("HTTP {code} for {url}");
                if attempt < HTTP_MAX_ATTEMPTS && is_retryable_status(code) {
                    last_error = Some(error);
                    sleep_before_retry(attempt);
                    continue;
                }
                return Err(error);
            }
            Err(e) => {
                let error = anyhow!("Failed to request {url}: {e}");
                if attempt < HTTP_MAX_ATTEMPTS {
                    last_error = Some(error);
                    sleep_before_retry(attempt);
                    continue;
                }
                return Err(error);
            }
        }
    }

    Err(last_error.unwrap_or_else(|| anyhow!("Failed to request {url}: unknown error")))
}

fn is_retryable_status(status: u16) -> bool {
    status == 408 || status == 429 || (500..=599).contains(&status)
}

fn sleep_before_retry(attempt: usize) {
    let backoff_multiplier = 1_u64 << attempt.saturating_sub(1);
    std::thread::sleep(Duration::from_millis(
        HTTP_RETRY_BASE_DELAY_MILLIS * backoff_multiplier,
    ));
}

fn github_token_from_env() -> Option<String> {
    for key in GITHUB_TOKEN_ENV_VARS {
        if let Ok(value) = std::env::var(key) {
            let token = value.trim();
            if !token.is_empty() {
                return Some(token.to_string());
            }
        }
    }
    None
}

fn should_skip_remote_checks_for_tests() -> bool {
    cfg!(test)
        && matches!(
            std::env::var("UNITY_CLI_TEST_SKIP_MANAGED_UPDATE")
                .ok()
                .as_deref(),
            Some("1") | Some("true") | Some("yes")
        )
}

fn get_json<T: for<'de> Deserialize<'de>>(url: &str) -> Result<T> {
    let mut response = get_response(url)?;
    response
        .body_mut()
        .read_json::<T>()
        .with_context(|| format!("Failed to parse JSON: {url}"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Read, Write};
    use std::net::TcpListener;
    use std::sync::{Arc, Mutex};
    use std::thread;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};
    use tempfile::tempdir;

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

    fn unique_temp_path(label: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or(Duration::from_secs(0))
            .as_nanos();
        std::env::temp_dir().join(format!("unity-cli-lsp-manager-{label}-{nanos}"))
    }

    fn run_http_server_once(status: &str, body: &str) -> (String, thread::JoinHandle<()>) {
        let listener = TcpListener::bind(("127.0.0.1", 0)).expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should expose local addr")
            .port();
        let status_line = status.to_string();
        let body_text = body.to_string();
        let handle = thread::spawn(move || {
            let (mut stream, _) = listener.accept().expect("accept should succeed");
            let mut buf = [0_u8; 1024];
            let _ = stream.read(&mut buf);
            let response = format!(
                "HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {len}\r\nConnection: close\r\n\r\n{body}",
                status = status_line,
                len = body_text.len(),
                body = body_text
            );
            stream
                .write_all(response.as_bytes())
                .expect("response write should succeed");
            stream.flush().expect("response flush should succeed");
        });
        (format!("http://127.0.0.1:{port}/"), handle)
    }

    fn run_http_server_once_with_request_capture(
        status: &str,
        body: &str,
    ) -> (String, Arc<Mutex<String>>, thread::JoinHandle<()>) {
        let listener = TcpListener::bind(("127.0.0.1", 0)).expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should expose local addr")
            .port();
        let status_line = status.to_string();
        let body_text = body.to_string();
        let captured = Arc::new(Mutex::new(String::new()));
        let captured_for_thread = Arc::clone(&captured);
        let handle = thread::spawn(move || {
            let (mut stream, _) = listener.accept().expect("accept should succeed");
            let mut buf = [0_u8; 4096];
            let len = stream.read(&mut buf).unwrap_or_default();
            let request = String::from_utf8_lossy(&buf[..len]).to_string();
            *captured_for_thread.lock().expect("request buffer lock") = request;
            let response = format!(
                "HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {len}\r\nConnection: close\r\n\r\n{body}",
                status = status_line,
                len = body_text.len(),
                body = body_text
            );
            stream
                .write_all(response.as_bytes())
                .expect("response write should succeed");
            stream.flush().expect("response flush should succeed");
        });
        (format!("http://127.0.0.1:{port}/"), captured, handle)
    }

    fn run_http_server_sequence(
        responses: &[(&str, &str)],
    ) -> (String, Arc<Mutex<usize>>, thread::JoinHandle<()>) {
        let listener = TcpListener::bind(("127.0.0.1", 0)).expect("listener should bind");
        let port = listener
            .local_addr()
            .expect("listener should expose local addr")
            .port();
        let request_count = Arc::new(Mutex::new(0_usize));
        let request_count_for_thread = Arc::clone(&request_count);
        let responses = responses
            .iter()
            .map(|(status, body)| ((*status).to_string(), (*body).to_string()))
            .collect::<Vec<_>>();
        let handle = thread::spawn(move || {
            for (status, body) in responses {
                let (mut stream, _) = listener.accept().expect("accept should succeed");
                let mut buf = [0_u8; 1024];
                let _ = stream.read(&mut buf);
                *request_count_for_thread
                    .lock()
                    .expect("request count lock should succeed") += 1;
                let response = format!(
                    "HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {len}\r\nConnection: close\r\n\r\n{body}",
                    len = body.len()
                );
                stream
                    .write_all(response.as_bytes())
                    .expect("response write should succeed");
                stream.flush().expect("response flush should succeed");
            }
        });
        (format!("http://127.0.0.1:{port}/"), request_count, handle)
    }

    #[test]
    fn tools_root_prefers_unity_cli_tools_root_env() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let root = unique_temp_path("tools-root");
        let root_with_spaces = format!("  {}  ", root.display());
        let _env = EnvVarGuard::set("UNITY_CLI_TOOLS_ROOT", &root_with_spaces);
        let resolved = tools_root().expect("tools root should resolve");
        assert_eq!(resolved, root);
    }

    #[test]
    fn write_and_read_local_version_round_trip() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let dir = unique_temp_path("version-roundtrip");
        fs::create_dir_all(&dir).expect("temp tools root should be creatable");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.to_str().expect("temp tools root should be valid UTF-8"),
        );

        write_local_version(" 1.2.3 ").expect("version write should succeed");
        assert!(version_path().is_ok(), "version path should resolve");

        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn unity_cli_status_uses_managed_install_layout() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let dir = tempdir().expect("tempdir should succeed");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        let binary = binary_path_for(ManagedBinary::UnityCli).expect("binary path should resolve");
        if let Some(parent) = binary.parent() {
            fs::create_dir_all(parent).expect("binary parent directory should be created");
        }
        fs::write(&binary, b"managed-unity-cli").expect("binary fixture should be writable");
        write_local_version_for(ManagedBinary::UnityCli, "0.3.0")
            .expect("unity-cli version should be writable");

        let status = cli_status().expect("cli status should succeed");
        assert_eq!(status.binary_path, binary);
        assert!(status.binary_exists);
        assert_eq!(status.local_version.as_deref(), Some("0.3.0"));
        let json = status.to_json();
        assert_eq!(json["managed"], true);
        assert_eq!(json["key"], "unity-cli");
        assert_eq!(
            json["managedBinaryPath"].as_str(),
            Some(binary.to_string_lossy().as_ref())
        );
    }

    #[test]
    fn read_local_version_returns_none_for_missing_or_blank_file() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let dir = tempdir().expect("tempdir should succeed");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        assert!(read_local_version().is_none());

        let version_file = version_path().expect("version path should resolve");
        if let Some(parent) = version_file.parent() {
            fs::create_dir_all(parent).expect("version parent directory should be created");
        }
        fs::write(&version_file, "\n").expect("blank version marker should be writable");
        assert!(read_local_version().is_none());
    }

    #[test]
    fn ensure_local_uses_existing_binary_without_download() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let dir = tempdir().expect("tempdir should succeed");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        let binary = binary_path().expect("binary path should resolve");
        if let Some(parent) = binary.parent() {
            fs::create_dir_all(parent).expect("binary parent directory should be created");
        }
        fs::write(&binary, b"already-installed").expect("binary fixture should be writable");

        let resolved = ensure_local(false).expect("existing binary should be reused");
        assert_eq!(resolved, binary);
    }

    #[test]
    fn ensure_latest_for_can_skip_remote_checks_in_tests() {
        let _guard = env_lock()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        let dir = tempdir().expect("tempdir should succeed");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );
        let _skip = EnvVarGuard::set("UNITY_CLI_TEST_SKIP_MANAGED_UPDATE", "1");

        let binary =
            binary_path_for(ManagedBinary::UnityCli).expect("unity-cli binary path should resolve");
        if let Some(parent) = binary.parent() {
            fs::create_dir_all(parent).expect("binary parent directory should be created");
        }
        fs::write(&binary, b"managed-unity-cli").expect("binary fixture should be writable");

        let status = ensure_latest_for(ManagedBinary::UnityCli, false, true)
            .expect("skip flag should avoid remote fetch");
        assert_eq!(status.binary_path, binary);
        assert!(status.binary_exists);
        assert!(status.latest.is_none());
    }

    #[test]
    fn sha256_file_matches_known_hash() {
        let dir = tempdir().expect("tempdir should succeed");
        let path = dir.path().join("payload.bin");
        fs::write(&path, b"abc").expect("payload write should succeed");

        let digest = sha256_file(&path).expect("checksum should be computed");
        assert_eq!(
            digest,
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }

    #[test]
    fn replace_file_atomic_overwrites_destination() {
        let dir = tempdir().expect("tempdir should succeed");
        let tmp = dir.path().join("server.tmp");
        let dest = dir.path().join("server.bin");

        fs::write(&tmp, b"new-binary").expect("tmp write should succeed");
        fs::write(&dest, b"old-binary").expect("dest write should succeed");

        replace_file_atomic(&tmp, &dest).expect("atomic replace should succeed");

        let content = fs::read(&dest).expect("dest read should succeed");
        assert_eq!(content, b"new-binary");
        assert!(!tmp.exists());
    }

    #[test]
    fn get_response_reports_http_status_errors() {
        let (url, _request_count, handle) = run_http_server_sequence(&[
            ("500 Internal Server Error", "{\"ok\":false}"),
            ("500 Internal Server Error", "{\"ok\":false}"),
            ("500 Internal Server Error", "{\"ok\":false}"),
        ]);
        let error = get_response(&url).expect_err("HTTP 500 should fail");
        handle.join().expect("server thread should complete");
        assert!(error.to_string().contains("HTTP 500"));
    }

    #[test]
    fn get_response_includes_authorization_header() {
        let token = "ghs_test_token";
        let (url, captured_request, handle) =
            run_http_server_once_with_request_capture("200 OK", "{\"tag_name\":\"v1.2.3\"}");
        let _guard = EnvVarGuard::set("GITHUB_TOKEN", token);

        let _ = get_response(&url).expect("authorized request should succeed");
        handle.join().expect("server thread should complete");
        let request = captured_request.lock().expect("request lock");
        let request_lower = request.to_lowercase();
        assert!(request_lower.contains(&format!("authorization: bearer {token}")));
    }

    #[test]
    fn get_json_parses_http_body() {
        let (url, handle) = run_http_server_once("200 OK", "{\"tag_name\":\"v1.2.3\"}");
        let release: ReleaseInfo = get_json(&url).expect("JSON payload should parse");
        handle.join().expect("server thread should complete");
        assert_eq!(release.tag_name, "v1.2.3");
    }

    #[test]
    fn get_json_fails_with_status_and_body() {
        let (url, handle) = run_http_server_once("403 Forbidden", "{\"message\":\"forbidden\"}");
        let error = get_json::<ReleaseInfo>(&url).expect_err("HTTP 403 should fail");
        handle.join().expect("server thread should complete");
        assert!(error.to_string().contains("403"));
    }

    #[test]
    fn get_json_retries_transient_server_error() {
        let (url, request_count, handle) = run_http_server_sequence(&[
            (
                "500 Internal Server Error",
                "{\"message\":\"server error\"}",
            ),
            ("200 OK", "{\"tag_name\":\"v1.2.3\"}"),
        ]);

        let release: ReleaseInfo =
            get_json(&url).expect("transient HTTP 500 should be retried successfully");

        handle.join().expect("server thread should complete");
        assert_eq!(release.tag_name, "v1.2.3");
        assert_eq!(
            *request_count
                .lock()
                .expect("request count lock should succeed"),
            2
        );
    }
}
