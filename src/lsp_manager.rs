use std::env;
use std::fs;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use serde::Deserialize;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use ureq::Agent;

const DOWNLOAD_TIMEOUT_SECS: u64 = 30;
const USER_AGENT_VALUE: &str = "unity-cli";
const MANIFEST_REPOS: &[&str] = &["akiojin/unity-cli"];
const GITHUB_TOKEN_ENV_VARS: &[&str] = &["GITHUB_TOKEN", "GH_TOKEN"];

#[derive(Debug, Deserialize)]
struct ReleaseInfo {
    tag_name: String,
}

#[derive(Debug, Deserialize)]
struct LspManifest {
    #[serde(default)]
    version: Option<String>,
    #[serde(default)]
    assets: std::collections::HashMap<String, LspAsset>,
}

#[derive(Debug, Deserialize)]
struct LspAsset {
    url: String,
    sha256: String,
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

pub fn executable_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "server.exe"
    } else {
        "server"
    }
}

pub fn tools_root() -> Result<PathBuf> {
    env::var("UNITY_CLI_TOOLS_ROOT")
        .ok()
        .map(|root| PathBuf::from(root.trim()))
        .or_else(|| dirs::home_dir().map(|home| home.join(".unity/tools")))
        .ok_or_else(|| anyhow!("Unable to resolve tools root"))
}

pub fn install_dir() -> Result<PathBuf> {
    Ok(tools_root()?.join("csharp-lsp").join(detect_rid()))
}

pub fn binary_path() -> Result<PathBuf> {
    Ok(install_dir()?.join(executable_name()))
}

pub fn version_path() -> Result<PathBuf> {
    Ok(install_dir()?.join("VERSION"))
}

pub fn read_local_version() -> Option<String> {
    let path = version_path().ok()?;
    fs::read_to_string(path)
        .ok()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
}

pub fn ensure_local(force_download: bool) -> Result<PathBuf> {
    let path = binary_path()?;
    if path.exists() && !force_download {
        return Ok(path);
    }

    download_latest_binary(&path)
}

pub fn install_latest() -> Result<Value> {
    let binary = ensure_local(true)?;
    Ok(json!({
        "success": true,
        "rid": detect_rid(),
        "binaryPath": binary.to_string_lossy().to_string(),
        "version": read_local_version()
    }))
}

pub fn doctor() -> Result<Value> {
    let rid = detect_rid().to_string();
    let root = tools_root()?;
    let binary = binary_path()?;
    let version = read_local_version();

    let latest = fetch_latest_manifest().ok().map(|(manifest, repo, tag)| {
        json!({
            "repo": repo,
            "tag": tag,
            "version": manifest.version
        })
    });

    Ok(json!({
        "success": true,
        "rid": rid,
        "toolsRoot": root.to_string_lossy().to_string(),
        "binaryPath": binary.to_string_lossy().to_string(),
        "binaryExists": binary.exists(),
        "localVersion": version,
        "latest": latest
    }))
}

fn download_latest_binary(dest: &Path) -> Result<PathBuf> {
    let rid = detect_rid();
    let (manifest, _repo, tag) = fetch_latest_manifest()?;
    let asset = manifest
        .assets
        .get(rid)
        .ok_or_else(|| anyhow!("manifest missing asset for RID: {rid}"))?;

    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create install directory: {}", parent.display()))?;
    }

    let tmp = dest.with_extension("download");
    download_to(&asset.url, &tmp)?;
    let actual = sha256_file(&tmp)?;
    if !actual.eq_ignore_ascii_case(&asset.sha256) {
        let _ = fs::remove_file(&tmp);
        return Err(anyhow!("checksum mismatch for downloaded LSP binary"));
    }

    replace_file_atomic(&tmp, dest)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let mut permissions = fs::metadata(dest)?.permissions();
        permissions.set_mode(0o755);
        fs::set_permissions(dest, permissions)?;
    }

    let version = manifest
        .version
        .unwrap_or_else(|| tag.trim_start_matches('v').to_string());
    write_local_version(&version)?;

    Ok(dest.to_path_buf())
}

fn write_local_version(version: &str) -> Result<()> {
    let path = version_path()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("Failed to create VERSION directory: {}", parent.display()))?;
    }
    fs::write(&path, format!("{}\n", version.trim()))
        .with_context(|| format!("Failed to write VERSION marker: {}", path.display()))
}

fn fetch_latest_manifest() -> Result<(LspManifest, String, String)> {
    let mut errors = Vec::new();
    for repo in MANIFEST_REPOS {
        match fetch_latest_manifest_for_repo(repo) {
            Ok((manifest, tag)) => return Ok((manifest, (*repo).to_string(), tag)),
            Err(error) => errors.push(format!("{repo}: {error}")),
        }
    }

    Err(anyhow!(
        "Failed to fetch csharp-lsp manifest from known repositories: {}",
        errors.join(" | ")
    ))
}

fn fetch_latest_manifest_for_repo(repo: &str) -> Result<(LspManifest, String)> {
    let release_url = format!("https://api.github.com/repos/{repo}/releases/latest");
    let release: ReleaseInfo = get_json(&release_url)?;

    let tag = release.tag_name;
    let manifest_url =
        format!("https://github.com/{repo}/releases/download/{tag}/csharp-lsp-manifest.json");

    let manifest: LspManifest = get_json(&manifest_url)?;

    Ok((manifest, tag))
}

fn download_to(url: &str, dest: &Path) -> Result<()> {
    let response = get_response(url)?;
    let mut reader = response.into_reader();

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
    Ok(format!("{:x}", hasher.finalize()))
}

fn replace_file_atomic(tmp: &Path, dest: &Path) -> Result<()> {
    match fs::rename(tmp, dest) {
        Ok(_) => Ok(()),
        Err(_) => {
            let _ = fs::remove_file(dest);
            fs::rename(tmp, dest)
                .with_context(|| format!("Failed to move {} to {}", tmp.display(), dest.display()))
        }
    }
}

fn http_client() -> Result<Agent> {
    Ok(ureq::AgentBuilder::new()
        .timeout(Duration::from_secs(DOWNLOAD_TIMEOUT_SECS))
        .build())
}

fn get_response(url: &str) -> Result<ureq::Response> {
    let client = http_client()?;
    let mut request = client.get(url).set("User-Agent", USER_AGENT_VALUE);

    if let Some(token) = github_token_from_env() {
        request = request.set("Authorization", &format!("Bearer {token}"));
    }

    match request.call() {
        Ok(response) => Ok(response),
        Err(ureq::Error::Status(status, response)) => {
            let body = response
                .into_string()
                .map(|body| format!(" body={body}"))
                .unwrap_or_default();
            Err(anyhow!("HTTP {status} for {url}{body}"))
        }
        Err(error) => Err(anyhow!("Failed to request {url}: {error}")),
    }
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

fn get_json<T: for<'de> Deserialize<'de>>(url: &str) -> Result<T> {
    let response = get_response(url)?;
    response
        .into_json::<T>()
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
        let dir = tempdir().expect("tempdir should succeed");
        let _env = EnvVarGuard::set(
            "UNITY_CLI_TOOLS_ROOT",
            dir.path()
                .to_str()
                .expect("tempdir path should be valid UTF-8"),
        );

        write_local_version(" 1.2.3 ").expect("version write should succeed");
        assert_eq!(read_local_version().as_deref(), Some("1.2.3"));
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
        let (url, handle) = run_http_server_once("500 Internal Server Error", "{\"ok\":false}");
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
        assert!(request.contains(&format!("Authorization: Bearer {token}")));
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
        assert!(error.to_string().contains("HTTP 403"));
        assert!(error.to_string().contains("forbidden"));
    }
}
