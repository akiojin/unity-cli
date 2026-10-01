//! Embedded skill distribution. Planning validates all destinations before writing.

use anyhow::{bail, ensure, Context, Result};
use clap::ValueEnum;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::{BTreeMap, BTreeSet},
    fs,
    io::Write,
    path::{Component, Path, PathBuf},
};

include!(concat!(env!("OUT_DIR"), "/embedded_skills.rs"));
const LEDGER: &str = ".unity-cli-skills.json";

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum SkillClient {
    ClaudeCode,
    Cursor,
    Windsurf,
    Vscode,
}

impl SkillClient {
    const ALL: [Self; 4] = [Self::ClaudeCode, Self::Cursor, Self::Windsurf, Self::Vscode];

    fn directory(self, local: bool) -> &'static str {
        match (self, local) {
            (Self::ClaudeCode, _) => ".claude/skills",
            (Self::Cursor, _) => ".cursor/skills",
            (Self::Windsurf, true) => ".windsurf/skills",
            (Self::Windsurf, false) => ".codeium/windsurf/skills",
            (Self::Vscode, true) => ".github/skills",
            (Self::Vscode, false) => ".copilot/skills",
        }
    }
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
struct Ledger {
    schema_version: u32,
    client: SkillClient,
    version: String,
    files: BTreeMap<String, String>,
}

struct Plan {
    root: PathBuf,
    writes: BTreeMap<PathBuf, Vec<u8>>,
    removals: Vec<PathBuf>,
    ledger: Vec<u8>,
    ledger_changed: bool,
}

fn hash(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

fn scope_root(local: bool) -> Result<PathBuf> {
    let root = if local {
        std::env::current_dir()?
    } else {
        dirs::home_dir().context("cannot determine the user home directory")?
    };
    ensure!(
        !fs::symlink_metadata(&root)?.file_type().is_symlink(),
        "refusing symlink: {}",
        root.display()
    );
    // Resolve OS aliases (e.g. macOS /var) once. All client paths below this
    // trusted scope are inspected without following symlinks.
    root.canonicalize()
        .context("resolve skill installation scope")
}

/// Inspect every component below the scope; a dangling symlink also fails.
fn check_path(scope: &Path, path: &Path, directory: bool) -> Result<bool> {
    let relative = path
        .strip_prefix(scope)
        .context("destination escapes installation scope")?;
    let components: Vec<_> = relative.components().collect();
    let mut current = scope.to_path_buf();
    for (index, component) in components.iter().enumerate() {
        ensure!(
            matches!(component, Component::Normal(_)),
            "invalid destination path"
        );
        current.push(component);
        let metadata = match fs::symlink_metadata(&current) {
            Ok(metadata) => metadata,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(false),
            Err(error) => {
                return Err(error).with_context(|| format!("inspect {}", current.display()))
            }
        };
        ensure!(
            !metadata.file_type().is_symlink(),
            "refusing symlink: {}",
            current.display()
        );
        if directory || index + 1 < components.len() {
            ensure!(
                metadata.is_dir(),
                "expected directory: {}",
                current.display()
            );
        } else {
            ensure!(
                metadata.is_file(),
                "expected regular file: {}",
                current.display()
            );
        }
    }
    Ok(true)
}

fn validate_entry(name: &str, digest: &str) -> Result<()> {
    let parts: Vec<_> = name.split('/').collect();
    ensure!(
        parts.len() >= 2
            && parts[0].starts_with("unity-")
            && parts
                .iter()
                .all(|part| !part.is_empty() && *part != "." && *part != "..")
            && !name.contains(['\\', ':', '\0']),
        "invalid ledger path: {name}"
    );
    ensure!(
        digest.len() == 64 && digest.bytes().all(|byte| byte.is_ascii_hexdigit()),
        "invalid ledger hash for {name}"
    );
    Ok(())
}

fn read_ledger(scope: &Path, root: &Path, client: SkillClient) -> Result<Option<Ledger>> {
    let path = root.join(LEDGER);
    if !check_path(scope, &path, false)? {
        return Ok(None);
    }
    let ledger: Ledger = serde_json::from_slice(&fs::read(&path)?)
        .with_context(|| format!("invalid skill ownership ledger: {}", path.display()))?;
    ensure!(
        ledger.schema_version == 1 && ledger.client == client,
        "incompatible skill ownership ledger: {}",
        path.display()
    );
    for (name, digest) in &ledger.files {
        validate_entry(name, digest)?;
    }
    Ok(Some(ledger))
}

fn plan(
    scope: &Path,
    client: SkillClient,
    local: bool,
    force: bool,
    refresh: bool,
) -> Result<Plan> {
    let root = scope.join(client.directory(local));
    check_path(scope, &root, true)?;
    let previous = read_ledger(scope, &root, client)?;
    ensure!(
        !refresh || previous.is_some(),
        "no installed skills at {}; run skills install first",
        root.display()
    );
    let old = previous.map(|ledger| ledger.files).unwrap_or_default();
    let files: BTreeMap<String, String> = FILES
        .iter()
        .map(|(name, bytes)| ((*name).to_owned(), hash(bytes)))
        .collect();
    let skill_names: BTreeSet<_> = files
        .keys()
        .chain(old.keys())
        .map(|name| name.split('/').next().unwrap())
        .collect();
    for name in skill_names {
        let skill = root.join(name);
        if check_path(scope, &skill, true)? {
            ensure!(
                force || old.keys().any(|path| path.starts_with(&format!("{name}/"))),
                "unowned skill directory: {}; use --force to replace bundled files",
                skill.display()
            );
            for entry in walkdir::WalkDir::new(&skill).follow_links(false) {
                let entry = entry?;
                ensure!(
                    !entry.file_type().is_symlink(),
                    "refusing symlink: {}",
                    entry.path().display()
                );
            }
        }
    }
    let mut writes = BTreeMap::new();
    for (name, bytes) in FILES {
        let path = root.join(name);
        if check_path(scope, &path, false)? {
            let current = fs::read(&path)?;
            ensure!(
                force || old.contains_key(*name),
                "unowned skill file: {}; use --force",
                path.display()
            );
            if current == *bytes {
                continue;
            }
            ensure!(
                force || old.get(*name) == Some(&hash(&current)),
                "locally modified skill file: {}; use --force",
                path.display()
            );
        }
        writes.insert(path, bytes.to_vec());
    }
    let mut removals = Vec::new();
    for (name, expected) in &old {
        if !files.contains_key(name) {
            let path = root.join(name);
            if check_path(scope, &path, false)? {
                ensure!(
                    force || hash(&fs::read(&path)?) == *expected,
                    "locally modified retired skill file: {}; use --force",
                    path.display()
                );
                removals.push(path);
            }
        }
    }
    let ledger = serde_json::to_vec_pretty(&Ledger {
        schema_version: 1,
        client,
        version: env!("CARGO_PKG_VERSION").to_owned(),
        files,
    })?;
    let ledger_changed = match fs::read(root.join(LEDGER)) {
        Ok(current) => current != ledger,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => true,
        Err(error) => return Err(error.into()),
    };
    Ok(Plan {
        root,
        writes,
        removals,
        ledger,
        ledger_changed,
    })
}

fn replace_file(scope: &Path, path: &Path, bytes: &[u8]) -> Result<()> {
    check_path(scope, path, false)?;
    let parent = path.parent().context("file has no parent")?;
    fs::create_dir_all(parent)?;
    // Same-directory rename avoids truncating existing files (including hardlinks),
    // and makes each file and the final ledger independently atomic.
    let mut temporary = tempfile::NamedTempFile::new_in(parent)?;
    temporary.write_all(bytes)?;
    temporary.as_file().sync_all()?;
    temporary
        .persist(path)
        .with_context(|| format!("replace {}", path.display()))?;
    Ok(())
}

/// Returns the paths written or removed. Dry-run performs the same validation
/// without creating directories, lock files, temporary files, or the ledger.
pub fn distribute(
    client: Option<SkillClient>,
    local: bool,
    force: bool,
    dry_run: bool,
    refresh: bool,
) -> Result<Vec<PathBuf>> {
    let scope = scope_root(local)?;
    let clients: Vec<_> = match client {
        Some(client) => vec![client],
        None => {
            let mut installed = Vec::new();
            for client in SkillClient::ALL {
                if read_ledger(&scope, &scope.join(client.directory(local)), client)?.is_some() {
                    installed.push(client);
                }
            }
            ensure!(
                !installed.is_empty(),
                "no installed skills in this scope; run skills install first"
            );
            installed
        }
    };
    let plans = clients
        .iter()
        .map(|client| plan(&scope, *client, local, force, refresh))
        .collect::<Result<Vec<_>>>()?;
    let mut paths = Vec::new();
    for plan in plans {
        paths.extend(plan.writes.keys().cloned());
        paths.extend(plan.removals.iter().cloned());
        if plan.ledger_changed {
            paths.push(plan.root.join(LEDGER));
        }
        if !dry_run {
            for (path, bytes) in &plan.writes {
                replace_file(&scope, path, bytes)?;
            }
            for path in &plan.removals {
                check_path(&scope, path, false)?;
                fs::remove_file(path)?;
            }
            if plan.ledger_changed {
                replace_file(&scope, &plan.root.join(LEDGER), &plan.ledger)?;
            }
        }
    }
    paths.sort();
    Ok(paths)
}

pub fn show(name: Option<&str>) -> Result<String> {
    if let Some(name) = name {
        let key = format!("{name}/SKILL.md");
        let Some((_, bytes)) = FILES.iter().find(|(path, _)| *path == key) else {
            bail!("unknown embedded skill: {name}");
        };
        Ok(std::str::from_utf8(bytes)?.to_owned())
    } else {
        Ok(FILES
            .iter()
            .filter_map(|(path, _)| path.strip_suffix("/SKILL.md"))
            .collect::<Vec<_>>()
            .join("\n"))
    }
}
