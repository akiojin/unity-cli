use std::{env, fs, path::Path, process::Command};

fn link_macos_runtime() {
    // Only Apple Silicon statically links the downloaded ONNX Runtime. Its
    // CoreML objects use __isPlatformVersionAtLeast from compiler-rt, which
    // rustc's -nodefaultlibs otherwise leaves out. Intel uses ort-load-dynamic.
    if env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("macos")
        || env::var("CARGO_CFG_TARGET_ARCH").as_deref() != Ok("aarch64")
    {
        return;
    }
    println!("cargo:rerun-if-env-changed=DEVELOPER_DIR");
    println!("cargo:rerun-if-env-changed=SDKROOT");
    let output = Command::new("xcrun")
        .args(["--sdk", "macosx", "clang", "--print-resource-dir"])
        .output()
        .expect("find compiler-rt: install Xcode Command Line Tools (xcode-select --install)");
    assert!(
        output.status.success(),
        "find compiler-rt with Xcode clang: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    let resource_dir = String::from_utf8(output.stdout).expect("compiler-rt path must be UTF-8");
    let archive = Path::new(resource_dir.trim())
        .join("lib")
        .join("darwin")
        .join("libclang_rt.osx.a");
    assert!(
        archive.is_absolute() && archive.is_file(),
        "Xcode clang could not locate libclang_rt.osx.a: {}. Check xcode-select / DEVELOPER_DIR.",
        archive.display()
    );
    println!(
        "cargo:rustc-link-search=native={}",
        archive.parent().unwrap().display()
    );
    println!("cargo:rustc-link-lib=static=clang_rt.osx");
}

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
    link_macos_runtime();
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
