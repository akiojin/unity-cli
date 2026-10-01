//! Client-specific configuration formats; no client process is launched.
use crate::cli::McpClient;
use anyhow::{anyhow, bail, ensure, Context, Result};
use serde_json::{json, Value};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
};

fn relative_path(client: McpClient, local: bool, os: &str) -> Result<&'static str> {
    use McpClient::*;
    Ok(match (client, local, os) {
        (ClaudeCode, true, _) => ".mcp.json",
        (ClaudeCode, false, _) => ".claude.json",
        (Cursor, _, _) => ".cursor/mcp.json",
        (Codex, _, _) => ".codex/config.toml",
        (Vscode, true, _) => ".vscode/mcp.json",
        (Vscode, false, "macos") => "Library/Application Support/Code/User/mcp.json",
        (Vscode, false, "windows") => "AppData/Roaming/Code/User/mcp.json",
        (Vscode, false, _) => ".config/Code/User/mcp.json",
        (Windsurf, true, _) => {
            bail!("Windsurf does not support project-local MCP settings; omit --local")
        }
        (Windsurf, false, _) => ".codeium/windsurf/mcp_config.json",
    })
}

fn destination(client: McpClient, local: bool) -> Result<PathBuf> {
    let relative = relative_path(client, local, std::env::consts::OS)?;
    let base = if local {
        std::env::current_dir()?
    } else {
        dirs::home_dir().context("Home directory unavailable")?
    }
    .canonicalize()?;
    if !local {
        if client == McpClient::Codex {
            if let Some(root) = std::env::var_os("CODEX_HOME") {
                return configured_root(Path::new(&root), "config.toml");
            }
        }
        if client == McpClient::Vscode {
            let env = if cfg!(windows) {
                "APPDATA"
            } else {
                "XDG_CONFIG_HOME"
            };
            if !cfg!(target_os = "macos") {
                if let Some(root) = std::env::var_os(env) {
                    return configured_root(Path::new(&root), "Code/User/mcp.json");
                }
            }
        }
    }
    Ok(base.join(relative))
}

fn configured_root(root: &Path, relative: &str) -> Result<PathBuf> {
    // Treat the explicit configuration root as the trust boundary, while still
    // refusing a symlink for that root. System aliases above it (macOS /var)
    // are normalized, just as they are for the home/current directory.
    if let Ok(meta) = fs::symlink_metadata(root) {
        ensure!(
            !meta.file_type().is_symlink(),
            "refusing symlink: {}",
            root.display()
        );
    }
    let root = if root.is_absolute() {
        root.to_owned()
    } else {
        std::env::current_dir()?.join(root)
    };
    let ancestor = root
        .ancestors()
        .find(|path| path.exists())
        .context("Configuration root has no existing ancestor")?;
    let normalized = ancestor.canonicalize()?.join(root.strip_prefix(ancestor)?);
    Ok(normalized.join(relative))
}

fn reject_symlinks(path: &Path) -> Result<()> {
    for ancestor in path.ancestors() {
        match fs::symlink_metadata(ancestor) {
            Ok(meta) => ensure!(
                !meta.file_type().is_symlink(),
                "refusing symlink: {}",
                ancestor.display()
            ),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => (),
            Err(e) => return Err(e.into()),
        }
    }
    Ok(())
}

fn merge(before: &str, client: McpClient, args: &[String]) -> Result<String> {
    if client == McpClient::Codex {
        let mut doc = before
            .parse::<toml_edit::DocumentMut>()
            .context("Invalid Codex TOML")?;
        if doc.get("mcp_servers").is_none() {
            doc["mcp_servers"] = toml_edit::Item::Table(toml_edit::Table::new());
        }
        ensure!(
            doc["mcp_servers"].is_table_like(),
            "mcp_servers must be a table"
        );
        if doc["mcp_servers"].get("unity-cli").is_none() {
            doc["mcp_servers"]["unity-cli"] = toml_edit::Item::Table(toml_edit::Table::new());
        }
        let server = doc["mcp_servers"]["unity-cli"]
            .as_table_like_mut()
            .context("unity-cli must be a table")?;
        ensure!(
            server.get("url").is_none(),
            "Existing unity-cli server uses HTTP; refusing to overwrite its transport"
        );
        server.insert("command", toml_edit::value("unity-cli"));
        let mut array = toml_edit::Array::new();
        for arg in args {
            array.push(arg.as_str());
        }
        server.insert("args", toml_edit::value(array));
        return Ok(doc.to_string());
    }
    let mut doc: Value = if before.trim().is_empty() {
        json!({})
    } else {
        serde_json::from_str(before).context("Invalid MCP JSON; existing file left unchanged")?
    };
    let object = doc
        .as_object_mut()
        .context("MCP configuration must be an object")?;
    let key = if client == McpClient::Vscode {
        "servers"
    } else {
        "mcpServers"
    };
    let servers = object
        .entry(key)
        .or_insert_with(|| json!({}))
        .as_object_mut()
        .with_context(|| format!("{key} must be an object"))?;
    let server = servers
        .entry("unity-cli")
        .or_insert_with(|| json!({}))
        .as_object_mut()
        .context("unity-cli must be an object")?;
    ensure!(
        !server.contains_key("url") && !server.contains_key("serverUrl"),
        "Existing unity-cli server uses HTTP; refusing to overwrite its transport"
    );
    server.insert("type".into(), json!("stdio"));
    server.insert("command".into(), json!("unity-cli"));
    server.insert("args".into(), json!(args));
    Ok(format!("{}\n", serde_json::to_string_pretty(&doc)?))
}

fn update(path: &Path, client: McpClient, args: &[String], dry_run: bool) -> Result<String> {
    reject_symlinks(path)?;
    let before = match fs::read_to_string(path) {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(e) => return Err(e.into()),
    };
    let after = merge(&before, client, args)?;
    let label = path.display().to_string();
    let diff = similar::TextDiff::from_lines(&before, &after)
        .unified_diff()
        .header(&label, &label)
        .to_string();
    if dry_run || before == after {
        return Ok(diff);
    }
    let parent = path
        .parent()
        .context("Configuration has no parent directory")?;
    fs::create_dir_all(parent)?;
    reject_symlinks(path)?;
    let mut temp = tempfile::NamedTempFile::new_in(parent)?;
    if let Ok(meta) = fs::metadata(path) {
        temp.as_file().set_permissions(meta.permissions())?;
    }
    temp.write_all(after.as_bytes())?;
    temp.as_file().sync_all()?;
    reject_symlinks(path)?;
    // Do not replace an edit made while our candidate was being prepared.
    let current = match fs::read_to_string(path) {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(e) => return Err(e.into()),
    };
    ensure!(
        current == before,
        "Configuration changed concurrently; retry"
    );
    temp.persist(path).map_err(|e| anyhow!(e.error))?;
    Ok(diff)
}

pub fn run(client: McpClient, local: bool, dry_run: bool) -> Result<()> {
    let path = destination(client, local)?;
    let mut args = vec!["mcp".to_owned()];
    if local {
        args.extend([
            "--project-path".to_owned(),
            std::env::current_dir()?.to_string_lossy().into_owned(),
        ]);
    }
    let diff = update(&path, client, &args, dry_run)?;
    print!("{diff}");
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merges_inline_toml_without_losing_other_servers_or_options() {
        let before = r#"model = "kept"
mcp_servers = { other = { command = "keep" }, unity-cli = { command = "old", env = { CUSTOM = "keep" } } }
"#;
        let after = merge(before, McpClient::Codex, &["mcp".into()]).unwrap();
        let doc = after.parse::<toml_edit::DocumentMut>().unwrap();
        assert_eq!(doc["model"].as_str(), Some("kept"));
        assert_eq!(
            doc["mcp_servers"]["other"]["command"].as_str(),
            Some("keep")
        );
        assert_eq!(
            doc["mcp_servers"]["unity-cli"]["command"].as_str(),
            Some("unity-cli")
        );
        assert_eq!(
            doc["mcp_servers"]["unity-cli"]["env"]["CUSTOM"].as_str(),
            Some("keep")
        );
        assert_eq!(
            doc["mcp_servers"]["unity-cli"]["args"][0].as_str(),
            Some("mcp")
        );
    }

    #[test]
    fn malformed_shapes_and_http_name_collisions_are_rejected() {
        for before in [
            "[]",
            r#"{"mcpServers":[]}"#,
            r#"{"mcpServers":{"unity-cli":7}}"#,
            r#"{"mcpServers":{"unity-cli":{"url":"https://example.test"}}}"#,
        ] {
            assert!(
                merge(before, McpClient::Cursor, &["mcp".into()]).is_err(),
                "{before}"
            );
        }
        for before in [
            "mcp_servers = 7",
            "[mcp_servers]\nunity-cli = 7",
            "[mcp_servers.unity-cli]\nurl = 'https://example.test'",
        ] {
            assert!(
                merge(before, McpClient::Codex, &["mcp".into()]).is_err(),
                "{before}"
            );
        }
    }

    #[test]
    fn custom_root_may_have_missing_ancestors_without_creating_them() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir
            .path()
            .canonicalize()
            .unwrap()
            .join("first/second/codex");
        assert_eq!(
            configured_root(&root, "config.toml").unwrap(),
            root.join("config.toml")
        );
        assert!(!root.parent().unwrap().exists());
    }

    #[test]
    #[cfg(unix)]
    fn rejects_parent_and_dangling_symlinks_preserves_permissions() {
        use std::os::unix::fs::{symlink, PermissionsExt};
        let dir = tempfile::tempdir().unwrap();
        let base = dir.path().canonicalize().unwrap();
        let target = base.join("target");
        fs::create_dir(&target).unwrap();
        let link = base.join("link");
        symlink(&target, &link).unwrap();
        assert!(update(
            &link.join("config"),
            McpClient::Cursor,
            &["mcp".into()],
            false
        )
        .is_err());
        assert!(configured_root(&link, "config.toml").is_err());
        let dangling = base.join("dangling");
        symlink(base.join("missing"), &dangling).unwrap();
        assert!(update(&dangling, McpClient::Cursor, &["mcp".into()], true).is_err());
        let path = base.join("config");
        fs::write(&path, "{}").unwrap();
        fs::set_permissions(&path, fs::Permissions::from_mode(0o640)).unwrap();
        update(&path, McpClient::Cursor, &["mcp".into()], false).unwrap();
        assert_eq!(
            fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o640
        );
    }
    #[test]
    fn windows_and_local_paths() {
        for (client, expected) in [
            (McpClient::ClaudeCode, ".claude.json"),
            (McpClient::Cursor, ".cursor/mcp.json"),
            (McpClient::Vscode, "AppData/Roaming/Code/User/mcp.json"),
            (McpClient::Windsurf, ".codeium/windsurf/mcp_config.json"),
            (McpClient::Codex, ".codex/config.toml"),
        ] {
            assert_eq!(relative_path(client, false, "windows").unwrap(), expected);
        }
        assert!(relative_path(McpClient::Windsurf, true, "macos").is_err());
    }

    #[test]
    fn invalid_config_is_never_overwritten_and_preview_creates_nothing() {
        for client in [
            McpClient::ClaudeCode,
            McpClient::Cursor,
            McpClient::Vscode,
            McpClient::Windsurf,
            McpClient::Codex,
        ] {
            let dir = tempfile::tempdir().unwrap();
            let path = dir.path().canonicalize().unwrap().join("new/config");
            assert!(!update(&path, client, &["mcp".into()], true)
                .unwrap()
                .is_empty());
            assert!(!path.parent().unwrap().exists());
            fs::create_dir_all(path.parent().unwrap()).unwrap();
            fs::write(&path, "[ broken").unwrap();
            assert!(update(&path, client, &["mcp".into()], false).is_err());
            assert_eq!(fs::read_to_string(&path).unwrap(), "[ broken");
        }
    }
}
