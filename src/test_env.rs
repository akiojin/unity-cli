use std::sync::{Mutex, OnceLock};

pub(crate) fn env_lock() -> &'static Mutex<()> {
    static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
    LOCK.get_or_init(|| Mutex::new(()))
}

/// Isolate tests that discover Editors, read configuration, or contact daemons.
/// Acquire `env_lock()` first, then keep this guard alive until all test tasks
/// have finished. Continue running cargo tests with `--test-threads=1`: other
/// code can read process-wide environment variables without taking this lock.
pub(crate) struct TestEnvironment<'a> {
    previous: Vec<(&'static str, Option<std::ffi::OsString>)>,
    _root: tempfile::TempDir,
    _lock: &'a std::sync::MutexGuard<'static, ()>,
}

impl<'a> TestEnvironment<'a> {
    pub(crate) fn new(lock: &'a std::sync::MutexGuard<'static, ()>) -> Self {
        let root = tempfile::tempdir().expect("test runtime directory");
        let mut environment = Self {
            previous: Vec::new(),
            _root: root,
            _lock: lock,
        };
        for (key, suffix) in [
            ("UNITY_CLI_TOOLS_ROOT", "tools"),
            ("UNITY_CLI_CACHE_ROOT", "cache"),
            ("UNITY_CLI_REGISTRY_PATH", "instances.json"),
            ("UNITY_CLI_EDITORS_DIR", "editors"),
        ] {
            environment.previous.push((key, std::env::var_os(key)));
            std::env::set_var(key, environment._root.path().join(suffix));
        }
        for key in [
            "UNITY_CLI_HOST",
            "UNITY_CLI_PORT",
            "UNITY_CLI_TIMEOUT_MS",
            "UNITY_PROJECT_ROOT",
        ] {
            environment.previous.push((key, std::env::var_os(key)));
            std::env::remove_var(key);
        }
        environment
    }
}

impl Drop for TestEnvironment<'_> {
    fn drop(&mut self) {
        for (key, value) in self.previous.iter().rev() {
            match value {
                Some(value) => std::env::set_var(key, value),
                None => std::env::remove_var(key),
            }
        }
    }
}

#[test]
fn isolated_runtime_restores_inherited_environment_after_unwind() {
    let lock = env_lock().lock().unwrap_or_else(|p| p.into_inner());
    // The outer scope preserves the real caller's environment. The inner scope
    // must leave this simulated caller's files and overrides intact on panic.
    let _caller = TestEnvironment::new(&lock);
    let caller_root = crate::core::managed_binaries::tools_root().unwrap();
    std::fs::create_dir_all(&caller_root).unwrap();
    let sentinel = caller_root.join("do-not-touch");
    std::fs::write(&sentinel, "caller data").unwrap();
    std::env::set_var("UNITY_CLI_HOST", "caller-host");
    let keys = [
        "UNITY_CLI_TOOLS_ROOT",
        "UNITY_CLI_CACHE_ROOT",
        "UNITY_CLI_REGISTRY_PATH",
        "UNITY_CLI_EDITORS_DIR",
        "UNITY_CLI_HOST",
        "UNITY_CLI_PORT",
        "UNITY_CLI_TIMEOUT_MS",
        "UNITY_PROJECT_ROOT",
    ];
    let before: Vec<_> = keys.iter().map(std::env::var_os).collect();
    let mut isolated_root = None;
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let _test = TestEnvironment::new(&lock);
        let tools = crate::core::managed_binaries::tools_root().unwrap();
        assert_ne!(tools, caller_root);
        let daemon = crate::daemon::runtime::DaemonRuntimePaths::new("unityd").unwrap();
        assert!(daemon.pid_file().unwrap().starts_with(&tools));
        assert!(crate::core::editor_discovery::discover().is_empty());
        assert!(std::env::var_os("UNITY_CLI_HOST").is_none());
        isolated_root = Some(tools);
        panic!("simulate test failure");
    }));
    assert!(result.is_err());
    assert!(!isolated_root.unwrap().exists());
    assert_eq!(std::fs::read_to_string(sentinel).unwrap(), "caller data");
    let after: Vec<_> = keys.iter().map(std::env::var_os).collect();
    assert_eq!(before, after);
}
