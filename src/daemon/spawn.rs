use std::io;
use std::process::{Child, Command};

/// Spawn a long-lived background process without handing it the caller's
/// standard handles.
///
/// On Windows, `Command::spawn` passes every inheritable handle in this process
/// to the child, including this process's own stdin/stdout/stderr, even when the
/// child's stdio is redirected. A daemon spawned by a CLI whose stdout is a pipe
/// then holds that pipe open, so whatever reads the CLI's output never sees EOF
/// until the daemon exits. Clear `HANDLE_FLAG_INHERIT` on this process's std
/// handles for the duration of the spawn. Handles set explicitly with
/// `Command::stdin`/`stdout`/`stderr` are unaffected: std duplicates those as
/// inheritable itself.
pub fn spawn_detached(command: &mut Command) -> io::Result<Child> {
    #[cfg(windows)]
    let _guard = windows::StdHandlesNotInherited::new();
    command.spawn()
}

#[cfg(windows)]
mod windows {
    use std::ffi::c_void;

    type Handle = *mut c_void;

    const STD_INPUT_HANDLE: u32 = -10i32 as u32;
    const STD_OUTPUT_HANDLE: u32 = -11i32 as u32;
    const STD_ERROR_HANDLE: u32 = -12i32 as u32;
    const HANDLE_FLAG_INHERIT: u32 = 0x1;
    const INVALID_HANDLE_VALUE: Handle = -1isize as Handle;

    #[link(name = "kernel32")]
    extern "system" {
        fn GetStdHandle(std_handle: u32) -> Handle;
        fn GetHandleInformation(handle: Handle, flags: *mut u32) -> i32;
        fn SetHandleInformation(handle: Handle, mask: u32, flags: u32) -> i32;
    }

    /// Clears `HANDLE_FLAG_INHERIT` on the std handles that have it, and
    /// restores it on drop.
    pub struct StdHandlesNotInherited {
        cleared: Vec<Handle>,
    }

    impl StdHandlesNotInherited {
        pub fn new() -> Self {
            let mut cleared = Vec::new();
            for id in [STD_INPUT_HANDLE, STD_OUTPUT_HANDLE, STD_ERROR_HANDLE] {
                // SAFETY: Win32 calls on this process's own std handles. A missing
                // handle (null or INVALID_HANDLE_VALUE, e.g. no console) is skipped.
                unsafe {
                    let handle = GetStdHandle(id);
                    if handle.is_null()
                        || handle == INVALID_HANDLE_VALUE
                        || cleared.contains(&handle)
                    {
                        continue;
                    }
                    let mut flags = 0u32;
                    if GetHandleInformation(handle, &mut flags) != 0
                        && flags & HANDLE_FLAG_INHERIT != 0
                        && SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0) != 0
                    {
                        cleared.push(handle);
                    }
                }
            }
            Self { cleared }
        }
    }

    impl Drop for StdHandlesNotInherited {
        fn drop(&mut self) {
            for &handle in &self.cleared {
                // SAFETY: restores the flag cleared in `new` on the same handle.
                unsafe {
                    SetHandleInformation(handle, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::process::{Command, Stdio};

    use tempfile::tempdir;

    use super::spawn_detached;

    #[test]
    fn spawn_detached_starts_the_command() {
        let mut command = if cfg!(windows) {
            let mut command = Command::new("cmd");
            command.args(["/C", "exit 0"]);
            command
        } else {
            Command::new("true")
        };
        let mut child = spawn_detached(
            command
                .stdin(Stdio::null())
                .stdout(Stdio::null())
                .stderr(Stdio::null()),
        )
        .expect("spawn should succeed");
        assert!(child.wait().expect("wait should succeed").success());
    }

    // A CLI whose stdout is a pipe (the caller reads its output) spawns a
    // long-lived child with its own stdio redirected. Closing the CLI's write end
    // must give the reader EOF right away, not when the child exits.
    #[cfg(windows)]
    #[test]
    fn spawned_child_does_not_hold_the_callers_stdout_pipe() {
        use std::ffi::c_void;
        use std::io::Read;
        use std::os::windows::io::AsRawHandle;
        use std::sync::mpsc;
        use std::time::Duration;

        const STD_OUTPUT_HANDLE: u32 = -11i32 as u32;
        const HANDLE_FLAG_INHERIT: u32 = 0x1;
        #[link(name = "kernel32")]
        extern "system" {
            fn GetStdHandle(std_handle: u32) -> *mut c_void;
            fn SetStdHandle(std_handle: u32, handle: *mut c_void) -> i32;
            fn SetHandleInformation(handle: *mut c_void, mask: u32, flags: u32) -> i32;
        }

        let (mut reader, writer) = std::io::pipe().expect("pipe should be created");
        // SAFETY: swaps this process's stdout for an inheritable pipe writer and
        // restores the original right after the spawn.
        let original = unsafe {
            let original = GetStdHandle(STD_OUTPUT_HANDLE);
            assert_ne!(
                SetHandleInformation(
                    writer.as_raw_handle(),
                    HANDLE_FLAG_INHERIT,
                    HANDLE_FLAG_INHERIT
                ),
                0
            );
            assert_ne!(SetStdHandle(STD_OUTPUT_HANDLE, writer.as_raw_handle()), 0);
            original
        };
        let spawned = spawn_detached(
            Command::new("ping")
                .args(["-n", "6", "127.0.0.1"])
                .stdin(Stdio::null())
                .stdout(Stdio::null())
                .stderr(Stdio::null()),
        );
        // SAFETY: restores the handle saved above.
        unsafe {
            SetStdHandle(STD_OUTPUT_HANDLE, original);
        }
        let mut child = spawned.expect("spawn should succeed");
        drop(writer);

        let (tx, rx) = mpsc::channel();
        std::thread::spawn(move || {
            let _ = reader.read_to_end(&mut Vec::new());
            let _ = tx.send(());
        });
        let eof = rx.recv_timeout(Duration::from_secs(2)).is_ok();
        let still_running = child.try_wait().expect("try_wait should succeed").is_none();
        let _ = child.kill();
        let _ = child.wait();

        assert!(
            still_running,
            "fixture child exited too early to prove anything"
        );
        assert!(
            eof,
            "reader saw no EOF while the child ran: it inherited the caller's stdout pipe"
        );
    }

    #[test]
    fn spawn_module_builds_for_windows_without_warnings() {
        let dir = tempdir().expect("tempdir should succeed");
        let probe_path = dir.path().join("probe.rs");
        fs::write(&probe_path, "fn main() {}").expect("write probe");
        let probe = Command::new("rustc")
            .args(["--edition=2021", "--target", "x86_64-pc-windows-msvc"])
            .arg("--emit=metadata")
            .arg("--out-dir")
            .arg(dir.path())
            .args(["--crate-name", "target_probe"])
            .arg(&probe_path)
            .output();
        if probe.as_ref().map_or(true, |o| !o.status.success()) {
            eprintln!("skipping: x86_64-pc-windows-msvc target not available");
            return;
        }

        let spawn_path = format!("{}/src/daemon/spawn.rs", env!("CARGO_MANIFEST_DIR"));
        let source_path = dir.path().join("spawn_windows_check.rs");
        let source = format!(
            r##"mod spawn {{
    include!(r#"{spawn_path}"#);
}}

fn main() {{
    let _ = spawn::spawn_detached(&mut std::process::Command::new("cmd"));
}}
"##
        );
        fs::write(&source_path, source).expect("fixture source should be written");

        let output = Command::new("rustc")
            .args(["--edition=2021", "--target", "x86_64-pc-windows-msvc"])
            .args(["-D", "warnings", "--emit=metadata", "--out-dir"])
            .arg(dir.path())
            .args(["--crate-name", "spawn_windows_check"])
            .arg(&source_path)
            .output()
            .expect("rustc should run");
        assert!(
            output.status.success(),
            "spawn.rs should compile cleanly for Windows\nstdout:\n{}\nstderr:\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
    }
}
