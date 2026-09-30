//! Unity project bridge package management (`unity-cli bridge *`) and the
//! CLI ↔ bridge version check shared with `setup` / `system ping`.

use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Map, Value};
use std::fs;
use std::path::{Path, PathBuf};

pub const BRIDGE_PACKAGE: &str = "com.akiojin.unity-cli-bridge";
pub const OPENUPM_URL: &str = "https://package.openupm.com";
pub const OPENUPM_REGISTRY_NAME: &str = "package.openupm.com";

pub fn cli_version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BridgeAction {
    Install,
    Upgrade,
}

/// Resolve the Unity project root: the explicit path, or the nearest ancestor
/// of `cwd` that contains `Packages/manifest.json` or `ProjectSettings/`.
pub fn resolve_project_root(explicit: Option<&Path>, cwd: &Path) -> Result<PathBuf> {
    if let Some(path) = explicit {
        let root = if path.is_absolute() {
            path.to_path_buf()
        } else {
            cwd.join(path)
        };
        if !is_unity_project(&root) {
            bail!(
                "{} is not a Unity project (missing Packages/manifest.json and ProjectSettings/)",
                root.display()
            );
        }
        return Ok(root);
    }
    cwd.ancestors()
        .find(|dir| is_unity_project(dir))
        .map(Path::to_path_buf)
        .ok_or_else(|| {
            anyhow!(
                "No Unity project found from {}. Run inside a Unity project or pass --project-path",
                cwd.display()
            )
        })
}

fn is_unity_project(dir: &Path) -> bool {
    dir.join("Packages").join("manifest.json").is_file() || dir.join("ProjectSettings").is_dir()
}

fn manifest_path(root: &Path) -> PathBuf {
    root.join("Packages").join("manifest.json")
}

fn read_manifest(root: &Path) -> Result<Value> {
    let path = manifest_path(root);
    if !path.exists() {
        return Ok(json!({ "dependencies": {} }));
    }
    let text =
        fs::read_to_string(&path).with_context(|| format!("Failed to read {}", path.display()))?;
    let value: Value = serde_json::from_str(&text)
        .with_context(|| format!("Failed to parse {}", path.display()))?;
    if !value.is_object() {
        bail!("{} must contain a JSON object", path.display());
    }
    Ok(value)
}

/// Add (install) or update (upgrade) the bridge dependency and its OpenUPM
/// scoped registry. The manifest is written only when its content changes,
/// so repeated runs leave the file byte-identical.
pub fn apply(root: &Path, action: BridgeAction, dry_run: bool) -> Result<Value> {
    let mut manifest = read_manifest(root)?;
    let before = manifest.clone();
    let target = cli_version();
    let embedded = embedded_package_dir(root);
    let mut notes = Vec::new();

    if let Some(dir) = &embedded {
        notes.push(format!(
            "Embedded bridge package at {} takes precedence; manifest left unchanged",
            dir.display()
        ));
    } else {
        let current = dependency_spec(&manifest);
        match (action, current.as_deref()) {
            (_, None) => set_dependency(&mut manifest, target)?,
            (BridgeAction::Upgrade, Some(spec)) if is_registry_version(spec) => {
                if spec != target {
                    set_dependency(&mut manifest, target)?;
                }
            }
            (BridgeAction::Upgrade, Some(spec)) => notes.push(format!(
                "Dependency uses a custom source ({spec}); update it manually to change versions"
            )),
            (BridgeAction::Install, Some(spec)) if spec != target => notes.push(format!(
                "Bridge {spec} is already declared; run `unity-cli bridge upgrade` to pin {target}"
            )),
            (BridgeAction::Install, Some(_)) => {}
        }
        if dependency_spec(&manifest).is_some_and(|spec| is_registry_version(&spec)) {
            ensure_openupm_registry(&mut manifest)?;
        }
    }

    let changed = manifest != before;
    if changed && !dry_run {
        let path = manifest_path(root);
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        let mut text = serde_json::to_string_pretty(&manifest)?;
        text.push('\n');
        fs::write(&path, text).with_context(|| format!("Failed to write {}", path.display()))?;
    }

    let mut report = status_from(root, &manifest, embedded.as_deref());
    report["action"] = json!(match action {
        BridgeAction::Install => "install",
        BridgeAction::Upgrade => "upgrade",
    });
    report["manifestChanged"] = json!(changed);
    report["dryRun"] = json!(dry_run);
    report["notes"] = json!(notes);
    Ok(report)
}

/// Read-only view of the bridge declaration, lock resolution, and version check.
pub fn status(root: &Path) -> Result<Value> {
    let manifest = read_manifest(root)?;
    let embedded = embedded_package_dir(root);
    Ok(status_from(root, &manifest, embedded.as_deref()))
}

fn status_from(root: &Path, manifest: &Value, embedded: Option<&Path>) -> Value {
    let declared = dependency_spec(manifest);
    let resolved = embedded
        .and_then(|dir| package_json_version(&dir.join("package.json")))
        .or_else(|| locked_version(root));
    let source = if embedded.is_some() {
        "embedded"
    } else {
        match declared.as_deref() {
            None => "missing",
            Some(spec) if is_registry_version(spec) => "registry",
            Some(_) => "custom",
        }
    };
    // Prefer the Package Manager resolution; fall back to the declared semver.
    let effective = resolved.clone().or_else(|| {
        declared
            .as_deref()
            .filter(|spec| is_registry_version(spec))
            .map(str::to_owned)
    });
    json!({
        "projectPath": root.display().to_string(),
        "manifestPath": manifest_path(root).display().to_string(),
        "package": BRIDGE_PACKAGE,
        "installed": declared.is_some() || embedded.is_some(),
        "source": source,
        "declared": declared,
        "resolvedVersion": resolved,
        "registry": has_openupm_scope(manifest),
        "versionCheck": version_check(effective.as_deref()),
    })
}

/// Compare the CLI version with a bridge version reported by the manifest,
/// the package lock, or the live Editor (`system ping`).
pub fn version_check(bridge_version: Option<&str>) -> Value {
    let cli = cli_version();
    let bridge = bridge_version
        .map(|v| v.trim().trim_start_matches('v'))
        .filter(|v| !v.is_empty() && *v != "unknown");
    let (status, message) = match bridge {
        None => (
            "unknown",
            "Bridge version is not reported; run `unity-cli bridge upgrade` to pin the CLI version"
                .to_string(),
        ),
        Some(v) if v == cli => ("match", format!("CLI and bridge are both {cli}")),
        Some(v) => (
            "mismatch",
            format!(
                "CLI {cli} does not match bridge {v}; run `unity-cli bridge upgrade` (or update the CLI) so both use the same version"
            ),
        ),
    };
    json!({
        "status": status,
        "cliVersion": cli,
        "bridgeVersion": bridge,
        "message": message,
    })
}

fn dependency_spec(manifest: &Value) -> Option<String> {
    manifest
        .get("dependencies")
        .and_then(|deps| deps.get(BRIDGE_PACKAGE))
        .and_then(Value::as_str)
        .map(str::to_owned)
}

fn set_dependency(manifest: &mut Value, version: &str) -> Result<()> {
    let root = manifest
        .as_object_mut()
        .ok_or_else(|| anyhow!("manifest.json must be a JSON object"))?;
    let deps = root
        .entry("dependencies")
        .or_insert_with(|| Value::Object(Map::new()))
        .as_object_mut()
        .ok_or_else(|| anyhow!("manifest.json `dependencies` must be an object"))?;
    deps.insert(BRIDGE_PACKAGE.to_string(), json!(version));
    Ok(())
}

fn is_registry_version(spec: &str) -> bool {
    spec.chars().next().is_some_and(|c| c.is_ascii_digit())
        && spec
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '-' | '+'))
}

fn is_openupm(registry: &Value) -> bool {
    registry
        .get("url")
        .and_then(Value::as_str)
        .is_some_and(|url| url.trim_end_matches('/') == OPENUPM_URL)
}

fn scope_covers(scope: &str) -> bool {
    BRIDGE_PACKAGE == scope || BRIDGE_PACKAGE.starts_with(&format!("{scope}."))
}

fn has_openupm_scope(manifest: &Value) -> bool {
    manifest
        .get("scopedRegistries")
        .and_then(Value::as_array)
        .is_some_and(|registries| {
            registries.iter().filter(|r| is_openupm(r)).any(|r| {
                r.get("scopes")
                    .and_then(Value::as_array)
                    .is_some_and(|scopes| scopes.iter().filter_map(Value::as_str).any(scope_covers))
            })
        })
}

fn ensure_openupm_registry(manifest: &mut Value) -> Result<()> {
    if has_openupm_scope(manifest) {
        return Ok(());
    }
    let root = manifest
        .as_object_mut()
        .ok_or_else(|| anyhow!("manifest.json must be a JSON object"))?;
    let registries = root
        .entry("scopedRegistries")
        .or_insert_with(|| Value::Array(Vec::new()))
        .as_array_mut()
        .ok_or_else(|| anyhow!("manifest.json `scopedRegistries` must be an array"))?;
    if let Some(existing) = registries.iter_mut().find(|r| is_openupm(r)) {
        let scopes = existing
            .as_object_mut()
            .ok_or_else(|| anyhow!("OpenUPM scoped registry must be an object"))?
            .entry("scopes")
            .or_insert_with(|| Value::Array(Vec::new()))
            .as_array_mut()
            .ok_or_else(|| anyhow!("OpenUPM scoped registry `scopes` must be an array"))?;
        scopes.push(json!(BRIDGE_PACKAGE));
    } else {
        registries.push(json!({
            "name": OPENUPM_REGISTRY_NAME,
            "url": OPENUPM_URL,
            "scopes": [BRIDGE_PACKAGE],
        }));
    }
    Ok(())
}

fn embedded_package_dir(root: &Path) -> Option<PathBuf> {
    let entries = fs::read_dir(root.join("Packages")).ok()?;
    entries
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .filter(|dir| dir.is_dir())
        .find(|dir| {
            fs::read_to_string(dir.join("package.json"))
                .ok()
                .and_then(|text| serde_json::from_str::<Value>(&text).ok())
                .is_some_and(|pkg| pkg.get("name").and_then(Value::as_str) == Some(BRIDGE_PACKAGE))
        })
}

fn package_json_version(path: &Path) -> Option<String> {
    let text = fs::read_to_string(path).ok()?;
    let value: Value = serde_json::from_str(&text).ok()?;
    value.get("version")?.as_str().map(str::to_owned)
}

fn locked_version(root: &Path) -> Option<String> {
    let text = fs::read_to_string(root.join("Packages").join("packages-lock.json")).ok()?;
    let value: Value = serde_json::from_str(&text).ok()?;
    value
        .pointer(&format!("/dependencies/{BRIDGE_PACKAGE}/version"))?
        .as_str()
        .filter(|v| is_registry_version(v))
        .map(str::to_owned)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    const NEW_PROJECT_MANIFEST: &str = r#"{
  "dependencies": {
    "com.unity.modules.physics": "1.0.0",
    "com.unity.ugui": "2.0.0"
  }
}
"#;

    fn project(manifest: &str) -> TempDir {
        let dir = TempDir::new().unwrap();
        fs::create_dir_all(dir.path().join("Packages")).unwrap();
        fs::create_dir_all(dir.path().join("ProjectSettings")).unwrap();
        fs::write(dir.path().join("Packages/manifest.json"), manifest).unwrap();
        dir
    }

    fn manifest_text(dir: &TempDir) -> String {
        fs::read_to_string(dir.path().join("Packages/manifest.json")).unwrap()
    }

    #[test]
    fn install_adds_openupm_registry_and_cli_version() {
        let dir = project(NEW_PROJECT_MANIFEST);
        let report = apply(dir.path(), BridgeAction::Install, false).unwrap();

        let manifest: Value = serde_json::from_str(&manifest_text(&dir)).unwrap();
        assert_eq!(manifest["dependencies"][BRIDGE_PACKAGE], cli_version());
        assert_eq!(manifest["dependencies"]["com.unity.ugui"], "2.0.0");
        assert_eq!(manifest["scopedRegistries"][0]["url"], OPENUPM_URL);
        assert_eq!(manifest["scopedRegistries"][0]["scopes"][0], BRIDGE_PACKAGE);
        assert_eq!(report["manifestChanged"], true);
        assert_eq!(report["source"], "registry");
        assert_eq!(report["versionCheck"]["status"], "match");
    }

    #[test]
    fn install_twice_leaves_manifest_byte_identical() {
        let dir = project(NEW_PROJECT_MANIFEST);
        apply(dir.path(), BridgeAction::Install, false).unwrap();
        let first = manifest_text(&dir);
        let second_report = apply(dir.path(), BridgeAction::Install, false).unwrap();

        assert_eq!(manifest_text(&dir), first);
        assert_eq!(second_report["manifestChanged"], false);
    }

    #[test]
    fn install_is_noop_when_already_configured_by_hand() {
        let manifest = format!(
            r#"{{
    "dependencies": {{ "{BRIDGE_PACKAGE}": "{}" }},
    "scopedRegistries": [{{ "name": "OpenUPM", "url": "https://package.openupm.com/", "scopes": ["com.akiojin"] }}]
}}"#,
            cli_version()
        );
        let dir = project(&manifest);
        let report = apply(dir.path(), BridgeAction::Install, false).unwrap();

        assert_eq!(manifest_text(&dir), manifest);
        assert_eq!(report["manifestChanged"], false);
        assert_eq!(report["registry"], true);
    }

    #[test]
    fn install_adds_scope_to_existing_openupm_registry() {
        let dir = project(
            r#"{"dependencies":{},"scopedRegistries":[{"name":"OpenUPM","url":"https://package.openupm.com","scopes":["com.other"]}]}"#,
        );
        apply(dir.path(), BridgeAction::Install, false).unwrap();

        let manifest: Value = serde_json::from_str(&manifest_text(&dir)).unwrap();
        let registries = manifest["scopedRegistries"].as_array().unwrap();
        assert_eq!(registries.len(), 1);
        assert_eq!(
            registries[0]["scopes"],
            json!(["com.other", BRIDGE_PACKAGE])
        );
    }

    #[test]
    fn install_keeps_other_version_and_upgrade_pins_cli_version() {
        let dir = project(&format!(
            r#"{{"dependencies":{{"{BRIDGE_PACKAGE}":"0.1.0"}}}}"#
        ));
        let install = apply(dir.path(), BridgeAction::Install, false).unwrap();
        assert_eq!(install["declared"], "0.1.0");
        assert_eq!(install["versionCheck"]["status"], "mismatch");
        assert_eq!(install["notes"].as_array().unwrap().len(), 1);

        let upgrade = apply(dir.path(), BridgeAction::Upgrade, false).unwrap();
        assert_eq!(upgrade["declared"], cli_version());
        assert_eq!(upgrade["manifestChanged"], true);
        assert_eq!(upgrade["versionCheck"]["status"], "match");
    }

    #[test]
    fn upgrade_leaves_custom_sources_alone() {
        let git = "https://github.com/akiojin/unity-cli.git?path=UnityCliBridge/Packages/unity-cli-bridge";
        let manifest = format!(r#"{{"dependencies":{{"{BRIDGE_PACKAGE}":"{git}"}}}}"#);
        let dir = project(&manifest);
        let report = apply(dir.path(), BridgeAction::Upgrade, false).unwrap();

        assert_eq!(manifest_text(&dir), manifest);
        assert_eq!(report["source"], "custom");
        assert_eq!(report["versionCheck"]["status"], "unknown");
    }

    #[test]
    fn dry_run_reports_change_without_writing() {
        let dir = project(NEW_PROJECT_MANIFEST);
        let report = apply(dir.path(), BridgeAction::Install, true).unwrap();

        assert_eq!(manifest_text(&dir), NEW_PROJECT_MANIFEST);
        assert_eq!(report["manifestChanged"], true);
        assert_eq!(report["dryRun"], true);
    }

    #[test]
    fn embedded_package_is_reported_and_not_duplicated() {
        let dir = project(NEW_PROJECT_MANIFEST);
        let pkg = dir.path().join("Packages/unity-cli-bridge");
        fs::create_dir_all(&pkg).unwrap();
        fs::write(
            pkg.join("package.json"),
            format!(r#"{{"name":"{BRIDGE_PACKAGE}","version":"0.0.1"}}"#),
        )
        .unwrap();
        let report = apply(dir.path(), BridgeAction::Install, false).unwrap();

        assert_eq!(manifest_text(&dir), NEW_PROJECT_MANIFEST);
        assert_eq!(report["source"], "embedded");
        assert_eq!(report["resolvedVersion"], "0.0.1");
        assert_eq!(report["versionCheck"]["status"], "mismatch");
    }

    #[test]
    fn status_prefers_lock_resolution_and_reports_missing() {
        let dir = project(NEW_PROJECT_MANIFEST);
        let missing = status(dir.path()).unwrap();
        assert_eq!(missing["installed"], false);
        assert_eq!(missing["source"], "missing");

        apply(dir.path(), BridgeAction::Install, false).unwrap();
        fs::write(
            dir.path().join("Packages/packages-lock.json"),
            format!(r#"{{"dependencies":{{"{BRIDGE_PACKAGE}":{{"version":"9.9.9"}}}}}}"#),
        )
        .unwrap();
        let resolved = status(dir.path()).unwrap();
        assert_eq!(resolved["resolvedVersion"], "9.9.9");
        assert_eq!(resolved["versionCheck"]["status"], "mismatch");
    }

    #[test]
    fn resolve_project_root_walks_up_and_validates_explicit_path() {
        let dir = project(NEW_PROJECT_MANIFEST);
        let nested = dir.path().join("Assets/Scripts");
        fs::create_dir_all(&nested).unwrap();

        assert_eq!(resolve_project_root(None, &nested).unwrap(), dir.path());
        assert_eq!(
            resolve_project_root(Some(Path::new(".")), dir.path()).unwrap(),
            dir.path().join(".")
        );
        let outside = TempDir::new().unwrap();
        assert!(resolve_project_root(None, outside.path()).is_err());
        assert!(resolve_project_root(Some(outside.path()), dir.path()).is_err());
    }

    #[test]
    fn version_check_classifies_versions() {
        assert_eq!(version_check(Some(cli_version()))["status"], "match");
        assert_eq!(
            version_check(Some(&format!("v{}", cli_version())))["status"],
            "match"
        );
        assert_eq!(version_check(Some("0.0.1"))["status"], "mismatch");
        assert_eq!(version_check(Some("unknown"))["status"], "unknown");
        assert_eq!(version_check(None)["status"], "unknown");
    }

    #[test]
    fn rejects_malformed_manifest() {
        let dir = project("[]");
        assert!(apply(dir.path(), BridgeAction::Install, false).is_err());
        let dir = project(r#"{"dependencies":[]}"#);
        assert!(apply(dir.path(), BridgeAction::Install, false).is_err());
    }
}
