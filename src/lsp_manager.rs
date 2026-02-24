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
    match client.get(url).set("User-Agent", USER_AGENT_VALUE).call() {
        Ok(response) => Ok(response),
        Err(ureq::Error::Status(status, _)) => Err(anyhow!("HTTP {status} for {url}")),
        Err(error) => Err(anyhow!("Failed to request {url}: {error}")),
    }
}

fn get_json<T: for<'de> Deserialize<'de>>(url: &str) -> Result<T> {
    let response = get_response(url)?;
    response
        .into_json::<T>()
        .with_context(|| format!("Failed to parse JSON: {url}"))
}
