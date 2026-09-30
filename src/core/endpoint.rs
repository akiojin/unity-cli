use std::path::{Path, PathBuf};

use anyhow::Result;

use crate::core::config::{default_host, default_port};
use crate::core::editor_discovery::{self, DiscoveredEditor, TargetError};
use crate::core::instances;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ResolvedEndpoint {
    pub host: String,
    pub port: u16,
}

/// Resolves the Unity endpoint for a command. Precedence:
/// 1. explicit port (`--port` / `UNITY_CLI_PORT`), then explicit host alone
/// 2. project path (`--project-path` / `UNITY_PROJECT_ROOT`) via lockfiles
/// 3. the `instances set-active` selection
/// 4. the deepest running project containing the current directory
/// 5. the only running Editor; several running Editors are `AMBIGUOUS_EDITOR`
/// 6. the default endpoint (Bridge versions without lockfiles)
pub fn resolve_endpoint(
    host_override: Option<String>,
    port_override: Option<u16>,
    project_path_override: Option<PathBuf>,
) -> Result<ResolvedEndpoint> {
    // Explicit endpoints never read lockfiles or the working directory.
    if let Some(endpoint) = explicit_endpoint(host_override.clone(), port_override) {
        return Ok(endpoint);
    }
    let editors = editor_discovery::discover();
    let cwd = std::env::current_dir().ok();
    resolve_endpoint_with(
        host_override,
        port_override,
        project_path_override,
        &editors,
        cwd.as_deref(),
    )
}

fn resolve_endpoint_with(
    host_override: Option<String>,
    port_override: Option<u16>,
    project_path_override: Option<PathBuf>,
    editors: &[DiscoveredEditor],
    cwd: Option<&Path>,
) -> Result<ResolvedEndpoint> {
    if let Some(endpoint) = explicit_endpoint(host_override, port_override) {
        return Ok(endpoint);
    }

    let live: Vec<DiscoveredEditor> = editors.iter().filter(|e| e.is_live()).cloned().collect();

    let from_flag = project_path_override.is_some();
    let project_path = project_path_override
        .or_else(|| crate::core::config::read_env(&["UNITY_PROJECT_ROOT"]).map(PathBuf::from));
    if let Some(project_path) = project_path {
        let matches = editor_discovery::select_for_path(editors, &project_path);
        match matches.as_slice() {
            [editor] => return Ok(endpoint_of(editor)),
            [] if !from_flag && live.is_empty() => {}
            [] => {
                return Err(TargetError::NotFound {
                    message: format!(
                        "No running Unity Editor found for project path {}. Run `unity-cli instances list` to see running Editors.",
                        project_path.display()
                    ),
                    candidates: live,
                }
                .into())
            }
            many => return Err(ambiguous(&project_path.display().to_string(), many)),
        }
    }

    if let Some((active_host, active_port)) = instances::active_endpoint()? {
        return Ok(ResolvedEndpoint {
            host: active_host,
            port: active_port,
        });
    }

    if let Some(cwd) = cwd {
        let matches = editor_discovery::select_for_path(editors, cwd);
        match matches.as_slice() {
            [editor] => return Ok(endpoint_of(editor)),
            [] => {}
            many => return Err(ambiguous(&cwd.display().to_string(), many)),
        }
    }

    match live.as_slice() {
        [editor] => Ok(endpoint_of(editor)),
        [] => Ok(ResolvedEndpoint {
            host: default_host(),
            port: default_port(),
        }),
        _ => Err(TargetError::Ambiguous {
            message: format!(
                "{} Unity Editors are running and none contains the current directory. Pass --project-path <path> (or run inside a project) to choose one.",
                live.len()
            ),
            candidates: live,
        }
        .into()),
    }
}

/// `--port` / `UNITY_CLI_PORT` (with an optional host), or a host alone on the
/// default port.
fn explicit_endpoint(
    host_override: Option<String>,
    port_override: Option<u16>,
) -> Option<ResolvedEndpoint> {
    let host = host_override
        .map(|value| value.trim().to_string())
        .or_else(|| crate::core::config::read_env(&["UNITY_CLI_HOST"]));
    let port = port_override.or_else(|| crate::core::config::read_env_u16("UNITY_CLI_PORT"));
    match (host, port) {
        (host, Some(port)) => Some(ResolvedEndpoint {
            host: host.unwrap_or_else(default_host),
            port,
        }),
        (Some(host), None) => Some(ResolvedEndpoint {
            host,
            port: default_port(),
        }),
        (None, None) => None,
    }
}

fn endpoint_of(editor: &DiscoveredEditor) -> ResolvedEndpoint {
    ResolvedEndpoint {
        host: editor.lock.host.clone(),
        port: editor.lock.port,
    }
}

fn ambiguous(target: &str, matches: &[&DiscoveredEditor]) -> anyhow::Error {
    TargetError::Ambiguous {
        message: format!(
            "{} running Unity Editors match {target}. Pass --port to choose one.",
            matches.len()
        ),
        candidates: matches.iter().map(|editor| (*editor).clone()).collect(),
    }
    .into()
}

#[cfg(test)]
mod tests {
    use super::{resolve_endpoint, resolve_endpoint_with};
    use crate::core::editor_discovery::tests::{dead_pid, live_pid, write_lock};
    use crate::core::editor_discovery::{discover, DiscoveredEditor, TargetError};
    use std::path::{Path, PathBuf};

    struct Isolated {
        _guard: std::sync::MutexGuard<'static, ()>,
        dir: tempfile::TempDir,
    }

    impl Isolated {
        fn new() -> Self {
            let guard = crate::test_env::env_lock()
                .lock()
                .unwrap_or_else(|poison| poison.into_inner());
            let dir = tempfile::tempdir().unwrap();
            for key in ["UNITY_CLI_HOST", "UNITY_CLI_PORT", "UNITY_PROJECT_ROOT"] {
                std::env::remove_var(key);
            }
            std::env::set_var("UNITY_CLI_EDITORS_DIR", dir.path().join("editors"));
            std::env::set_var("UNITY_CLI_REGISTRY_PATH", dir.path().join("instances.json"));
            Self { _guard: guard, dir }
        }

        fn project(&self, name: &str) -> PathBuf {
            let path = self.dir.path().join(name);
            std::fs::create_dir_all(path.join("Assets")).unwrap();
            path
        }

        fn lock(&self, project: &Path, port: u16) {
            write_lock(
                &self.dir.path().join("editors"),
                live_pid(),
                project,
                port,
                now(),
            );
        }

        fn editors(&self) -> Vec<DiscoveredEditor> {
            discover()
        }
    }

    impl Drop for Isolated {
        fn drop(&mut self) {
            for key in [
                "UNITY_CLI_EDITORS_DIR",
                "UNITY_CLI_REGISTRY_PATH",
                "UNITY_CLI_HOST",
                "UNITY_CLI_PORT",
                "UNITY_PROJECT_ROOT",
            ] {
                std::env::remove_var(key);
            }
        }
    }

    fn now() -> f64 {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs_f64()
    }

    fn target_error(error: anyhow::Error) -> TargetError {
        error
            .downcast::<TargetError>()
            .expect("error should be a TargetError")
    }

    #[test]
    fn cli_override_wins_over_active_instance() {
        let _env = Isolated::new();
        std::env::set_var("UNITY_CLI_HOST", "env-host");
        std::env::set_var("UNITY_CLI_PORT", "7777");
        let value = resolve_endpoint(Some("cli-host".to_string()), Some(9999), None)
            .expect("endpoint should resolve");
        assert_eq!(value.host, "cli-host");
        assert_eq!(value.port, 9999);
    }

    #[test]
    fn env_port_wins_when_no_cli_override() {
        let _env = Isolated::new();
        std::env::set_var("UNITY_CLI_HOST", "env-host");
        std::env::set_var("UNITY_CLI_PORT", "7777");
        let value = resolve_endpoint(None, None, None).expect("endpoint should resolve");
        assert_eq!(value.host, "env-host");
        assert_eq!(value.port, 7777);
    }

    #[test]
    fn explicit_port_wins_over_project_path_and_lockfiles() {
        let env = Isolated::new();
        let a = env.project("A");
        env.lock(&a, 6401);
        let value =
            resolve_endpoint_with(None, Some(6555), Some(a.clone()), &env.editors(), Some(&a))
                .unwrap();
        assert_eq!(value.port, 6555);
    }

    #[test]
    fn project_path_selects_matching_editor() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        env.lock(&b, 6401);
        let editors = env.editors();
        let outside = env.dir.path();

        let to_a =
            resolve_endpoint_with(None, None, Some(a.clone()), &editors, Some(outside)).unwrap();
        let to_b =
            resolve_endpoint_with(None, None, Some(b.join("Assets")), &editors, Some(outside))
                .unwrap();

        assert_eq!((to_a.host.as_str(), to_a.port), ("127.0.0.1", 6400));
        assert_eq!(to_b.port, 6401);
    }

    #[test]
    fn unity_project_root_env_acts_as_project_path() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        env.lock(&b, 6401);
        std::env::set_var("UNITY_PROJECT_ROOT", &b);

        let value =
            resolve_endpoint_with(None, None, None, &env.editors(), Some(env.dir.path())).unwrap();

        assert_eq!(value.port, 6401);
    }

    #[test]
    fn project_path_without_running_editor_is_not_found() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);

        let error = resolve_endpoint_with(None, None, Some(b), &env.editors(), None).unwrap_err();

        let error = target_error(error);
        assert_eq!(error.code(), "EDITOR_NOT_FOUND");
    }

    #[test]
    fn project_root_env_falls_back_to_default_without_any_lockfile() {
        let env = Isolated::new();
        std::env::set_var("UNITY_PROJECT_ROOT", env.project("A"));

        let value = resolve_endpoint_with(None, None, None, &[], None).unwrap();

        assert_eq!((value.host.as_str(), value.port), ("127.0.0.1", 6400));
    }

    #[test]
    fn cwd_inside_project_selects_that_editor() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        env.lock(&b, 6401);

        let value =
            resolve_endpoint_with(None, None, None, &env.editors(), Some(&b.join("Assets")))
                .unwrap();

        assert_eq!(value.port, 6401);
    }

    #[test]
    fn cwd_outside_projects_with_two_editors_is_ambiguous() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        env.lock(&b, 6401);

        let error = resolve_endpoint_with(None, None, None, &env.editors(), Some(env.dir.path()))
            .unwrap_err();

        let error = target_error(error);
        assert_eq!(error.code(), "AMBIGUOUS_EDITOR");
        let json = error.to_json();
        assert_eq!(json["data"]["candidates"].as_array().unwrap().len(), 2);
    }

    #[test]
    fn single_running_editor_is_used_from_anywhere() {
        let env = Isolated::new();
        let a = env.project("A");
        env.lock(&a, 6402);

        let value =
            resolve_endpoint_with(None, None, None, &env.editors(), Some(env.dir.path())).unwrap();

        assert_eq!(value.port, 6402);
    }

    #[test]
    fn stale_editors_do_not_cause_ambiguity() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        write_lock(&env.dir.path().join("editors"), dead_pid(), &b, 6401, now());

        let value =
            resolve_endpoint_with(None, None, None, &env.editors(), Some(env.dir.path())).unwrap();

        assert_eq!(value.port, 6400);
    }

    #[test]
    fn active_instance_keeps_priority_over_lockfile_discovery() {
        let env = Isolated::new();
        let (a, b) = (env.project("A"), env.project("B"));
        env.lock(&a, 6400);
        env.lock(&b, 6401);
        std::fs::write(
            env.dir.path().join("instances.json"),
            r#"{"active_id":"127.0.0.1:6499","entries":[]}"#,
        )
        .unwrap();

        let value = resolve_endpoint_with(None, None, None, &env.editors(), Some(&a)).unwrap();

        assert_eq!(value.port, 6499);
    }

    #[test]
    fn no_lockfiles_keeps_legacy_default() {
        let env = Isolated::new();
        let value = resolve_endpoint_with(None, None, None, &[], Some(env.dir.path())).unwrap();
        assert_eq!((value.host.as_str(), value.port), ("127.0.0.1", 6400));
    }
}
