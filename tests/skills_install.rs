use std::{
    fs,
    path::Path,
    process::{Command, Output},
};

fn run(root: &Path, args: &[&str]) -> Output {
    Command::new(env!("CARGO_BIN_EXE_unity-cli"))
        .current_dir(root)
        .env("HOME", root)
        .env("USERPROFILE", root)
        .env_remove("UNITY_CLI_NO_AUTO_UPDATE")
        .args(args)
        .output()
        .unwrap()
}

fn ok(output: Output) -> String {
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    String::from_utf8(output.stdout).unwrap()
}

#[test]
fn dry_run_prints_only_paths_without_creating_files() {
    let root = tempfile::tempdir().unwrap();
    let output = ok(run(
        root.path(),
        &["skills", "install", "cursor", "--local", "--dry-run"],
    ));
    // Compare path components: the separator is `\` on Windows.
    assert!(output
        .lines()
        .any(|p| Path::new(p).ends_with("unity-scene-create/SKILL.md")));
    let resolved_root = root.path().canonicalize().unwrap();
    assert!(output
        .lines()
        .all(|p| Path::new(p).starts_with(&resolved_root)));
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
}

#[test]
fn global_dry_run_flag_also_prevents_installation() {
    let root = tempfile::tempdir().unwrap();
    ok(run(
        root.path(),
        &["--dry-run", "skills", "install", "cursor", "--local"],
    ));
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
}

#[test]
fn install_protects_unowned_directory_and_force_keeps_unrelated_files() {
    let root = tempfile::tempdir().unwrap();
    let skill = root.path().join(".cursor/skills/unity-scene-create");
    fs::create_dir_all(&skill).unwrap();
    fs::write(skill.join("SKILL.md"), "user skill").unwrap();
    fs::write(skill.join("notes.txt"), "user notes").unwrap();
    assert!(
        !run(root.path(), &["skills", "install", "cursor", "--local"])
            .status
            .success()
    );
    assert_eq!(
        fs::read_to_string(skill.join("SKILL.md")).unwrap(),
        "user skill"
    );
    assert!(!root
        .path()
        .join(".cursor/skills/unity-addressables")
        .exists());
    ok(run(
        root.path(),
        &["skills", "install", "cursor", "--local", "--force"],
    ));
    assert!(fs::read_to_string(skill.join("SKILL.md"))
        .unwrap()
        .contains("name: unity-scene-create"));
    assert_eq!(
        fs::read_to_string(skill.join("notes.txt")).unwrap(),
        "user notes"
    );
}

#[test]
fn refresh_syncs_an_older_owned_version_and_protects_local_edits() {
    use sha2::{Digest, Sha256};
    let root = tempfile::tempdir().unwrap();
    ok(run(
        root.path(),
        &["skills", "install", "cursor", "--local"],
    ));
    let base = root.path().join(".cursor/skills");
    let path = "unity-scene-create/SKILL.md";
    let original = fs::read(base.join(path)).unwrap();
    let ledger_path = base.join(".unity-cli-skills.json");
    let mut ledger: serde_json::Value =
        serde_json::from_slice(&fs::read(&ledger_path).unwrap()).unwrap();
    ledger["version"] = "0.0.1".into();
    let digest = |bytes: &[u8]| {
        Sha256::digest(bytes)
            .iter()
            .map(|b| format!("{b:02x}"))
            .collect::<String>()
    };
    ledger["files"][path] = digest(b"old embedded version").into();
    ledger["files"]["unity-scene-create/retired.md"] = digest(b"retired").into();
    fs::write(base.join(path), "old embedded version").unwrap();
    fs::write(base.join("unity-scene-create/retired.md"), "retired").unwrap();
    fs::write(&ledger_path, serde_json::to_vec(&ledger).unwrap()).unwrap();
    ok(run(root.path(), &["skills", "refresh", "--local"]));
    assert_eq!(fs::read(base.join(path)).unwrap(), original);
    assert!(!base.join("unity-scene-create/retired.md").exists());
    fs::write(base.join(path), "local edit").unwrap();
    assert!(
        !run(root.path(), &["skills", "refresh", "cursor", "--local"])
            .status
            .success()
    );
    assert_eq!(fs::read_to_string(base.join(path)).unwrap(), "local edit");
    ok(run(
        root.path(),
        &["skills", "refresh", "cursor", "--local", "--force"],
    ));
    assert_eq!(fs::read(base.join(path)).unwrap(), original);
}

#[test]
fn invalid_ledger_paths_are_rejected_even_with_force() {
    let root = tempfile::tempdir().unwrap();
    ok(run(
        root.path(),
        &["skills", "install", "cursor", "--local"],
    ));
    let ledger_path = root.path().join(".cursor/skills/.unity-cli-skills.json");
    let mut ledger: serde_json::Value =
        serde_json::from_slice(&fs::read(&ledger_path).unwrap()).unwrap();
    ledger["files"]["../../outside"] = "0".repeat(64).into();
    fs::write(&ledger_path, serde_json::to_vec(&ledger).unwrap()).unwrap();
    let before = fs::read(&ledger_path).unwrap();
    assert!(!run(
        root.path(),
        &["skills", "refresh", "cursor", "--local", "--force"]
    )
    .status
    .success());
    assert_eq!(fs::read(&ledger_path).unwrap(), before);
}

#[test]
fn refresh_does_not_install_clients_without_a_ledger() {
    let root = tempfile::tempdir().unwrap();
    assert!(!run(root.path(), &["skills", "refresh", "--local"])
        .status
        .success());
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
    ok(run(
        root.path(),
        &["skills", "install", "cursor", "--local"],
    ));
    ok(run(
        root.path(),
        &["skills", "refresh", "--local", "--dry-run"],
    ));
    assert!(!root.path().join(".claude").exists());
    assert!(!root.path().join(".windsurf").exists());
    assert!(!root.path().join(".github").exists());
}

#[test]
fn all_clients_install_embedded_references_and_show_works_outside_repository() {
    let root = tempfile::tempdir().unwrap();
    for (client, local, global) in [
        ("claude-code", ".claude/skills", ".claude/skills"),
        ("cursor", ".cursor/skills", ".cursor/skills"),
        ("windsurf", ".windsurf/skills", ".codeium/windsurf/skills"),
        ("vscode", ".github/skills", ".copilot/skills"),
    ] {
        ok(run(root.path(), &["skills", "install", client, "--local"]));
        assert!(root
            .path()
            .join(local)
            .join("unity-scene-create/references/runtime-checklist.md")
            .is_file());
        ok(run(root.path(), &["skills", "install", client]));
        assert!(root
            .path()
            .join(global)
            .join("unity-scene-create/SKILL.md")
            .is_file());
    }
    assert!(ok(run(root.path(), &["skills", "show"])).contains("unity-scene-create"));
    assert!(
        ok(run(root.path(), &["skills", "show", "unity-scene-create"]))
            .contains("# Scene Bootstrap")
    );
    assert!(!run(root.path(), &["skills", "show", "unknown"])
        .status
        .success());
    assert!(!run(root.path(), &["skills", "install", "claude-desktop"])
        .status
        .success());
}

#[cfg(unix)]
#[test]
fn force_never_follows_symlinks() {
    use std::os::unix::fs::symlink;
    for relative in [
        ".cursor",
        ".cursor/skills/unity-scene-create",
        ".cursor/skills/unity-scene-create/SKILL.md",
        ".cursor/skills/.unity-cli-skills.json",
    ] {
        let root = tempfile::tempdir().unwrap();
        let outside = tempfile::tempdir().unwrap();
        let link = root.path().join(relative);
        fs::create_dir_all(link.parent().unwrap()).unwrap();
        symlink(outside.path(), &link).unwrap();
        let output = run(
            root.path(),
            &["skills", "install", "cursor", "--local", "--force"],
        );
        assert!(!output.status.success(), "{relative}");
        assert!(
            String::from_utf8_lossy(&output.stderr).contains("symlink"),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert_eq!(fs::read_dir(outside.path()).unwrap().count(), 0);
    }
}
