use std::{env, fs, path::Path};

fn collect(root: &Path, dir: &Path, files: &mut Vec<String>) {
    for entry in fs::read_dir(dir).expect("read canonical skills") {
        let entry = entry.expect("read skill entry");
        let kind = entry.file_type().expect("read skill file type");
        assert!(
            !kind.is_symlink(),
            "canonical skills must not contain symlinks"
        );
        let path = entry.path();
        if kind.is_dir() {
            collect(root, &path, files);
        } else {
            assert!(
                kind.is_file(),
                "canonical skill entries must be regular files"
            );
            files.push(
                path.strip_prefix(root)
                    .unwrap()
                    .to_str()
                    .unwrap()
                    .replace('\\', "/"),
            );
        }
    }
}

fn main() {
    let manifest = env::var("CARGO_MANIFEST_DIR").unwrap();
    let root = Path::new(&manifest).join(".claude-plugin/plugins/unity-cli/skills");
    println!("cargo:rerun-if-changed={}", root.display());
    let mut files = Vec::new();
    for entry in fs::read_dir(&root).expect("canonical skills must be included in the crate") {
        let entry = entry.unwrap();
        if entry.file_name().to_string_lossy().starts_with("unity-") {
            assert!(
                entry.file_type().unwrap().is_dir(),
                "skill must be a real directory"
            );
            assert!(
                entry.path().join("SKILL.md").is_file(),
                "skill must have SKILL.md"
            );
            collect(&root, &entry.path(), &mut files);
        }
    }
    files.sort();
    assert!(!files.is_empty(), "no canonical skills to embed");
    let mut generated = String::from("pub static FILES: &[(&str, &[u8])] = &[\n");
    for name in files {
        let path = root.join(&name);
        generated.push_str(&format!(
            "({name:?}, include_bytes!({:?})),\n",
            path.to_str().unwrap()
        ));
    }
    generated.push_str("];\n");
    fs::write(
        Path::new(&env::var("OUT_DIR").unwrap()).join("embedded_skills.rs"),
        generated,
    )
    .unwrap();
}
