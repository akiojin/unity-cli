use std::fs;
use std::path::Path;

use anyhow::{anyhow, Context, Result};

const PROJECT_VERSION_REL_PATH: &str = "ProjectSettings/ProjectVersion.txt";
const EDITOR_VERSION_KEY: &str = "m_EditorVersion:";

const VERSION_BRANCH_MAP: &[(&str, &str)] = &[
    ("2020.3", "2020.3/staging"),
    ("2021.3", "2021.3/staging"),
    ("2022.3", "2022.3/staging"),
    ("2023.1", "2023.1/staging"),
    ("2023.2", "2023.2/staging"),
    ("6000.0", "6000.0/staging"),
];

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DetectedVersion {
    pub version: String,
    pub branch: String,
}

pub fn detect_from_project(project_root: &Path) -> Result<DetectedVersion> {
    let version = read_from_project(project_root)?;
    let branch = resolve_branch(&version)?;
    Ok(DetectedVersion { version, branch })
}

pub fn read_from_project(project_root: &Path) -> Result<String> {
    let path = project_root.join(PROJECT_VERSION_REL_PATH);
    let contents =
        fs::read_to_string(&path).with_context(|| format!("failed to read {}", path.display()))?;
    parse_editor_version(&contents).ok_or_else(|| {
        anyhow!(
            "{} does not contain a '{}' line",
            path.display(),
            EDITOR_VERSION_KEY
        )
    })
}

fn parse_editor_version(contents: &str) -> Option<String> {
    for line in contents.lines() {
        let trimmed = line.trim_start();
        if let Some(rest) = trimmed.strip_prefix(EDITOR_VERSION_KEY) {
            let value = rest.trim();
            if !value.is_empty() {
                return Some(value.to_string());
            }
        }
    }
    None
}

fn resolve_branch(version: &str) -> Result<String> {
    let minor_key = minor_version_key(version)?;
    for (prefix, branch) in VERSION_BRANCH_MAP {
        if minor_key == *prefix {
            return Ok((*branch).to_string());
        }
    }
    Err(anyhow!(
        "Unity version '{}' is not in the static branch map. Pass --branch <branch-name> explicitly to fetch.",
        version
    ))
}

fn minor_version_key(version: &str) -> Result<String> {
    let mut iter = version.splitn(3, '.');
    let major = iter
        .next()
        .ok_or_else(|| anyhow!("invalid Unity version '{}': missing major segment", version))?;
    let minor = iter
        .next()
        .ok_or_else(|| anyhow!("invalid Unity version '{}': missing minor segment", version))?;
    if major.is_empty() || minor.is_empty() {
        return Err(anyhow!("invalid Unity version '{}'", version));
    }
    Ok(format!("{major}.{minor}"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    fn write_project_version(dir: &Path, contents: &str) {
        let settings_dir = dir.join("ProjectSettings");
        fs::create_dir_all(&settings_dir).unwrap();
        fs::write(settings_dir.join("ProjectVersion.txt"), contents).unwrap();
    }

    #[test]
    fn detects_known_2023_2_lts() {
        let tmp = TempDir::new().unwrap();
        write_project_version(
            tmp.path(),
            "m_EditorVersion: 2023.2.20f1\nm_EditorVersionWithRevision: 2023.2.20f1 (foo)\n",
        );
        let detected = detect_from_project(tmp.path()).unwrap();
        assert_eq!(detected.version, "2023.2.20f1");
        assert_eq!(detected.branch, "2023.2/staging");
    }

    #[test]
    fn rejects_unknown_minor_with_branch_hint() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_EditorVersion: 9999.9.0f1\n");
        let err = detect_from_project(tmp.path()).unwrap_err();
        let message = format!("{err:#}");
        assert!(
            message.contains("9999.9"),
            "expected version in error: {message}"
        );
        assert!(
            message.contains("--branch"),
            "expected --branch hint in error: {message}"
        );
    }

    #[test]
    fn detect_returns_error_when_project_version_missing() {
        let tmp = TempDir::new().unwrap();
        let err = detect_from_project(tmp.path()).unwrap_err();
        assert!(format!("{err:#}").contains("failed to read"));
    }

    #[test]
    fn detect_returns_error_when_editor_version_line_absent() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_OtherKey: value\n");
        let err = detect_from_project(tmp.path()).unwrap_err();
        let message = format!("{err:#}");
        assert!(message.contains("m_EditorVersion"));
    }

    #[test]
    fn detect_returns_error_when_editor_version_value_empty() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_EditorVersion: \n");
        let err = detect_from_project(tmp.path()).unwrap_err();
        let message = format!("{err:#}");
        assert!(message.contains("m_EditorVersion"));
    }

    #[test]
    fn parse_editor_version_returns_value_directly() {
        assert_eq!(
            parse_editor_version("m_EditorVersion: 2022.3.10f1\n"),
            Some("2022.3.10f1".to_string())
        );
        assert_eq!(
            parse_editor_version("m_OtherKey: foo\nm_EditorVersion: 2022.3.10f1\n"),
            Some("2022.3.10f1".to_string())
        );
        assert_eq!(parse_editor_version(""), None);
        assert_eq!(parse_editor_version("m_EditorVersion: "), None);
    }

    #[test]
    fn resolve_branch_handles_supported_minors() {
        for (input, branch) in &[
            ("2020.3.0f1", "2020.3/staging"),
            ("2021.3.0f1", "2021.3/staging"),
            ("2022.3.5f1", "2022.3/staging"),
            ("2023.1.0f1", "2023.1/staging"),
            ("6000.0.0f1", "6000.0/staging"),
        ] {
            assert_eq!(resolve_branch(input).unwrap(), branch.to_string());
        }
    }

    #[test]
    fn minor_version_key_rejects_invalid_inputs() {
        assert!(minor_version_key("").is_err());
        assert!(minor_version_key("2022").is_err());
        assert!(minor_version_key(".3.0f1").is_err());
        assert!(minor_version_key("2022.").is_err());
    }
}
