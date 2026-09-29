use std::fs;
use std::path::PathBuf;
use std::time::SystemTime;

use anyhow::{anyhow, Context, Result};
use serde::{Deserialize, Serialize};

const REFERENCE_SUBDIR: &str = "UnityCsReference";
const META_FILE_NAME: &str = ".unity-cli-meta.json";

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CacheMeta {
    pub version: String,
    pub branch: String,
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub commit_sha: Option<String>,
    #[serde(default)]
    pub source_ref: Option<String>,
    #[serde(default)]
    pub exact_match: Option<bool>,
    #[serde(default)]
    pub selection_reason: Option<String>,
    pub fetched_at: String,
    pub source_url: String,
}

pub fn reference_root() -> Result<PathBuf> {
    Ok(crate::core::managed_binaries::cache_root()?.join(REFERENCE_SUBDIR))
}

pub fn version_dir(version: &str) -> Result<PathBuf> {
    if version.trim().is_empty()
        || version == "."
        || version == ".."
        || version.contains(['/', '\\', ':', '\0'])
    {
        return Err(anyhow!(
            "version must be a non-empty cache name, not a path"
        ));
    }
    Ok(reference_root()?.join(version))
}

pub fn read_meta(version: &str) -> Result<CacheMeta> {
    let path = version_dir(version)?.join(META_FILE_NAME);
    let contents =
        fs::read_to_string(&path).with_context(|| format!("failed to read {}", path.display()))?;
    let meta: CacheMeta = serde_json::from_str(&contents)
        .with_context(|| format!("failed to parse {}", path.display()))?;
    Ok(meta)
}

pub fn write_meta(meta: &CacheMeta) -> Result<()> {
    let dir = version_dir(&meta.version)?;
    fs::create_dir_all(&dir).with_context(|| format!("failed to create {}", dir.display()))?;
    let path = dir.join(META_FILE_NAME);
    let contents = serde_json::to_string_pretty(meta)
        .with_context(|| format!("failed to serialize meta for {}", meta.version))?;
    fs::write(&path, contents).with_context(|| format!("failed to write {}", path.display()))?;
    Ok(())
}

pub fn list_versions() -> Result<Vec<String>> {
    let root = reference_root()?;
    if !root.exists() {
        return Ok(Vec::new());
    }
    let mut versions = Vec::new();
    for entry in
        fs::read_dir(&root).with_context(|| format!("failed to read {}", root.display()))?
    {
        let entry = entry?;
        if !entry.file_type()?.is_dir() {
            continue;
        }
        if let Some(name) = entry.file_name().to_str() {
            versions.push(name.to_string());
        }
    }
    versions.sort();
    Ok(versions)
}

pub fn gc(keep: usize, dry_run: bool) -> Result<Vec<PathBuf>> {
    let root = reference_root()?;
    if !root.exists() {
        return Ok(Vec::new());
    }
    let mut entries: Vec<(PathBuf, SystemTime)> = Vec::new();
    for entry in
        fs::read_dir(&root).with_context(|| format!("failed to read {}", root.display()))?
    {
        let entry = entry?;
        if !entry.file_type()?.is_dir() {
            continue;
        }
        let meta = entry.metadata()?;
        let mtime = meta.modified()?;
        entries.push((entry.path(), mtime));
    }
    entries.sort_by_key(|entry| std::cmp::Reverse(entry.1));
    let removed: Vec<PathBuf> = entries.into_iter().skip(keep).map(|(p, _)| p).collect();
    if !dry_run {
        for path in &removed {
            fs::remove_dir_all(path)
                .with_context(|| format!("failed to remove {}", path.display()))?;
        }
    }
    Ok(removed)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::env;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    struct EnvVarGuard {
        key: &'static str,
        previous: Option<String>,
    }

    impl EnvVarGuard {
        fn set(key: &'static str, value: &str) -> Self {
            let previous = env::var(key).ok();
            env::set_var(key, value);
            Self { key, previous }
        }
    }

    impl Drop for EnvVarGuard {
        fn drop(&mut self) {
            if let Some(value) = &self.previous {
                env::set_var(self.key, value);
            } else {
                env::remove_var(self.key);
            }
        }
    }

    fn unique_temp_path(label: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or(Duration::from_secs(0))
            .as_nanos();
        env::temp_dir().join(format!("unity-cli-reference-{label}-{nanos}"))
    }

    #[test]
    fn version_dir_under_cache_root() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("vdir");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let dir = version_dir("2023.2.20f1").expect("version_dir resolves");
        assert_eq!(dir, root.join("UnityCsReference").join("2023.2.20f1"));
    }

    #[test]
    fn version_dir_rejects_empty() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("vdir-empty");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = version_dir("").unwrap_err();
        assert!(format!("{err:#}").contains("version"));
    }

    #[test]
    fn version_dir_rejects_paths_outside_single_cache_entry() {
        for version in [
            "/tmp/other",
            "../other",
            "a/b",
            "a\\b",
            ".",
            "..",
            "C:\\other",
        ] {
            assert!(
                version_dir(version).is_err(),
                "accepted unsafe version: {version}"
            );
        }
    }

    #[test]
    fn meta_roundtrip() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("meta");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let meta = CacheMeta {
            version: "2023.2.20f1".to_string(),
            branch: "2023.2/staging".to_string(),
            commit_sha: Some("abcdef".to_string()),
            source_ref: None,
            exact_match: None,
            selection_reason: None,
            fetched_at: "2026-05-11T11:00:00Z".to_string(),
            source_url: "https://github.com/Unity-Technologies/UnityCsReference.git".to_string(),
        };
        write_meta(&meta).expect("write meta");
        let loaded = read_meta(&meta.version).expect("read meta");
        assert_eq!(loaded, meta);
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn list_versions_returns_existing_dirs() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("list");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        for v in &["2022.3.0f1", "2023.2.20f1", "2024.0.0f1"] {
            fs::create_dir_all(base.join(v)).unwrap();
        }
        let listed = list_versions().expect("list_versions resolves");
        assert_eq!(
            listed,
            vec![
                "2022.3.0f1".to_string(),
                "2023.2.20f1".to_string(),
                "2024.0.0f1".to_string(),
            ]
        );
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn gc_keeps_n_newest_and_returns_removed() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("gc");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        for v in &["a", "b", "c"] {
            fs::create_dir_all(base.join(v)).unwrap();
        }
        let removed_dry = gc(1, true).expect("gc dry_run resolves");
        assert_eq!(removed_dry.len(), 2);
        assert!(base.join("a").exists() && base.join("b").exists() && base.join("c").exists());
        let removed = gc(1, false).expect("gc actual resolves");
        assert_eq!(removed.len(), 2);
        let remaining: Vec<String> = fs::read_dir(&base)
            .unwrap()
            .filter_map(|e| e.ok().map(|e| e.file_name().to_string_lossy().to_string()))
            .collect();
        assert_eq!(remaining.len(), 1);
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn list_versions_empty_when_root_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("list-missing");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        assert!(list_versions().unwrap().is_empty());
    }

    #[test]
    fn gc_keep_zero_removes_all_versions() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("gc-zero");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        for v in &["x", "y"] {
            fs::create_dir_all(base.join(v)).unwrap();
        }
        let removed = gc(0, false).unwrap();
        assert_eq!(removed.len(), 2);
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn gc_skips_non_directory_entries() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("gc-mixed");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        fs::create_dir_all(&base).unwrap();
        fs::create_dir_all(base.join("v1")).unwrap();
        fs::write(base.join("stray.txt"), b"ignore").unwrap();
        let removed = gc(1, true).unwrap();
        assert!(removed.is_empty());
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn gc_empty_root_returns_empty() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("gc-empty");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let removed = gc(1, true).unwrap();
        assert!(removed.is_empty());
    }

    #[test]
    fn read_meta_returns_error_when_missing() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("read-meta-miss");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let err = read_meta("not-fetched").unwrap_err();
        assert!(format!("{err:#}").contains("failed to read"));
    }

    #[test]
    fn read_meta_returns_error_for_invalid_json() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("read-meta-bad");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let dir = version_dir("bad-version").unwrap();
        fs::create_dir_all(&dir).unwrap();
        fs::write(dir.join(".unity-cli-meta.json"), "not json {").unwrap();
        let err = read_meta("bad-version").unwrap_err();
        assert!(format!("{err:#}").contains("failed to parse"));
        let _ = fs::remove_dir_all(&root);
    }

    #[test]
    fn list_versions_skips_files_in_root() {
        let _guard = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let root = unique_temp_path("list-files");
        let _env = EnvVarGuard::set("UNITY_CLI_CACHE_ROOT", root.to_str().unwrap());
        let base = root.join("UnityCsReference");
        fs::create_dir_all(&base).unwrap();
        fs::create_dir_all(base.join("v1")).unwrap();
        fs::write(base.join("stray.txt"), b"x").unwrap();
        let listed = list_versions().unwrap();
        assert_eq!(listed, vec!["v1".to_string()]);
        let _ = fs::remove_dir_all(&root);
    }
}
