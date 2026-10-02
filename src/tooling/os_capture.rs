//! OS-level screenshot fallback for `capture_screenshot` (#369).
//!
//! When the Editor main thread is blocked (modal dialog, API Updater, ...)
//! the bridge never answers, so the CLI captures the desktop itself.

use std::path::{Path, PathBuf};

use anyhow::{anyhow, Result};
use serde_json::{json, Value};

/// CLI-only `capture_screenshot` parameter. Defaults to `true`; stripped before
/// the request reaches the bridge.
pub const OS_FALLBACK_PARAM: &str = "osFallback";

/// Placeholder replaced with the output path in candidate arguments.
const PATH_ARG: &str = "{path}";

/// Removes the CLI-only flag from `params` and returns whether the fallback is enabled.
pub fn take_fallback_flag(params: &mut Value) -> bool {
    params
        .as_object_mut()
        .and_then(|map| map.remove(OS_FALLBACK_PARAM))
        .and_then(|value| value.as_bool())
        .unwrap_or(true)
}

/// True when `error` means Unity accepted the connection but did not answer in time.
/// Matches the message text because the daemon route forwards errors as strings.
pub fn is_response_timeout(error: &anyhow::Error) -> bool {
    use crate::unity::transport::{PAYLOAD_TIMEOUT_MESSAGE, RESPONSE_TIMEOUT_MESSAGE};
    let message = format!("{error:#}");
    message.contains(RESPONSE_TIMEOUT_MESSAGE) || message.contains(PAYLOAD_TIMEOUT_MESSAGE)
}

/// Turns a response timeout into an OS screenshot when the fallback is enabled;
/// any other error (or a disabled fallback) is returned unchanged.
pub fn recover_from_timeout(
    error: anyhow::Error,
    enabled: bool,
    capture: impl FnOnce() -> Result<Value>,
) -> Result<Value> {
    if !enabled || !is_response_timeout(&error) {
        return Err(error);
    }
    let editor_error = format!("{error:#}");
    tracing::warn!(error = %editor_error, "Unity did not respond; falling back to OS screenshot");
    let mut value = capture().map_err(|capture_error| {
        error.context(format!(
            "OS screenshot fallback also failed: {capture_error:#}"
        ))
    })?;
    if let Some(map) = value.as_object_mut() {
        map.insert("fallback".to_string(), json!("os"));
        map.insert(
            "note".to_string(),
            json!(format!(
                "Unity Editor did not respond ({editor_error}); captured the whole desktop with an OS screenshot instead. \
                 The Editor main thread may be blocked by a modal dialog. \
                 Pass \"{OS_FALLBACK_PARAM}\": false to get the timeout error instead."
            )),
        );
    }
    Ok(value)
}

/// Captures the desktop into the bridge capture directory of the resolved project.
pub fn capture_desktop(capture_mode: &str) -> Result<Value> {
    let project = crate::core::doctor::resolve_project_path(None);
    let millis = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis();
    let path = non_verbatim(output_path(project.as_deref(), millis));
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|e| anyhow!("Failed to create {}: {e}", parent.display()))?;
    }
    capture_with(&platform_candidates(), &path, capture_mode)
}

/// `<project>/.unity/capture/image_os_<millis>.png`, mirroring `CapturePathResolver`
/// on the bridge; falls back to the temp directory outside a Unity project.
fn output_path(project: Option<&Path>, millis: u128) -> PathBuf {
    let base = project
        .map(|root| root.join(".unity"))
        .unwrap_or_else(|| std::env::temp_dir().join("unity-cli"));
    base.join("capture").join(format!("image_os_{millis}.png"))
}

/// Drops the verbatim prefix (`\\?\C:\...`, `\\?\UNC\server\share\...`) that
/// `std::fs::canonicalize` puts on an explicit project path on Windows. GDI+ in
/// Windows PowerShell rejects it ("The given path's format is not supported").
#[cfg(any(windows, test))]
fn strip_verbatim_prefix(path: &str) -> String {
    if let Some(unc) = path.strip_prefix(r"\\?\UNC\") {
        return format!(r"\\{unc}");
    }
    match path.strip_prefix(r"\\?\") {
        Some(rest) if rest.as_bytes().get(1) == Some(&b':') => rest.to_string(),
        _ => path.to_string(),
    }
}

/// Drops the Windows verbatim prefix (`\\?\C:\x` -> `C:\x`). A canonicalized
/// project root carries it, and Windows PowerShell's `Image.Save` rejects it.
fn non_verbatim(path: PathBuf) -> PathBuf {
    let text = path.to_string_lossy().into_owned();
    if let Some(unc) = text.strip_prefix(r"\\?\UNC\") {
        PathBuf::from(format!(r"\\{unc}"))
    } else if let Some(rest) = text.strip_prefix(r"\\?\") {
        PathBuf::from(rest)
    } else {
        path
    }
}

struct Candidate {
    program: String,
    args: Vec<String>,
}

impl Candidate {
    fn new(program: &str, args: Vec<&str>) -> Self {
        Self {
            program: program.to_string(),
            args: args.into_iter().map(str::to_string).collect(),
        }
    }
}

#[cfg(target_os = "macos")]
fn platform_candidates() -> Vec<Candidate> {
    vec![Candidate::new(
        "screencapture",
        vec!["-x", "-t", "png", PATH_ARG],
    )]
}

#[cfg(target_os = "windows")]
fn platform_candidates() -> Vec<Candidate> {
    // GDI capture of the whole virtual screen; the path arrives via env to avoid quoting.
    const SCRIPT: &str = "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; \
        $b=[System.Windows.Forms.SystemInformation]::VirtualScreen; \
        $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; \
        $g=[System.Drawing.Graphics]::FromImage($bmp); \
        $g.CopyFromScreen($b.Left,$b.Top,0,0,$bmp.Size); \
        $bmp.Save($env:UNITY_CLI_OS_CAPTURE_PATH,[System.Drawing.Imaging.ImageFormat]::Png); \
        $g.Dispose(); $bmp.Dispose()";
    vec![Candidate::new(
        "powershell",
        vec!["-NoProfile", "-NonInteractive", "-Command", SCRIPT],
    )]
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
fn platform_candidates() -> Vec<Candidate> {
    let wayland = vec![
        Candidate::new("grim", vec![PATH_ARG]),
        Candidate::new("gnome-screenshot", vec!["-f", PATH_ARG]),
        Candidate::new("spectacle", vec!["-b", "-n", "-f", "-o", PATH_ARG]),
    ];
    let x11 = vec![
        Candidate::new("import", vec!["-window", "root", PATH_ARG]),
        Candidate::new("scrot", vec!["-o", PATH_ARG]),
        Candidate::new("maim", vec![PATH_ARG]),
    ];
    if std::env::var_os("WAYLAND_DISPLAY").is_some() {
        wayland.into_iter().chain(x11).collect()
    } else {
        x11.into_iter().chain(wayland).collect()
    }
}

/// Runs candidates in order until one writes a non-empty file at `path`.
fn capture_with(candidates: &[Candidate], path: &Path, capture_mode: &str) -> Result<Value> {
    let path_text = path.to_string_lossy().to_string();
    let mut failures = Vec::new();
    for candidate in candidates {
        let _ = std::fs::remove_file(path);
        let args = candidate.args.iter().map(|arg| {
            if arg == PATH_ARG {
                path_text.clone()
            } else {
                arg.clone()
            }
        });
        let output = std::process::Command::new(&candidate.program)
            .args(args)
            .env("UNITY_CLI_OS_CAPTURE_PATH", path)
            .output();
        let failure = match output {
            Ok(output) if output.status.success() => match std::fs::read(path) {
                Ok(bytes) if !bytes.is_empty() => {
                    let (width, height) = png_size(&bytes).unzip();
                    return Ok(json!({
                        "path": path_text,
                        "width": width,
                        "height": height,
                        "captureMode": capture_mode,
                        "fileSize": bytes.len(),
                        "fallbackTool": candidate.program,
                        "message": "Desktop screenshot captured by the OS (Unity Editor did not respond)"
                    }));
                }
                _ => "exited successfully but wrote no image".to_string(),
            },
            Ok(output) => format!(
                "exited with {}: {}",
                output.status,
                String::from_utf8_lossy(&output.stderr).trim()
            ),
            Err(error) => error.to_string(),
        };
        failures.push(format!("{}: {failure}", candidate.program));
    }
    Err(anyhow!(
        "No OS screenshot tool succeeded ({})",
        failures.join("; ")
    ))
}

/// Width and height from a PNG IHDR chunk.
fn png_size(bytes: &[u8]) -> Option<(u32, u32)> {
    if bytes.len() < 24 || &bytes[..8] != b"\x89PNG\r\n\x1a\n" || &bytes[12..16] != b"IHDR" {
        return None;
    }
    let width = u32::from_be_bytes(bytes[16..20].try_into().ok()?);
    let height = u32::from_be_bytes(bytes[20..24].try_into().ok()?);
    Some((width, height))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn timeout_error() -> anyhow::Error {
        anyhow!(crate::unity::transport::RESPONSE_TIMEOUT_MESSAGE)
    }

    #[test]
    fn fallback_flag_defaults_on_and_is_stripped() {
        let mut params = json!({"captureMode": "game"});
        assert!(take_fallback_flag(&mut params));

        let mut params = json!({"captureMode": "game", "osFallback": false});
        assert!(!take_fallback_flag(&mut params));
        assert!(params.get(OS_FALLBACK_PARAM).is_none());
    }

    #[test]
    fn disabled_fallback_returns_the_timeout_error() {
        let result = recover_from_timeout(timeout_error(), false, || {
            panic!("OS capture must not run when disabled")
        });
        let message = format!("{:#}", result.unwrap_err());
        assert!(message.contains("Timed out"), "{message}");
    }

    #[test]
    fn non_timeout_errors_are_not_recovered() {
        let result = recover_from_timeout(anyhow!("Unity command failed"), true, || {
            panic!("OS capture must not run for non-timeout errors")
        });
        assert!(result.is_err());
    }

    #[test]
    fn timeout_is_recovered_with_os_fallback_marker() {
        let value =
            recover_from_timeout(timeout_error(), true, || Ok(json!({"path": "/tmp/x.png"})))
                .unwrap();
        assert_eq!(value["fallback"], "os");
        assert_eq!(value["path"], "/tmp/x.png");
        assert!(value["note"].as_str().unwrap().contains("did not respond"));
    }

    #[test]
    fn failed_os_capture_keeps_both_errors() {
        let error =
            recover_from_timeout(timeout_error(), true, || Err(anyhow!("no tool"))).unwrap_err();
        let message = format!("{error:#}");
        assert!(message.contains("Timed out"), "{message}");
        assert!(message.contains("no tool"), "{message}");
    }

    #[test]
    fn detects_daemon_stringified_timeouts() {
        let daemon = anyhow!(
            "unityd request failed: {}",
            crate::unity::transport::RESPONSE_TIMEOUT_MESSAGE
        );
        assert!(is_response_timeout(&daemon));
        assert!(!is_response_timeout(&anyhow!(
            "Connection timeout while connecting to Unity"
        )));
    }

    #[cfg(unix)]
    #[test]
    fn capture_uses_first_available_candidate_and_reads_png_size() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("shot.png");
        let script = dir.path().join("fake-shot.sh");
        // PNG header (signature + IHDR width=3, height=2).
        std::fs::write(
            &script,
            "#!/bin/sh\nprintf '\\211PNG\\r\\n\\032\\n\\000\\000\\000\\rIHDR\\000\\000\\000\\003\\000\\000\\000\\002' > \"$1\"\n",
        )
        .unwrap();
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).unwrap();

        let candidates = vec![
            Candidate::new("definitely-missing-screenshot-tool", vec![PATH_ARG]),
            Candidate::new(script.to_str().unwrap(), vec![PATH_ARG]),
        ];
        let value = capture_with(&candidates, &path, "game").unwrap();
        assert_eq!(value["path"], path.to_string_lossy().as_ref());
        assert_eq!(value["width"], 3);
        assert_eq!(value["height"], 2);
        assert_eq!(value["captureMode"], "game");
    }

    #[test]
    fn capture_fails_when_no_candidate_exists() {
        let dir = tempfile::tempdir().unwrap();
        let candidates = vec![Candidate::new(
            "definitely-missing-screenshot-tool",
            vec![PATH_ARG],
        )];
        let error = capture_with(&candidates, &dir.path().join("x.png"), "game").unwrap_err();
        assert!(format!("{error:#}").contains("definitely-missing-screenshot-tool"));
    }

    #[test]
    fn verbatim_prefix_is_dropped_before_the_path_reaches_os_tools() {
        assert_eq!(
            non_verbatim(PathBuf::from(r"\\?\C:\proj\.unity\capture\a.png")),
            PathBuf::from(r"C:\proj\.unity\capture\a.png")
        );
        assert_eq!(
            non_verbatim(PathBuf::from(r"\\?\UNC\server\share\a.png")),
            PathBuf::from(r"\\server\share\a.png")
        );
        assert_eq!(
            non_verbatim(PathBuf::from("/proj/a.png")),
            PathBuf::from("/proj/a.png")
        );
    }

    #[test]
    fn output_path_follows_bridge_capture_directory() {
        let path = output_path(Some(Path::new("/proj")), 42);
        assert_eq!(path, PathBuf::from("/proj/.unity/capture/image_os_42.png"));
    }
}
