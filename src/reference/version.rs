use std::fs;
use std::path::Path;

use anyhow::{anyhow, Context, Result};

const PROJECT_VERSION_REL_PATH: &str = "ProjectSettings/ProjectVersion.txt";
const EDITOR_VERSION_KEY: &str = "m_EditorVersion:";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PublishedRef {
    pub name: String,
    pub commit_sha: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RefSelection {
    pub source_ref: String,
    pub commit_sha: String,
    pub exact_match: bool,
    pub selection_reason: String,
}

/// Parse heads and tags, retaining the commit rather than an annotated tag object.
pub fn parse_ls_remote(text: &str) -> Result<Vec<PublishedRef>> {
    let mut refs = std::collections::BTreeMap::new();
    let mut peeled = std::collections::BTreeMap::new();
    for line in text.lines().filter(|line| !line.trim().is_empty()) {
        let fields: Vec<_> = line.split_whitespace().collect();
        if fields.len() != 2
            || fields[0].len() != 40
            || !fields[0].bytes().all(|byte| byte.is_ascii_hexdigit())
        {
            return Err(anyhow!("invalid git ls-remote output: {line}"));
        }
        let (sha, name) = (fields[0], fields[1]);
        if !name.starts_with("refs/heads/") && !name.starts_with("refs/tags/") {
            continue;
        }
        if let Some(tag) = name.strip_suffix("^{}") {
            if tag.starts_with("refs/tags/") {
                peeled.insert(tag.to_string(), sha.to_string());
            }
        } else {
            refs.insert(name.to_string(), sha.to_string());
        }
    }
    for (name, sha) in peeled {
        refs.insert(name, sha);
    }
    Ok(refs
        .into_iter()
        .map(|(name, commit_sha)| PublishedRef { name, commit_sha })
        .collect())
}

pub fn select_ref(
    version: &str,
    explicit: Option<&str>,
    refs: &[PublishedRef],
) -> Result<RefSelection> {
    let exact_tag = format!("refs/tags/{version}");
    let exact_head = format!("refs/heads/{version}");
    let (candidates, reason): (Vec<_>, _) = if let Some(requested) = explicit {
        (
            refs.iter()
                .filter(|reference| {
                    reference.name == requested
                        || reference.name.strip_prefix("refs/heads/") == Some(requested)
                        || reference.name.strip_prefix("refs/tags/") == Some(requested)
                })
                .collect(),
            "explicit ref requested",
        )
    } else {
        let exact: Vec<_> = refs
            .iter()
            .filter(|reference| reference.name == exact_tag || reference.name == exact_head)
            .collect();
        if !exact.is_empty() {
            (exact, "published ref named for the requested version")
        } else {
            let minor = minor_version_key(version)?;
            let minor_head = format!("refs/heads/{minor}");
            let staging = format!("refs/heads/{minor}/staging");
            (
                refs.iter()
                    .filter(|reference| reference.name == minor_head || reference.name == staging)
                    .collect(),
                "same minor branch; exact patch match is not guaranteed",
            )
        }
    };
    if candidates.len() > 1 {
        return Err(anyhow!("ambiguous published refs for Unity {version}: {}. Pass --branch <full-ref> explicitly.", candidates.iter().map(|reference| reference.name.as_str()).collect::<Vec<_>>().join(", ")));
    }
    let selected = candidates.first().ok_or_else(|| {
        let available = refs.iter().filter(|reference| reference.name.starts_with("refs/heads/")).map(|reference| reference.name.as_str()).collect::<Vec<_>>().join(", ");
        anyhow!("no published ref matches Unity {version}{}; available branches: [{}]. Pass --branch <ref> explicitly.", explicit.map(|name| format!(" (explicit ref {name})")).unwrap_or_default(), available)
    })?;
    let exact_match = selected.name == exact_tag;
    let selection_reason = if selected.name.starts_with("refs/heads/") {
        format!("{reason}; branch content can change and does not guarantee an exact version match")
    } else if exact_match {
        format!("{reason}; exact version tag")
    } else {
        format!("{reason}; tag does not match the requested version")
    };
    Ok(RefSelection {
        source_ref: selected.name.clone(),
        commit_sha: selected.commit_sha.clone(),
        exact_match,
        selection_reason,
    })
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

fn minor_version_key(version: &str) -> Result<String> {
    let mut iter = version.splitn(3, '.');
    let major = iter
        .next()
        .ok_or_else(|| anyhow!("invalid Unity version '{}': missing major segment", version))?;
    let minor = iter
        .next()
        .ok_or_else(|| anyhow!("invalid Unity version '{}': missing minor segment", version))?;
    if major.is_empty()
        || minor.is_empty()
        || !major.bytes().all(|byte| byte.is_ascii_digit())
        || !minor.bytes().all(|byte| byte.is_ascii_digit())
    {
        return Err(anyhow!("invalid Unity version '{}'", version));
    }
    Ok(format!("{major}.{minor}"))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    fn published(names: &[&str]) -> Vec<PublishedRef> {
        names
            .iter()
            .map(|name| PublishedRef {
                name: (*name).to_string(),
                commit_sha: "a".repeat(40),
            })
            .collect()
    }

    #[test]
    fn selects_new_and_existing_minor_branches_without_claiming_exact_match() {
        for (version, branch) in [
            ("6000.4.12f1", "refs/heads/6000.4"),
            ("2022.3.5f1", "refs/heads/2022.3/staging"),
        ] {
            let selected = select_ref(version, None, &published(&[branch])).unwrap();
            assert_eq!(selected.source_ref, branch);
            assert!(!selected.exact_match);
            assert!(selected.selection_reason.contains("minor"));
        }
    }

    #[test]
    fn exact_tag_precedes_minor_but_version_named_head_is_not_exact() {
        let refs = published(&["refs/heads/6000.4", "refs/tags/6000.4.12f1"]);
        let selected = select_ref("6000.4.12f1", None, &refs).unwrap();
        assert_eq!(selected.source_ref, "refs/tags/6000.4.12f1");
        assert!(selected.exact_match);
        let selected =
            select_ref("6000.4.12f1", None, &published(&["refs/heads/6000.4.12f1"])).unwrap();
        assert!(!selected.exact_match);
        assert!(selected.selection_reason.contains("branch"));
    }

    #[test]
    fn explicit_ref_has_priority_and_ambiguous_short_names_require_full_ref() {
        let refs = published(&[
            "refs/heads/custom",
            "refs/tags/custom",
            "refs/tags/6000.4.12f1",
        ]);
        assert!(select_ref("6000.4.12f1", Some("custom"), &refs)
            .unwrap_err()
            .to_string()
            .contains("ambiguous"));
        let selected = select_ref("6000.4.12f1", Some("refs/heads/custom"), &refs).unwrap();
        assert_eq!(selected.source_ref, "refs/heads/custom");
        assert!(!selected.exact_match);
        assert!(selected.selection_reason.contains("explicit"));
        assert!(select_ref("6000.4.12f1", Some("missing"), &refs).is_err());
    }

    #[test]
    fn ambiguous_auto_candidates_and_unknown_versions_return_candidates() {
        for names in [
            vec!["refs/heads/6000.4", "refs/heads/6000.4/staging"],
            vec!["refs/heads/6000.4.12f1", "refs/tags/6000.4.12f1"],
        ] {
            let message = select_ref("6000.4.12f1", None, &published(&names))
                .unwrap_err()
                .to_string();
            assert!(message.contains("ambiguous"));
            for name in names {
                assert!(message.contains(name));
            }
        }
        let message = select_ref("9999.9.0f1", None, &published(&["refs/heads/6000.4"]))
            .unwrap_err()
            .to_string();
        assert!(message.contains("refs/heads/6000.4"));
        assert!(message.contains("--branch"));
    }

    #[test]
    fn parses_remote_refs_with_peeled_tag_commits_in_any_order() {
        let object = "a".repeat(40);
        let commit = "b".repeat(40);
        for output in [
            format!("{object}\trefs/tags/6000.4.12f1\n{commit}\trefs/tags/6000.4.12f1^{{}}\n"),
            format!("{commit}\trefs/tags/6000.4.12f1^{{}}\n{object}\trefs/tags/6000.4.12f1\n"),
        ] {
            let refs = parse_ls_remote(&output).unwrap();
            assert_eq!(refs.len(), 1);
            assert_eq!(refs[0].commit_sha, commit);
        }
        assert!(parse_ls_remote("not a ref").is_err());
        assert!(parse_ls_remote("bogus\trefs/heads/main").is_err());
        assert!(parse_ls_remote(&format!("{}\trefs/heads/main", "a".repeat(64))).is_err());
        assert!(parse_ls_remote("").unwrap().is_empty());
    }

    fn write_project_version(dir: &Path, contents: &str) {
        let settings_dir = dir.join("ProjectSettings");
        fs::create_dir_all(&settings_dir).unwrap();
        fs::write(settings_dir.join("ProjectVersion.txt"), contents).unwrap();
    }

    #[test]
    fn reads_known_project_version() {
        let tmp = TempDir::new().unwrap();
        write_project_version(
            tmp.path(),
            "m_EditorVersion: 2023.2.20f1\nm_EditorVersionWithRevision: 2023.2.20f1 (foo)\n",
        );
        assert_eq!(read_from_project(tmp.path()).unwrap(), "2023.2.20f1");
    }

    #[test]
    fn reads_unknown_minor_without_static_mapping() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_EditorVersion: 9999.9.0f1\n");
        assert_eq!(read_from_project(tmp.path()).unwrap(), "9999.9.0f1");
    }

    #[test]
    fn detect_returns_error_when_project_version_missing() {
        let tmp = TempDir::new().unwrap();
        let err = read_from_project(tmp.path()).unwrap_err();
        assert!(format!("{err:#}").contains("failed to read"));
    }

    #[test]
    fn detect_returns_error_when_editor_version_line_absent() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_OtherKey: value\n");
        let err = read_from_project(tmp.path()).unwrap_err();
        let message = format!("{err:#}");
        assert!(message.contains("m_EditorVersion"));
    }

    #[test]
    fn detect_returns_error_when_editor_version_value_empty() {
        let tmp = TempDir::new().unwrap();
        write_project_version(tmp.path(), "m_EditorVersion: \n");
        let err = read_from_project(tmp.path()).unwrap_err();
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
    fn public_resolver_handles_previously_supported_minors() {
        for (input, branch) in &[
            ("2020.3.0f1", "2020.3/staging"),
            ("2021.3.0f1", "2021.3/staging"),
            ("2022.3.5f1", "2022.3/staging"),
            ("2023.1.0f1", "2023.1/staging"),
            ("6000.0.0f1", "6000.0/staging"),
        ] {
            let full_ref = format!("refs/heads/{branch}");
            assert_eq!(
                select_ref(input, None, &published(&[&full_ref]))
                    .unwrap()
                    .source_ref,
                full_ref
            );
        }
    }

    #[test]
    fn minor_version_key_rejects_invalid_inputs() {
        assert!(minor_version_key("").is_err());
        assert!(minor_version_key("2022").is_err());
        assert!(minor_version_key(".3.0f1").is_err());
        assert!(minor_version_key("2022.").is_err());
        assert!(minor_version_key("invalid.4.12f1").is_err());
        assert!(minor_version_key("6000.bad.12f1").is_err());
    }
}
