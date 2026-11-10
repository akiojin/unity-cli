use std::fs;
#[cfg(unix)]
use std::hash::{Hash, Hasher};
use std::path::PathBuf;

use anyhow::Result;

use crate::core::runtime_paths::RuntimePaths;

#[cfg(any(not(unix), test))]
pub fn tcp_port(variable: &str, default: u16) -> Result<u16> {
    match std::env::var_os(variable) {
        None => Ok(default),
        Some(value) => value
            .to_str()
            .and_then(|value| value.parse::<u16>().ok())
            .filter(|port| *port != 0)
            .ok_or_else(|| {
                std::io::Error::new(
                    std::io::ErrorKind::InvalidInput,
                    format!("{variable} must be a TCP port between 1 and 65535"),
                )
                .into()
            }),
    }
}

#[derive(Debug, Clone)]
pub struct DaemonRuntimePaths {
    name: &'static str,
    paths: RuntimePaths,
}

impl DaemonRuntimePaths {
    pub fn new(name: &'static str) -> Result<Self> {
        Ok(Self {
            name,
            paths: RuntimePaths::discover()?,
        })
    }

    pub fn dir(&self) -> Result<PathBuf> {
        self.paths.daemon_dir(self.name)
    }

    pub fn pid_file(&self) -> Result<PathBuf> {
        Ok(self.dir()?.join(format!("{}.pid", self.name)))
    }

    #[cfg(unix)]
    pub fn socket_file(&self) -> Result<PathBuf> {
        let preferred = self.dir()?.join(format!("{}.sock", self.name));
        if preferred.as_os_str().len() < 100 {
            return Ok(preferred);
        }

        let mut hasher = std::collections::hash_map::DefaultHasher::new();
        preferred.hash(&mut hasher);
        let shortened = std::env::temp_dir().join(format!(
            "unity-cli-{}-{:016x}.sock",
            self.name,
            hasher.finish()
        ));
        Ok(shortened)
    }

    pub fn cleanup(&self) {
        if let Ok(path) = self.pid_file() {
            let _ = fs::remove_file(path);
        }
        #[cfg(unix)]
        {
            if let Ok(path) = self.socket_file() {
                let _ = fs::remove_file(path);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::process::Command;

    use tempfile::tempdir;

    #[test]
    fn tcp_ports_are_configurable_and_reject_invalid_values() {
        let lock = crate::test_env::env_lock()
            .lock()
            .unwrap_or_else(|p| p.into_inner());
        let _env = crate::test_env::TestEnvironment::new(&lock);
        for (name, default) in [
            ("UNITY_CLI_UNITYD_PORT", 6422),
            ("UNITY_CLI_LSPD_PORT", 6421),
        ] {
            std::env::remove_var(name);
            assert_eq!(super::tcp_port(name, default).unwrap(), default);
            std::env::set_var(name, "54321");
            assert_eq!(super::tcp_port(name, default).unwrap(), 54321);
            for invalid in ["", "0", "65536", "-1", "not-a-port"] {
                std::env::set_var(name, invalid);
                let error = super::tcp_port(name, default).unwrap_err().to_string();
                assert!(error.contains(name), "{error}");
            }
        }
    }

    #[test]
    fn runtime_module_builds_without_warnings_when_unix_code_is_disabled() {
        let dir = tempdir().expect("tempdir should succeed");

        // Probe: try compiling a trivial file to check the target is usable.
        let probe_path = dir.path().join("probe.rs");
        fs::write(&probe_path, "fn main() {}").expect("write probe");
        let probe = Command::new("rustc")
            .arg("--edition=2021")
            .arg("--target")
            .arg("x86_64-pc-windows-msvc")
            .arg("--emit=metadata")
            .arg("--out-dir")
            .arg(dir.path())
            .arg("--crate-name")
            .arg("target_probe")
            .arg(&probe_path)
            .output();
        if probe.as_ref().map_or(true, |o| !o.status.success()) {
            eprintln!("skipping: x86_64-pc-windows-msvc target not available");
            return;
        }

        let source_path = dir.path().join("runtime_windows_check.rs");
        let runtime_path = format!("{}/src/daemon/runtime.rs", env!("CARGO_MANIFEST_DIR"));
        let source = format!(
            r##"#![allow(dead_code)]

extern crate self as anyhow;
pub type Result<T> = std::result::Result<T, std::io::Error>;

mod core {{
    pub mod runtime_paths {{
        use std::path::PathBuf;

        use crate::Result;

        #[derive(Debug, Clone)]
        pub struct RuntimePaths;

        impl RuntimePaths {{
            pub fn discover() -> Result<Self> {{
                Ok(Self)
            }}

            pub fn daemon_dir(&self, name: &str) -> Result<PathBuf> {{
                Ok(std::env::temp_dir().join(name))
            }}
        }}
    }}
}}

mod daemon {{
    pub mod runtime {{
        include!(r#"{runtime_path}"#);
    }}
}}

fn main() {{
    let paths = daemon::runtime::DaemonRuntimePaths::new("unityd").expect("runtime paths");
    let _ = paths.dir();
    let _ = paths.pid_file();
    paths.cleanup();
}}
"##
        );
        fs::write(&source_path, source).expect("fixture source should be written");

        let output = Command::new("rustc")
            .arg("--edition=2021")
            .arg("--target")
            .arg("x86_64-pc-windows-msvc")
            .arg("-D")
            .arg("warnings")
            .arg("--emit=metadata")
            .arg("--out-dir")
            .arg(dir.path())
            .arg("--crate-name")
            .arg("runtime_windows_check")
            .arg(&source_path)
            .output()
            .expect("rustc should run");

        assert!(
            output.status.success(),
            "runtime.rs should compile cleanly when unix-only code is disabled\nstdout:\n{}\nstderr:\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
    }
}
