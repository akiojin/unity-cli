//! Unity Editor discovery through the lockfiles the Bridge writes per Editor
//! process (`<editors dir>/<pid>.json`), and target selection by project path.

use std::fs;
use std::path::{Component, Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

/// A lockfile whose heartbeat is older than this is stale even when its pid is
/// alive (hung or recycled pid). The Bridge refreshes it every few seconds and
/// keeps it across domain reloads, so this only needs to cover a reload gap.
pub const HEARTBEAT_STALE_SECS: f64 = 120.0;

/// Exit code for `AMBIGUOUS_EDITOR`, matching the official Unity CLI.
pub const AMBIGUOUS_EDITOR_EXIT_CODE: i32 = 6;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorLock {
    pub pid: u32,
    pub project_path: String,
    pub host: String,
    pub port: u16,
    #[serde(default)]
    pub configured_port: Option<u16>,
    #[serde(default)]
    pub unity_version: Option<String>,
    #[serde(default)]
    pub bridge_version: Option<String>,
    #[serde(default)]
    pub state: Option<String>,
    #[serde(default)]
    pub heartbeat_at: f64,
}

#[derive(Debug, Clone)]
pub struct DiscoveredEditor {
    pub lock: EditorLock,
    pub lockfile: PathBuf,
    /// `Some(reason)` when the Editor behind the lockfile is gone.
    pub stale_reason: Option<String>,
}

impl DiscoveredEditor {
    pub fn is_live(&self) -> bool {
        self.stale_reason.is_none()
    }

    pub fn candidate_json(&self) -> Value {
        json!({
            "projectPath": self.lock.project_path,
            "host": self.lock.host,
            "port": self.lock.port,
            "pid": self.lock.pid,
            "unityVersion": self.lock.unity_version,
            "state": self.lock.state,
        })
    }
}

/// Target resolution failures that must stop before anything reaches an Editor.
#[derive(Debug, thiserror::Error)]
pub enum TargetError {
    #[error("{message}")]
    Ambiguous {
        message: String,
        candidates: Vec<DiscoveredEditor>,
    },
    #[error("{message}")]
    NotFound {
        message: String,
        candidates: Vec<DiscoveredEditor>,
    },
}

impl TargetError {
    pub fn code(&self) -> &'static str {
        match self {
            Self::Ambiguous { .. } => "AMBIGUOUS_EDITOR",
            Self::NotFound { .. } => "EDITOR_NOT_FOUND",
        }
    }

    pub fn exit_code(&self) -> i32 {
        match self {
            Self::Ambiguous { .. } => AMBIGUOUS_EDITOR_EXIT_CODE,
            Self::NotFound { .. } => 7,
        }
    }

    pub fn to_json(&self) -> Value {
        let (message, candidates) = match self {
            Self::Ambiguous {
                message,
                candidates,
            }
            | Self::NotFound {
                message,
                candidates,
            } => (message, candidates),
        };
        json!({
            "success": false,
            "error": { "code": self.code(), "message": message },
            "data": {
                "candidates": candidates.iter().map(DiscoveredEditor::candidate_json).collect::<Vec<_>>()
            }
        })
    }
}

pub fn editors_dir() -> Result<PathBuf> {
    if let Some(raw) = crate::core::config::read_env(&["UNITY_CLI_EDITORS_DIR"]) {
        return Ok(PathBuf::from(raw));
    }
    let home = dirs::home_dir().ok_or_else(|| anyhow!("Unable to resolve home directory"))?;
    Ok(home.join(".unity-cli").join("editors"))
}

/// Reads every lockfile. Unreadable or malformed files are skipped: a
/// half-written heartbeat must never break command routing.
pub fn discover() -> Vec<DiscoveredEditor> {
    let Ok(dir) = editors_dir() else {
        return Vec::new();
    };
    discover_in(&dir, unix_now())
}

fn discover_in(dir: &Path, now: f64) -> Vec<DiscoveredEditor> {
    let Ok(entries) = fs::read_dir(dir) else {
        return Vec::new();
    };
    let mut editors: Vec<DiscoveredEditor> = entries
        .filter_map(|entry| entry.ok().map(|entry| entry.path()))
        .filter(|path| path.extension().and_then(|ext| ext.to_str()) == Some("json"))
        .filter_map(|path| {
            let raw = fs::read_to_string(&path).ok()?;
            let lock: EditorLock = serde_json::from_str(&raw).ok()?;
            let stale_reason = stale_reason(&lock, now);
            Some(DiscoveredEditor {
                lock,
                lockfile: path,
                stale_reason,
            })
        })
        .collect();
    editors.sort_by(|a, b| {
        a.lock
            .project_path
            .cmp(&b.lock.project_path)
            .then(a.lock.port.cmp(&b.lock.port))
    });
    editors
}

fn stale_reason(lock: &EditorLock, now: f64) -> Option<String> {
    if !pid_alive(lock.pid) {
        return Some(format!("process {} is not running", lock.pid));
    }
    let age = now - lock.heartbeat_at;
    if age > HEARTBEAT_STALE_SECS {
        return Some(format!("heartbeat is {age:.0}s old"));
    }
    None
}

/// Live Editors whose project contains `path`, keeping only the deepest
/// project so nested projects win over their parents.
pub fn select_for_path<'a>(
    editors: &'a [DiscoveredEditor],
    path: &Path,
) -> Vec<&'a DiscoveredEditor> {
    let target = normalize(path);
    let matches: Vec<(usize, &DiscoveredEditor)> = editors
        .iter()
        .filter(|editor| editor.is_live())
        .filter_map(|editor| {
            let project = normalize(Path::new(&editor.lock.project_path));
            target
                .starts_with(&project)
                .then_some((project.components().count(), editor))
        })
        .collect();
    let Some(deepest) = matches.iter().map(|(depth, _)| *depth).max() else {
        return Vec::new();
    };
    matches
        .into_iter()
        .filter(|(depth, _)| *depth == deepest)
        .map(|(_, editor)| editor)
        .collect()
}

/// Absolute, symlink-resolved and (on case-insensitive platforms) lowercased
/// path, so `/tmp` vs `/private/tmp` or case differences still match.
fn normalize(path: &Path) -> PathBuf {
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()
            .map(|cwd| cwd.join(path))
            .unwrap_or_else(|_| path.to_path_buf())
    };
    let resolved = fs::canonicalize(&absolute).unwrap_or_else(|_| lexical_clean(&absolute));
    if cfg!(any(target_os = "macos", target_os = "windows")) {
        PathBuf::from(resolved.to_string_lossy().to_lowercase())
    } else {
        resolved
    }
}

fn lexical_clean(path: &Path) -> PathBuf {
    let mut cleaned = PathBuf::new();
    for component in path.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                cleaned.pop();
            }
            other => cleaned.push(other.as_os_str()),
        }
    }
    cleaned
}

#[cfg(unix)]
fn pid_alive(pid: u32) -> bool {
    let Ok(pid) = libc::pid_t::try_from(pid) else {
        return false;
    };
    if pid <= 0 {
        return false;
    }
    // SAFETY: signal 0 performs only the existence/permission check.
    if unsafe { libc::kill(pid, 0) } == 0 {
        return true;
    }
    std::io::Error::last_os_error().raw_os_error() == Some(libc::EPERM)
}

#[cfg(windows)]
fn pid_alive(pid: u32) -> bool {
    std::process::Command::new("tasklist")
        .args(["/FI", &format!("PID eq {pid}"), "/NH", "/FO", "CSV"])
        .output()
        .map(|output| String::from_utf8_lossy(&output.stdout).contains(&format!("\"{pid}\"")))
        .unwrap_or(true)
}

fn unix_now() -> f64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs_f64())
        .unwrap_or_default()
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    pub(crate) fn write_lock(dir: &Path, pid: u32, project: &Path, port: u16, heartbeat_at: f64) {
        fs::create_dir_all(dir).unwrap();
        let lock = json!({
            "schemaVersion": 1,
            "pid": pid,
            "projectPath": project.to_string_lossy(),
            "host": "127.0.0.1",
            "port": port,
            "configuredPort": 6400,
            "unityVersion": "6000.3.25f1",
            "bridgeVersion": "0.15.3",
            "state": "ready",
            "heartbeatAt": heartbeat_at,
        });
        fs::write(dir.join(format!("{pid}-{port}.json")), lock.to_string()).unwrap();
    }

    pub(crate) fn live_pid() -> u32 {
        std::process::id()
    }

    pub(crate) fn dead_pid() -> u32 {
        let mut child = std::process::Command::new(if cfg!(windows) { "cmd" } else { "true" })
            .args(if cfg!(windows) {
                &["/C", "exit"][..]
            } else {
                &[][..]
            })
            .spawn()
            .unwrap();
        let pid = child.id();
        child.wait().unwrap();
        pid
    }

    #[test]
    fn discover_reads_lockfiles_and_skips_garbage() {
        let dir = tempfile::tempdir().unwrap();
        let project = dir.path().join("ProjectA");
        write_lock(dir.path(), live_pid(), &project, 6401, unix_now());
        fs::write(dir.path().join("broken.json"), "{not json").unwrap();
        fs::write(dir.path().join("note.txt"), "ignored").unwrap();

        let editors = discover_in(dir.path(), unix_now());

        assert_eq!(editors.len(), 1);
        assert_eq!(editors[0].lock.port, 6401);
        assert_eq!(
            editors[0].lock.unity_version.as_deref(),
            Some("6000.3.25f1")
        );
        assert!(editors[0].is_live());
    }

    #[test]
    fn dead_pid_marks_lockfile_stale() {
        let dir = tempfile::tempdir().unwrap();
        write_lock(
            dir.path(),
            dead_pid(),
            &dir.path().join("P"),
            6400,
            unix_now(),
        );

        let editors = discover_in(dir.path(), unix_now());

        let reason = editors[0].stale_reason.as_deref().expect("should be stale");
        assert!(reason.contains("not running"), "{reason}");
    }

    #[test]
    fn expired_heartbeat_marks_lockfile_stale() {
        let dir = tempfile::tempdir().unwrap();
        let now = unix_now();
        write_lock(
            dir.path(),
            live_pid(),
            &dir.path().join("P"),
            6400,
            now - HEARTBEAT_STALE_SECS - 1.0,
        );

        let editors = discover_in(dir.path(), now);

        let reason = editors[0].stale_reason.as_deref().expect("should be stale");
        assert!(reason.contains("heartbeat"), "{reason}");
    }

    #[test]
    fn select_for_path_prefers_deepest_live_project() {
        let dir = tempfile::tempdir().unwrap();
        let outer = dir.path().join("Outer");
        let inner = outer.join("Nested").join("Inner");
        let other = dir.path().join("Other");
        for path in [&outer, &inner, &other] {
            fs::create_dir_all(path.join("Assets")).unwrap();
        }
        let locks = dir.path().join("locks");
        write_lock(&locks, live_pid(), &outer, 6400, unix_now());
        write_lock(&locks, live_pid(), &inner, 6401, unix_now());
        write_lock(&locks, live_pid(), &other, 6402, unix_now());
        let editors = discover_in(&locks, unix_now());

        let deep = select_for_path(&editors, &inner.join("Assets"));
        assert_eq!(deep.len(), 1);
        assert_eq!(deep[0].lock.port, 6401);

        let shallow = select_for_path(&editors, &outer.join("Assets"));
        assert_eq!(shallow.len(), 1);
        assert_eq!(shallow[0].lock.port, 6400);

        assert!(select_for_path(&editors, dir.path()).is_empty());
    }

    #[test]
    fn select_for_path_ignores_stale_editors_and_sibling_prefixes() {
        let dir = tempfile::tempdir().unwrap();
        let project = dir.path().join("Game");
        let sibling = dir.path().join("GameTools");
        fs::create_dir_all(&project).unwrap();
        fs::create_dir_all(&sibling).unwrap();
        let locks = dir.path().join("locks");
        write_lock(&locks, dead_pid(), &project, 6400, unix_now());
        let editors = discover_in(&locks, unix_now());

        assert!(select_for_path(&editors, &project).is_empty());
        assert!(select_for_path(&editors, &sibling).is_empty());
    }

    #[test]
    fn ambiguous_error_json_lists_candidates() {
        let dir = tempfile::tempdir().unwrap();
        write_lock(
            dir.path(),
            live_pid(),
            &dir.path().join("A"),
            6400,
            unix_now(),
        );
        write_lock(
            dir.path(),
            live_pid(),
            &dir.path().join("B"),
            6401,
            unix_now(),
        );
        let editors = discover_in(dir.path(), unix_now());
        let error = TargetError::Ambiguous {
            message: "ambiguous".into(),
            candidates: editors,
        };

        let value = error.to_json();

        assert_eq!(error.exit_code(), AMBIGUOUS_EDITOR_EXIT_CODE);
        assert_eq!(value["error"]["code"], "AMBIGUOUS_EDITOR");
        let candidates = value["data"]["candidates"].as_array().unwrap();
        assert_eq!(candidates.len(), 2);
        assert_eq!(candidates[0]["port"], 6400);
        assert_eq!(candidates[0]["pid"], live_pid());
        assert!(candidates[0]["projectPath"]
            .as_str()
            .unwrap()
            .ends_with("A"));
    }
}
