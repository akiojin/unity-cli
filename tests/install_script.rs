//! Exercises `scripts/install.sh` against a local `file://` release so the
//! checksum gate is verified without touching GitHub.
#![cfg(unix)]

use std::fs;
use std::path::{Path, PathBuf};
use std::process::{Command, Output};

use sha2::{Digest, Sha256};
use tempfile::TempDir;

const TAG: &str = "v9.9.9";
const UNIX_RIDS: &[&str] = &["osx-arm64", "osx-x64", "linux-x64", "linux-arm64"];

fn sha256_hex(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|b| format!("{b:02x}"))
        .collect()
}

fn install_script() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("scripts/install.sh")
}

/// Lays out `<root>/release/<TAG>/unity-cli-<rid>` with `served` bytes and
/// returns the release base URL.
fn write_release(root: &Path, served: &[u8]) -> (String, PathBuf) {
    let tag_dir = root.join("release").join(TAG);
    fs::create_dir_all(&tag_dir).expect("release dir");
    for rid in UNIX_RIDS {
        fs::write(tag_dir.join(format!("unity-cli-{rid}")), served).expect("asset");
    }
    (
        format!("file://{}", root.join("release").display()),
        tag_dir,
    )
}

fn write_sha256sums(tag_dir: &Path, genuine: &[u8]) {
    let sha = sha256_hex(genuine);
    let sums: String = UNIX_RIDS
        .iter()
        .map(|rid| format!("{sha}  unity-cli-{rid}\n"))
        .collect();
    fs::write(tag_dir.join("SHA256SUMS"), sums).expect("SHA256SUMS");
}

fn run_install(home: &Path, base_url: &str) -> Output {
    Command::new("sh")
        .arg(install_script())
        .env("HOME", home)
        .env("UNITY_CLI_VERSION", TAG)
        .env("UNITY_CLI_RELEASE_BASE_URL", base_url)
        .output()
        .expect("install.sh should run")
}

fn installed_binaries(home: &Path) -> Vec<PathBuf> {
    UNIX_RIDS
        .iter()
        .map(|rid| {
            home.join(".unity/tools/unity-cli")
                .join(rid)
                .join("unity-cli")
        })
        .filter(|path| path.exists())
        .collect()
}

#[test]
fn install_sh_installs_binary_matching_sha256sums() {
    let root = TempDir::new().expect("tempdir");
    let home = root.path().join("home");
    let (base_url, tag_dir) = write_release(root.path(), b"genuine binary");
    write_sha256sums(&tag_dir, b"genuine binary");

    let output = run_install(&home, &base_url);

    assert!(
        output.status.success(),
        "install should succeed: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    let installed = installed_binaries(&home);
    assert_eq!(installed.len(), 1, "exactly one RID should be installed");
    assert_eq!(fs::read(&installed[0]).expect("binary"), b"genuine binary");
    assert_eq!(
        fs::read_to_string(installed[0].with_file_name("VERSION")).expect("VERSION"),
        "9.9.9\n"
    );
    assert!(home.join(".local/bin/unity-cli").exists());
}

#[test]
fn install_sh_aborts_when_binary_does_not_match_sha256sums() {
    let root = TempDir::new().expect("tempdir");
    let home = root.path().join("home");
    let (base_url, tag_dir) = write_release(root.path(), b"tampered binary");
    write_sha256sums(&tag_dir, b"genuine binary");

    let output = run_install(&home, &base_url);

    assert!(!output.status.success(), "tampered install must fail");
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(stderr.contains("checksum mismatch"), "stderr: {stderr}");
    assert!(installed_binaries(&home).is_empty());
    assert!(!home.join(".local/bin/unity-cli").exists());
}

#[test]
fn install_sh_aborts_when_sha256sums_lacks_the_platform_entry() {
    let root = TempDir::new().expect("tempdir");
    let home = root.path().join("home");
    let (base_url, tag_dir) = write_release(root.path(), b"genuine binary");
    fs::write(
        tag_dir.join("SHA256SUMS"),
        format!("{}  unity-cli-win-x64\n", sha256_hex(b"genuine binary")),
    )
    .expect("SHA256SUMS");

    let output = run_install(&home, &base_url);

    assert!(!output.status.success());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(
        stderr.contains("SHA256SUMS has no entry"),
        "stderr: {stderr}"
    );
    assert!(installed_binaries(&home).is_empty());
}

#[test]
fn install_sh_falls_back_to_manifest_checksum_without_sha256sums() {
    let root = TempDir::new().expect("tempdir");
    let home = root.path().join("home");
    let (base_url, tag_dir) = write_release(root.path(), b"tampered binary");
    let sha = sha256_hex(b"genuine binary");
    let assets: Vec<String> = UNIX_RIDS
        .iter()
        .map(|rid| {
            format!(
                "\"{rid}\": {{\n\"url\": \"file://{}\",\n\"sha256\": \"{sha}\"\n}}",
                tag_dir.join(format!("unity-cli-{rid}")).display()
            )
        })
        .collect();
    fs::write(
        tag_dir.join("unity-cli-manifest.json"),
        format!(
            "{{\n\"version\": \"9.9.9\",\n\"assets\": {{\n{}\n}}\n}}\n",
            assets.join(",\n")
        ),
    )
    .expect("manifest");

    let output = run_install(&home, &base_url);

    assert!(
        !output.status.success(),
        "manifest checksum must be enforced"
    );
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(stderr.contains("checksum mismatch"), "stderr: {stderr}");
    assert!(installed_binaries(&home).is_empty());
}
